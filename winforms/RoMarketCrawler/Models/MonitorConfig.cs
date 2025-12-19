using System;
using System.Collections.Generic;

namespace RoMarketCrawler.Models
{
    /// <summary>
    /// Configuration for monitoring feature including item list and refresh settings
    /// </summary>
    public class MonitorConfig
    {
        /// <summary>
        /// List of items to monitor
        /// </summary>
        public List<MonitorItem> Items { get; set; } = new();

        /// <summary>
        /// Auto-refresh interval in seconds (0 = disabled, minimum 60 seconds = 1 minute)
        /// </summary>
        public int RefreshIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// Default server ID for new items (-1 = all servers)
        /// </summary>
        public int DefaultServerId { get; set; } = -1;
    }

    /// <summary>
    /// Single item to monitor
    /// </summary>
    public class MonitorItem
    {
        /// <summary>
        /// Item name to search (exact match)
        /// </summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>
        /// Server ID to search (-1 = all servers, 1-4 = specific server)
        /// </summary>
        public int ServerId { get; set; } = -1;

        /// <summary>
        /// When this item was added to monitoring list
        /// </summary>
        public DateTime AddedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Watch price threshold - alert when current price drops below this (null = disabled)
        /// </summary>
        public long? WatchPrice { get; set; }

        /// <summary>
        /// Server name for display (computed from ServerId)
        /// </summary>
        public string ServerName => Server.GetServerName(ServerId);

        /// <summary>
        /// Next scheduled refresh time (runtime only, not persisted)
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? NextRefreshTime { get; set; }

        /// <summary>
        /// Whether this item is waiting in the refresh queue (runtime only)
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsQueued { get; set; }

        /// <summary>
        /// Whether this item is currently being refreshed (runtime only)
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsRefreshing { get; set; }

        /// <summary>
        /// Whether this item's results are being processed/rendered (runtime only)
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsProcessing { get; set; }
    }

    /// <summary>
    /// Result of monitoring a single item - combines deals and price statistics
    /// </summary>
    public class MonitorResult
    {
        /// <summary>
        /// The monitored item configuration
        /// </summary>
        public MonitorItem Item { get; set; } = new();

        /// <summary>
        /// List of current deals found for this item
        /// </summary>
        public List<DealItem> Deals { get; set; } = new();

        /// <summary>
        /// Price statistics from price history
        /// </summary>
        public PriceStatistics? Statistics { get; set; }

        /// <summary>
        /// Lowest price among current deals
        /// </summary>
        public long? LowestCurrentPrice => Deals.Count > 0 ? Deals.Min(d => d.Price) : null;

        /// <summary>
        /// Number of current deals
        /// </summary>
        public int DealCount => Deals.Count;

        /// <summary>
        /// Check if lowest price is below yesterday's average
        /// </summary>
        public bool IsBelowYesterdayAvg =>
            LowestCurrentPrice.HasValue &&
            Statistics?.YesterdayAvgPrice.HasValue == true &&
            LowestCurrentPrice.Value < Statistics.YesterdayAvgPrice.Value;

        /// <summary>
        /// Check if lowest price is below 7-day average
        /// </summary>
        public bool IsBelowWeekAvg =>
            LowestCurrentPrice.HasValue &&
            Statistics?.Week7AvgPrice.HasValue == true &&
            LowestCurrentPrice.Value < Statistics.Week7AvgPrice.Value;

        /// <summary>
        /// Check if price is a good deal (below both averages)
        /// </summary>
        public bool IsGoodDeal => IsBelowYesterdayAvg && IsBelowWeekAvg;

        /// <summary>
        /// Last refresh time
        /// </summary>
        public DateTime LastRefreshed { get; set; } = DateTime.Now;

        /// <summary>
        /// Error message if refresh failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Whether this result was blocked by rate limiting
        /// </summary>
        public bool IsRateLimited { get; set; }

        /// <summary>
        /// Time when rate limit will expire (if rate limited)
        /// </summary>
        public DateTime? RateLimitedUntil { get; set; }

        /// <summary>
        /// Price difference from 7-day average (percentage)
        /// </summary>
        public double? PriceDiffPercent
        {
            get
            {
                if (!LowestCurrentPrice.HasValue || Statistics?.Week7AvgPrice == null || Statistics.Week7AvgPrice == 0)
                    return null;

                return ((double)LowestCurrentPrice.Value - Statistics.Week7AvgPrice.Value) / Statistics.Week7AvgPrice.Value * 100;
            }
        }
    }
}
