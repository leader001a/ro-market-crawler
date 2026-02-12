namespace RoMarketCrawler.Models;

/// <summary>
/// A complete crawl session result (saved to JSON)
/// </summary>
public class CrawlSession
{
    public string SearchTerm { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public DateTime CrawledAt { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public List<DealItem> Items { get; set; } = new();

    /// <summary>
    /// Last page that was successfully crawled (0 = not started, TotalPages = complete)
    /// Used for resume functionality when crawling is interrupted.
    /// </summary>
    public int LastCrawledPage { get; set; }

    /// <summary>
    /// Total pages available on the server at the time of crawling.
    /// Used to determine if crawling was completed.
    /// </summary>
    public int TotalServerPages { get; set; }

    /// <summary>
    /// Whether this session was collected via incremental update
    /// </summary>
    public bool IsIncremental { get; set; }

    /// <summary>
    /// Whether this session was fully completed (all pages crawled)
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsComplete => TotalServerPages > 0 && LastCrawledPage >= TotalServerPages;
}

/// <summary>
/// Filter criteria for local search on crawled data
/// </summary>
public class CrawlSearchFilter
{
    public string? ItemName { get; set; }
    public string? CardEnchant { get; set; }
    public long? MinPrice { get; set; }
    public long? MaxPrice { get; set; }
}

/// <summary>
/// Metadata about a saved crawl file
/// </summary>
public class CrawlFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public DateTime CrawledAt { get; set; }
    public int TotalItems { get; set; }
}
