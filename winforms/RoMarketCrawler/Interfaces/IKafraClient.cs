using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for kafra.kr API client for item/enchant effect descriptions
/// </summary>
public interface IKafraClient : IDisposable
{
    /// <summary>
    /// Number of items currently in cache
    /// </summary>
    int CacheCount { get; }

    /// <summary>
    /// Search for items by name and return matching results
    /// </summary>
    /// <param name="searchTerm">Item name to search for (max 100 chars)</param>
    /// <param name="itemType">Item type (0-999, use 999 for all types)</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    Task<List<KafraItem>> SearchItemsAsync(string searchTerm, int itemType = 999, int maxResults = 50);

    /// <summary>
    /// Get item by ID directly from kafra.kr API
    /// </summary>
    Task<KafraItem?> GetItemByIdAsync(int itemId);

    /// <summary>
    /// Get item effect description by exact name match
    /// </summary>
    Task<string?> GetItemEffectAsync(string itemName);

    /// <summary>
    /// Get full item data by name (returns complete KafraItem, not just ItemText)
    /// </summary>
    Task<KafraItem?> GetFullItemDataAsync(string itemName);

    /// <summary>
    /// Preload cache with common enchants
    /// </summary>
    Task PreloadCommonEnchantsAsync();

    /// <summary>
    /// Clear the item cache
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Get monster info by ID
    /// </summary>
    Task<MonsterInfo?> GetMonsterByIdAsync(int mobId);

    /// <summary>
    /// Search monsters by name
    /// </summary>
    Task<List<MonsterInfo>> SearchMonstersAsync(string searchTerm, int startId = 1001, int endId = 4000);

    /// <summary>
    /// Get multiple monsters by ID range (for browsing)
    /// </summary>
    Task<List<MonsterInfo>> GetMonsterRangeAsync(int startId, int count = 50);
}
