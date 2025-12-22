using System.Text.Json.Serialization;

namespace RoMarketCrawler.Models;

/// <summary>
/// GitHub Release API response model
/// </summary>
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();

    /// <summary>
    /// Parse version from tag name (e.g., "v1.0.3" -> Version(1,0,3))
    /// </summary>
    public Version? GetVersion()
    {
        var versionStr = TagName.TrimStart('v', 'V');
        return Version.TryParse(versionStr, out var version) ? version : null;
    }
}

/// <summary>
/// GitHub Release Asset model
/// </summary>
public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
}
