namespace RoMarketCrawler.Models;

/// <summary>
/// Configuration for costume monitoring (watch list)
/// </summary>
public class CostumeMonitorConfig
{
    public List<CostumeWatchItem> Items { get; set; } = new();
}

/// <summary>
/// A single costume watch item for price monitoring
/// </summary>
public class CostumeWatchItem
{
    public string StoneName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public long WatchPrice { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
