using System.Diagnostics;
using System.Text.Json;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Service for managing the complete item index from kafra.kr API
/// Provides efficient name-to-ID lookup and data integrity management
/// </summary>
public class ItemIndexService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private const string BaseUrl = "http://api.kafra.kr/KRO";
    private const int ItemsPerPage = 50;
    private const int MaxPagesPerCategory = 500; // Safety limit (50*500=25000 items max)

    // In-memory index
    private Dictionary<int, KafraItemDto> _itemsById = new();
    private Dictionary<string, int> _idByScreenName = new(StringComparer.OrdinalIgnoreCase);
    private ItemIndexMetadata _metadata = new();
    private readonly object _indexLock = new();

    // State
    private bool _isLoaded = false;
    private bool _isLoading = false;

    public ItemIndexService(string? cacheDirectory = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        var dataDir = cacheDirectory ?? Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        _cacheFilePath = Path.Combine(dataDir, "ItemIndex.json");
    }

    #region Properties

    public bool IsLoaded => _isLoaded;
    public bool IsLoading => _isLoading;
    public int TotalCount => _metadata.TotalCount;
    public DateTime? LastUpdated => _isLoaded ? _metadata.UpdatedAt : null;
    public string CacheFilePath => _cacheFilePath;

    public IReadOnlyDictionary<int, CategoryMeta> Categories => _metadata.Categories;

    #endregion

    #region Public Methods

    /// <summary>
    /// Load index from cache file if exists
    /// </summary>
    public async Task<bool> LoadFromCacheAsync()
    {
        if (!File.Exists(_cacheFilePath))
        {
            Debug.WriteLine("[ItemIndexService] Cache file not found");
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_cacheFilePath);
            var indexFile = JsonSerializer.Deserialize<ItemIndexFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (indexFile == null || !indexFile.Metadata.Validate())
            {
                Debug.WriteLine("[ItemIndexService] Cache file invalid or corrupted");
                return false;
            }

            lock (_indexLock)
            {
                _metadata = indexFile.Metadata;
                _itemsById.Clear();
                _idByScreenName.Clear();

                // Convert string keys to int keys
                foreach (var kvp in indexFile.Items)
                {
                    if (int.TryParse(kvp.Key, out var id))
                    {
                        _itemsById[id] = kvp.Value;
                        if (!string.IsNullOrEmpty(kvp.Value.ScreenName))
                        {
                            _idByScreenName[kvp.Value.ScreenName] = kvp.Value.Id;
                        }
                    }
                }

                _isLoaded = true;
            }

            Debug.WriteLine($"[ItemIndexService] Loaded {_metadata.TotalCount} items from cache");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemIndexService] Load cache error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fetch all items from "전체" (All) category using type=999
    /// Then classify by type field locally
    /// </summary>
    public async Task<bool> RebuildIndexAsync(IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_isLoading)
        {
            Debug.WriteLine("[ItemIndexService] Already loading");
            return false;
        }

        _isLoading = true;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progress?.Report(new IndexProgress
            {
                Phase = "전체(999) 카테고리 수집 시작...",
                ItemsCollected = 0
            });

            // Fetch ALL items from type=999 (전체/All category)
            var allItems = await FetchCategoryAsync(999, progress, cancellationToken);

            if (allItems.Count == 0)
            {
                Debug.WriteLine("[ItemIndexService] No items fetched from type=999");
                progress?.Report(new IndexProgress { Phase = "아이템을 찾을 수 없습니다", HasError = true });
                return false;
            }

            // Build index dictionary (ID-based deduplication)
            var newItems = new Dictionary<int, KafraItemDto>();
            foreach (var item in allItems)
            {
                newItems[item.Id] = item;
            }

            // Calculate category counts from type field
            var categoryCounts = newItems.Values
                .GroupBy(i => i.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            // Build metadata
            var newMetadata = new ItemIndexMetadata
            {
                Version = "1.0.0",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TotalCount = newItems.Count
            };

            foreach (var (type, count) in categoryCounts)
            {
                newMetadata.Categories[type] = new CategoryMeta
                {
                    Name = ItemTypes.GetTypeName(type),
                    Count = count,
                    MaxPages = (count + ItemsPerPage - 1) / ItemsPerPage
                };
                Debug.WriteLine($"[ItemIndexService] Type {type} ({ItemTypes.GetTypeName(type)}): {count} items");
            }

            if (!newMetadata.Validate())
            {
                Debug.WriteLine("[ItemIndexService] Metadata validation failed");
                return false;
            }

            // Apply to in-memory index
            lock (_indexLock)
            {
                _itemsById = newItems;
                _metadata = newMetadata;
                _idByScreenName.Clear();

                foreach (var item in _itemsById.Values)
                {
                    if (!string.IsNullOrEmpty(item.ScreenName))
                    {
                        _idByScreenName[item.ScreenName] = item.Id;
                    }
                }

                _isLoaded = true;
            }

            await SaveToCacheAsync();

            stopwatch.Stop();
            Debug.WriteLine($"[ItemIndexService] Index rebuilt: {newItems.Count} items in {stopwatch.Elapsed.TotalSeconds:F1}s");

            progress?.Report(new IndexProgress
            {
                Phase = "완료",
                IsComplete = true,
                ItemsCollected = newItems.Count,
                CategoryIndex = categoryCounts.Count,
                TotalCategories = categoryCounts.Count
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[ItemIndexService] Rebuild cancelled");
            progress?.Report(new IndexProgress { Phase = "취소됨", IsCancelled = true });
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemIndexService] Rebuild error: {ex.Message}");
            progress?.Report(new IndexProgress { Phase = $"오류: {ex.Message}", HasError = true });
            return false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Get item by ID
    /// </summary>
    public KafraItemDto? GetItemById(int id)
    {
        lock (_indexLock)
        {
            return _itemsById.TryGetValue(id, out var item) ? item : null;
        }
    }

    /// <summary>
    /// Get item by screen name (case-insensitive)
    /// </summary>
    public KafraItemDto? GetItemByName(string screenName)
    {
        if (string.IsNullOrEmpty(screenName)) return null;

        lock (_indexLock)
        {
            if (_idByScreenName.TryGetValue(screenName, out var id))
            {
                return _itemsById.TryGetValue(id, out var item) ? item : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Get all screen names for autocomplete
    /// </summary>
    public IReadOnlyList<string> GetAllScreenNames()
    {
        if (!_isLoaded) return Array.Empty<string>();

        lock (_indexLock)
        {
            return _idByScreenName.Keys.ToList();
        }
    }

    /// <summary>
    /// Search items by partial name match
    /// </summary>
    public List<KafraItemDto> SearchByName(string searchTerm, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return new List<KafraItemDto>();

        var results = new List<KafraItemDto>();
        var searchLower = searchTerm.ToLowerInvariant();

        lock (_indexLock)
        {
            foreach (var item in _itemsById.Values)
            {
                if (results.Count >= maxResults) break;

                if (item.ScreenName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                    item.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                {
                    results.Add(item);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Get items by type
    /// </summary>
    public List<KafraItemDto> GetItemsByType(int itemType)
    {
        lock (_indexLock)
        {
            return _itemsById.Values.Where(i => i.Type == itemType).ToList();
        }
    }

    /// <summary>
    /// Count items matching the search criteria (for pagination).
    /// </summary>
    public int CountItems(string searchTerm, int itemType = 999)
        => CountItems(searchTerm, new HashSet<int> { itemType });

    /// <summary>
    /// Count items matching the search criteria with multiple types (for pagination).
    /// </summary>
    public int CountItems(string searchTerm, HashSet<int> itemTypes)
    {
        var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
        var allTypes = itemTypes.Contains(999);
        var count = 0;

        lock (_indexLock)
        {
            foreach (var item in _itemsById.Values)
            {
                // Type filter (999 = all types)
                if (!allTypes && !itemTypes.Contains(item.Type))
                    continue;

                // Name filter (if search term provided)
                if (hasSearchTerm)
                {
                    if (item.ScreenName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) != true &&
                        item.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Search items with type filtering, name matching, and pagination.
    /// </summary>
    public List<KafraItem> SearchItems(string searchTerm, int itemType = 999, int skip = 0, int take = 100)
        => SearchItems(searchTerm, new HashSet<int> { itemType }, skip, take);

    /// <summary>
    /// Search items with multiple type filtering, name matching, and pagination.
    /// Returns KafraItem list for UI binding compatibility.
    /// </summary>
    public List<KafraItem> SearchItems(string searchTerm, HashSet<int> itemTypes, int skip = 0, int take = 100)
    {
        var results = new List<KafraItem>();
        var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
        var allTypes = itemTypes.Contains(999);
        var skipped = 0;

        lock (_indexLock)
        {
            foreach (var item in _itemsById.Values)
            {
                if (results.Count >= take) break;

                // Type filter (999 = all types)
                if (!allTypes && !itemTypes.Contains(item.Type))
                    continue;

                // Name filter (if search term provided)
                if (hasSearchTerm)
                {
                    if (item.ScreenName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) != true &&
                        item.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }
                }

                // Skip for pagination
                if (skipped < skip)
                {
                    skipped++;
                    continue;
                }

                results.Add(item.ToKafraItem());
            }
        }

        return results;
    }

    /// <summary>
    /// Check if cache needs refresh (older than specified hours)
    /// </summary>
    public bool IsCacheStale(int maxAgeHours = 168) // 7 days default
    {
        if (!_isLoaded) return true;
        return (DateTime.UtcNow - _metadata.UpdatedAt).TotalHours > maxAgeHours;
    }

    #endregion

    #region Private Methods

    private async Task<List<KafraItemDto>> FetchCategoryAsync(
        int itemType,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<KafraItemDto>();
        var page = 1;

        while (page <= MaxPagesPerCategory)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageItems = await FetchPageAsync(itemType, page);

            if (pageItems == null || pageItems.Count == 0)
            {
                break; // No more items
            }

            items.AddRange(pageItems);
            page++;

            // Small delay to avoid rate limiting
            await Task.Delay(50, cancellationToken);

            // Report progress every 5 pages for better user feedback
            if (page % 5 == 0 || items.Count % 500 == 0)
            {
                progress?.Report(new IndexProgress
                {
                    Phase = $"전체 아이템 수집 중 ({items.Count:N0}개)",
                    CurrentCategory = ItemTypes.GetTypeName(itemType),
                    CurrentPage = page,
                    ItemsCollected = items.Count
                });
            }
        }

        return items;
    }

    private async Task<List<KafraItemDto>?> FetchPageAsync(int itemType, int page)
    {
        try
        {
            // API: /KRO/{type}/{page}/itemdetail_name.json?perPage=50
            var url = $"{BaseUrl}/{itemType}/{page}/itemdetail_name.json?perPage={ItemsPerPage}";

            Debug.WriteLine($"[ItemIndexService] Fetching: {url}");
            var response = await _httpClient.GetStringAsync(url);

            var items = JsonSerializer.Deserialize<List<KafraItemRaw>>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null) return null;

            // Convert to DTO (handle nullable fields from API)
            return items.Select(item =>
            {
                var itemType = item.Type ?? 0;
                var itemText = NormalizeLineBreaks(item.ItemText);
                return new KafraItemDto
                {
                    Id = item.ItemConst,
                    Name = item.Name,
                    ScreenName = item.ScreenName,
                    Type = itemType,
                    Slots = item.Slots ?? 0,
                    Weight = item.Weight ?? 0,
                    PriceBuy = item.PriceBuy,
                    PriceSell = item.PriceSell,
                    ItemText = itemText,
                    EquipJobsText = item.EquipJobsText,
                    Details = ItemTextParser.Parse(itemText, itemType)
                };
            }).ToList();
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[ItemIndexService] HTTP error for type {itemType} page {page}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemIndexService] Fetch error for type {itemType} page {page}: {ex.Message}");
            return null;
        }
    }

    private async Task SaveToCacheAsync()
    {
        try
        {
            // Convert int keys to string keys for JSON serialization
            var stringKeyItems = new Dictionary<string, KafraItemDto>();
            foreach (var kvp in _itemsById)
            {
                stringKeyItems[kvp.Key.ToString()] = kvp.Value;
            }

            var indexFile = new ItemIndexFile
            {
                Metadata = _metadata,
                Items = stringKeyItems
            };

            var json = JsonSerializer.Serialize(indexFile, new JsonSerializerOptions
            {
                WriteIndented = false, // Compact for smaller file size
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await File.WriteAllTextAsync(_cacheFilePath, json);
            Debug.WriteLine($"[ItemIndexService] Saved cache: {_cacheFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemIndexService] Save cache error: {ex.Message}");
        }
    }

    private static string? NormalizeLineBreaks(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Remove color codes like ^ffffff_, ^000000
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\^[0-9a-fA-F]{6}_?", "");

        // Normalize line breaks
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        return text.Trim();
    }

    #endregion

    #region ID Range Scan (for missing items like weapons)

    /// <summary>
    /// Scan a range of item IDs directly using /KRO/{id}/item.json endpoint
    /// This bypasses the itemdetail_name.json API limitation for certain item types
    /// </summary>
    public async Task<int> ScanIdRangeAsync(
        int startId,
        int endId,
        IProgress<IndexProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_isLoaded)
        {
            await LoadFromCacheAsync();
        }

        var newItemsCount = 0;
        var scannedCount = 0;
        var totalIds = endId - startId + 1;
        var semaphore = new SemaphoreSlim(10); // Max 10 concurrent requests

        progress?.Report(new IndexProgress
        {
            Phase = $"ID {startId}-{endId} 범위 스캔 중...",
            ItemsCollected = 0,
            TotalCategories = totalIds
        });

        var tasks = new List<Task>();

        for (int id = startId; id <= endId; id++)
        {
            if (ct.IsCancellationRequested) break;

            // Skip if already in index
            lock (_indexLock)
            {
                if (_itemsById.ContainsKey(id))
                {
                    Interlocked.Increment(ref scannedCount);
                    continue;
                }
            }

            var currentId = id;
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var item = await FetchItemByIdAsync(currentId);
                    if (item != null)
                    {
                        lock (_indexLock)
                        {
                            if (!_itemsById.ContainsKey(item.Id))
                            {
                                _itemsById[item.Id] = item;
                                if (!string.IsNullOrEmpty(item.ScreenName))
                                {
                                    _idByScreenName[item.ScreenName] = item.Id;
                                }
                                Interlocked.Increment(ref newItemsCount);
                            }
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                    var current = Interlocked.Increment(ref scannedCount);
                    if (current % 100 == 0)
                    {
                        progress?.Report(new IndexProgress
                        {
                            Phase = $"ID 스캔 중... ({current}/{totalIds})",
                            ItemsCollected = newItemsCount,
                            CategoryIndex = current,
                            TotalCategories = totalIds
                        });
                    }
                }
            }, ct));

            // Process in batches of 100
            if (tasks.Count >= 100)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        // Wait for remaining tasks
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        // Update metadata
        if (newItemsCount > 0)
        {
            _metadata.TotalCount = _itemsById.Count;
            _metadata.UpdatedAt = DateTime.UtcNow;

            // Recalculate category counts
            var categoryCounts = _itemsById.Values
                .GroupBy(i => i.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var kvp in categoryCounts)
            {
                if (!_metadata.Categories.ContainsKey(kvp.Key))
                {
                    _metadata.Categories[kvp.Key] = new CategoryMeta
                    {
                        Name = ItemTypes.GetTypeName(kvp.Key),
                        Count = kvp.Value
                    };
                }
                else
                {
                    _metadata.Categories[kvp.Key].Count = kvp.Value;
                }
            }

            await SaveToCacheAsync();
        }

        progress?.Report(new IndexProgress
        {
            Phase = $"스캔 완료: {newItemsCount}개 새 아이템 발견",
            ItemsCollected = newItemsCount,
            IsComplete = true,
            TotalCategories = totalIds,
            CategoryIndex = totalIds
        });

        return newItemsCount;
    }

    /// <summary>
    /// Fetch a single item by ID using /KRO/{id}/item.json endpoint
    /// </summary>
    private async Task<KafraItemDto?> FetchItemByIdAsync(int itemId)
    {
        try
        {
            var url = $"{BaseUrl}/{itemId}/item.json";
            var response = await _httpClient.GetStringAsync(url);

            var items = JsonSerializer.Deserialize<List<KafraItemRaw>>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items != null && items.Count > 0)
            {
                var item = items[0];
                var itemType = item.Type ?? 0;
                var itemText = NormalizeLineBreaks(item.ItemText);
                return new KafraItemDto
                {
                    Id = item.ItemConst,
                    Name = item.Name,
                    ScreenName = item.ScreenName,
                    Type = itemType,
                    Slots = item.Slots ?? 0,
                    Weight = item.Weight ?? 0,
                    PriceBuy = item.PriceBuy,
                    PriceSell = item.PriceSell,
                    ItemText = itemText,
                    EquipJobsText = item.EquipJobsText,
                    Details = ItemTextParser.Parse(itemText, itemType)
                };
            }
        }
        catch
        {
            // Item doesn't exist or error - silently skip
        }

        return null;
    }

    /// <summary>
    /// Scan common weapon ID ranges (1101-2999) to collect missing weapons
    /// </summary>
    public async Task<int> ScanWeaponsAsync(
        IProgress<IndexProgress>? progress = null,
        CancellationToken ct = default)
    {
        // RO weapon ID ranges: 1101-1999 (classic), 13000-14999 (new weapons)
        var totalFound = 0;

        progress?.Report(new IndexProgress { Phase = "무기 ID 범위 1101-1999 스캔 중..." });
        totalFound += await ScanIdRangeAsync(1101, 1999, progress, ct);

        if (!ct.IsCancellationRequested)
        {
            progress?.Report(new IndexProgress { Phase = "무기 ID 범위 13000-13999 스캔 중..." });
            totalFound += await ScanIdRangeAsync(13000, 13999, progress, ct);
        }

        return totalFound;
    }

    #endregion

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Progress information for index rebuild operation
/// </summary>
public class IndexProgress
{
    public string Phase { get; set; } = string.Empty;
    public string? CurrentCategory { get; set; }
    public int CategoryIndex { get; set; }
    public int TotalCategories { get; set; }
    public int CurrentPage { get; set; }
    public int ItemsCollected { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
    public bool HasError { get; set; }

    public double ProgressPercent => TotalCategories > 0
        ? (CategoryIndex * 100.0) / TotalCategories
        : 0;
}

/// <summary>
/// Internal class for API deserialization
/// Named differently to avoid conflict with KafraClient.KafraItem
/// </summary>
internal class KafraItemRaw
{
    [System.Text.Json.Serialization.JsonPropertyName("item_const")]
    public int ItemConst { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("screen_name")]
    public string? ScreenName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public int? Type { get; set; }  // Nullable - API returns null for some items in type=999

    [System.Text.Json.Serialization.JsonPropertyName("slots")]
    public int? Slots { get; set; }  // Nullable for safety

    [System.Text.Json.Serialization.JsonPropertyName("weight")]
    public int? Weight { get; set; }  // Nullable for safety

    [System.Text.Json.Serialization.JsonPropertyName("price_buy")]
    public int? PriceBuy { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("price_sell")]
    public int? PriceSell { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("item_text")]
    public string? ItemText { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("equip_jobs_text")]
    public string? EquipJobsText { get; set; }
}
