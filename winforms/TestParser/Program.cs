using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var handler = new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All
};

var client = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(30)
};

client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9");

var searchTerm = "12딤 글레이시아 스피어";
var url = $"https://ro.gnjoy.com/itemDeal/itemDealList.asp?svrID=1&itemFullName={Uri.EscapeDataString(searchTerm)}&itemOrder=regdate&curpage=1";

Console.WriteLine($"URL: {url}");
Console.WriteLine();

var response = await client.GetAsync(url);
response.EnsureSuccessStatusCode();

var html = await response.Content.ReadAsStringAsync();
Console.WriteLine($"Response length: {html.Length} chars");

var doc = new HtmlDocument();
doc.LoadHtml(html);

var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'dealList')]");
Console.WriteLine($"Table found: {table != null}");

if (table != null)
{
    var rows = table.SelectNodes(".//tr");
    Console.WriteLine($"Total rows: {rows?.Count ?? 0}");

    if (rows != null && rows.Count > 1)
    {
        var dataRows = rows.Skip(1).ToList();
        Console.WriteLine($"Data rows: {dataRows.Count}");
        Console.WriteLine();

        foreach (var row in dataRows.Take(2))
        {
            var cells = row.SelectNodes(".//td");
            if (cells != null && cells.Count >= 5)
            {
                var itemCell = cells[1];

                // Get full item name from img alt
                var img = itemCell.SelectSingleNode(".//img[@alt]");
                string? fullItemNameFromAlt = null;
                if (img != null)
                {
                    fullItemNameFromAlt = img.GetAttributeValue("alt", "");
                    Console.WriteLine($"Full item name from alt: '{fullItemNameFromAlt}'");
                }

                // Parse item name
                var (itemName, refine, cardSlots) = ParseItemName(fullItemNameFromAlt ?? itemCell.InnerText);
                Console.WriteLine($"  Parsed ItemName: '{itemName}'");
                Console.WriteLine($"  Refine: {refine}");
                Console.WriteLine($"  CardSlots: {cardSlots}");
                Console.WriteLine();
            }
        }
    }
}

Console.WriteLine("Done!");

static (string itemName, int? refine, string? cardSlots) ParseItemName(string text)
{
    Console.WriteLine($"  [ParseItemName] input: '{text}'");

    // Extract refine level (+1 ~ +20) - handle Korean suffix like "+12딤"
    int? refine = null;
    var refineMatch = Regex.Match(text, @"\+(\d+)(딤|레|제|단)?");
    if (refineMatch.Success)
    {
        refine = int.Parse(refineMatch.Groups[1].Value);
        text = Regex.Replace(text, @"\+\d+(딤|레|제|단)?\s*", "");
        Console.WriteLine($"  [ParseItemName] After refine removal: '{text}', refine={refine}");
    }

    // Extract card slots - only numeric [N] patterns at the end
    string? cardSlots = null;
    var cardMatch = Regex.Match(text, @"\[(\d+)\]$");
    if (cardMatch.Success)
    {
        cardSlots = cardMatch.Groups[1].Value;
        text = Regex.Replace(text, @"\[\d+\]$", "");
        Console.WriteLine($"  [ParseItemName] After card slots removal: '{text}', cardSlots={cardSlots}");
    }

    // Remove grade markers like [UNIQUE], [RARE] etc.
    text = Regex.Replace(text, @"\[(UNIQUE|RARE|EPIC|LEGEND|MYTHIC)\]\s*", "", RegexOptions.IgnoreCase);

    return (text.Trim(), refine, cardSlots);
}
