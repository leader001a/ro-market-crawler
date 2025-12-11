using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Client for kafra.kr API to fetch item/enchant effect descriptions
/// API: http://api.kafra.kr/KRO/{type}/{page}/itemdetail_name.json?q={query}&perPage={perPage}
/// </summary>
/// <remarks>
/// SECURITY NOTE: This API uses HTTP (not HTTPS) because kafra.kr does not support TLS.
/// Data transmitted includes only public game item information (no sensitive user data).
/// If kafra.kr adds HTTPS support in the future, update BaseUrl accordingly.
/// </remarks>
public class KafraClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, KafraItem> _cache = new();
    private readonly object _cacheLock = new();
    private const string BaseUrl = "http://api.kafra.kr/KRO";
    private const int DefaultPerPage = 50;

    // Input validation constants
    private const int MaxSearchTermLength = 100;
    private const int MinItemType = 0;
    private const int MaxItemType = 999;

    // GNJOY to kafra.kr card name mapping (different Korean transliterations)
    // GNJOY uses Japanese/German style, kafra.kr uses English style
    private static readonly Dictionary<string, string> CardNameMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Monster cards with name differences
        { "미노타우르스", "마이너우로스" },       // Minorous
        { "오크워리어", "오크 워리어" },          // Orc Warrior
        { "페코페코", "페코 페코" },              // Peco Peco
        { "스켈워커", "스켈 워커" },              // Skel Worker
        { "클럭", "클락" },                       // Clock
        { "드레이크", "드레인리아" },             // Drake variations
        { "힐슬리터", "힐 슬리터" },             // Hill Slither
        { "맨티스", "사마귀" },                   // Mantis
        { "포이즌스포어", "포이즌 스포어" },     // Poison Spore
        { "골렘", "고렘" },                       // Golem
        // Add more mappings as discovered
    };

    // Card screen_name to kafra.kr ID mapping for cards with internal name "이름없는카드"
    // These cards can't be found by name search, only by direct ID lookup
    private static readonly Dictionary<string, int> CardIdMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // MVP/Boss cards with "이름없는카드" internal name
        { "파라오 카드", 4148 },
        { "다크로드 카드", 4168 },
        { "봉인된 파라오 카드", 4489 },
        // Add more cards as discovered
    };

    public KafraClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Search for items by name and return matching results
    /// </summary>
    /// <param name="searchTerm">Item name to search for (max 100 chars)</param>
    /// <param name="itemType">Item type (0-999, use 999 for all types)</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    public async Task<List<KafraItem>> SearchItemsAsync(string searchTerm, int itemType = 999, int maxResults = 50)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(searchTerm))
            return new List<KafraItem>();

        // Truncate search term if too long
        if (searchTerm.Length > MaxSearchTermLength)
            searchTerm = searchTerm.Substring(0, MaxSearchTermLength);

        // Validate item type range
        itemType = Math.Clamp(itemType, MinItemType, MaxItemType);

        try
        {
            var encodedQuery = Uri.EscapeDataString(searchTerm);
            var url = $"{BaseUrl}/{itemType}/1/itemdetail_name.json?q={encodedQuery}&perPage={maxResults}";

            Debug.WriteLine($"[KafraClient] Searching: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var items = JsonSerializer.Deserialize<List<KafraItem>>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items != null)
            {
                // Cache results
                lock (_cacheLock)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.ScreenName))
                        {
                            _cache[item.ScreenName] = item;
                        }
                    }
                }
                Debug.WriteLine($"[KafraClient] Found {items.Count} items for '{searchTerm}'");
                return items;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KafraClient] Search error: {ex.Message}");
        }

        return new List<KafraItem>();
    }

    /// <summary>
    /// Get item by ID directly from kafra.kr API
    /// API: http://api.kafra.kr/KRO/{id}/item.json
    /// </summary>
    public async Task<KafraItem?> GetItemByIdAsync(int itemId)
    {
        try
        {
            var url = $"{BaseUrl}/{itemId}/item.json";
            Debug.WriteLine($"[KafraClient] GetItemById: {url}");

            var response = await _httpClient.GetStringAsync(url);
            var items = JsonSerializer.Deserialize<List<KafraItem>>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items != null && items.Count > 0)
            {
                var item = items[0];
                // Normalize ItemText line breaks
                if (!string.IsNullOrEmpty(item.ItemText))
                {
                    item.ItemText = NormalizeLineBreaks(item.ItemText);
                }
                // Cache the result by screen_name
                if (!string.IsNullOrEmpty(item.ScreenName))
                {
                    lock (_cacheLock)
                    {
                        _cache[item.ScreenName] = item;
                    }
                }
                Debug.WriteLine($"[KafkaClient] GetItemById found: {item.ScreenName}");
                return item;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KafraClient] GetItemById error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get item effect description by exact name match
    /// First checks cache, then searches API if not found
    /// </summary>
    public async Task<string?> GetItemEffectAsync(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return null;

        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(itemName, out var cachedItem))
            {
                return NormalizeLineBreaks(cachedItem.ItemText);
            }
        }

        // Search API with original name
        var items = await SearchItemsAsync(itemName, 999, 20);
        var match = FindBestMatch(items, itemName);
        Debug.WriteLine($"[KafraClient] Initial search found {items.Count} items, match: {match?.ScreenName ?? "null"}");

        // If no exact match, try additional search strategies
        if (match == null)
        {
            // Strategy 1: If it's a card name with "카드" suffix, try without it
            if (itemName.Contains("카드"))
            {
                var baseName = itemName.Replace(" 카드", "").Replace("카드", "").Trim();
                if (!string.IsNullOrEmpty(baseName))
                {
                    Debug.WriteLine($"[KafraClient] Retrying card search with base name: {baseName}");
                    items = await SearchItemsAsync(baseName, 999, 30);
                    // Look for card type items (type 6) that match
                    match = items.FirstOrDefault(i =>
                        i.Type == 6 && i.ScreenName?.Contains(baseName, StringComparison.OrdinalIgnoreCase) == true);
                    Debug.WriteLine($"[KafraClient] Card retry found: {match?.ScreenName ?? "null"}");
                }
            }
            // Strategy 2: If no "카드" in name, search might still return a card - check type 6 items
            else
            {
                // Check if any returned item is a card (type 6) matching the search term
                match = items.FirstOrDefault(i =>
                    i.Type == 6 && i.ScreenName?.Contains(itemName, StringComparison.OrdinalIgnoreCase) == true);

                if (match == null)
                {
                    // Also try searching with "카드" appended
                    Debug.WriteLine($"[KafraClient] Trying search with 카드 appended: {itemName} 카드");
                    items = await SearchItemsAsync(itemName + " 카드", 999, 20);
                    match = items.FirstOrDefault(i =>
                        i.Type == 6 && (i.ScreenName?.Contains(itemName, StringComparison.OrdinalIgnoreCase) == true ||
                                       i.FixName?.Contains(itemName, StringComparison.OrdinalIgnoreCase) == true));
                    Debug.WriteLine($"[KafraClient] Search with 카드 appended found: {match?.ScreenName ?? "null"}");
                }
            }
        }

        return NormalizeLineBreaks(match?.ItemText);
    }

    /// <summary>
    /// Get full item data by name (returns complete KafraItem, not just ItemText)
    /// Handles card prefixes like "미노타우르스 퓨리어스 임팩트" -> "퓨리어스 임팩트"
    /// Handles card suffixes like "퓨리어스 임팩트 오브 인피니티" -> "퓨리어스 임팩트"
    /// </summary>
    public async Task<KafraItem?> GetFullItemDataAsync(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return null;

        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(itemName, out var cachedItem))
            {
                return cachedItem;
            }
        }

        // Step 1: Clean refine level prefix (+10, +12, etc.)
        var cleanedName = System.Text.RegularExpressions.Regex.Replace(itemName, @"^\+?\d+\s*", "").Trim();
        Debug.WriteLine($"[KafraClient] GetFullItemData: original='{itemName}', cleaned='{cleanedName}'");

        // Step 2: Search with cleaned name first
        var items = await SearchItemsAsync(cleanedName, 999, 20);
        var match = FindBestMatch(items, cleanedName);
        Debug.WriteLine($"[KafraClient] Search 1 '{cleanedName}': found {items.Count} items, match={match?.ScreenName ?? "null"}");

        var words = cleanedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Step 3: Try removing words from the END (card suffixes like "오브 인피니티")
        if (match == null && words.Length >= 2)
        {
            for (int takeWords = words.Length - 1; takeWords >= 1; takeWords--)
            {
                var shorterName = string.Join(" ", words.Take(takeWords));
                Debug.WriteLine($"[KafraClient] Trying without suffix (take {takeWords} words): '{shorterName}'");

                items = await SearchItemsAsync(shorterName, 999, 20);
                match = FindBestMatch(items, shorterName);

                if (match != null)
                {
                    Debug.WriteLine($"[KafraClient] Found match with '{shorterName}': {match.ScreenName}");
                    break;
                }
            }
        }

        // Step 4: Try removing words from the BEGINNING (card prefixes)
        if (match == null && words.Length >= 2)
        {
            for (int skipWords = 1; skipWords < words.Length; skipWords++)
            {
                var shorterName = string.Join(" ", words.Skip(skipWords));
                Debug.WriteLine($"[KafraClient] Trying without prefix (skip {skipWords} words): '{shorterName}'");

                items = await SearchItemsAsync(shorterName, 999, 20);
                match = FindBestMatch(items, shorterName);

                if (match != null)
                {
                    Debug.WriteLine($"[KafraClient] Found match with '{shorterName}': {match.ScreenName}");
                    break;
                }
            }
        }

        // Step 5: Try middle words only (both prefix and suffix might exist)
        if (match == null && words.Length >= 3)
        {
            // Try removing 1 from start and 1+ from end
            for (int skipStart = 1; skipStart < words.Length - 1; skipStart++)
            {
                for (int skipEnd = 1; skipEnd < words.Length - skipStart; skipEnd++)
                {
                    var middleWords = words.Skip(skipStart).Take(words.Length - skipStart - skipEnd);
                    var shorterName = string.Join(" ", middleWords);
                    if (string.IsNullOrEmpty(shorterName)) continue;

                    Debug.WriteLine($"[KafraClient] Trying middle words (skip {skipStart} start, {skipEnd} end): '{shorterName}'");

                    items = await SearchItemsAsync(shorterName, 999, 20);
                    match = FindBestMatch(items, shorterName);

                    if (match != null)
                    {
                        Debug.WriteLine($"[KafraClient] Found match with '{shorterName}': {match.ScreenName}");
                        break;
                    }
                }
                if (match != null) break;
            }
        }

        // Step 6: Try without spaces (some items have internal names without spaces)
        // e.g., "포링 선글래스" -> "포링선글래스"
        if (match == null && cleanedName.Contains(' '))
        {
            var noSpaceName = cleanedName.Replace(" ", "");
            Debug.WriteLine($"[KafraClient] Trying without spaces: '{noSpaceName}'");

            items = await SearchItemsAsync(noSpaceName, 999, 20);
            match = FindBestMatch(items, cleanedName); // Still match against original screen_name

            if (match != null)
            {
                Debug.WriteLine($"[KafraClient] Found match with no-space search: {match.ScreenName}");
            }
        }

        // Step 7: Try with more results (some items need broader search)
        if (match == null)
        {
            // Try just the first word with more results
            var firstWord = words.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstWord) && firstWord.Length >= 2)
            {
                Debug.WriteLine($"[KafraClient] Trying first word with more results: '{firstWord}'");
                items = await SearchItemsAsync(firstWord, 999, 100);
                match = FindBestMatch(items, cleanedName);

                if (match != null)
                {
                    Debug.WriteLine($"[KafraClient] Found match with first word search: {match.ScreenName}");
                }
            }
        }

        // Step 8: Card-specific fallback (similar to GetItemEffectAsync)
        // If name contains "카드" and no match yet, try without the suffix
        if (match == null && cleanedName.Contains("카드"))
        {
            var baseName = cleanedName.Replace(" 카드", "").Replace("카드", "").Trim();
            if (!string.IsNullOrEmpty(baseName))
            {
                Debug.WriteLine($"[KafraClient] Card fallback: trying base name '{baseName}'");
                items = await SearchItemsAsync(baseName, 999, 50);

                // Look for card type items (type 6) that contain the base name
                match = items.FirstOrDefault(i =>
                    i.Type == 6 && i.ScreenName?.Contains(baseName, StringComparison.OrdinalIgnoreCase) == true);

                if (match != null)
                {
                    Debug.WriteLine($"[KafraClient] Card fallback found: {match.ScreenName}");
                }
                else
                {
                    // Also try searching for the base name + " 카드" pattern match
                    match = items.FirstOrDefault(i =>
                        i.Type == 6 && (
                            i.FixName?.Contains(baseName, StringComparison.OrdinalIgnoreCase) == true ||
                            i.Name?.Contains(baseName, StringComparison.OrdinalIgnoreCase) == true
                        ));

                    if (match != null)
                    {
                        Debug.WriteLine($"[KafraClient] Card fallback (FixName/Name) found: {match.ScreenName}");
                    }
                }

                // Step 8b: Try with name mapping (GNJOY -> kafra.kr transliteration)
                if (match == null && CardNameMapping.TryGetValue(baseName, out var mappedName))
                {
                    Debug.WriteLine($"[KafraClient] Card name mapping: '{baseName}' -> '{mappedName}'");
                    items = await SearchItemsAsync(mappedName, 999, 30);
                    match = items.FirstOrDefault(i =>
                        i.Type == 6 && i.ScreenName?.Contains(mappedName, StringComparison.OrdinalIgnoreCase) == true);

                    if (match != null)
                    {
                        Debug.WriteLine($"[KafraClient] Card mapping found: {match.ScreenName}");
                    }
                }
            }

            // Step 8c: Try direct ID lookup for cards with "이름없는카드" internal name
            // These cards can't be found by name search, only by direct ID lookup
            if (match == null && CardIdMapping.TryGetValue(cleanedName, out var cardId))
            {
                Debug.WriteLine($"[KafraClient] Card ID mapping: '{cleanedName}' -> ID {cardId}");
                match = await GetItemByIdAsync(cardId);
                if (match != null)
                {
                    Debug.WriteLine($"[KafraClient] Card ID lookup found: {match.ScreenName}");
                }
            }
        }

        // Normalize ItemText line breaks if found
        if (match != null && !string.IsNullOrEmpty(match.ItemText))
        {
            match.ItemText = NormalizeLineBreaks(match.ItemText);
        }

        return match;
    }

    /// <summary>
    /// Find best matching item from search results
    /// </summary>
    private static KafraItem? FindBestMatch(List<KafraItem> items, string searchName)
    {
        if (items.Count == 0) return null;

        var searchNameNoSpace = searchName.Replace(" ", "");

        // Try exact match on ScreenName first
        var match = items.FirstOrDefault(i =>
            i.ScreenName?.Equals(searchName, StringComparison.OrdinalIgnoreCase) == true);
        if (match != null) return match;

        // Try exact match on Name (internal name, often without spaces)
        match = items.FirstOrDefault(i =>
            i.Name?.Equals(searchNameNoSpace, StringComparison.OrdinalIgnoreCase) == true ||
            i.Name?.Equals(searchName, StringComparison.OrdinalIgnoreCase) == true);
        if (match != null) return match;

        // Try partial match (search name contained in screen name)
        match = items.FirstOrDefault(i =>
            i.ScreenName?.Contains(searchName, StringComparison.OrdinalIgnoreCase) == true);
        if (match != null) return match;

        // Try partial match on Name without spaces
        match = items.FirstOrDefault(i =>
            i.Name?.Contains(searchNameNoSpace, StringComparison.OrdinalIgnoreCase) == true);
        if (match != null) return match;

        // Try reverse partial match (screen name contained in search name)
        match = items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.ScreenName) &&
            searchName.Contains(i.ScreenName, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // Try matching with screen name without spaces
        match = items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.ScreenName) &&
            i.ScreenName.Replace(" ", "").Equals(searchNameNoSpace, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    /// <summary>
    /// Normalize line breaks for Windows TextBox display
    /// </summary>
    private static string? NormalizeLineBreaks(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Remove color codes like ^ffffff_, ^000000, ^777777
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\^[0-9a-fA-F]{6}_?", "");

        // Normalize line breaks to Windows format
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

        return text.Trim();
    }

    /// <summary>
    /// Preload cache with common enchants
    /// </summary>
    public async Task PreloadCommonEnchantsAsync()
    {
        var commonSearchTerms = new[]
        {
            "설화 마력",
            "설화 물리",
            "축복",
            "투지",
            "마력",
            "광휘",
            "명궁",
            "대장장이"
        };

        foreach (var term in commonSearchTerms)
        {
            try
            {
                await SearchItemsAsync(term, 999, 100);
                await Task.Delay(100); // Rate limiting
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KafraClient] Preload error for '{term}': {ex.Message}");
            }
        }

        Debug.WriteLine($"[KafraClient] Preloaded {_cache.Count} items");
    }

    public int CacheCount
    {
        get
        {
            lock (_cacheLock)
            {
                return _cache.Count;
            }
        }
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// Get monster info by ID
    /// </summary>
    public async Task<MonsterInfo?> GetMonsterByIdAsync(int mobId)
    {
        try
        {
            var url = $"{BaseUrl}/{mobId}/mob.json";
            Debug.WriteLine($"[KafraClient] Fetching monster: {url}");

            var response = await _httpClient.GetStringAsync(url);
            var monsters = JsonSerializer.Deserialize<List<MonsterInfo>>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var monster = monsters?.FirstOrDefault();
            if (monster != null)
            {
                Debug.WriteLine($"[KafraClient] Found monster: {monster.DisplayName} (ID: {monster.MobConst})");
            }
            return monster;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KafraClient] GetMonsterByIdAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Search monsters by name (searches through item drop data)
    /// Since kafra.kr doesn't have a direct monster search API, we search items
    /// that drop from monsters to find monster names
    /// </summary>
    public async Task<List<MonsterInfo>> SearchMonstersAsync(string searchTerm, int startId = 1001, int endId = 4000)
    {
        var results = new List<MonsterInfo>();
        if (string.IsNullOrWhiteSpace(searchTerm)) return results;

        var searchLower = searchTerm.ToLower();
        var tasks = new List<Task<MonsterInfo?>>();

        // Search a range of monster IDs
        // Common monster ID ranges: 1001-2500 (normal), 2501-3000 (rare), etc.
        for (int id = startId; id <= Math.Min(endId, startId + 500); id++)
        {
            var mobId = id;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var monster = await GetMonsterByIdAsync(mobId);
                    if (monster != null)
                    {
                        var nameKo = monster.NameKo?.ToLower() ?? "";
                        var nameEn = monster.NameEn?.ToLower() ?? "";
                        var name = monster.Name?.ToLower() ?? "";

                        if (nameKo.Contains(searchLower) ||
                            nameEn.Contains(searchLower) ||
                            name.Contains(searchLower))
                        {
                            return monster;
                        }
                    }
                }
                catch { }
                return null;
            }));
        }

        var completed = await Task.WhenAll(tasks);
        results.AddRange(completed.Where(m => m != null)!);

        Debug.WriteLine($"[KafraClient] SearchMonstersAsync found {results.Count} monsters for '{searchTerm}'");
        return results;
    }

    /// <summary>
    /// Get multiple monsters by ID range (for browsing)
    /// </summary>
    public async Task<List<MonsterInfo>> GetMonsterRangeAsync(int startId, int count = 50)
    {
        var results = new List<MonsterInfo>();
        var tasks = new List<Task<MonsterInfo?>>();

        for (int id = startId; id < startId + count; id++)
        {
            var mobId = id;
            tasks.Add(GetMonsterByIdAsync(mobId));
        }

        var completed = await Task.WhenAll(tasks);
        results.AddRange(completed.Where(m => m != null)!);

        return results.OrderBy(m => m.MobConst).ToList();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Kafra.kr API item response model
/// Field names mapped from snake_case API response
/// </summary>
public class KafraItem
{
    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("item_const")]
    public int ItemConst { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("screen_name")]
    public string? ScreenName { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("price_buy")]
    public int? PriceBuy { get; set; }

    [JsonPropertyName("price_sell")]
    public int? PriceSell { get; set; }

    [JsonPropertyName("slots")]
    public int Slots { get; set; }

    [JsonPropertyName("cardillustname")]
    public string? CardIllustName { get; set; }

    [JsonPropertyName("fix")]
    public string? Fix { get; set; }

    [JsonPropertyName("fixname")]
    public string? FixName { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("item_text")]
    public string? ItemText { get; set; }

    [JsonPropertyName("equip_jobs_text")]
    public string? EquipJobsText { get; set; }

    [JsonPropertyName("extra_info")]
    public string? ExtraInfo { get; set; }

    /// <summary>
    /// Get formatted effect text (single line)
    /// </summary>
    public string? GetShortEffect()
    {
        if (string.IsNullOrEmpty(ItemText))
            return null;

        // Get first line or first sentence
        var lines = ItemText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
        {
            var firstLine = lines[0].Trim();
            if (firstLine.Length > 80)
            {
                return firstLine.Substring(0, 77) + "...";
            }
            return firstLine;
        }
        return null;
    }

    /// <summary>
    /// Get item type display name in Korean
    /// </summary>
    public string GetTypeDisplayName()
    {
        return Type switch
        {
            0 => "힐링 아이템",
            2 => "사용 아이템",
            3 => "기타 아이템",
            4 => "무기",
            5 => "방어구",
            6 => "카드",
            7 => "펫 알",
            8 => "펫 장비",
            10 => "화살/탄환",
            11 => "사용 아이템",
            12 => "쉐도우 장비",
            _ => $"타입 {Type}"
        };
    }

    /// <summary>
    /// Get formatted weight (divide by 10 for actual weight)
    /// </summary>
    public string GetFormattedWeight()
    {
        return (Weight / 10.0).ToString("0.#");
    }

    /// <summary>
    /// Get formatted NPC buy price
    /// </summary>
    public string GetFormattedNpcBuyPrice()
    {
        if (PriceBuy == null || PriceBuy == 0)
            return "-";
        return PriceBuy.Value.ToString("N0") + " z";
    }

    /// <summary>
    /// Get formatted NPC sell price
    /// </summary>
    public string GetFormattedNpcSellPrice()
    {
        if (PriceSell == null || PriceSell == 0)
            return "-";
        return PriceSell.Value.ToString("N0") + " z";
    }

    // Display properties for DataGridView binding
    public string TypeDisplay => GetTypeDisplayName();
    public string WeightDisplay => GetFormattedWeight();
    public string NpcBuyPrice => GetFormattedNpcBuyPrice();
}
