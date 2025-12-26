namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for managing API rate limit state.
/// Provides centralized rate limit tracking across all API clients.
/// </summary>
public interface IRateLimitManager
{
    /// <summary>
    /// Event fired when rate limit status changes
    /// </summary>
    event EventHandler<RateLimitChangedEventArgs>? RateLimitChanged;

    /// <summary>
    /// Current rate limit status - null if not rate limited
    /// </summary>
    DateTime? RateLimitedUntil { get; }

    /// <summary>
    /// Check if currently rate limited
    /// </summary>
    bool IsRateLimited { get; }

    /// <summary>
    /// Get remaining rate limit time in seconds
    /// </summary>
    int RemainingRateLimitSeconds { get; }

    /// <summary>
    /// Get current request count in the monitoring window
    /// </summary>
    int RequestCount { get; }

    /// <summary>
    /// Get requests per minute rate
    /// </summary>
    double RequestsPerMinute { get; }

    /// <summary>
    /// Set rate limit status (called when 429 response received)
    /// </summary>
    /// <param name="retryAfterSeconds">Number of seconds to wait</param>
    void SetRateLimit(int retryAfterSeconds);

    /// <summary>
    /// Clear rate limit status (called when rate limit is lifted)
    /// </summary>
    void ClearRateLimit();

    /// <summary>
    /// Increment request counter (called before each API request)
    /// </summary>
    void IncrementRequestCounter();
}

/// <summary>
/// Event arguments for rate limit status changes
/// </summary>
public class RateLimitChangedEventArgs : EventArgs
{
    /// <summary>
    /// Whether currently rate limited
    /// </summary>
    public bool IsRateLimited { get; init; }

    /// <summary>
    /// Rate limited until (null if not limited)
    /// </summary>
    public DateTime? RateLimitedUntil { get; init; }

    /// <summary>
    /// Remaining seconds if rate limited
    /// </summary>
    public int RemainingSeconds { get; init; }
}
