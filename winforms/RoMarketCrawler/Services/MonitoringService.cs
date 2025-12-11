using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services
{
    /// <summary>
    /// Service for managing item monitoring with persistent storage and auto-refresh
    /// </summary>
    public class MonitoringService : IDisposable
    {
        private readonly GnjoyClient _gnjoyClient;
        private readonly string _configFilePath;
        private MonitorConfig _config;
        private readonly Dictionary<string, MonitorResult> _results;
        private readonly object _lock = new();
        private bool _disposed;

        // Performance optimization: parallel processing with rate limiting
        private readonly SemaphoreSlim _refreshSemaphore = new(3); // Max 3 concurrent refreshes

        // Performance optimization: global price statistics cache with TTL
        private static readonly ConcurrentDictionary<string, (PriceStatistics? Stats, DateTime CachedAt)> _priceStatsCache = new();
        private const int PriceCacheTtlMinutes = 5;

        public MonitoringService(GnjoyClient gnjoyClient, string? dataDirectory = null)
        {
            _gnjoyClient = gnjoyClient;
            var dataDir = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            _configFilePath = Path.Combine(dataDir, "MonitorConfig.json");
            _config = new MonitorConfig();
            _results = new Dictionary<string, MonitorResult>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Current monitoring configuration
        /// </summary>
        public MonitorConfig Config => _config;

        /// <summary>
        /// Current monitoring results
        /// </summary>
        public IReadOnlyDictionary<string, MonitorResult> Results
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, MonitorResult>(_results);
                }
            }
        }

        /// <summary>
        /// Number of monitored items
        /// </summary>
        public int ItemCount => _config.Items.Count;

        /// <summary>
        /// Load configuration from persistent storage
        /// </summary>
        public async Task LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    Debug.WriteLine("[MonitoringService] No config file found, using defaults");
                    return;
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                var config = JsonSerializer.Deserialize<MonitorConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config != null)
                {
                    _config = config;
                    Debug.WriteLine($"[MonitoringService] Loaded {_config.Items.Count} items from config");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MonitoringService] Failed to load config: {ex.Message}");
            }
        }

        /// <summary>
        /// Save configuration to persistent storage
        /// </summary>
        public async Task SaveConfigAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_configFilePath, json);
                Debug.WriteLine($"[MonitoringService] Saved {_config.Items.Count} items to config");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MonitoringService] Failed to save config: {ex.Message}");
            }
        }

        /// <summary>
        /// Add an item to the monitoring list
        /// </summary>
        public async Task<bool> AddItemAsync(string itemName, int serverId = -1)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return false;

            itemName = itemName.Trim();

            // Check for duplicates
            if (_config.Items.Any(i => i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId))
            {
                Debug.WriteLine($"[MonitoringService] Item '{itemName}' already exists");
                return false;
            }

            var item = new MonitorItem
            {
                ItemName = itemName,
                ServerId = serverId,
                AddedAt = DateTime.Now
            };

            _config.Items.Add(item);
            await SaveConfigAsync();

            Debug.WriteLine($"[MonitoringService] Added item '{itemName}' (server={serverId})");
            return true;
        }

        /// <summary>
        /// Remove an item from the monitoring list
        /// </summary>
        public async Task<bool> RemoveItemAsync(string itemName, int serverId = -1)
        {
            var item = _config.Items.FirstOrDefault(i =>
                i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId);

            if (item == null)
                return false;

            _config.Items.Remove(item);

            lock (_lock)
            {
                var key = GetResultKey(itemName, serverId);
                _results.Remove(key);
            }

            await SaveConfigAsync();
            Debug.WriteLine($"[MonitoringService] Removed item '{itemName}'");
            return true;
        }

        /// <summary>
        /// Update the server ID of an existing monitored item
        /// </summary>
        public async Task<bool> UpdateItemServerAsync(string itemName, int oldServerId, int newServerId)
        {
            var item = _config.Items.FirstOrDefault(i =>
                i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == oldServerId);

            if (item == null)
                return false;

            // Remove old result from cache
            lock (_lock)
            {
                var oldKey = GetResultKey(itemName, oldServerId);
                _results.Remove(oldKey);
            }

            // Update the server ID
            item.ServerId = newServerId;

            await SaveConfigAsync();
            Debug.WriteLine($"[MonitoringService] Updated item '{itemName}' server from {oldServerId} to {newServerId}");
            return true;
        }

        /// <summary>
        /// Rename a monitored item
        /// </summary>
        public async Task<bool> RenameItemAsync(string oldName, int serverId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            var item = _config.Items.FirstOrDefault(i =>
                i.ItemName.Equals(oldName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId);

            if (item == null)
                return false;

            // Check if new name already exists for same server
            var exists = _config.Items.Any(i =>
                i.ItemName.Equals(newName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId && i != item);

            if (exists)
            {
                Debug.WriteLine($"[MonitoringService] Cannot rename - item '{newName}' already exists for server {serverId}");
                return false;
            }

            // Remove old result from cache
            lock (_lock)
            {
                var oldKey = GetResultKey(oldName, serverId);
                _results.Remove(oldKey);
            }

            // Update the item name
            item.ItemName = newName;

            await SaveConfigAsync();
            Debug.WriteLine($"[MonitoringService] Renamed item from '{oldName}' to '{newName}' on server {serverId}");
            return true;
        }

        /// <summary>
        /// Clear all items from the monitoring list
        /// </summary>
        public async Task ClearAllItemsAsync()
        {
            _config.Items.Clear();

            lock (_lock)
            {
                _results.Clear();
            }

            await SaveConfigAsync();
            Debug.WriteLine("[MonitoringService] Cleared all monitoring items");
        }

        /// <summary>
        /// Update refresh interval setting
        /// </summary>
        public async Task SetRefreshIntervalAsync(int seconds)
        {
            _config.RefreshIntervalSeconds = Math.Max(0, seconds);
            await SaveConfigAsync();
            Debug.WriteLine($"[MonitoringService] Set refresh interval to {seconds} seconds");
        }


        /// <summary>
        /// Refresh all monitored items using parallel processing for improved performance
        /// </summary>
        public async Task RefreshAllAsync(
            IProgress<MonitorProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var items = _config.Items.ToList();
            var total = items.Count;

            if (total == 0)
            {
                progress?.Report(new MonitorProgress
                {
                    Phase = "완료",
                    CurrentItem = "",
                    CurrentIndex = 0,
                    TotalItems = 0,
                    ProgressPercent = 100
                });
                return;
            }

            Debug.WriteLine($"[MonitoringService] Refreshing {total} items with parallel processing (max 3 concurrent)...");

            // Track completion count for progress reporting
            int completedCount = 0;

            progress?.Report(new MonitorProgress
            {
                Phase = "병렬 조회 중",
                CurrentItem = $"{total}개 아이템 처리 중...",
                CurrentIndex = 0,
                TotalItems = total,
                ProgressPercent = 0
            });

            // Create tasks for parallel execution with semaphore rate limiting
            var tasks = items.Select(async item =>
            {
                await _refreshSemaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await RefreshItemAsync(item, cancellationToken);

                    lock (_lock)
                    {
                        var key = GetResultKey(item.ItemName, item.ServerId);
                        _results[key] = result;
                    }

                    // Update progress after each item completes
                    var currentCompleted = Interlocked.Increment(ref completedCount);
                    progress?.Report(new MonitorProgress
                    {
                        Phase = "병렬 조회 중",
                        CurrentItem = item.ItemName,
                        CurrentIndex = currentCompleted,
                        TotalItems = total,
                        ProgressPercent = (int)((double)currentCompleted / total * 100)
                    });

                    return result;
                }
                finally
                {
                    _refreshSemaphore.Release();
                }
            }).ToList();

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            progress?.Report(new MonitorProgress
            {
                Phase = "완료",
                CurrentItem = "",
                CurrentIndex = total,
                TotalItems = total,
                ProgressPercent = 100
            });

            Debug.WriteLine($"[MonitoringService] Parallel refresh completed for {total} items");
        }

        /// <summary>
        /// Refresh a single monitored item
        /// </summary>
        private async Task<MonitorResult> RefreshItemAsync(MonitorItem item, CancellationToken cancellationToken)
        {
            var result = new MonitorResult
            {
                Item = item,
                LastRefreshed = DateTime.Now
            };

            try
            {
                Debug.WriteLine($"[MonitoringService] RefreshItemAsync: item='{item.ItemName}', ServerId={item.ServerId}");

                // Fetch current deals from ALL pages (not just page 1)
                // GNJOY returns 10 items per page, and expensive items like EPIC/RARE may be on later pages
                // Use maxPages: 10 to match deal search tab (up to 100 items)
                var allDeals = await _gnjoyClient.SearchAllItemDealsAsync(
                    item.ItemName,
                    item.ServerId,
                    maxPages: 10,  // Up to 100 items (same as deal search tab)
                    cancellationToken);

                // Filter to only include selling shops (exclude buying shops)
                var deals = allDeals.Where(d => d.DealType == "sale").ToList();
                Debug.WriteLine($"[MonitoringService] Filtered: {allDeals.Count} total -> {deals.Count} sale only");

                result.Deals = deals;

                // Fetch price statistics for EACH unique item name in the deals
                // This is necessary because a single search can return different item variants
                // (e.g., "포링 선글래스" search returns both base item and enhanced "+" version)
                var priceServerId = item.ServerId == -1 ? 1 : item.ServerId;
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "price_lookup.log");
                File.AppendAllText(logPath, $"\n[{DateTime.Now:HH:mm:ss}] === Processing monitor item: '{item.ItemName}' ===\n");

                // Group deals by actual item name for processing
                var dealsByItemName = deals.GroupBy(d => d.ItemName ?? "").ToList();

                // Cache for statistics to avoid duplicate lookups
                var statsCache = new Dictionary<string, PriceStatistics?>(StringComparer.OrdinalIgnoreCase);

                foreach (var dealGroup in dealsByItemName)
                {
                    var dealItemName = dealGroup.Key;
                    var sampleDeal = dealGroup.First();
                    var itemId = sampleDeal.GetEffectiveItemId();
                    var baseItemName = GetBaseSearchName(dealItemName);

                    // Check if this item has any grade at all
                    var hasGrade = !string.IsNullOrEmpty(sampleDeal.Grade);

                    File.AppendAllText(logPath, $"\n  --- Fetching stats for deal item: '{dealItemName}' (ItemId: {itemId?.ToString() ?? "null"}, Base: '{baseItemName}', Grade: '{sampleDeal.Grade ?? "none"}') ---\n");

                    // Skip stats for items with ANY grade (GNJOY API doesn't provide grade-specific prices)
                    // Grade items have unreliable price history due to grade-specific pricing
                    if (hasGrade)
                    {
                        File.AppendAllText(logPath, $"    Skipping stats - item has grade '{sampleDeal.Grade}'\n");
                        continue;
                    }

                    // Try to get cached statistics first
                    if (statsCache.TryGetValue(dealItemName, out var cachedStats))
                    {
                        foreach (var deal in dealGroup)
                        {
                            deal.ApplyStatistics(cachedStats);
                        }
                        continue;
                    }

                    // Build search name including refine level if available
                    // GNJOY quoteSearch.asp supports refine-prefixed searches like "10매드니스"
                    string searchName;
                    var dealRefine = sampleDeal.Refine;

                    // Check if dealItemName already has a refine prefix (e.g., "10매드니스 브레스 슈즈")
                    // This happens when items are grouped by Form1.cs GetBaseItemName with refine prefix
                    var refinePatternMatch = System.Text.RegularExpressions.Regex.Match(dealItemName, @"^(\d+)([가-힣].*)$");
                    bool alreadyHasRefinePrefix = refinePatternMatch.Success;

                    if (alreadyHasRefinePrefix)
                    {
                        // dealItemName already contains refine prefix (e.g., "10매드니스 브레스 슈즈 쉐도우")
                        // Use it as-is for the search
                        searchName = dealItemName;
                        File.AppendAllText(logPath, $"    Search name: '{searchName}' (already has refine prefix)\n");
                    }
                    else if (dealRefine.HasValue && dealRefine.Value > 0)
                    {
                        // Item doesn't have refine prefix but has refine value
                        // Add refine level to search name for more accurate price statistics
                        searchName = $"{dealRefine.Value}{dealItemName}";
                        File.AppendAllText(logPath, $"    Search name: '{searchName}' (refine-prefixed from '{dealItemName}', Refine={dealRefine})\n");
                    }
                    else
                    {
                        // No refine or zero, use base item name
                        searchName = GetBaseSearchName(dealItemName);
                        File.AppendAllText(logPath, $"    Search name: '{searchName}' (base from '{dealItemName}')\n");
                    }

                    // Fetch price list using search name
                    var priceListItems = await _gnjoyClient.SearchPriceListAsync(
                        searchName,
                        priceServerId,
                        cancellationToken);

                    File.AppendAllText(logPath, $"    Price list returned {priceListItems.Count} items:\n");
                    foreach (var pli in priceListItems)
                    {
                        File.AppendAllText(logPath, $"      - '{pli.ExactItemName}'\n");
                    }

                    PriceStatistics? stats = null;

                    if (priceListItems.Count > 0)
                    {
                        // Find the best matching price list item
                        var priceListMatch = FindBestPriceListMatch(dealItemName, priceListItems);

                        if (priceListMatch != null)
                        {
                            File.AppendAllText(logPath, $"    MATCH: '{priceListMatch}'\n");
                            // Use cached price statistics to reduce API calls
                            stats = await GetCachedPriceStatisticsAsync(
                                priceListMatch,
                                priceServerId,
                                cancellationToken);
                            File.AppendAllText(logPath, $"    RESULT: Yesterday={stats?.YesterdayAvgPrice:N0}, Week={stats?.Week7AvgPrice:N0}\n");
                        }
                        else
                        {
                            File.AppendAllText(logPath, $"    NO MATCH found in price list\n");
                        }
                    }

                    // Cache and apply statistics
                    statsCache[dealItemName] = stats;
                    foreach (var deal in dealGroup)
                    {
                        deal.ApplyStatistics(stats);
                    }
                }

                // Store the first group's statistics as the overall result statistics
                result.Statistics = statsCache.Values.FirstOrDefault(s => s != null);

                Debug.WriteLine($"[MonitoringService] Refreshed '{item.ItemName}': {deals.Count} deals across {dealsByItemName.Count} item names");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                Debug.WriteLine($"[MonitoringService] Failed to refresh '{item.ItemName}': {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Get result for a specific item
        /// </summary>
        public MonitorResult? GetResult(string itemName, int serverId = -1)
        {
            lock (_lock)
            {
                var key = GetResultKey(itemName, serverId);
                return _results.TryGetValue(key, out var result) ? result : null;
            }
        }

        /// <summary>
        /// Get all results with good deals (below both averages)
        /// </summary>
        public List<MonitorResult> GetGoodDeals()
        {
            lock (_lock)
            {
                return _results.Values.Where(r => r.IsGoodDeal).ToList();
            }
        }

        /// <summary>
        /// Find the best matching item name in the price list for a given deal item name.
        /// Handles various formats: base items, enhanced (+), slot indicators ([N]), refine levels (+10), etc.
        /// </summary>
        private static string? FindBestPriceListMatch(string dealItemName, List<PriceListItem> priceListItems)
        {
            var trimmedName = dealItemName?.Trim() ?? "";
            if (string.IsNullOrEmpty(trimmedName)) return null;

            // Extract base name (without refine level prefix) for matching
            var baseName = GetBaseSearchName(trimmedName);
            var hasRefinePrefix = baseName != trimmedName;

            // Priority 1: Exact match with original name
            var exactMatch = priceListItems.FirstOrDefault(p =>
                string.Equals(p.ExactItemName?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null) return exactMatch.ExactItemName;

            // Priority 2: For refined items (+10, +12, etc.), match using base name
            if (hasRefinePrefix)
            {
                // Try exact match with base name
                var baseExactMatch = priceListItems.FirstOrDefault(p =>
                    string.Equals(p.ExactItemName?.Trim(), baseName, StringComparison.OrdinalIgnoreCase));
                if (baseExactMatch != null) return baseExactMatch.ExactItemName;

                // Try base name + slot indicator (e.g., "퓨리어스 임팩트" matches "퓨리어스 임팩트[2]")
                var baseWithSlot = priceListItems.FirstOrDefault(p =>
                    p.ExactItemName != null &&
                    p.ExactItemName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                    p.ExactItemName.Length > baseName.Length &&
                    p.ExactItemName[baseName.Length] == '[');
                if (baseWithSlot != null) return baseWithSlot.ExactItemName;
            }

            // Priority 3: Deal item name + slot indicator (e.g., "포링 선글래스+" matches "포링 선글래스+[1]")
            if (trimmedName.EndsWith("+"))
            {
                var matchWithSlot = priceListItems.FirstOrDefault(p =>
                    p.ExactItemName != null &&
                    p.ExactItemName.StartsWith(trimmedName, StringComparison.OrdinalIgnoreCase) &&
                    p.ExactItemName.Length > trimmedName.Length &&
                    p.ExactItemName[trimmedName.Length] == '[');
                if (matchWithSlot != null) return matchWithSlot.ExactItemName;
            }

            // Priority 4: Base item + slot indicator (e.g., "퓨리어스 임팩트" matches "퓨리어스 임팩트[2]")
            if (!trimmedName.EndsWith("+") && !trimmedName.EndsWith("]"))
            {
                var matchWithSlot = priceListItems.FirstOrDefault(p =>
                    p.ExactItemName != null &&
                    p.ExactItemName.StartsWith(trimmedName, StringComparison.OrdinalIgnoreCase) &&
                    p.ExactItemName.Length > trimmedName.Length &&
                    p.ExactItemName[trimmedName.Length] == '[');
                if (matchWithSlot != null) return matchWithSlot.ExactItemName;
            }

            // Priority 5: First item that starts with the deal name (for other variations)
            var startsWithMatch = priceListItems.FirstOrDefault(p =>
                p.ExactItemName != null &&
                p.ExactItemName.StartsWith(trimmedName, StringComparison.OrdinalIgnoreCase));
            if (startsWithMatch != null) return startsWithMatch.ExactItemName;

            // Priority 6: First item that starts with base name (fallback for refined items)
            if (hasRefinePrefix)
            {
                var baseStartsWithMatch = priceListItems.FirstOrDefault(p =>
                    p.ExactItemName != null &&
                    p.ExactItemName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
                if (baseStartsWithMatch != null) return baseStartsWithMatch.ExactItemName;
            }

            return null;
        }

        /// <summary>
        /// Extract base item name for price list search by removing refine level prefix.
        /// Examples: "+10퓨리어스 임팩트" → "퓨리어스 임팩트", "+12some item" → "some item"
        /// Items without refine prefix are returned as-is.
        /// </summary>
        private static string GetBaseSearchName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return itemName;

            var trimmed = itemName.Trim();

            // Check for refine level prefix pattern: +N where N is 1-15
            // Pattern: starts with '+' followed by digits, then the actual item name
            if (trimmed.StartsWith("+"))
            {
                // Find where the digits end
                int i = 1;
                while (i < trimmed.Length && char.IsDigit(trimmed[i]))
                {
                    i++;
                }

                // If we found at least one digit and there's more content after
                if (i > 1 && i < trimmed.Length)
                {
                    // Extract the base name (everything after +N)
                    return trimmed.Substring(i).TrimStart();
                }
            }

            return trimmed;
        }

        /// <summary>
        /// Get price statistics with caching to reduce API calls.
        /// Cache has a TTL of 5 minutes.
        /// </summary>
        private async Task<PriceStatistics?> GetCachedPriceStatisticsAsync(
            string priceListMatchName,
            int serverId,
            CancellationToken cancellationToken)
        {
            var cacheKey = $"{priceListMatchName}|{serverId}";

            // Check if we have a valid cached entry
            if (_priceStatsCache.TryGetValue(cacheKey, out var cached))
            {
                var age = DateTime.Now - cached.CachedAt;
                if (age.TotalMinutes < PriceCacheTtlMinutes)
                {
                    Debug.WriteLine($"[MonitoringService] Cache hit for '{priceListMatchName}' (age: {age.TotalSeconds:F0}s)");
                    return cached.Stats;
                }
                else
                {
                    Debug.WriteLine($"[MonitoringService] Cache expired for '{priceListMatchName}' (age: {age.TotalMinutes:F1}m)");
                }
            }

            // Fetch fresh statistics
            var stats = await _gnjoyClient.FetchPriceHistoryAsync(
                priceListMatchName,
                serverId,
                cancellationToken);

            // Cache the result (even if null)
            _priceStatsCache[cacheKey] = (stats, DateTime.Now);
            Debug.WriteLine($"[MonitoringService] Cached price stats for '{priceListMatchName}': Yesterday={stats?.YesterdayAvgPrice:N0}");

            return stats;
        }

        private static string GetResultKey(string itemName, int serverId)
        {
            return $"{itemName}|{serverId}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    /// <summary>
    /// Progress information for monitoring refresh
    /// </summary>
    public class MonitorProgress
    {
        public string Phase { get; set; } = string.Empty;
        public string CurrentItem { get; set; } = string.Empty;
        public int CurrentIndex { get; set; }
        public int TotalItems { get; set; }
        public int ProgressPercent { get; set; }
    }
}
