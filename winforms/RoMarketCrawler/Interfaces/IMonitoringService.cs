using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for monitoring service managing item price tracking
/// </summary>
public interface IMonitoringService : IDisposable
{
    #region Properties

    /// <summary>
    /// Maximum number of items that can be monitored
    /// </summary>
    int MaxItemCount { get; }

    /// <summary>
    /// Current monitoring configuration
    /// </summary>
    MonitorConfig Config { get; }

    /// <summary>
    /// Current monitoring results
    /// </summary>
    IReadOnlyDictionary<string, MonitorResult> Results { get; }

    /// <summary>
    /// Number of monitored items
    /// </summary>
    int ItemCount { get; }

    #endregion

    #region Configuration

    /// <summary>
    /// Load configuration from persistent storage
    /// </summary>
    Task LoadConfigAsync();

    /// <summary>
    /// Save configuration to persistent storage
    /// </summary>
    Task SaveConfigAsync();

    #endregion

    #region Item Management

    /// <summary>
    /// Add an item to the monitoring list
    /// </summary>
    /// <returns>Tuple of (success, errorMessage)</returns>
    Task<(bool Success, string? ErrorReason)> AddItemAsync(string itemName, int serverId = -1);

    /// <summary>
    /// Remove an item from the monitoring list
    /// </summary>
    Task<bool> RemoveItemAsync(string itemName, int serverId = -1);

    /// <summary>
    /// Update the server ID of an existing monitored item
    /// </summary>
    Task<bool> UpdateItemServerAsync(string itemName, int oldServerId, int newServerId);

    /// <summary>
    /// Clear cached results for a specific item
    /// </summary>
    void ClearItemCache(string itemName, int serverId);

    /// <summary>
    /// Rename a monitored item
    /// </summary>
    Task<bool> RenameItemAsync(string oldName, int serverId, string newName);

    /// <summary>
    /// Clear all items from the monitoring list
    /// </summary>
    Task ClearAllItemsAsync();

    #endregion

    #region Refresh Settings

    /// <summary>
    /// Update refresh interval setting
    /// </summary>
    Task SetRefreshIntervalAsync(int seconds);

    /// <summary>
    /// Initialize refresh schedule for individual item refresh mode
    /// </summary>
    void InitializeRefreshSchedule();

    /// <summary>
    /// Get the next item that needs to be refreshed
    /// </summary>
    MonitorItem? GetNextItemToRefresh();

    /// <summary>
    /// Get all items that need to be refreshed
    /// </summary>
    List<MonitorItem> GetAllItemsDueForRefresh();

    /// <summary>
    /// Schedule the next refresh time for the given items
    /// </summary>
    void ScheduleNextRefresh(IEnumerable<MonitorItem> items);

    /// <summary>
    /// Schedule the next refresh time for all monitored items
    /// </summary>
    void ScheduleNextRefreshForAll();

    #endregion

    #region Refresh Operations

    /// <summary>
    /// Refresh a single item and update its next refresh time
    /// </summary>
    Task<MonitorResult> RefreshSingleItemAsync(MonitorItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh all monitored items using sequential processing
    /// </summary>
    Task RefreshAllAsync(IProgress<MonitorProgress>? progress = null, CancellationToken cancellationToken = default);

    #endregion

    #region Results

    /// <summary>
    /// Get result for a specific item
    /// </summary>
    MonitorResult? GetResult(string itemName, int serverId = -1);

    /// <summary>
    /// Get all results with good deals
    /// </summary>
    List<MonitorResult> GetGoodDeals();

    #endregion
}
