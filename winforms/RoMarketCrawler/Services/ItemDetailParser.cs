using System.Diagnostics;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace RoMarketCrawler.Services;

/// <summary>
/// Parser for GNJOY itemDealView.asp HTML response
/// Extracts slot info (enchants/cards), random options, element, maker
/// </summary>
public class ItemDetailParser
{
    /// <summary>
    /// Parse item detail response from itemDealView.asp
    /// </summary>
    public ItemDetailInfo Parse(string html)
    {
        var info = new ItemDetailInfo();

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find the detail table by caption (GNJOY tables use caption, not class)
            // Primary method: find table with caption containing "상세정보"
            var table = doc.DocumentNode.SelectSingleNode("//table[caption[contains(text(),'상세정보')]]");
            Debug.WriteLine($"[ItemDetailParser] Table by caption '상세정보': {(table != null ? "found" : "not found")}");

            if (table == null)
            {
                // Fallback: try class-based selectors for compatibility
                table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'dealView')]");
                if (table == null)
                {
                    table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'tbl')]");
                }
            }

            if (table == null)
            {
                // Try any table with slot/enchant related headers
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables != null)
                {
                    foreach (var t in tables)
                    {
                        var header = t.SelectSingleNode(".//th[contains(text(),'슬롯') or contains(text(),'인챈트') or contains(text(),'카드')]");
                        if (header != null)
                        {
                            table = t;
                            Debug.WriteLine("[ItemDetailParser] Table found by header text");
                            break;
                        }
                    }
                }
            }

            if (table == null)
            {
                Debug.WriteLine("[ItemDetailParser] Table not found in HTML");
                // Try parsing from raw text as fallback
                return ParseFromText(html);
            }

            Debug.WriteLine($"[ItemDetailParser] Table found");

            var rows = table.SelectNodes(".//tr");
            if (rows == null)
            {
                return info;
            }

            foreach (var row in rows)
            {
                // Handle multiple th/td pairs per row (GNJOY uses 2 pairs per row)
                var thNodes = row.SelectNodes(".//th");
                var tdNodes = row.SelectNodes(".//td");

                if (thNodes == null || tdNodes == null) continue;

                // Process each th/td pair
                for (int i = 0; i < thNodes.Count && i < tdNodes.Count; i++)
                {
                    var th = thNodes[i];
                    var td = tdNodes[i];

                    var headerText = th.InnerText.Trim();
                    Debug.WriteLine($"[ItemDetailParser] Row header[{i}]: '{headerText}'");

                    if (headerText.Contains("슬롯") || headerText.Contains("인챈트") || headerText.Contains("카드"))
                    {
                        info.SlotInfo = ParseSlotInfo(td);
                        Debug.WriteLine($"[ItemDetailParser] Parsed {info.SlotInfo.Count} slot items");
                    }
                    else if (headerText.Contains("랜덤") || headerText.Contains("옵션"))
                    {
                        info.RandomOptions = ParseRandomOptions(td);
                        Debug.WriteLine($"[ItemDetailParser] Parsed {info.RandomOptions.Count} random options");
                    }
                    else if (headerText.Contains("속성"))
                    {
                        info.Element = td.InnerText.Trim();
                        Debug.WriteLine($"[ItemDetailParser] Element: '{info.Element}'");
                    }
                    else if (headerText.Contains("제조자") || headerText.Contains("제작자"))
                    {
                        info.Maker = td.InnerText.Trim();
                        Debug.WriteLine($"[ItemDetailParser] Maker: '{info.Maker}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailParser] Parse failed: {ex.Message}");
        }

        return info;
    }

    /// <summary>
    /// Parse slot info (enchants and cards) from table cell
    /// </summary>
    private List<string> ParseSlotInfo(HtmlNode cell)
    {
        var slots = new List<string>();

        Debug.WriteLine($"[ItemDetailParser] ParseSlotInfo - Cell HTML: {cell.OuterHtml.Substring(0, Math.Min(500, cell.OuterHtml.Length))}...");

        try
        {
            // First try to get from list items
            var listItems = cell.SelectNodes(".//li");
            Debug.WriteLine($"[ItemDetailParser] Found {listItems?.Count ?? 0} <li> items");
            if (listItems != null)
            {
                foreach (var li in listItems)
                {
                    // Try to get from img alt first (most accurate)
                    var img = li.SelectSingleNode(".//img[@alt]");
                    string? slotText = null;

                    if (img != null)
                    {
                        slotText = img.GetAttributeValue("alt", "");
                        Debug.WriteLine($"[ItemDetailParser] From img alt: '{slotText}'");
                    }

                    // Fallback to li text
                    if (string.IsNullOrEmpty(slotText))
                    {
                        slotText = li.InnerText.Trim();
                        Debug.WriteLine($"[ItemDetailParser] From li text: '{slotText}'");
                    }

                    if (!string.IsNullOrEmpty(slotText))
                    {
                        // Clean up the text (remove extra whitespace, special chars)
                        slotText = CleanSlotText(slotText);
                        if (!string.IsNullOrEmpty(slotText))
                        {
                            Debug.WriteLine($"[ItemDetailParser] Adding slot: '{slotText}' (isCard={slotText.Contains("카드")})");
                            slots.Add(slotText);
                        }
                    }
                }
            }

            // If no list items, try spans or direct text
            if (slots.Count == 0)
            {
                var spans = cell.SelectNodes(".//span");
                if (spans != null)
                {
                    foreach (var span in spans)
                    {
                        var text = CleanSlotText(span.InnerText);
                        if (!string.IsNullOrEmpty(text))
                        {
                            slots.Add(text);
                        }
                    }
                }
            }

            // Last resort: split by newlines or commas
            if (slots.Count == 0)
            {
                var text = cell.InnerText.Trim();
                var parts = text.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var cleaned = CleanSlotText(part);
                    if (!string.IsNullOrEmpty(cleaned) && cleaned != "-" && cleaned != "없음")
                    {
                        slots.Add(cleaned);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailParser] ParseSlotInfo failed: {ex.Message}");
        }

        return slots;
    }

    /// <summary>
    /// Parse random options from table cell
    /// </summary>
    private List<string> ParseRandomOptions(HtmlNode cell)
    {
        var options = new List<string>();

        try
        {
            // Try list items first
            var listItems = cell.SelectNodes(".//li");
            if (listItems != null)
            {
                foreach (var li in listItems)
                {
                    var text = CleanOptionText(li.InnerText);
                    if (!string.IsNullOrEmpty(text))
                    {
                        options.Add(text);
                    }
                }
            }

            // Try spans
            if (options.Count == 0)
            {
                var spans = cell.SelectNodes(".//span");
                if (spans != null)
                {
                    foreach (var span in spans)
                    {
                        var text = CleanOptionText(span.InnerText);
                        if (!string.IsNullOrEmpty(text))
                        {
                            options.Add(text);
                        }
                    }
                }
            }

            // Fallback to splitting text
            if (options.Count == 0)
            {
                var text = cell.InnerText.Trim();
                var parts = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var cleaned = CleanOptionText(part);
                    if (!string.IsNullOrEmpty(cleaned) && cleaned != "-" && cleaned != "없음")
                    {
                        options.Add(cleaned);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailParser] ParseRandomOptions failed: {ex.Message}");
        }

        return options;
    }

    /// <summary>
    /// Fallback parsing from raw text
    /// </summary>
    private ItemDetailInfo ParseFromText(string html)
    {
        var info = new ItemDetailInfo();

        try
        {
            // Look for slot info patterns
            // Pattern: 설화 마력(마법등급) 5Lv, 설화 마력(크로스 레인), etc.
            var enchantPattern = @"(설화|예언|축복|각성|완성|초급|중급|고급)\s*[^\n<]+";
            var enchantMatches = Regex.Matches(html, enchantPattern);
            foreach (Match match in enchantMatches)
            {
                var text = CleanSlotText(match.Value);
                if (!string.IsNullOrEmpty(text) && !info.SlotInfo.Contains(text))
                {
                    info.SlotInfo.Add(text);
                }
            }

            // Look for card patterns (ends with 카드)
            var cardPattern = @"[가-힣]+\s*카드";
            var cardMatches = Regex.Matches(html, cardPattern);
            foreach (Match match in cardMatches)
            {
                var text = CleanSlotText(match.Value);
                if (!string.IsNullOrEmpty(text) && !info.SlotInfo.Contains(text))
                {
                    info.SlotInfo.Add(text);
                }
            }

            // Look for random option patterns
            // Pattern: STR + 5, DEX + 3, MaxHP + 100, etc.
            var optionPattern = @"(STR|AGI|VIT|INT|DEX|LUK|ATK|MATK|DEF|MDEF|HIT|FLEE|ASPD|MaxHP|MaxSP|HP|SP)\s*[+\-]\s*\d+";
            var optionMatches = Regex.Matches(html, optionPattern, RegexOptions.IgnoreCase);
            foreach (Match match in optionMatches)
            {
                var text = CleanOptionText(match.Value);
                if (!string.IsNullOrEmpty(text) && !info.RandomOptions.Contains(text))
                {
                    info.RandomOptions.Add(text);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailParser] ParseFromText failed: {ex.Message}");
        }

        return info;
    }

    private string CleanSlotText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Remove HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Remove extra whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    private string CleanOptionText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Remove HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Remove extra whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }
}

/// <summary>
/// Item detail information from itemDealView.asp
/// </summary>
public class ItemDetailInfo
{
    public List<string> SlotInfo { get; set; } = new();
    public List<string> RandomOptions { get; set; } = new();
    public string? Element { get; set; }
    public string? Maker { get; set; }
}
