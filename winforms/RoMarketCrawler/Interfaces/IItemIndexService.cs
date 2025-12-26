using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for item index service managing the complete item database
/// </summary>
public interface IItemIndexService : IDisposable
{
    #region Properties

    /// <summary>
    /// Whether the index has been loaded
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Whether the index is currently loading
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// Total count of items in the index
    /// </summary>
    int TotalCount { get; }

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    DateTime? LastUpdated { get; }

    /// <summary>
    /// Path to the cache file
    /// </summary>
    string CacheFilePath { get; }

    /// <summary>
    /// Category metadata dictionary
    /// </summary>
    IReadOnlyDictionary<int, CategoryMeta> Categories { get; }

    #endregion

    #region Core Methods

    /// <summary>
    /// Load index from cache file if exists
    /// </summary>
    Task<bool> LoadFromCacheAsync();

    /// <summary>
    /// Fetch all items from API and rebuild the index
    /// </summary>
    Task<bool> RebuildIndexAsync(IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get item by ID
    /// </summary>
    KafraItemDto? GetItemById(int id);

    /// <summary>
    /// Get item by screen name (case-insensitive)
    /// </summary>
    KafraItemDto? GetItemByName(string screenName);

    /// <summary>
    /// Get all screen names for autocomplete
    /// </summary>
    IReadOnlyList<string> GetAllScreenNames();

    /// <summary>
    /// Search items by partial name match
    /// </summary>
    List<KafraItemDto> SearchByName(string searchTerm, int maxResults = 50);

    /// <summary>
    /// Get items by type
    /// </summary>
    List<KafraItemDto> GetItemsByType(int itemType);

    /// <summary>
    /// Count items matching the search criteria (for pagination)
    /// </summary>
    int CountItems(string searchTerm, int itemType = 999, bool searchDescription = false);

    /// <summary>
    /// Count items matching the search criteria with multiple types
    /// </summary>
    int CountItems(string searchTerm, HashSet<int> itemTypes, bool searchDescription = false);

    /// <summary>
    /// Search items with type filtering, name matching, and pagination
    /// </summary>
    List<KafraItem> SearchItems(string searchTerm, int itemType = 999, int skip = 0, int take = 100, bool searchDescription = false);

    /// <summary>
    /// Search items with multiple type filtering, name matching, and pagination
    /// </summary>
    List<KafraItem> SearchItems(string searchTerm, HashSet<int> itemTypes, int skip = 0, int take = 100, bool searchDescription = false);

    /// <summary>
    /// Check if cache needs refresh
    /// </summary>
    bool IsCacheStale(int maxAgeHours = 168);

    #endregion

    #region ID Range Scan

    /// <summary>
    /// Scan a range of item IDs directly using /KRO/{id}/item.json endpoint
    /// </summary>
    Task<int> ScanIdRangeAsync(int startId, int endId, IProgress<IndexProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Scan common weapon ID ranges to collect missing weapons
    /// </summary>
    Task<int> ScanWeaponsAsync(IProgress<IndexProgress>? progress = null, CancellationToken ct = default);

    #endregion
}
