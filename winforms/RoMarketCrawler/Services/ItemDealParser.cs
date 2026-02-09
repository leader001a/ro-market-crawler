using System.Diagnostics;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Parser for GNJOY itemDealList.asp HTML response
/// </summary>
public class ItemDealParser
{
    /// <summary>
    /// Parse deal list and return result with total count
    /// </summary>
    public DealSearchResult ParseDealListWithCount(string html, int defaultServerId = -1)
    {
        var result = new DealSearchResult
        {
            Items = ParseDealList(html, defaultServerId),
            TotalCount = ParseTotalCount(html)
        };
        return result;
    }

    /// <summary>
    /// Parse total count from HTML (e.g., "검색결과 : 2,161건" or "검색결과 : <b>2,161건</b>")
    /// </summary>
    public int ParseTotalCount(string html)
    {
        try
        {
            // Pattern: "검색결과 : 2,161건" - handles HTML tags like <b>, <strong> around the number
            // Examples: "검색결과 : 2,161건", "검색결과 : <b>2,161건</b>", "검색결과: <strong>2,161</strong>건"
            var match = Regex.Match(html, @"검색결과\s*:\s*(?:<[^>]+>\s*)*([\d,]+)\s*건");
            if (match.Success)
            {
                var countStr = match.Groups[1].Value.Replace(",", "");
                if (int.TryParse(countStr, out var count))
                {
                    Debug.WriteLine($"[ItemDealParser] Total count: {count}");
                    return count;
                }
            }

            // Fallback: Try to find any pattern with numbers followed by 건
            match = Regex.Match(html, @"검색결과[^<]*?(?:<[^>]*>)*\s*([\d,]+)\s*(?:<[^>]*>)*\s*건");
            if (match.Success)
            {
                var countStr = match.Groups[1].Value.Replace(",", "");
                if (int.TryParse(countStr, out var count))
                {
                    Debug.WriteLine($"[ItemDealParser] Total count (fallback): {count}");
                    return count;
                }
            }

            Debug.WriteLine($"[ItemDealParser] Total count not found in HTML (length: {html.Length})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDealParser] ParseTotalCount failed: {ex.Message}");
        }
        return 0;
    }

    public List<DealItem> ParseDealList(string html, int defaultServerId = -1)
    {
        var items = new List<DealItem>();

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find the deal table
            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'dealList')]");
            if (table == null)
            {
                table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'tbl_deal')]");
            }
            if (table == null)
            {
                table = doc.DocumentNode.SelectSingleNode("//table[@id='dealList']");
            }
            if (table == null)
            {
                // Try to find any table with deal-related headers
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables != null)
                {
                    foreach (var t in tables)
                    {
                        var header = t.SelectSingleNode(".//th[contains(text(),'서버') or contains(text(),'아이템') or contains(text(),'가격')]");
                        if (header != null)
                        {
                            table = t;
                            break;
                        }
                    }
                }
            }

            if (table == null)
            {
                Debug.WriteLine("[ItemDealParser] Table not found in HTML");
                return items;
            }

            Debug.WriteLine($"[ItemDealParser] Table found: {table.GetAttributeValue("class", "no-class")}");

            // Parse table rows
            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count < 2)
            {
                Debug.WriteLine($"[ItemDealParser] No rows found or only header row");
                return items;
            }

            Debug.WriteLine($"[ItemDealParser] Found {rows.Count} rows (including header)");

            // Skip header row
            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 5)
                {
                    continue;
                }

                try
                {
                    var item = ParseRow(cells, defaultServerId);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ItemDealParser] Failed to parse row: {ex.Message}");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDealParser] ParseDealList failed: {ex.Message}");
        }

        return items;
    }

    private DealItem? ParseRow(HtmlNodeCollection cells, int defaultServerId)
    {
        try
        {
            // Server (column 0)
            var serverText = cells[0].InnerText.Trim();
            var serverId = ParseServer(serverText, defaultServerId);
            var serverName = Server.GetServerName(serverId);

            // Item cell (column 1) - extract more info
            var itemCell = cells[1];

            // Get item name from img alt attribute
            string? fullItemNameFromAlt = null;
            var img = itemCell.SelectSingleNode(".//img[@alt]");
            if (img != null)
            {
                fullItemNameFromAlt = img.GetAttributeValue("alt", "");
                Debug.WriteLine($"[ItemDealParser] Full item name from img alt: '{fullItemNameFromAlt}'");
            }

            // Extract grade from full item name before parsing (e.g., [UNIQUE], [RARE])
            // Grade markers are at the BEGINNING of text, so even truncated text can have grade
            // Try both sources and use whichever has the grade marker
            string? grade = null;
            var cellText = itemCell.InnerText.Trim();
            var altText = fullItemNameFromAlt?.Trim() ?? "";

            Debug.WriteLine($"[ItemDealParser] Grade extraction - cellText: '{cellText}', altText: '{altText}'");

            // Try cellText first (more likely to have grade info)
            var gradeMatch = Regex.Match(cellText, @"\[(UNIQUE|RARE|EPIC|LEGEND|MYTHIC|MAGIC)\]", RegexOptions.IgnoreCase);
            if (gradeMatch.Success)
            {
                grade = gradeMatch.Groups[1].Value.ToUpper();
                Debug.WriteLine($"[ItemDealParser] Grade found in cellText: '{grade}'");
            }
            else if (!string.IsNullOrEmpty(altText))
            {
                // Try altText if cellText didn't have grade
                gradeMatch = Regex.Match(altText, @"\[(UNIQUE|RARE|EPIC|LEGEND|MYTHIC|MAGIC)\]", RegexOptions.IgnoreCase);
                if (gradeMatch.Success)
                {
                    grade = gradeMatch.Groups[1].Value.ToUpper();
                    Debug.WriteLine($"[ItemDealParser] Grade found in altText: '{grade}'");
                }
            }

            var (itemName, refine, cardSlots) = ParseItemName(itemCell, fullItemNameFromAlt);

            // Extract item_id, mapId, ssi and image URL from onclick
            int? itemId = null;
            int? mapId = null;
            string? ssi = null;
            string? itemImageUrl = null;

            // Find onclick with CallItemDealView(svrID, mapID, 'ssi', page)
            // Example: onclick="javascript:CallItemDealView(129,2023,'7579659357999176565',1)"
            var link = itemCell.SelectSingleNode(".//a[@onclick]");
            if (link != null)
            {
                var onclick = link.GetAttributeValue("onclick", "");
                // Pattern: CallItemDealView(129,2023,'7579659357999176565',1)
                var dealViewMatch = Regex.Match(onclick, @"CallItemDealView\((\d+),(\d+),'([^']+)',(\d+)\)");
                if (dealViewMatch.Success)
                {
                    // svrID is already parsed from cell[0]
                    mapId = int.Parse(dealViewMatch.Groups[2].Value);
                    ssi = dealViewMatch.Groups[3].Value;
                    Debug.WriteLine($"[ItemDealParser] Parsed CallItemDealView: mapId={mapId}, ssi={ssi}");
                }

                // Fallback: old pattern for item_id (if CallItemDealView pattern doesn't match)
                if (!mapId.HasValue)
                {
                    var idMatch = Regex.Match(onclick, @"CallItemDealView\(\d+,(\d+),");
                    if (idMatch.Success)
                    {
                        itemId = int.Parse(idMatch.Groups[1].Value);
                    }
                }
            }

            // Reuse img from earlier or get it for image URL
            if (img != null)
            {
                var srcValue = img.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(srcValue)) itemImageUrl = srcValue;
            }

            // Quantity (column 2)
            var quantityText = cells[2].InnerText.Trim();
            var quantity = ParseNumber(quantityText);

            // Price (column 3)
            var priceCell = cells[3];
            var priceText = priceCell.InnerText.Trim();
            var price = ParseNumber(priceText);
            var priceFormatted = priceText.Replace(" ", "").Trim();
            if (string.IsNullOrEmpty(priceFormatted) || priceFormatted == "0")
            {
                priceFormatted = price.ToString("N0");
            }

            // Shop name and deal type (column 4)
            var shopCell = cells[4];
            var shopName = shopCell.InnerText.Trim();
            string? dealType = null;
            var shopClass = shopCell.GetAttributeValue("class", "");
            Debug.WriteLine($"[ItemDealParser] Shop cell class: '{shopClass}'");
            if (shopClass.Contains("buy"))
            {
                dealType = "buy";
            }
            else if (shopClass.Contains("sale"))
            {
                dealType = "sale";
            }
            Debug.WriteLine($"[ItemDealParser] Parsed dealType: '{dealType}' for shop '{shopName}'");

            // Map (optional - column 5)
            string? mapName = null;
            if (cells.Count > 5)
            {
                mapName = cells[5].InnerText.Trim();
                if (string.IsNullOrEmpty(mapName)) mapName = null;
            }

            var item = new DealItem
            {
                ServerId = serverId,
                ServerName = serverName,
                ItemId = itemId,
                ItemName = itemName,
                ItemImageUrl = itemImageUrl,
                Refine = refine,
                Grade = grade,
                CardSlots = cardSlots,
                Quantity = quantity,
                Price = price,
                PriceFormatted = priceFormatted,
                DealType = dealType,
                ShopName = shopName,
                MapName = mapName,
                MapId = mapId,
                Ssi = ssi,
                CrawledAt = DateTime.Now,
            };

            item.ComputeFields();
            return item;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDealParser] ParseRow failed: {ex.Message}");
            return null;
        }
    }

    private int ParseServer(string text, int defaultId)
    {
        text = text.Trim();

        foreach (var kvp in Server.ServerNames)
        {
            if (kvp.Value.Contains(text) || text.Contains(kvp.Value))
            {
                return kvp.Key;
            }
        }

        // Try numeric match
        var match = Regex.Match(text, @"\d+");
        if (match.Success)
        {
            return int.Parse(match.Value);
        }

        return defaultId;
    }

    private (string itemName, int? refine, string? cardSlots) ParseItemName(HtmlNode cell, string? fullItemNameFromAlt = null)
    {
        // Get both sources
        var cellText = cell.InnerText.Trim();
        var altText = fullItemNameFromAlt?.Trim() ?? "";

        Debug.WriteLine($"[ItemDealParser] cellText: '{cellText}', altText: '{altText}'");

        // Choose the item name source
        // IMPORTANT: With WebView2, cellText can be corrupted (doubled text from img alt + link text)
        // So we STRONGLY prefer altText (from img alt attribute) as the primary source
        string text;
        bool altTextTruncated = altText.Contains("...");

        if (!string.IsNullOrEmpty(altText) && !altTextTruncated)
        {
            // altText is available and not truncated - use it (most reliable with WebView2)
            text = altText;
            Debug.WriteLine($"[ItemDealParser] Using altText (primary source): '{text}'");
        }
        else if (!string.IsNullOrEmpty(altText) && altTextTruncated && !cellText.Contains("..."))
        {
            // altText is truncated but cellText isn't - use cellText
            // But need to handle doubled text from WebView2
            // If cellText contains altText (without ...) twice, extract the clean part
            var altWithoutTrunc = altText.Replace("...", "").Replace("[...", "").Trim();
            if (cellText.Contains(altWithoutTrunc))
            {
                // Try to extract clean item name from cellText
                // Pattern: remove everything before and after the actual item name
                var idx = cellText.IndexOf(altWithoutTrunc);
                if (idx >= 0)
                {
                    // Find the end of the item name (look for repetition or end of string)
                    var remaining = cellText.Substring(idx);
                    var halfLen = remaining.Length / 2;
                    if (halfLen > 0 && remaining.Length >= halfLen * 2)
                    {
                        var firstHalf = remaining.Substring(0, halfLen);
                        var secondHalf = remaining.Substring(halfLen);
                        if (firstHalf == secondHalf)
                        {
                            // Text is doubled, use first half
                            text = firstHalf;
                            Debug.WriteLine($"[ItemDealParser] Using deduplicated cellText: '{text}'");
                        }
                        else
                        {
                            text = cellText;
                            Debug.WriteLine($"[ItemDealParser] Using cellText (altText truncated): '{text}'");
                        }
                    }
                    else
                    {
                        text = cellText;
                        Debug.WriteLine($"[ItemDealParser] Using cellText (altText truncated): '{text}'");
                    }
                }
                else
                {
                    text = cellText;
                }
            }
            else
            {
                text = cellText;
                Debug.WriteLine($"[ItemDealParser] Using cellText (altText truncated): '{text}'");
            }
        }
        else if (string.IsNullOrEmpty(altText))
        {
            // No altText available - must use cellText but try to deduplicate
            // Check if cellText is doubled (same text repeated)
            var halfLen = cellText.Length / 2;
            if (halfLen > 0)
            {
                var firstHalf = cellText.Substring(0, halfLen);
                var secondHalf = cellText.Substring(halfLen);
                if (firstHalf == secondHalf)
                {
                    text = firstHalf;
                    Debug.WriteLine($"[ItemDealParser] Using deduplicated cellText (no altText): '{text}'");
                }
                else
                {
                    text = cellText;
                    Debug.WriteLine($"[ItemDealParser] Using cellText (no altText): '{text}'");
                }
            }
            else
            {
                text = cellText;
                Debug.WriteLine($"[ItemDealParser] Using cellText (no altText): '{text}'");
            }
        }
        else
        {
            // Both truncated - prefer altText as it's cleaner
            text = altText;
            Debug.WriteLine($"[ItemDealParser] Using altText (both truncated): '{text}'");
        }

        Debug.WriteLine($"[ItemDealParser] ParseItemName input: '{text}'");

        // Extract refine level (+1 ~ +20)
        // Note: "딤" means "dimension" and is part of item name (e.g., "12딤 글레이시아 스피어")
        // Pattern: +숫자 followed by space, [, end-of-string, OR Korean characters
        // Korean chars are allowed because items like "+11장교의 모자" have Korean directly after refine
        // The + prefix distinguishes refine from dimension numbers like "12딤"
        int? refine = null;
        var refineMatch = Regex.Match(text, @"\+(\d+)(?=\s|\[|$|[가-힣])");
        if (refineMatch.Success)
        {
            refine = int.Parse(refineMatch.Groups[1].Value);
            // Remove only the +숫자 part, preserve text that follows (like "딤")
            text = text.Remove(refineMatch.Index, refineMatch.Length);
            Debug.WriteLine($"[ItemDealParser] After refine removal: '{text}', refine={refine}");
        }

        // Remove truncation marker [...  or [... at end (GNJOY truncates long item names)
        text = Regex.Replace(text, @"\[\.\.\.$", "");
        text = Regex.Replace(text, @"\[\.\.\.\s*$", "");

        // Extract card slots - only numeric [N] patterns at the end
        string? cardSlots = null;
        var cardMatch = Regex.Match(text, @"\[(\d+)\]$");
        if (cardMatch.Success)
        {
            cardSlots = cardMatch.Groups[1].Value;
            text = Regex.Replace(text, @"\[\d+\]$", "");
            Debug.WriteLine($"[ItemDealParser] After card slots removal: '{text}', cardSlots={cardSlots}");
        }

        // Remove grade markers like [UNIQUE], [RARE] etc. (these are handled separately)
        text = Regex.Replace(text, @"\[(UNIQUE|RARE|EPIC|LEGEND|MYTHIC|MAGIC)\]\s*", "", RegexOptions.IgnoreCase);

        // Final cleanup: remove any remaining truncation markers
        text = Regex.Replace(text, @"\[\.\.\.", "");

        var result = text.Trim();
        Debug.WriteLine($"[ItemDealParser] ParseItemName result: '{result}'");
        return (result, refine, cardSlots);
    }

    private int ParseNumber(string text)
    {
        // Remove commas, 'z' (zeny), and other non-numeric chars
        var clean = Regex.Replace(text, @"[^\d]", "");
        return string.IsNullOrEmpty(clean) ? 0 : int.Parse(clean);
    }
}
