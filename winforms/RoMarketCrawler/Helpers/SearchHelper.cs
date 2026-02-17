namespace RoMarketCrawler.Helpers;

/// <summary>
/// Search utility supporting % wildcard matching.
/// "명중%걸칠" matches any string containing "명중" followed (anywhere) by "걸칠".
/// </summary>
public static class SearchHelper
{
    /// <summary>
    /// Check if <paramref name="text"/> matches the <paramref name="pattern"/>.
    /// The pattern may contain '%' as a wildcard (matches any characters).
    /// Without '%', behaves like a simple Contains check.
    /// </summary>
    public static bool WildcardContains(string? text, string pattern)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var parts = pattern.Split('%', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;

        int searchFrom = 0;
        foreach (var part in parts)
        {
            var idx = text.IndexOf(part, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            searchFrom = idx + part.Length;
        }
        return true;
    }
}
