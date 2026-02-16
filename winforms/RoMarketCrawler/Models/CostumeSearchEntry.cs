namespace RoMarketCrawler.Models;

/// <summary>
/// A single costume search history entry
/// </summary>
public class CostumeSearchEntry
{
    public string? StoneName { get; set; }
    public string? ItemName { get; set; }

    public string DisplayText =>
        (!string.IsNullOrEmpty(ItemName) && !string.IsNullOrEmpty(StoneName))
            ? $"{ItemName} | {StoneName}"
            : ItemName ?? StoneName ?? "";
}
