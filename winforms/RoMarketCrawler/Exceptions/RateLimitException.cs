namespace RoMarketCrawler.Exceptions;

/// <summary>
/// Exception thrown when API rate limit (HTTP 429) is encountered.
/// Triggers a 24-hour lockout to protect the user's IP from extended blocking.
/// </summary>
public class RateLimitException : Exception
{
    /// <summary>
    /// Time when the lockout will expire
    /// </summary>
    public DateTime LockoutUntil { get; }

    /// <summary>
    /// Formatted unlock datetime string for display
    /// </summary>
    public string UnlockTimeText => LockoutUntil.ToString("yyyy-MM-dd HH:mm");

    public RateLimitException(DateTime lockoutUntil)
        : base($"API 요청이 제한되었습니다. {lockoutUntil:yyyy-MM-dd HH:mm} 이후 이용 가능합니다.")
    {
        LockoutUntil = lockoutUntil;
    }
}
