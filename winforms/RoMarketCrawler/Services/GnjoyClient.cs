using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// HTTP client for GNJOY Ragnarok Online API
/// </summary>
public class GnjoyClient : IDisposable
{
    private const string BaseUrl = "https://ro.gnjoy.com/itemDeal";
    private const string DealListEndpoint = "itemDealList.asp";
    private const string DealViewEndpoint = "itemDealView.asp";
    private const string PriceListEndpoint = "itemPriceList.asp";
    private const string PriceExtendListEndpoint = "itemPriceExtendList.asp";

    private readonly HttpClient _client;
    private readonly ItemDealParser _parser;
    private readonly PriceQuoteParser _quoteParser;
    private readonly ItemDetailParser _detailParser;

    public GnjoyClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7");
        _client.DefaultRequestHeaders.Referrer = new Uri("https://ro.gnjoy.com/");

        _parser = new ItemDealParser();
        _quoteParser = new PriceQuoteParser();
        _detailParser = new ItemDetailParser();
    }

    /// <summary>
    /// Convert UI server ID to GNJOY quote server ID
    /// 바포=1->129, 이그=2->229, 다크=3->529, 이프=4->729
    /// </summary>
    private int ToQuoteServerId(int serverId)
    {
        return serverId switch
        {
            1 => 129,
            2 => 229,
            3 => 529,
            4 => 729,
            129 or 229 or 529 or 729 => serverId,
            _ => 129 // default to 바포메트
        };
    }

    /// <summary>
    /// Fetch price history for an item from GNJOY 시세 조회
    /// Uses itemPriceExtendList.asp endpoint which returns actual price history data
    /// </summary>
    /// <param name="itemName">Exact item name to search</param>
    /// <param name="serverId">Server ID (1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <returns>PriceStatistics with historical data</returns>
    public async Task<PriceStatistics?> FetchPriceHistoryAsync(
        string itemName,
        int serverId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var quoteServerId = ToQuoteServerId(serverId);
            var url = $"{BaseUrl}/{PriceExtendListEndpoint}?svrID={quoteServerId}&itemFullName={Uri.EscapeDataString(itemName)}&curpage=1";

            Debug.WriteLine($"[GnjoyClient] FetchPriceHistoryAsync called with itemName='{itemName}', serverId={serverId}");
            Debug.WriteLine($"[GnjoyClient] Fetching price history: {url}");

            var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Price history response length: {htmlContent.Length}");

            var history = _quoteParser.ParsePriceExtendList(htmlContent);
            Debug.WriteLine($"[GnjoyClient] Parsed {history.Count} price history records for '{itemName}'");

            if (history.Count == 0)
            {
                return null; // No data found
            }

            return PriceStatistics.FromHistory(itemName, serverId, history);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GnjoyClient] FetchPriceHistoryAsync failed for '{itemName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Search for items in the price list (itemPriceList.asp) - returns matching item names
    /// This is the first step to find exact item names for price lookup
    /// </summary>
    public async Task<List<PriceListItem>> SearchPriceListAsync(
        string searchTerm,
        int serverId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var quoteServerId = ToQuoteServerId(serverId);
            var url = $"{BaseUrl}/{PriceListEndpoint}?svrID={quoteServerId}&itemFullName={Uri.EscapeDataString(searchTerm)}&curpage=1";

            Debug.WriteLine($"[GnjoyClient] SearchPriceListAsync: {url}");

            var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Price list response length: {htmlContent.Length}");

            var items = _quoteParser.ParsePriceList(htmlContent);
            Debug.WriteLine($"[GnjoyClient] Found {items.Count} items in price list for '{searchTerm}'");

            return items;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchPriceListAsync failed for '{searchTerm}': {ex.Message}");
            return new List<PriceListItem>();
        }
    }

    /// <summary>
    /// Fetch price history with 2-step approach:
    /// 1. Search itemPriceList.asp with search term to find exact item names
    /// 2. Use exact item name with itemPriceExtendList.asp to get detailed price history
    /// Falls back to direct query if search returns no results
    /// </summary>
    public async Task<PriceStatistics?> FetchPriceHistoryWithFallbackAsync(
        string primaryName,
        string? fallbackName,
        int serverId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Search price list to find exact item names matching the search term
        Debug.WriteLine($"[GnjoyClient] Step 1: Searching price list for '{primaryName}'");
        var priceListItems = await SearchPriceListAsync(primaryName, serverId, cancellationToken);

        if (priceListItems.Count > 0)
        {
            // Found matching items - use the first exact item name for detailed price lookup
            var exactItemName = priceListItems[0].ExactItemName;
            Debug.WriteLine($"[GnjoyClient] Step 2: Found exact item name '{exactItemName}', fetching price history");

            var stats = await FetchPriceHistoryAsync(exactItemName, serverId, cancellationToken);
            if (stats != null)
            {
                Debug.WriteLine($"[GnjoyClient] Successfully retrieved price history for '{exactItemName}'");
                return stats;
            }
        }

        // Fallback: Try direct lookup with primary name (for cases where exact match works)
        Debug.WriteLine($"[GnjoyClient] Fallback: Direct lookup with '{primaryName}'");
        var directStats = await FetchPriceHistoryAsync(primaryName, serverId, cancellationToken);
        if (directStats != null)
        {
            return directStats;
        }

        // Final fallback: Try with base item name
        if (!string.IsNullOrEmpty(fallbackName) && fallbackName != primaryName)
        {
            Debug.WriteLine($"[GnjoyClient] Final fallback: Trying base item name '{fallbackName}'");

            // Also try 2-step approach with fallback name
            var fallbackListItems = await SearchPriceListAsync(fallbackName, serverId, cancellationToken);
            if (fallbackListItems.Count > 0)
            {
                var exactFallbackName = fallbackListItems[0].ExactItemName;
                var fallbackStats = await FetchPriceHistoryAsync(exactFallbackName, serverId, cancellationToken);
                if (fallbackStats != null)
                {
                    return fallbackStats;
                }
            }

            // Direct lookup with fallback name
            var directFallbackStats = await FetchPriceHistoryAsync(fallbackName, serverId, cancellationToken);
            if (directFallbackStats != null)
            {
                return directFallbackStats;
            }
        }

        Debug.WriteLine($"[GnjoyClient] No price data found for '{primaryName}' or fallback '{fallbackName}'");
        return null;
    }

    /// <summary>
    /// Fetch price statistics for multiple items in parallel
    /// </summary>
    public async Task<Dictionary<string, PriceStatistics>> FetchMultiplePriceHistoryAsync(
        IEnumerable<string> itemNames,
        int serverId,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, PriceStatistics>();
        var tasks = new List<Task<(string itemName, PriceStatistics? stats)>>();

        foreach (var itemName in itemNames.Distinct())
        {
            var name = itemName;
            tasks.Add(Task.Run(async () =>
            {
                var stats = await FetchPriceHistoryAsync(name, serverId, cancellationToken);
                return (name, stats);
            }, cancellationToken));
        }

        var completed = await Task.WhenAll(tasks);

        foreach (var (itemName, stats) in completed)
        {
            if (stats != null)
            {
                results[itemName] = stats;
            }
        }

        return results;
    }

    /// <summary>
    /// Fetch item detail (enchants, cards, random options) from itemDealView.asp
    /// </summary>
    /// <param name="serverId">Server ID (129, 229, 529, 729)</param>
    /// <param name="mapId">Map ID from CallItemDealView</param>
    /// <param name="ssi">Unique item identifier</param>
    /// <returns>ItemDetailInfo with slot info and random options</returns>
    public async Task<ItemDetailInfo?> FetchItemDetailAsync(
        int serverId,
        int mapId,
        string ssi,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert UI server ID to GNJOY format if needed
            var gnjoyServerId = ToQuoteServerId(serverId);
            var url = $"{BaseUrl}/{DealViewEndpoint}?svrID={gnjoyServerId}&mapID={mapId}&ssi={Uri.EscapeDataString(ssi)}&curpage=1";

            Debug.WriteLine($"[GnjoyClient] FetchItemDetailAsync: {url}");

            var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Item detail response length: {htmlContent.Length}");

            // Log first 2000 chars of HTML for debugging
            var htmlPreview = htmlContent.Length > 2000 ? htmlContent.Substring(0, 2000) : htmlContent;
            Debug.WriteLine($"[GnjoyClient] Item detail HTML preview:\n{htmlPreview}");

            var detail = _detailParser.Parse(htmlContent);
            Debug.WriteLine($"[GnjoyClient] Parsed item detail: {detail.SlotInfo.Count} slots, {detail.RandomOptions.Count} options");
            foreach (var slot in detail.SlotInfo)
            {
                Debug.WriteLine($"[GnjoyClient] Slot: '{slot}'");
            }

            return detail;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GnjoyClient] FetchItemDetailAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Search for item deals by name (single page)
    /// </summary>
    /// <param name="itemName">Item name to search (Korean)</param>
    /// <param name="serverId">Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <returns>List of DealItem found</returns>
    public async Task<List<DealItem>> SearchItemDealsAsync(
        string itemName,
        int serverId = -1,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert UI server ID to GNJOY format for deal search
            // -1 stays as -1 (all servers), 1,2,3,4 convert to 129,229,529,729
            var gnjoyServerId = serverId == -1 ? -1 : ToQuoteServerId(serverId);
            var url = $"{BaseUrl}/{DealListEndpoint}?svrID={gnjoyServerId}&itemFullName={Uri.EscapeDataString(itemName)}&itemOrder=regdate&curpage={page}";
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync URL: {url}");

            var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            // GNJOY returns UTF-8 encoded HTML
            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Response length: {htmlContent.Length} chars");

            var items = _parser.ParseDealList(htmlContent, serverId);
            Debug.WriteLine($"[GnjoyClient] Parsed {items.Count} items");
            return items;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync HTTP error for '{itemName}': {ex.Message}");
            return new List<DealItem>();
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync cancelled for '{itemName}'");
            return new List<DealItem>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync failed for '{itemName}': {ex.Message}");
            return new List<DealItem>();
        }
    }

    /// <summary>
    /// Search for item deals by name (all pages)
    /// Fetches all pages until no more results are found
    /// </summary>
    /// <param name="itemName">Item name to search (Korean)</param>
    /// <param name="serverId">Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <param name="maxPages">Maximum pages to fetch (default 10)</param>
    /// <returns>List of all DealItems found across all pages</returns>
    public async Task<List<DealItem>> SearchAllItemDealsAsync(
        string itemName,
        int serverId = -1,
        int maxPages = 10,
        CancellationToken cancellationToken = default)
    {
        var allItems = new List<DealItem>();
        int page = 1;

        while (page <= maxPages)
        {
            if (cancellationToken.IsCancellationRequested) break;

            Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: Fetching page {page}");
            var items = await SearchItemDealsAsync(itemName, serverId, page, cancellationToken);

            if (items.Count == 0)
            {
                Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: No more items on page {page}, stopping");
                break;
            }

            allItems.AddRange(items);
            Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: Total items so far: {allItems.Count}");

            // GNJOY shows 10 items per page, if less than 10, it's the last page
            if (items.Count < 10)
            {
                Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: Last page reached (got {items.Count} items)");
                break;
            }

            page++;

            // Small delay to be respectful to the server
            await Task.Delay(100, cancellationToken);
        }

        Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: Completed with {allItems.Count} total items from {page} page(s)");
        return allItems;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
