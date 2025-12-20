using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoMarketCrawler.Services;

/// <summary>
/// Database for enchant and card effect descriptions
/// Uses KafraClient for online lookup with local JSON as fallback
/// </summary>
public class EnchantDatabase
{
    private static EnchantDatabase? _instance;
    private static readonly object _lock = new();

    private readonly Dictionary<string, EnchantInfo> _enchants = new();
    private readonly Dictionary<string, CardInfo> _cards = new();
    private readonly Dictionary<string, string> _kafraCache = new();
    private readonly object _kafraCacheLock = new();
    private KafraClient? _kafraClient;
    private bool _isLoaded;
    private bool _kafraInitialized;

    public static EnchantDatabase Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new EnchantDatabase();
                }
            }
            return _instance;
        }
    }

    private EnchantDatabase()
    {
        LoadDatabase();
    }

    private void LoadDatabase()
    {
        if (_isLoaded) return;

        try
        {
            // Load from embedded resource
            var json = ResourceHelper.GetEnchantEffectsJson();

            if (!string.IsNullOrEmpty(json))
            {
                ParseJson(json);
                Debug.WriteLine("[EnchantDatabase] Loaded from embedded resource");
            }
            else
            {
                Debug.WriteLine("[EnchantDatabase] Embedded resource not found");
                LoadDefaultData();
            }

            _isLoaded = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnchantDatabase] Failed to load: {ex.Message}");
            LoadDefaultData();
            _isLoaded = true;
        }
    }

    private void ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse enchants
            if (root.TryGetProperty("enchants", out var enchantsElement))
            {
                foreach (var enchant in enchantsElement.EnumerateObject())
                {
                    var info = new EnchantInfo
                    {
                        Name = enchant.Name,
                        Effects = new Dictionary<string, string>()
                    };

                    if (enchant.Value.TryGetProperty("category", out var cat))
                        info.Category = cat.GetString();

                    if (enchant.Value.TryGetProperty("type", out var type))
                        info.Type = type.GetString();

                    if (enchant.Value.TryGetProperty("effects", out var effects))
                    {
                        foreach (var effect in effects.EnumerateObject())
                        {
                            info.Effects[effect.Name] = effect.Value.GetString() ?? "";
                        }
                    }

                    _enchants[enchant.Name] = info;
                }
            }

            // Parse cards
            if (root.TryGetProperty("cards", out var cardsElement))
            {
                foreach (var card in cardsElement.EnumerateObject())
                {
                    var info = new CardInfo
                    {
                        Name = card.Name
                    };

                    if (card.Value.TryGetProperty("slot", out var slot))
                        info.Slot = slot.GetString();

                    if (card.Value.TryGetProperty("effects", out var effects))
                        info.Effects = effects.GetString();

                    _cards[card.Name] = info;
                }
            }

            Debug.WriteLine($"[EnchantDatabase] Parsed {_enchants.Count} enchants, {_cards.Count} cards");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnchantDatabase] Parse error: {ex.Message}");
        }
    }

    private void LoadDefaultData()
    {
        // Minimal default data if JSON file is not found
        _enchants["투지"] = new EnchantInfo
        {
            Name = "투지",
            Category = "특수",
            Type = "armor",
            Effects = new Dictionary<string, string> { { "default", "물리 공격 시 데미지 +2%, ATK +15" } }
        };

        _enchants["마력"] = new EnchantInfo
        {
            Name = "마력",
            Category = "특수",
            Type = "armor",
            Effects = new Dictionary<string, string> { { "default", "마법 공격 시 데미지 +2%, MATK +15" } }
        };

        _cards["미노타우르스 카드"] = new CardInfo
        {
            Name = "미노타우르스 카드",
            Slot = "weapon",
            Effects = "대형 몬스터 +15% 물리 데미지"
        };

        Debug.WriteLine("[EnchantDatabase] Loaded default data");
    }

    /// <summary>
    /// Get enchant effect description from enchant name
    /// Handles level suffix like "설화 마력(마법등급) 5Lv"
    /// </summary>
    public string? GetEnchantEffect(string enchantName)
    {
        if (string.IsNullOrEmpty(enchantName)) return null;

        // Try exact match first
        if (_enchants.TryGetValue(enchantName, out var info))
        {
            return info.Effects.TryGetValue("default", out var effect) ? effect : info.Effects.Values.FirstOrDefault();
        }

        // Extract level from name like "설화 마력(마법등급) 5Lv" or "축복 STR 3"
        var levelMatch = Regex.Match(enchantName, @"(\d+)\s*(Lv|레벨)?$", RegexOptions.IgnoreCase);
        string? level = null;
        var baseName = enchantName;

        if (levelMatch.Success)
        {
            level = levelMatch.Groups[1].Value;
            baseName = enchantName.Substring(0, levelMatch.Index).Trim();
        }

        // Try base name match
        if (_enchants.TryGetValue(baseName, out info))
        {
            // Try to get level-specific effect
            if (level != null && info.Effects.TryGetValue(level, out var levelEffect))
            {
                return levelEffect;
            }

            // Fall back to default
            if (info.Effects.TryGetValue("default", out var defaultEffect))
            {
                return defaultEffect;
            }

            return info.Effects.Values.FirstOrDefault();
        }

        // Try partial match for skill enchants like "설화 마력(크로스 레인)"
        foreach (var kvp in _enchants)
        {
            if (baseName.Contains(kvp.Key) || kvp.Key.Contains(baseName))
            {
                if (level != null && kvp.Value.Effects.TryGetValue(level, out var levelEffect))
                {
                    return levelEffect;
                }
                if (kvp.Value.Effects.TryGetValue("default", out var defaultEffect))
                {
                    return defaultEffect;
                }
                return kvp.Value.Effects.Values.FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>
    /// Get card effect description from card name
    /// </summary>
    public string? GetCardEffect(string cardName)
    {
        if (string.IsNullOrEmpty(cardName)) return null;

        // Exact match
        if (_cards.TryGetValue(cardName, out var info))
        {
            return info.Effects;
        }

        // Try with/without "카드" suffix
        var withCard = cardName.EndsWith("카드") ? cardName : cardName + " 카드";
        var withoutCard = cardName.EndsWith("카드") ? cardName.Replace(" 카드", "").Replace("카드", "") : cardName;

        if (_cards.TryGetValue(withCard, out info))
        {
            return info.Effects;
        }

        // Partial match
        foreach (var kvp in _cards)
        {
            if (kvp.Key.Contains(withoutCard) || withoutCard.Contains(kvp.Key.Replace(" 카드", "")))
            {
                return kvp.Value.Effects;
            }
        }

        return null;
    }

    /// <summary>
    /// Get effect description for any slot info item (enchant or card)
    /// </summary>
    public string? GetSlotEffect(string slotName)
    {
        if (string.IsNullOrEmpty(slotName)) return null;

        // Check if it's a card (contains "카드")
        if (slotName.Contains("카드"))
        {
            return GetCardEffect(slotName);
        }

        // Otherwise try enchant
        return GetEnchantEffect(slotName);
    }

    /// <summary>
    /// Format slot info with effect description
    /// </summary>
    public string FormatSlotWithEffect(string slotName)
    {
        var effect = GetSlotEffect(slotName);
        if (string.IsNullOrEmpty(effect))
        {
            return slotName;
        }
        return $"{slotName} - {effect}";
    }

    public int EnchantCount => _enchants.Count;
    public int CardCount => _cards.Count;

    /// <summary>
    /// Initialize KafraClient for online enchant lookup
    /// </summary>
    public void InitializeKafraClient()
    {
        if (_kafraInitialized) return;

        try
        {
            _kafraClient = new KafraClient();
            _kafraInitialized = true;
            Debug.WriteLine("[EnchantDatabase] KafraClient initialized");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnchantDatabase] Failed to initialize KafraClient: {ex.Message}");
        }
    }

    /// <summary>
    /// Get enchant effect asynchronously from Kafra API with local fallback
    /// </summary>
    public async Task<string?> GetEnchantEffectAsync(string enchantName)
    {
        if (string.IsNullOrEmpty(enchantName)) return null;

        // Check Kafra cache first
        lock (_kafraCacheLock)
        {
            if (_kafraCache.TryGetValue(enchantName, out var cachedEffect))
            {
                return cachedEffect;
            }
        }

        // Try Kafra API
        if (_kafraClient != null)
        {
            try
            {
                var effect = await _kafraClient.GetItemEffectAsync(enchantName);
                if (!string.IsNullOrEmpty(effect))
                {
                    lock (_kafraCacheLock)
                    {
                        _kafraCache[enchantName] = effect;
                    }
                    return effect;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnchantDatabase] Kafra lookup failed for '{enchantName}': {ex.Message}");
            }
        }

        // Fall back to local database
        return GetEnchantEffect(enchantName);
    }

    /// <summary>
    /// Get slot effect asynchronously (enchant or card)
    /// Uses GetFullItemDataAsync for cards to ensure comprehensive search
    /// </summary>
    public async Task<string?> GetSlotEffectAsync(string slotName)
    {
        if (string.IsNullOrEmpty(slotName)) return null;

        Debug.WriteLine($"[EnchantDatabase] GetSlotEffectAsync: '{slotName}'");

        // Check if it's explicitly a card (contains "카드")
        bool isExplicitCard = slotName.Contains("카드");
        Debug.WriteLine($"[EnchantDatabase] IsExplicitCard: {isExplicitCard}");

        // Check Kafra cache first
        lock (_kafraCacheLock)
        {
            if (_kafraCache.TryGetValue(slotName, out var cachedEffect))
            {
                Debug.WriteLine($"[EnchantDatabase] Cache hit for '{slotName}'");
                return cachedEffect;
            }
        }

        // Try Kafra API for ALL slot items (both cards and enchants)
        // This ensures we get the most accurate effect description
        if (_kafraClient != null)
        {
            try
            {
                Debug.WriteLine($"[EnchantDatabase] Calling KafraClient for: '{slotName}'");

                // For cards, use GetFullItemDataAsync which has comprehensive search logic
                // (handles prefix/suffix removal and progressive search)
                if (isExplicitCard)
                {
                    Debug.WriteLine($"[EnchantDatabase] Using GetFullItemDataAsync for card: '{slotName}'");
                    var kafkaItem = await _kafraClient.GetFullItemDataAsync(slotName);
                    if (kafkaItem != null && !string.IsNullOrEmpty(kafkaItem.ItemText))
                    {
                        Debug.WriteLine($"[EnchantDatabase] Card effect found: {kafkaItem.ItemText.Length} chars");
                        lock (_kafraCacheLock)
                        {
                            _kafraCache[slotName] = kafkaItem.ItemText;
                        }
                        return kafkaItem.ItemText;
                    }
                    Debug.WriteLine($"[EnchantDatabase] No effect from GetFullItemDataAsync for card: '{slotName}'");
                }
                else
                {
                    // For enchants, use GetItemEffectAsync (simpler search)
                    var effect = await _kafraClient.GetItemEffectAsync(slotName);
                    if (!string.IsNullOrEmpty(effect))
                    {
                        Debug.WriteLine($"[EnchantDatabase] Enchant effect found: {effect.Length} chars");
                        lock (_kafraCacheLock)
                        {
                            _kafraCache[slotName] = effect;
                        }
                        return effect;
                    }
                    Debug.WriteLine($"[EnchantDatabase] No effect from Kafra for enchant: '{slotName}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnchantDatabase] Kafra lookup error: {ex.Message}");
            }
        }
        else
        {
            Debug.WriteLine("[EnchantDatabase] KafraClient is null!");
        }

        // Fallback to local database
        if (isExplicitCard)
        {
            return GetCardEffect(slotName);
        }

        return GetEnchantEffect(slotName);
    }

    /// <summary>
    /// Format slot info with effect description (async version)
    /// Returns just the slot name for list display
    /// </summary>
    public async Task<string> FormatSlotWithEffectAsync(string slotName)
    {
        // Pre-fetch the effect to cache it
        await GetSlotEffectAsync(slotName);
        // Return just the slot name for cleaner list display
        return slotName;
    }

    /// <summary>
    /// Get full effect text for tooltip display
    /// </summary>
    public async Task<string?> GetFullEffectTextAsync(string slotName)
    {
        return await GetSlotEffectAsync(slotName);
    }

    public void Dispose()
    {
        _kafraClient?.Dispose();
    }
}

public class EnchantInfo
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, string> Effects { get; set; } = new();
}

public class CardInfo
{
    public string Name { get; set; } = "";
    public string? Slot { get; set; }
    public string? Effects { get; set; }
}
