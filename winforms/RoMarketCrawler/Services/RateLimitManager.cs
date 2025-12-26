using System.Diagnostics;
using RoMarketCrawler.Interfaces;

namespace RoMarketCrawler.Services;

/// <summary>
/// Centralized rate limit manager for API clients.
/// Thread-safe implementation with event-based notifications.
/// </summary>
public class RateLimitManager : IRateLimitManager
{
    private readonly object _lock = new();
    private DateTime? _rateLimitedUntil;
    private int _requestCount;
    private DateTime _counterStartTime = DateTime.Now;

    /// <inheritdoc/>
    public event EventHandler<RateLimitChangedEventArgs>? RateLimitChanged;

    /// <inheritdoc/>
    public DateTime? RateLimitedUntil
    {
        get
        {
            lock (_lock)
            {
                return _rateLimitedUntil;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsRateLimited
    {
        get
        {
            lock (_lock)
            {
                return _rateLimitedUntil.HasValue && DateTime.Now < _rateLimitedUntil.Value;
            }
        }
    }

    /// <inheritdoc/>
    public int RemainingRateLimitSeconds
    {
        get
        {
            lock (_lock)
            {
                if (!_rateLimitedUntil.HasValue || DateTime.Now >= _rateLimitedUntil.Value)
                    return 0;
                return (int)(_rateLimitedUntil.Value - DateTime.Now).TotalSeconds;
            }
        }
    }

    /// <inheritdoc/>
    public int RequestCount
    {
        get
        {
            lock (_lock)
            {
                ResetCounterIfNeeded();
                return _requestCount;
            }
        }
    }

    /// <inheritdoc/>
    public double RequestsPerMinute
    {
        get
        {
            lock (_lock)
            {
                ResetCounterIfNeeded();
                var elapsed = (DateTime.Now - _counterStartTime).TotalMinutes;
                return elapsed > 0 ? _requestCount / elapsed : 0;
            }
        }
    }

    /// <inheritdoc/>
    public void SetRateLimit(int retryAfterSeconds)
    {
        lock (_lock)
        {
            _rateLimitedUntil = DateTime.Now.AddSeconds(retryAfterSeconds);
            Debug.WriteLine($"[RateLimitManager] Rate limited for {retryAfterSeconds}s (until {_rateLimitedUntil})");
        }

        RaiseRateLimitChanged();
    }

    /// <inheritdoc/>
    public void ClearRateLimit()
    {
        bool wasLimited;
        lock (_lock)
        {
            wasLimited = _rateLimitedUntil.HasValue;
            _rateLimitedUntil = null;
            Debug.WriteLine("[RateLimitManager] Rate limit cleared");
        }

        if (wasLimited)
        {
            RaiseRateLimitChanged();
        }
    }

    /// <inheritdoc/>
    public void IncrementRequestCounter()
    {
        lock (_lock)
        {
            ResetCounterIfNeeded();
            _requestCount++;
            Debug.WriteLine($"[RateLimitManager] Request #{_requestCount} (Rate: {RequestsPerMinute:F1}/min)");
        }
    }

    /// <summary>
    /// Reset counter if more than 1 minute has passed
    /// </summary>
    private void ResetCounterIfNeeded()
    {
        if ((DateTime.Now - _counterStartTime).TotalMinutes >= 1)
        {
            Debug.WriteLine($"[RateLimitManager] Counter reset - Previous: {_requestCount} requests in 1 minute");
            _requestCount = 0;
            _counterStartTime = DateTime.Now;
        }
    }

    private void RaiseRateLimitChanged()
    {
        RateLimitChanged?.Invoke(this, new RateLimitChangedEventArgs
        {
            IsRateLimited = IsRateLimited,
            RateLimitedUntil = RateLimitedUntil,
            RemainingSeconds = RemainingRateLimitSeconds
        });
    }
}
