using System.Reflection;

namespace RoMarketCrawler.Services;

/// <summary>
/// Helper class for loading embedded resources
/// </summary>
public static class ResourceHelper
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    /// <summary>
    /// Load an embedded resource as a stream
    /// </summary>
    /// <param name="resourceName">The logical name of the resource (e.g., "RoMarketCrawler.Data.logo.png")</param>
    /// <returns>Stream containing the resource, or null if not found</returns>
    public static Stream? GetResourceStream(string resourceName)
    {
        return _assembly.GetManifestResourceStream(resourceName);
    }

    /// <summary>
    /// Load an embedded image resource
    /// </summary>
    /// <param name="resourceName">The logical name of the resource</param>
    /// <returns>Image loaded from the resource, or null if not found</returns>
    public static Image? GetImage(string resourceName)
    {
        using var stream = GetResourceStream(resourceName);
        if (stream == null) return null;

        // Create a copy to avoid stream disposal issues
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return Image.FromStream(ms);
    }

    /// <summary>
    /// Load an embedded text resource
    /// </summary>
    /// <param name="resourceName">The logical name of the resource</param>
    /// <returns>String content of the resource, or null if not found</returns>
    public static string? GetText(string resourceName)
    {
        using var stream = GetResourceStream(resourceName);
        if (stream == null) return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Get the logo image
    /// </summary>
    public static Image? GetLogoImage()
    {
        return GetImage("RoMarketCrawler.Data.logo.png");
    }

    /// <summary>
    /// Get the enchant effects JSON content
    /// </summary>
    public static string? GetEnchantEffectsJson()
    {
        return GetText("RoMarketCrawler.Data.EnchantEffects.json");
    }
}
