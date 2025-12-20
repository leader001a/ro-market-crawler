namespace RoMarketCrawler.Exceptions;

/// <summary>
/// Exception thrown when API rate limit (HTTP 429) is encountered
/// </summary>
public class RateLimitException : Exception
{
    /// <summary>
    /// Number of seconds to wait before retrying (from Retry-After header)
    /// </summary>
    public int RetryAfterSeconds { get; }

    /// <summary>
    /// Time when rate limit will expire
    /// </summary>
    public DateTime RetryAfterTime { get; }

    /// <summary>
    /// Human-readable remaining time description
    /// </summary>
    public string RemainingTimeText
    {
        get
        {
            var remaining = RetryAfterTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
                return "곧 해제됩니다";

            if (remaining.TotalMinutes >= 1)
                return $"약 {(int)remaining.TotalMinutes}분 {remaining.Seconds}초 후 해제";

            return $"약 {(int)remaining.TotalSeconds}초 후 해제";
        }
    }

    public RateLimitException(int retryAfterSeconds)
        : base($"API 요청 제한됨. {retryAfterSeconds}초 후 재시도 가능")
    {
        RetryAfterSeconds = retryAfterSeconds;
        RetryAfterTime = DateTime.Now.AddSeconds(retryAfterSeconds);
    }

    public RateLimitException(int retryAfterSeconds, string message)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
        RetryAfterTime = DateTime.Now.AddSeconds(retryAfterSeconds);
    }
}
