using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for GNJOY Ragnarok Online API client
/// </summary>
public interface IGnjoyClient : IDisposable
{
    /// <summary>
    /// Search for item deals by name (single page)
    /// </summary>
    /// <param name="itemName">Item name to search (Korean)</param>
    /// <param name="serverId">Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <returns>List of DealItem found</returns>
    Task<List<DealItem>> SearchItemDealsAsync(
        string itemName,
        int serverId = -1,
        int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for item deals by name (single page) with total count for pagination
    /// </summary>
    /// <param name="itemName">Item name to search (Korean)</param>
    /// <param name="serverId">Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <returns>DealSearchResult with items and total count</returns>
    Task<DealSearchResult> SearchItemDealsWithCountAsync(
        string itemName,
        int serverId = -1,
        int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for item deals by name (all pages)
    /// Fetches all pages until no more results are found
    /// </summary>
    /// <param name="itemName">Item name to search (Korean)</param>
    /// <param name="serverId">Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <param name="maxPages">Maximum pages to fetch (default 10)</param>
    /// <returns>List of all DealItems found across all pages</returns>
    Task<List<DealItem>> SearchAllItemDealsAsync(
        string itemName,
        int serverId = -1,
        int maxPages = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch item detail (enchants, cards, random options) from itemDealView.asp
    /// </summary>
    /// <param name="serverId">Server ID (129, 229, 529, 729)</param>
    /// <param name="mapId">Map ID from CallItemDealView</param>
    /// <param name="ssi">Unique item identifier</param>
    /// <returns>ItemDetailInfo with slot info and random options</returns>
    Task<ItemDetailInfo?> FetchItemDetailAsync(
        int serverId,
        int mapId,
        string ssi,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch price history for an item from GNJOY 시세 조회
    /// </summary>
    /// <param name="itemName">Exact item name to search</param>
    /// <param name="serverId">Server ID (1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <returns>PriceStatistics with historical data</returns>
    Task<PriceStatistics?> FetchPriceHistoryAsync(
        string itemName,
        int serverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for items in the price list (itemPriceList.asp) - returns matching item names
    /// </summary>
    Task<List<PriceListItem>> SearchPriceListAsync(
        string searchTerm,
        int serverId,
        CancellationToken cancellationToken = default);

}
