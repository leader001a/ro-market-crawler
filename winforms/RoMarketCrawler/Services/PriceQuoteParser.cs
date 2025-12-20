using System.Diagnostics;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Parser for GNJOY quoteSearch.asp (시세 조회) HTML response
/// </summary>
public class PriceQuoteParser
{
    /// <summary>
    /// Parse the extended price list from CallItemPriceExtendList response
    /// </summary>
    public List<PriceHistory> ParsePriceExtendList(string html)
    {
        var history = new List<PriceHistory>();

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find the price history table from itemPriceExtendList.asp
            // Priority 1: detailSearchList class (exact match for price history table)
            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'detailSearchList')]");

            // Priority 2: Look for table with date/price columns
            if (table == null)
            {
                table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'price')]");
            }
            if (table == null)
            {
                table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'tbl')]");
            }
            if (table == null)
            {
                // Try to find any table with date patterns (날짜, 최저가, 평균가, 최고가)
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables != null)
                {
                    foreach (var t in tables)
                    {
                        var header = t.SelectSingleNode(".//th[contains(text(),'날짜') or contains(text(),'일자') or contains(text(),'최저가')]");
                        if (header != null)
                        {
                            table = t;
                            break;
                        }
                    }
                }
            }

            Debug.WriteLine($"[PriceQuoteParser] Table found: {table != null}");

            if (table == null)
            {
                // Try parsing as raw text with date patterns
                return ParseFromText(html);
            }

            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count < 2)
            {
                return ParseFromText(html);
            }

            // Skip header row
            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 4)
                {
                    continue;
                }

                try
                {
                    var priceHistoryItem = ParsePriceRow(cells);
                    if (priceHistoryItem != null)
                    {
                        history.Add(priceHistoryItem);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PriceQuoteParser] Failed to parse price row: {ex.Message}");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PriceQuoteParser] ParsePriceExtendList failed, trying text fallback: {ex.Message}");
            return ParseFromText(html);
        }

        return history;
    }

    private PriceHistory? ParsePriceRow(HtmlNodeCollection cells)
    {
        try
        {
            // Date (column 0) - format: 2025.12.08 or 2025-12-08
            var dateText = cells[0].InnerText.Trim();
            if (!TryParseDate(dateText, out var date))
            {
                return null;
            }

            // Min price (column 1)
            var minPriceText = cells[1].InnerText.Trim();
            var minPrice = ParsePrice(minPriceText);

            // Avg price (column 2)
            var avgPriceText = cells[2].InnerText.Trim();
            var avgPrice = ParsePrice(avgPriceText);

            // Max price (column 3)
            var maxPriceText = cells[3].InnerText.Trim();
            var maxPrice = ParsePrice(maxPriceText);

            return new PriceHistory
            {
                Date = date,
                MinPrice = minPrice,
                AvgPrice = avgPrice,
                MaxPrice = maxPrice
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PriceQuoteParser] ParsePriceRow failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse price history from plain text (fallback when table parsing fails)
    /// </summary>
    private List<PriceHistory> ParseFromText(string text)
    {
        var history = new List<PriceHistory>();

        // Pattern: 2025.12.08 or 2025-12-08 followed by prices
        // Example: "2025.12.08 230,000,000 230,000,000 230,000,000"
        var datePattern = @"(\d{4}[\.\-/]\d{1,2}[\.\-/]\d{1,2})\s*[:\s]*([0-9,]+)\s*[/\s]*([0-9,]+)\s*[/\s]*([0-9,]+)";
        var matches = Regex.Matches(text, datePattern);

        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count >= 5)
            {
                if (TryParseDate(match.Groups[1].Value, out var date))
                {
                    history.Add(new PriceHistory
                    {
                        Date = date,
                        MinPrice = ParsePrice(match.Groups[2].Value),
                        AvgPrice = ParsePrice(match.Groups[3].Value),
                        MaxPrice = ParsePrice(match.Groups[4].Value)
                    });
                }
            }
        }

        return history;
    }

    private bool TryParseDate(string text, out DateTime date)
    {
        date = DateTime.MinValue;

        // Clean and normalize
        text = text.Trim().Replace("/", ".").Replace("-", ".");

        // Try various date formats
        string[] formats = { "yyyy.MM.dd", "yyyy.M.d", "yy.MM.dd", "yy.M.d" };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(text, format, null, System.Globalization.DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        return DateTime.TryParse(text, out date);
    }

    private long ParsePrice(string text)
    {
        // Remove commas, 'z' (zeny), spaces, and other non-numeric chars
        var clean = Regex.Replace(text, @"[^0-9]", "");
        return string.IsNullOrEmpty(clean) ? 0 : long.Parse(clean);
    }

    /// <summary>
    /// Parse item list from quoteSearch.asp to get item names for exact matching
    /// </summary>
    public List<string> ParseItemList(string html)
    {
        var items = new List<string>();

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find item links or list
            var itemLinks = doc.DocumentNode.SelectNodes("//a[contains(@onclick,'CallItemPriceExtendList') or contains(@onclick,'CallItemPrice')]");
            if (itemLinks != null)
            {
                foreach (var link in itemLinks)
                {
                    var itemName = link.InnerText.Trim();
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        items.Add(itemName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PriceQuoteParser] ParseItemList failed: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Parse item price list from itemPriceList.asp to get exact item names for price lookup
    /// Returns list of exact item names that can be used with itemPriceExtendList.asp
    /// </summary>
    public List<PriceListItem> ParsePriceList(string html)
    {
        var items = new List<PriceListItem>();

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find table with price list (searchList class in itemPriceList.asp)
            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'searchList')]");
            if (table == null)
            {
                table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'tbl')]");
            }
            if (table == null)
            {
                // Try any table with item/price headers
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables != null)
                {
                    foreach (var t in tables)
                    {
                        var header = t.SelectSingleNode(".//th[contains(text(),'아이템') or contains(text(),'가격')]");
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
                Debug.WriteLine("[PriceQuoteParser] ParsePriceList: Table not found");
                return items;
            }

            var rows = table.SelectNodes(".//tr");
            if (rows == null || rows.Count < 2)
            {
                return items;
            }

            // Skip header row
            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 2)
                {
                    continue;
                }

                try
                {
                    var itemCell = cells[0];

                    // Get exact item name from img alt attribute (most reliable)
                    string? exactItemName = null;
                    var img = itemCell.SelectSingleNode(".//img[@alt]");
                    if (img != null)
                    {
                        exactItemName = img.GetAttributeValue("alt", "");
                        Debug.WriteLine($"[PriceQuoteParser] Found item from img alt: '{exactItemName}'");
                    }

                    // Fallback: get from link text or span
                    if (string.IsNullOrEmpty(exactItemName))
                    {
                        var link = itemCell.SelectSingleNode(".//a");
                        if (link != null)
                        {
                            // Try to get from span inside link
                            var span = link.SelectSingleNode(".//span");
                            exactItemName = span?.InnerText.Trim() ?? link.InnerText.Trim();
                        }
                    }

                    if (string.IsNullOrEmpty(exactItemName))
                    {
                        exactItemName = itemCell.InnerText.Trim();
                    }

                    if (string.IsNullOrEmpty(exactItemName))
                    {
                        continue;
                    }

                    // Parse price info from second cell (format: "100,000,000 / 600,000,000")
                    long minPrice = 0, maxPrice = 0;
                    if (cells.Count >= 2)
                    {
                        var priceText = cells[1].InnerText.Trim();
                        var priceParts = priceText.Split('/');
                        if (priceParts.Length >= 2)
                        {
                            minPrice = ParsePrice(priceParts[0]);
                            maxPrice = ParsePrice(priceParts[1]);
                        }
                        else if (priceParts.Length == 1)
                        {
                            minPrice = maxPrice = ParsePrice(priceParts[0]);
                        }
                    }

                    items.Add(new PriceListItem
                    {
                        ExactItemName = exactItemName,
                        MinPrice = minPrice,
                        MaxPrice = maxPrice
                    });

                    Debug.WriteLine($"[PriceQuoteParser] Parsed price list item: '{exactItemName}' ({minPrice:N0} ~ {maxPrice:N0})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PriceQuoteParser] Failed to parse price list row: {ex.Message}");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PriceQuoteParser] ParsePriceList failed: {ex.Message}");
        }

        return items;
    }
}

/// <summary>
/// Represents an item from the price list search results
/// </summary>
public class PriceListItem
{
    public string ExactItemName { get; set; } = "";
    public long MinPrice { get; set; }
    public long MaxPrice { get; set; }
}
