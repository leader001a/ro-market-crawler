using System.Text.Json.Serialization;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Models;

/// <summary>
/// Metadata for the item index file, tracking version and category information
/// </summary>
public class ItemIndexMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("categories")]
    public Dictionary<int, CategoryMeta> Categories { get; set; } = new();

    /// <summary>
    /// Validate metadata integrity
    /// </summary>
    public bool Validate()
    {
        if (string.IsNullOrEmpty(Version)) return false;
        if (CreatedAt == default) return false;
        if (UpdatedAt == default) return false;
        if (TotalCount < 0) return false;

        // Verify category counts sum to total
        var categorySum = Categories.Values.Sum(c => c.Count);
        if (categorySum != TotalCount) return false;

        return true;
    }
}

/// <summary>
/// Metadata for a single item category
/// </summary>
public class CategoryMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("max_pages")]
    public int MaxPages { get; set; }
}

/// <summary>
/// Root structure for the item index JSON file
/// </summary>
public class ItemIndexFile
{
    [JsonPropertyName("metadata")]
    public ItemIndexMetadata Metadata { get; set; } = new();

    [JsonPropertyName("items")]
    public Dictionary<string, KafraItemDto> Items { get; set; } = new();
}

/// <summary>
/// Simplified KafraItem DTO for JSON serialization
/// Contains only essential fields for search optimization
/// </summary>
public class KafraItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("screen_name")]
    public string? ScreenName { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("slots")]
    public int Slots { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("price_buy")]
    public int? PriceBuy { get; set; }

    [JsonPropertyName("price_sell")]
    public int? PriceSell { get; set; }

    [JsonPropertyName("item_text")]
    public string? ItemText { get; set; }

    [JsonPropertyName("equip_jobs_text")]
    public string? EquipJobsText { get; set; }

    /// <summary>
    /// Parsed structured details from item_text
    /// </summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ParsedItemDetails? Details { get; set; }

    /// <summary>
    /// Get item type display name in Korean
    /// </summary>
    public string GetTypeDisplayName()
    {
        return ItemTypes.GetTypeName(Type);
    }

    /// <summary>
    /// Convert to KafraItem for compatibility with existing UI binding
    /// </summary>
    public KafraItem ToKafraItem()
    {
        return new KafraItem
        {
            ItemConst = Id,
            Name = Name,
            ScreenName = ScreenName,
            Type = Type,
            Slots = Slots,
            Weight = Weight,
            PriceBuy = PriceBuy,
            PriceSell = PriceSell,
            ItemText = ItemText,
            EquipJobsText = EquipJobsText
        };
    }
}

/// <summary>
/// Static helper for item type definitions
/// </summary>
public static class ItemTypes
{
    /// <summary>
    /// All item types to collect from kafra.kr
    /// Includes 0-50 range plus special types:
    /// - 998 (기타/Etc) - discovered from kafra.kr URL
    /// - 999 (전체/All) - may contain items not in individual categories
    /// </summary>
    public static readonly int[] CollectibleTypes = Enumerable.Range(0, 51).Concat(new[] { 998, 999 }).ToArray();

    /// <summary>
    /// Known item type names in Korean (from kafra.kr URL structure)
    /// </summary>
    public static readonly Dictionary<int, string> TypeNames = new()
    {
        { 0, "힐링 아이템" },
        { 2, "사용 아이템" },
        { 3, "기타 아이템" },
        { 4, "무기" },
        { 5, "방어구" },
        { 6, "카드" },
        { 7, "펫 알" },
        { 8, "펫 장비" },
        { 10, "화살/탄환" },
        { 11, "딜레이 소모품" },
        { 18, "상자/패키지" },
        { 19, "쉐도우 장비" },
        { 20, "의상" },
        { 998, "기타" },
        { 999, "전체" }
    };

    public static string GetTypeName(int type)
    {
        return TypeNames.TryGetValue(type, out var name) ? name : $"타입 {type}";
    }
}
