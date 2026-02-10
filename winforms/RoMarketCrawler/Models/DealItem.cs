namespace RoMarketCrawler.Models;

/// <summary>
/// Item deal listing from GNJOY search
/// </summary>
public class DealItem
{
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public int? ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ItemImageUrl { get; set; }
    public int? Refine { get; set; }
    public string? Grade { get; set; }
    public string? CardSlots { get; set; }
    public int Quantity { get; set; }
    public long Price { get; set; }
    public string? PriceFormatted { get; set; }
    public string? DealType { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public DateTime CrawledAt { get; set; } = DateTime.Now;
    public int? CrawledPage { get; set; }  // Page number where this item was crawled

    // Item detail view parameters (for fetching enchant/card info)
    public int? MapId { get; set; }
    public string? Ssi { get; set; }  // Unique item identifier for itemDealView.asp

    // Enchant and card info from item detail view
    public List<string> SlotInfo { get; set; } = new();  // 인챈트/카드 목록
    public List<string> RandomOptions { get; set; } = new();  // 랜덤 옵션 목록
    public string? Element { get; set; }  // 속성 (e.g., "무속성 : 0")
    public string? Maker { get; set; }  // 제조자

    // Price statistics from GNJOY 시세 조회
    public long? YesterdayAvgPrice { get; set; }
    public long? Week7AvgPrice { get; set; }
    public long? Week7MinPrice { get; set; }
    public long? Week7MaxPrice { get; set; }

    public void ComputeFields()
    {
        // Auto-generate PriceFormatted
        if (string.IsNullOrEmpty(PriceFormatted))
        {
            PriceFormatted = Price.ToString("N0");
        }

        // Auto-generate DisplayName
        // Format: [Grade]+Refine아이템명 [CardSlots]
        // Note: No space between grade/refine and item name (matches original game format)
        if (string.IsNullOrEmpty(DisplayName))
        {
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(Grade))
            {
                sb.Append($"[{Grade}]");
            }
            if (Refine.HasValue && Refine > 0)
            {
                sb.Append($"+{Refine}");
            }
            sb.Append(ItemName);
            if (!string.IsNullOrEmpty(CardSlots))
            {
                sb.Append($"[{CardSlots}]");
            }
            DisplayName = sb.ToString();
        }
    }

    public string DealTypeDisplay => DealType switch
    {
        "buy" => "구매",
        "sale" => "판매",
        _ => DealType ?? "-"
    };

    // Formatted display properties for UI
    public string YesterdayPriceDisplay =>
        YesterdayAvgPrice.HasValue ? YesterdayAvgPrice.Value.ToString("N0") : "-";

    public string Week7AvgPriceDisplay =>
        Week7AvgPrice.HasValue ? Week7AvgPrice.Value.ToString("N0") : "-";

    /// <summary>
    /// Slot info display for UI (enchants and cards) - vertical layout
    /// </summary>
    public string SlotInfoDisplay =>
        SlotInfo?.Count > 0 ? string.Join(Environment.NewLine, SlotInfo) : "-";

    /// <summary>
    /// Random options display for UI
    /// </summary>
    public string RandomOptionsDisplay =>
        RandomOptions?.Count > 0 ? string.Join(", ", RandomOptions) : "-";

    /// <summary>
    /// Combined display for cards, enchants, and random options
    /// </summary>
    public string SlotAndOptionsDisplay
    {
        get
        {
            var parts = new List<string>();

            if (SlotInfo?.Count > 0)
                parts.AddRange(SlotInfo);

            if (RandomOptions?.Count > 0)
                parts.AddRange(RandomOptions);

            return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : "-";
        }
    }

    /// <summary>
    /// Price comparison with 7-day average (percentage change)
    /// </summary>
    public string PriceCompareDisplay
    {
        get
        {
            if (!Week7AvgPrice.HasValue || Week7AvgPrice.Value == 0 || Price == 0)
                return "-";

            var diff = ((double)Price - Week7AvgPrice.Value) / Week7AvgPrice.Value * 100;

            if (Math.Abs(diff) < 1)
                return "=";
            else if (diff > 0)
                return $"+{diff:F0}%";
            else
                return $"{diff:F0}%";
        }
    }

    /// <summary>
    /// Apply price statistics from PriceStatistics
    /// </summary>
    public void ApplyStatistics(PriceStatistics? stats)
    {
        if (stats == null) return;

        YesterdayAvgPrice = stats.YesterdayAvgPrice;
        Week7AvgPrice = stats.Week7AvgPrice;
        Week7MinPrice = stats.Week7MinPrice;
        Week7MaxPrice = stats.Week7MaxPrice;
    }

    /// <summary>
    /// Apply item detail info (enchants, cards, random options)
    /// </summary>
    public void ApplyDetailInfo(Services.ItemDetailInfo? detail)
    {
        if (detail == null) return;

        SlotInfo = detail.SlotInfo ?? new();
        RandomOptions = detail.RandomOptions ?? new();
        Element = detail.Element;
        Maker = detail.Maker;
    }

    /// <summary>
    /// Check if this item has detail parameters for fetching enchant/card info
    /// </summary>
    public bool HasDetailParams => MapId.HasValue && !string.IsNullOrEmpty(Ssi);

    /// <summary>
    /// Try to extract item ID from GNJOY image URL
    /// GNJOY pattern: https://imgc1.gnjoy.com/games/ro1/object/201306/{ID}.png
    /// </summary>
    public int? ExtractItemIdFromImageUrl()
    {
        if (string.IsNullOrEmpty(ItemImageUrl)) return null;

        // Pattern 1: GNJOY format /object/XXXXXX/{ID}.png (e.g., /object/201306/5035.png)
        var match = System.Text.RegularExpressions.Regex.Match(
            ItemImageUrl,
            @"/object/\d+/(\d+)\.png",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
        {
            return id;
        }

        // Pattern 2: /data/item/XXXXX.gif or /data/item/XXXXX.png
        match = System.Text.RegularExpressions.Regex.Match(
            ItemImageUrl,
            @"/data/item/(\d+)\.(gif|png|jpg)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out id))
        {
            return id;
        }

        // Pattern 3: item_id=XXXXX in query string
        match = System.Text.RegularExpressions.Regex.Match(
            ItemImageUrl,
            @"item_id=(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out id))
        {
            return id;
        }

        // Pattern 4: Just a number in the filename like /images/19117.gif (3+ digits)
        match = System.Text.RegularExpressions.Regex.Match(
            ItemImageUrl,
            @"/(\d{3,6})\.(gif|png|jpg)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out id))
        {
            return id;
        }

        return null;
    }

    /// <summary>
    /// Get the effective ItemId - either from parsed data or extracted from image URL
    /// </summary>
    public int? GetEffectiveItemId()
    {
        // First try the directly parsed ItemId
        if (ItemId.HasValue) return ItemId;

        // Then try to extract from image URL
        return ExtractItemIdFromImageUrl();
    }

    /// <summary>
    /// Extract base item name by removing card/enchant prefixes/suffixes
    /// Cards add prefixes like "포링", "미노타우르스" to item names
    /// Enchants add suffixes like "오브 키린", "오브 인피니티"
    /// </summary>
    public string GetBaseItemName()
    {
        var baseName = ItemName;

        // Step 1: Remove known enchant suffix patterns directly from item name
        // These patterns are added by special cards/enchants and may not appear in SlotInfo
        // Pattern: "오브 X" where X is Korean text (e.g., "오브 키린", "오브 인피니티")
        // The negative lookbehind (?<!\s) ensures we don't match "오브" when it's a standalone word
        // like in "스태프 오브 디스트럭션" (Staff of Destruction)
        // This correctly matches:
        //   - "포링 선글래스+오브 키린" -> "포링 선글래스+" (+ before 오브)
        //   - "클로스오브 이프리트" -> "클로스" (no separator before 오브)
        // But NOT:
        //   - "스태프 오브 디스트럭션" (space before 오브, not an enchant)
        var orbPattern = @"(?<!\s)오브\s+[가-힣]+$";
        baseName = System.Text.RegularExpressions.Regex.Replace(baseName, orbPattern, "").Trim();

        // Step 2: If no slot info, return after pattern-based removal
        if (SlotInfo == null || SlotInfo.Count == 0)
        {
            return baseName;
        }

        // Step 3: Extract all slot items that might be part of the item name
        // This includes:
        // 1. Card prefixes: "미노타우르스 카드" -> "미노타우르스"
        // 2. Enchant suffixes: "오브 키린" (used as-is)
        var slotParts = new List<string>();
        foreach (var slot in SlotInfo)
        {
            if (slot.Contains("카드"))
            {
                // Card: Extract prefix from card name: "미노타우르스 카드" -> "미노타우르스"
                var cardName = slot.Replace(" 카드", "").Replace("카드", "").Trim();
                if (!string.IsNullOrEmpty(cardName))
                {
                    slotParts.Add(cardName);
                }
            }
            else
            {
                // Enchant: Use as-is (e.g., "오브 키린", "오브 인피니티")
                var enchantName = slot.Trim();
                if (!string.IsNullOrEmpty(enchantName))
                {
                    slotParts.Add(enchantName);
                }
            }
        }

        // Step 4: Remove slot parts from item name (as prefix or suffix)
        foreach (var part in slotParts)
        {
            // Try removing as prefix (card prefixes)
            if (baseName.StartsWith(part + " "))
            {
                baseName = baseName.Substring(part.Length + 1).Trim();
            }
            else if (baseName.StartsWith(part))
            {
                baseName = baseName.Substring(part.Length).Trim();
            }
            // Try removing as suffix (enchant suffixes like "오브 X")
            else if (baseName.EndsWith(" " + part))
            {
                baseName = baseName.Substring(0, baseName.Length - part.Length - 1).Trim();
            }
            else if (baseName.EndsWith(part))
            {
                baseName = baseName.Substring(0, baseName.Length - part.Length).Trim();
            }
        }

        return baseName;
    }
}

/// <summary>
/// Search result with items and total count for pagination
/// </summary>
public class DealSearchResult
{
    public List<DealItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / 10.0);
    public int CurrentPage { get; set; } = 1;
    public bool HasMorePages => CurrentPage < TotalPages;
}
