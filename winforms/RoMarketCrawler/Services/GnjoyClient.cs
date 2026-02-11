using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RoMarketCrawler.Exceptions;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// HTTP client for GNJOY Ragnarok Online API
/// </summary>
public class GnjoyClient : IGnjoyClient
{
    private const string BaseUrl = "https://ro.gnjoy.com/itemDeal";
    private const string DealListEndpoint = "itemDealList.asp";
    private const string DealViewEndpoint = "itemDealView.asp";
    private const string PriceListEndpoint = "itemPriceList.asp";
    private const string PriceExtendListEndpoint = "itemPriceExtendList.asp";

    private readonly HttpClient _client;
    private readonly SocketsHttpHandler _handler;
    private readonly ItemDealParser _parser;
    private readonly PriceQuoteParser _quoteParser;
    private readonly ItemDetailParser _detailParser;

    // WebView2 for Cloudflare bypass
    private WebView2Helper? _webView2Helper;
    private bool _useWebView2 = false;

    // Retry settings with exponential backoff
    private const int MaxRetries = 4;
    private const int BaseRetryDelayMs = 1000;  // 1s -> 2s -> 4s -> 8s

    // Singleton RateLimitManager for backward compatibility
    // Form1 and other code can still use static properties, but rate limiting is centralized
    private static readonly Lazy<RateLimitManager> _sharedRateLimitManager = new(() => new RateLimitManager());

    /// <summary>
    /// Shared RateLimitManager instance (singleton)
    /// </summary>
    public static IRateLimitManager SharedRateLimitManager => _sharedRateLimitManager.Value;

    /// <summary>
    /// Current rate limit status - null if not rate limited
    /// </summary>
    public static DateTime? RateLimitedUntil => _sharedRateLimitManager.Value.RateLimitedUntil;

    /// <summary>
    /// Check if currently rate limited
    /// </summary>
    public static bool IsRateLimited => _sharedRateLimitManager.Value.IsRateLimited;

    /// <summary>
    /// Get remaining rate limit time in seconds
    /// </summary>
    public static int RemainingRateLimitSeconds => _sharedRateLimitManager.Value.RemainingRateLimitSeconds;

    /// <summary>
    /// Clear rate limit status (call when rate limit is lifted)
    /// </summary>
    public static void ClearRateLimit() => _sharedRateLimitManager.Value.ClearRateLimit();

    /// <summary>
    /// Get current request count in the monitoring window
    /// </summary>
    public static int RequestCount => _sharedRateLimitManager.Value.RequestCount;

    /// <summary>
    /// Get requests per minute rate
    /// </summary>
    public static double RequestsPerMinute => _sharedRateLimitManager.Value.RequestsPerMinute;

    /// <summary>
    /// Check if WebView2 mode is enabled
    /// </summary>
    public bool IsWebView2Enabled => _useWebView2 && _webView2Helper?.IsReady == true;

    /// <summary>
    /// Set WebView2Helper for Cloudflare bypass
    /// </summary>
    public void SetWebView2Helper(WebView2Helper helper)
    {
        _webView2Helper = helper;
        Debug.WriteLine("[GnjoyClient] WebView2Helper set");
    }

    /// <summary>
    /// Enable or disable WebView2 mode
    /// </summary>
    public void SetUseWebView2(bool enabled)
    {
        _useWebView2 = enabled;
        Debug.WriteLine($"[GnjoyClient] WebView2 mode: {(enabled ? "ENABLED" : "DISABLED")}");
    }

    public GnjoyClient()
    {
        // .NET 8 uses SocketsHttpHandler by default - configure it explicitly for connection pool management
        _handler = new SocketsHttpHandler
        {
            // Automatic decompression for gzip/deflate
            AutomaticDecompression = DecompressionMethods.All,

            // Connection pool settings - CRITICAL for preventing pool exhaustion
            // Rotate connections every 2 minutes to prevent stale connections
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            // Close idle connections after 90 seconds
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            // Allow up to 10 connections per server (default is 2 in some scenarios)
            MaxConnectionsPerServer = 10,

            // Keep-alive settings
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),

            // Connection timeout
            ConnectTimeout = TimeSpan.FromSeconds(15),

            // Enable connection reuse
            EnableMultipleHttp2Connections = true
        };

        _client = new HttpClient(_handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Browser-like headers to avoid Cloudflare detection
        // Full Chrome User-Agent string
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        // Accept headers matching Chrome
        _client.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7");

        // Additional browser headers
        _client.DefaultRequestHeaders.Referrer = new Uri("https://ro.gnjoy.com/");
        _client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        _client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        _client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
        _client.DefaultRequestHeaders.Add("sec-fetch-dest", "document");
        _client.DefaultRequestHeaders.Add("sec-fetch-mode", "navigate");
        _client.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
        _client.DefaultRequestHeaders.Add("sec-fetch-user", "?1");
        _client.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");
        _client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        _client.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");

        _parser = new ItemDealParser();
        _quoteParser = new PriceQuoteParser();
        _detailParser = new ItemDetailParser();

        Debug.WriteLine("[GnjoyClient] Initialized with SocketsHttpHandler - MaxConnectionsPerServer=10, PooledConnectionLifetime=2min");
    }

    /// <summary>
    /// Fetch HTML content from URL using either WebView2 or HttpClient
    /// </summary>
    private async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken)
    {
        // Use WebView2 if enabled and ready
        if (_useWebView2 && _webView2Helper?.IsReady == true)
        {
            Debug.WriteLine($"[GnjoyClient] Using WebView2 to fetch: {url}");
            try
            {
                var html = await _webView2Helper.FetchHtmlAsync(url, cancellationToken);

                // Check if we got a valid response (not Cloudflare challenge page)
                if (html.Contains("Just a moment") || html.Contains("Checking your browser"))
                {
                    Debug.WriteLine("[GnjoyClient] WebView2 got Cloudflare challenge page, waiting longer...");
                    // Wait and retry once
                    await Task.Delay(3000, cancellationToken);
                    html = await _webView2Helper.FetchHtmlAsync(url, cancellationToken);
                }

                // Clear rate limit on successful WebView2 request
                ClearRateLimit();
                return html;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GnjoyClient] WebView2 fetch failed: {ex.Message}, falling back to HttpClient");
                // Fall through to HttpClient
            }
        }

        // Use HttpClient (original method)
        using var response = await GetWithRetryAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    /// HTTP GET with exponential backoff retry (1s -> 2s -> 4s -> 8s)
    /// Throws RateLimitException on 429 with Retry-After header
    /// </summary>
    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        Exception? lastException = null;

        // Check if currently rate limited (24-hour lockout)
        if (IsRateLimited)
        {
            var lockoutUntil = RateLimitedUntil!.Value;
            Debug.WriteLine($"[GnjoyClient] Currently locked out until {lockoutUntil:yyyy-MM-dd HH:mm}");
            throw new RateLimitException(lockoutUntil);
        }

        for (int retryCount = 0; retryCount <= MaxRetries; retryCount++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (retryCount > 0)
                {
                    // Exponential backoff: 1s, 2s, 4s, 8s
                    var delayMs = BaseRetryDelayMs * (int)Math.Pow(2, retryCount - 1);
                    Debug.WriteLine($"[GnjoyClient] Retry {retryCount}/{MaxRetries} after {delayMs}ms (exponential backoff): {url}");
                    await Task.Delay(delayMs, cancellationToken);
                }

                // Increment request counter for rate limit monitoring
                _sharedRateLimitManager.Value.IncrementRequestCounter();

                response = await _client.GetAsync(url, cancellationToken);

                // Handle 429 Too Many Requests - 24-hour lockout
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _sharedRateLimitManager.Value.SetRateLimit();
                    var lockoutUntil = _sharedRateLimitManager.Value.RateLimitedUntil!.Value;

                    Debug.WriteLine($"[GnjoyClient] HTTP 429 - Locked out until {lockoutUntil:yyyy-MM-dd HH:mm}");
                    response.Dispose();

                    throw new RateLimitException(lockoutUntil);
                }

                // Handle 503/504 - retry with exponential backoff
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    response.StatusCode == HttpStatusCode.GatewayTimeout)
                {
                    Debug.WriteLine($"[GnjoyClient] HTTP {(int)response.StatusCode} - will retry with backoff");
                    response.Dispose();
                    continue;
                }

                // Success - clear any previous rate limit
                if (response.IsSuccessStatusCode)
                {
                    ClearRateLimit();
                }

                return response;
            }
            catch (RateLimitException)
            {
                // Don't catch RateLimitException - let it propagate
                throw;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                Debug.WriteLine($"[GnjoyClient] HTTP error: {ex.Message}");
                response?.Dispose();
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
        }

        // All retries failed
        throw lastException ?? new HttpRequestException($"Failed after {MaxRetries} retries: {url}");
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

            Debug.WriteLine($"[GnjoyClient] FetchPriceHistoryAsync: {url}");

            // Use FetchHtmlAsync which handles WebView2/HttpClient selection
            var htmlContent = await FetchHtmlAsync(url, cancellationToken);
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

            // Use FetchHtmlAsync which handles WebView2/HttpClient selection
            var htmlContent = await FetchHtmlAsync(url, cancellationToken);
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

            // Use FetchHtmlAsync which handles WebView2/HttpClient selection
            var htmlContent = await FetchHtmlAsync(url, cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Item detail response length: {htmlContent.Length}");

            var detail = _detailParser.Parse(htmlContent);
            Debug.WriteLine($"[GnjoyClient] Parsed item detail: {detail.SlotInfo.Count} slots, {detail.RandomOptions.Count} options");

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
        Debug.WriteLine($"[GnjoyClient] ----- SearchItemDealsAsync START -----");
        Debug.WriteLine($"[GnjoyClient] itemName='{itemName}', page={page}, WebView2={_useWebView2}");

        try
        {
            // Convert UI server ID to GNJOY format for deal search
            // -1 stays as -1 (all servers), 1,2,3,4 convert to 129,229,529,729
            var gnjoyServerId = serverId == -1 ? -1 : ToQuoteServerId(serverId);
            var url = $"{BaseUrl}/{DealListEndpoint}?svrID={gnjoyServerId}&itemFullName={Uri.EscapeDataString(itemName)}&itemOrder=regdate&curpage={page}";
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync URL: {url}");

            // Use FetchHtmlAsync which handles WebView2/HttpClient selection
            var htmlContent = await FetchHtmlAsync(url, cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Response length: {htmlContent.Length} chars");

            var items = _parser.ParseDealList(htmlContent, serverId);
            Debug.WriteLine($"[GnjoyClient] Parsed {items.Count} items");
            return items;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync HTTP ERROR for '{itemName}': {ex.Message}");
            return new List<DealItem>();
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync CANCELLED for '{itemName}': {ex.Message}");
            return new List<DealItem>();
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync OPERATION CANCELLED for '{itemName}': {ex.Message}");
            return new List<DealItem>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync EXCEPTION for '{itemName}': {ex.GetType().Name} - {ex.Message}");
            return new List<DealItem>();
        }
    }

    /// <summary>
    /// Search for item deals by name (single page) with total count for pagination
    /// </summary>
    /// <param name="itemName">Item name to search (Korean)</param>
    /// <param name="serverId">Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <returns>DealSearchResult with items and total count</returns>
    public async Task<DealSearchResult> SearchItemDealsWithCountAsync(
        string itemName,
        int serverId = -1,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"[GnjoyClient] ----- SearchItemDealsWithCountAsync START -----");
        Debug.WriteLine($"[GnjoyClient] itemName='{itemName}', page={page}, WebView2={_useWebView2}");

        try
        {
            var gnjoyServerId = serverId == -1 ? -1 : ToQuoteServerId(serverId);
            var url = $"{BaseUrl}/{DealListEndpoint}?svrID={gnjoyServerId}&itemFullName={Uri.EscapeDataString(itemName)}&itemOrder=regdate&curpage={page}";
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsWithCountAsync URL: {url}");

            var htmlContent = await FetchHtmlAsync(url, cancellationToken);
            Debug.WriteLine($"[GnjoyClient] Response length: {htmlContent.Length} chars");

            var result = _parser.ParseDealListWithCount(htmlContent, serverId);
            result.CurrentPage = page;
            Debug.WriteLine($"[GnjoyClient] Parsed {result.Items.Count} items, total count: {result.TotalCount}");
            return result;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsWithCountAsync HTTP ERROR for '{itemName}': {ex.Message}");
            return new DealSearchResult { CurrentPage = page };
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsWithCountAsync CANCELLED for '{itemName}'");
            return new DealSearchResult { CurrentPage = page };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsWithCountAsync OPERATION CANCELLED for '{itemName}'");
            return new DealSearchResult { CurrentPage = page };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsWithCountAsync EXCEPTION for '{itemName}': {ex.GetType().Name} - {ex.Message}");
            return new DealSearchResult { CurrentPage = page };
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
        Debug.WriteLine($"[GnjoyClient] ========== SearchAllItemDealsAsync START ==========");
        Debug.WriteLine($"[GnjoyClient] itemName='{itemName}', serverId={serverId}, maxPages={maxPages}");
        Debug.WriteLine($"[GnjoyClient] cancellationToken.IsCancellationRequested={cancellationToken.IsCancellationRequested}");

        var allItems = new List<DealItem>();
        int page = 1;

        while (page <= maxPages)
        {
            Debug.WriteLine($"[GnjoyClient] Loop iteration: page={page}, IsCancellationRequested={cancellationToken.IsCancellationRequested}");

            if (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"[GnjoyClient] CANCELLED at start of loop! Breaking.");
                break;
            }

            Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: Fetching page {page}");
            var items = await SearchItemDealsAsync(itemName, serverId, page, cancellationToken);
            Debug.WriteLine($"[GnjoyClient] SearchItemDealsAsync returned {items.Count} items");

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
        }

        Debug.WriteLine($"[GnjoyClient] SearchAllItemDealsAsync: Completed with {allItems.Count} total items from {page} page(s)");
        return allItems;
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
        Debug.WriteLine("[GnjoyClient] Disposed HttpClient and SocketsHttpHandler");
    }
}
