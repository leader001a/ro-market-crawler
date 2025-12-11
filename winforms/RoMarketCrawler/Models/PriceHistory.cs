namespace RoMarketCrawler.Models;

/// <summary>
/// Daily price history data from GNJOY 시세 조회
/// </summary>
public class PriceHistory
{
    public DateTime Date { get; set; }
    public long MinPrice { get; set; }
    public long AvgPrice { get; set; }
    public long MaxPrice { get; set; }
}

/// <summary>
/// Price statistics calculated from historical data
/// </summary>
public class PriceStatistics
{
    public string ItemName { get; set; } = string.Empty;
    public int ServerId { get; set; }

    /// <summary>
    /// Yesterday's average price (어제 평균가)
    /// </summary>
    public long? YesterdayAvgPrice { get; set; }

    /// <summary>
    /// 7-day average price (7일 평균가)
    /// </summary>
    public long? Week7AvgPrice { get; set; }

    /// <summary>
    /// 7-day minimum price (7일 최저가)
    /// </summary>
    public long? Week7MinPrice { get; set; }

    /// <summary>
    /// 7-day maximum price (7일 최고가)
    /// </summary>
    public long? Week7MaxPrice { get; set; }

    /// <summary>
    /// Number of days with data in the last 7 days
    /// </summary>
    public int DataDays { get; set; }

    /// <summary>
    /// Raw price history records
    /// </summary>
    public List<PriceHistory> History { get; set; } = new();

    /// <summary>
    /// Calculate statistics from history data
    /// </summary>
    public static PriceStatistics FromHistory(string itemName, int serverId, List<PriceHistory> history)
    {
        var stats = new PriceStatistics
        {
            ItemName = itemName,
            ServerId = serverId,
            History = history,
            DataDays = history.Count
        };

        if (history.Count == 0)
            return stats;

        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var weekAgo = today.AddDays(-7);

        // Find yesterday's data
        var yesterdayData = history.FirstOrDefault(h => h.Date.Date == yesterday);
        if (yesterdayData != null)
        {
            stats.YesterdayAvgPrice = yesterdayData.AvgPrice;
        }

        // Calculate 7-day statistics
        var weekData = history.Where(h => h.Date >= weekAgo).ToList();
        if (weekData.Count > 0)
        {
            stats.Week7AvgPrice = (long)weekData.Average(h => h.AvgPrice);
            stats.Week7MinPrice = weekData.Min(h => h.MinPrice);
            stats.Week7MaxPrice = weekData.Max(h => h.MaxPrice);
        }

        return stats;
    }
}
