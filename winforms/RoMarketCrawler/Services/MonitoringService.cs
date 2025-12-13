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
    /// Service for managing item monitoring with persistent storage and auto-refresh.
    /// Can operate with its own GnjoyClient for complete isolation from other tabs.
    /// </summary>
    public class MonitoringService : IDisposable
    {
        /// <summary>
        /// Maximum number of items that can be monitored (safety limit to prevent API abuse)
        /// </summary>
        public const int MaxItemCount = 20;

        private readonly GnjoyClient _gnjoyClient;
        private readonly bool _ownsGnjoyClient;
        private readonly string _configFilePath;
        private MonitorConfig _config;
        private readonly Dictionary<string, MonitorResult> _results;
        private readonly object _lock = new();
        private bool _disposed;

        // Parallel processing with atomic update pattern to prevent race conditions
        private readonly SemaphoreSlim _refreshSemaphore = new(3); // Max 3 concurrent refreshes

        /// <summary>
        /// Creates MonitoringService with its own dedicated GnjoyClient.
        /// This ensures complete isolation from other components using GnjoyClient.
        /// </summary>
        public MonitoringService(string? dataDirectory = null)
        {
            _gnjoyClient = new GnjoyClient();
            _ownsGnjoyClient = true;
            var dataDir = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            _configFilePath = Path.Combine(dataDir, "MonitorConfig.json");
            _config = new MonitorConfig();
            _results = new Dictionary<string, MonitorResult>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates MonitoringService with a shared GnjoyClient (legacy support).
        /// </summary>
        public MonitoringService(GnjoyClient gnjoyClient, string? dataDirectory = null)
        {
            _gnjoyClient = gnjoyClient;
            _ownsGnjoyClient = false;
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
        /// <returns>
        /// Tuple of (success, errorMessage):
        /// - (true, null) on success
        /// - (false, "duplicate") if item already exists
        /// - (false, "limit") if max item count reached
        /// </returns>
        public async Task<(bool Success, string? ErrorReason)> AddItemAsync(string itemName, int serverId = -1)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return (false, "empty");

            itemName = itemName.Trim();

            // Check for max item limit
            if (_config.Items.Count >= MaxItemCount)
            {
                Debug.WriteLine($"[MonitoringService] Cannot add '{itemName}' - max item limit ({MaxItemCount}) reached");
                return (false, "limit");
            }

            // Check for duplicates
            if (_config.Items.Any(i => i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId))
            {
                Debug.WriteLine($"[MonitoringService] Item '{itemName}' already exists");
                return (false, "duplicate");
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
            return (true, null);
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
            // NOTE: WinForms data binding updates item.ServerId to newServerId BEFORE CellValueChanged event fires
            // So we must search by newServerId (not oldServerId) to find the item
            var item = _config.Items.FirstOrDefault(i =>
                i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == newServerId);

            if (item == null)
                return false;

            // Remove old result from cache using the ORIGINAL serverId passed from CellBeginEdit
            lock (_lock)
            {
                var oldKey = GetResultKey(itemName, oldServerId);
                _results.Remove(oldKey);
            }

            // ServerId is already updated by data binding, just save config
            await SaveConfigAsync();
            Debug.WriteLine($"[MonitoringService] Updated item '{itemName}' server from {oldServerId} to {newServerId}");
            return true;
        }

        /// <summary>
        /// Clear cached results for a specific item (used when settings change)
        /// </summary>
        public void ClearItemCache(string itemName, int serverId)
        {
            lock (_lock)
            {
                var key = GetResultKey(itemName, serverId);
                _results.Remove(key);
                Debug.WriteLine($"[MonitoringService] Cleared cache for '{itemName}' (server={serverId})");
            }
        }

        /// <summary>
        /// Rename a monitored item
        /// </summary>
        public async Task<bool> RenameItemAsync(string oldName, int serverId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            // NOTE: WinForms data binding updates item.ItemName to newName BEFORE CellValueChanged event fires
            // So we must search by newName (not oldName) to find the item
            var item = _config.Items.FirstOrDefault(i =>
                i.ItemName.Equals(newName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId);

            if (item == null)
                return false;

            // Check if new name already exists for same server (excluding current item)
            var exists = _config.Items.Any(i =>
                i.ItemName.Equals(newName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId && i != item);

            if (exists)
            {
                Debug.WriteLine($"[MonitoringService] Cannot rename - item '{newName}' already exists for server {serverId}");
                return false;
            }

            // Remove old result from cache AND update item name atomically
            // This prevents race condition where atomic copy in RefreshAllAsync
            // could see the old name while computing currentItemKeys
            lock (_lock)
            {
                var oldKey = GetResultKey(oldName, serverId);
                _results.Remove(oldKey);

                // Update the item name INSIDE the lock to ensure atomicity
                item.ItemName = newName;
            }

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
        /// Initialize refresh schedule for individual item refresh mode.
        /// Distributes refresh times evenly across the refresh interval.
        /// </summary>
        public void InitializeRefreshSchedule()
        {
            var items = _config.Items;
            var intervalSeconds = _config.RefreshIntervalSeconds;

            if (items.Count == 0 || intervalSeconds <= 0)
                return;

            // Calculate spacing between items
            var spacingSeconds = (double)intervalSeconds / items.Count;
            var now = DateTime.Now;

            for (int i = 0; i < items.Count; i++)
            {
                // First item refreshes immediately, others are spaced out
                items[i].NextRefreshTime = now.AddSeconds(i * spacingSeconds);
            }

            Debug.WriteLine($"[MonitoringService] Initialized refresh schedule: {items.Count} items, {spacingSeconds:F1}s spacing");
        }

        /// <summary>
        /// Get the next item that needs to be refreshed (NextRefreshTime <= now)
        /// </summary>
        public MonitorItem? GetNextItemToRefresh()
        {
            var now = DateTime.Now;
            return _config.Items
                .Where(i => i.NextRefreshTime.HasValue && i.NextRefreshTime.Value <= now && !i.IsRefreshing)
                .OrderBy(i => i.NextRefreshTime)
                .FirstOrDefault();
        }

        /// <summary>
        /// Get all items that need to be refreshed (NextRefreshTime <= now)
        /// Used for parallel processing of multiple due items
        /// </summary>
        public List<MonitorItem> GetAllItemsDueForRefresh()
        {
            var now = DateTime.Now;
            return _config.Items
                .Where(i => i.NextRefreshTime.HasValue && i.NextRefreshTime.Value <= now && !i.IsRefreshing)
                .OrderBy(i => i.NextRefreshTime)
                .ToList();
        }

        /// <summary>
        /// Schedule the next refresh time for the given items.
        /// Should be called after UI rendering is complete to ensure countdown starts after results are visible.
        /// </summary>
        public void ScheduleNextRefresh(IEnumerable<MonitorItem> items)
        {
            var nextTime = DateTime.Now.AddSeconds(_config.RefreshIntervalSeconds);
            foreach (var item in items)
            {
                item.NextRefreshTime = nextTime;
                Debug.WriteLine($"[MonitoringService] Scheduled next refresh for '{item.ItemName}' at {nextTime:HH:mm:ss}");
            }
        }

        /// <summary>
        /// Schedule the next refresh time for all monitored items.
        /// Should be called after UI rendering is complete (e.g., after manual refresh).
        /// </summary>
        public void ScheduleNextRefreshForAll()
        {
            ScheduleNextRefresh(_config.Items);
        }

        /// <summary>
        /// Refresh a single item and update its next refresh time.
        /// Captures item name at start to prevent race conditions when item is renamed during refresh.
        /// </summary>
        public async Task<MonitorResult> RefreshSingleItemAsync(
            MonitorItem item,
            CancellationToken cancellationToken = default)
        {
            // Capture item identity at start to prevent race condition
            // If user renames item during async API call, we must discard stale results
            var searchName = item.ItemName;
            var searchServerId = item.ServerId;

            Debug.WriteLine($"[MonitoringService] Starting refresh for '{searchName}' (server={searchServerId})");

            var result = await RefreshItemAsync(item, cancellationToken);

            // Check if item was renamed during the async operation
            if (item.ItemName != searchName || item.ServerId != searchServerId)
            {
                Debug.WriteLine($"[MonitoringService] Item changed during refresh: '{searchName}' -> '{item.ItemName}', discarding stale results");
                // Return result but don't store it - the stale data would be stored under wrong key
                return result;
            }

            // Store result only if item identity unchanged
            lock (_lock)
            {
                var key = GetResultKey(searchName, searchServerId);
                _results[key] = result;
            }

            // Note: NextRefreshTime is now set by the caller after UI rendering completes
            // This ensures the countdown starts after the user sees the results

            Debug.WriteLine($"[MonitoringService] Refreshed '{searchName}' (next refresh will be scheduled after rendering)");

            return result;
        }

        /// <summary>
        /// Refresh all monitored items using parallel processing with atomic update
        /// Uses temporary dictionary to collect results, then updates _results atomically
        /// This prevents race conditions when refresh is cancelled mid-way
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

            Debug.WriteLine($"[MonitoringService] Refreshing {total} items with parallel processing (max 3 concurrent, atomic update)...");

            // Temporary dictionary for collecting results - prevents partial updates on cancellation
            var tempResults = new ConcurrentDictionary<string, MonitorResult>();
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
                // Capture item identity at start to prevent race condition
                // If user renames item during async API call, we must discard stale results
                var searchName = item.ItemName;
                var searchServerId = item.ServerId;

                await _refreshSemaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await RefreshItemAsync(item, cancellationToken);

                    // Check if item was renamed during the async operation
                    if (item.ItemName != searchName || item.ServerId != searchServerId)
                    {
                        Debug.WriteLine($"[MonitoringService] RefreshAllAsync: Item changed during refresh: '{searchName}' -> '{item.ItemName}', discarding stale results");
                        // Don't store - the stale data would be stored under wrong key
                    }
                    else
                    {
                        // Store in temporary dictionary only if item identity unchanged
                        var key = GetResultKey(searchName, searchServerId);
                        tempResults[key] = result;
                    }

                    // Update progress after each item completes
                    var currentCompleted = Interlocked.Increment(ref completedCount);
                    progress?.Report(new MonitorProgress
                    {
                        Phase = "병렬 조회 중",
                        CurrentItem = searchName,
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

            // Wait for ALL tasks to complete
            await Task.WhenAll(tasks);

            // ATOMIC UPDATE: Only update _results after ALL items complete successfully
            // This prevents partial results when refresh is cancelled
            // Also filter out stale keys from items that were renamed during refresh
            lock (_lock)
            {
                // Get current item keys to filter out stale results from renamed items
                var currentItemKeys = _config.Items
                    .Select(i => GetResultKey(i.ItemName, i.ServerId))
                    .ToHashSet();

                foreach (var kvp in tempResults)
                {
                    // Only copy if this key still corresponds to a currently monitored item
                    if (currentItemKeys.Contains(kvp.Key))
                    {
                        _results[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        Debug.WriteLine($"[MonitoringService] RefreshAllAsync: Filtering stale key '{kvp.Key}' during atomic copy");
                    }
                }
            }

            progress?.Report(new MonitorProgress
            {
                Phase = "완료",
                CurrentItem = "",
                CurrentIndex = total,
                TotalItems = total,
                ProgressPercent = 100
            });

            Debug.WriteLine($"[MonitoringService] Parallel refresh completed for {total} items (atomic update)");
        }

        /// <summary>
        /// Refresh a single monitored item
        /// </summary>
        private async Task<MonitorResult> RefreshItemAsync(MonitorItem item, CancellationToken cancellationToken)
        {
            // Capture item identity at method start to ensure consistency throughout execution
            // This prevents issues if item is renamed during async operations
            var capturedItemName = item.ItemName;
            var capturedServerId = item.ServerId;

            var result = new MonitorResult
            {
                Item = item,
                LastRefreshed = DateTime.Now
            };

            try
            {
                Debug.WriteLine($"[MonitoringService] RefreshItemAsync: item='{capturedItemName}', ServerId={capturedServerId}");

                // Fetch current deals from first 3 pages for speed optimization
                // GNJOY returns 10 items per page - 3 pages = 30 items is sufficient for monitoring
                // Most relevant deals appear on early pages anyway
                var allDeals = await _gnjoyClient.SearchAllItemDealsAsync(
                    capturedItemName,
                    capturedServerId,
                    maxPages: 3,  // Up to 30 items (optimized for monitoring speed)
                    cancellationToken);

                // Filter to only include selling shops (exclude buying shops)
                var deals = allDeals.Where(d => d.DealType == "sale").ToList();
                Debug.WriteLine($"[MonitoringService] Filtered: {allDeals.Count} total -> {deals.Count} sale only");

                result.Deals = deals;

                // Fetch price statistics for EACH unique item name in the deals
                // This is necessary because a single search can return different item variants
                // (e.g., "포링 선글래스" search returns both base item and enhanced "+" version)
                var priceServerId = capturedServerId == -1 ? 1 : capturedServerId;

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

                    Debug.WriteLine($"[MonitoringService] Processing deal item: '{dealItemName}' (Grade: '{sampleDeal.Grade ?? "none"}')");

                    // Skip stats for items with ANY grade (GNJOY API doesn't provide grade-specific prices)
                    // Grade items have unreliable price history due to grade-specific pricing
                    if (hasGrade)
                    {
                        Debug.WriteLine($"[MonitoringService] Skipping stats - item has grade '{sampleDeal.Grade}'");
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
                    }
                    else if (dealRefine.HasValue && dealRefine.Value > 0)
                    {
                        // Item doesn't have refine prefix but has refine value
                        // Add refine level to search name for more accurate price statistics
                        searchName = $"{dealRefine.Value}{dealItemName}";
                    }
                    else
                    {
                        // No refine or zero, use base item name
                        searchName = GetBaseSearchName(dealItemName);
                    }

                    Debug.WriteLine($"[MonitoringService] Search name: '{searchName}' for '{dealItemName}'");

                    // Fetch price list using search name
                    var priceListItems = await _gnjoyClient.SearchPriceListAsync(
                        searchName,
                        priceServerId,
                        cancellationToken);

                    Debug.WriteLine($"[MonitoringService] Price list returned {priceListItems.Count} items for '{searchName}'");

                    PriceStatistics? stats = null;

                    if (priceListItems.Count > 0)
                    {
                        // Find the best matching price list item
                        var priceListMatch = FindBestPriceListMatch(dealItemName, priceListItems);

                        if (priceListMatch != null)
                        {
                            Debug.WriteLine($"[MonitoringService] Match found: '{priceListMatch}'");
                            // Fetch price statistics directly from API
                            stats = await _gnjoyClient.FetchPriceHistoryAsync(
                                priceListMatch,
                                priceServerId,
                                cancellationToken);
                            Debug.WriteLine($"[MonitoringService] Stats: Yesterday={stats?.YesterdayAvgPrice:N0}, Week={stats?.Week7AvgPrice:N0}");
                        }
                        else
                        {
                            Debug.WriteLine($"[MonitoringService] No match found in price list for '{dealItemName}'");
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

                Debug.WriteLine($"[MonitoringService] Refreshed '{capturedItemName}': {deals.Count} deals across {dealsByItemName.Count} item names");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                Debug.WriteLine($"[MonitoringService] Failed to refresh '{capturedItemName}': {ex.Message}");
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

        private static string GetResultKey(string itemName, int serverId)
        {
            return $"{itemName}|{serverId}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Dispose owned resources
            if (_ownsGnjoyClient)
            {
                _gnjoyClient.Dispose();
            }
            _refreshSemaphore.Dispose();
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
