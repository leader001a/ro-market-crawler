using System.Diagnostics;
using System.Text.Json;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Service for saving/loading crawl data to/from JSON files and performing local search
/// </summary>
public class CrawlDataService
{
    private readonly string _crawlDir;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CrawlDataService(string dataDir)
    {
        _crawlDir = Path.Combine(dataDir, "Crawl");
        Directory.CreateDirectory(_crawlDir);
    }

    /// <summary>
    /// Save crawl session to JSON file
    /// Filename: {searchTerm}_{serverName}_{datetime}.json
    /// </summary>
    public async Task SaveAsync(CrawlSession session)
    {
        var timestamp = session.CrawledAt.ToString("yyyy-MM-dd_HHmmss");
        var fileName = $"{SanitizeFileName(session.SearchTerm)}_{SanitizeFileName(session.ServerName)}_{timestamp}.json";
        var filePath = Path.Combine(_crawlDir, fileName);

        var json = JsonSerializer.Serialize(session, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        Debug.WriteLine($"[CrawlDataService] Saved {session.Items.Count} items to {fileName}");
    }

    /// <summary>
    /// Load the most recent crawl session for a search term and server
    /// </summary>
    public async Task<CrawlSession?> LoadLatestAsync(string searchTerm, string serverName)
    {
        var files = GetSavedFiles(searchTerm, serverName);
        if (files.Count == 0) return null;

        var latest = files.OrderByDescending(f => f.CrawledAt).First();
        return await LoadFromFileAsync(latest.FilePath);
    }

    /// <summary>
    /// Load crawl session from a specific file
    /// </summary>
    public async Task<CrawlSession?> LoadFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<CrawlSession>(json, _jsonReadOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrawlDataService] Failed to load {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// List saved crawl files for a search term (optionally filtered by server)
    /// </summary>
    public List<CrawlFileInfo> GetSavedFiles(string searchTerm, string? serverName = null)
    {
        var result = new List<CrawlFileInfo>();
        if (!Directory.Exists(_crawlDir)) return result;

        var prefix = SanitizeFileName(searchTerm) + "_";
        var files = Directory.GetFiles(_crawlDir, $"{prefix}*.json");

        foreach (var file in files)
        {
            try
            {
                // Parse filename: {searchTerm}_{serverName}_{date}_{time}.json
                var name = Path.GetFileNameWithoutExtension(file);
                var parts = name.Split('_');
                if (parts.Length < 4) continue;

                var fileServerName = parts[1];
                if (serverName != null && fileServerName != SanitizeFileName(serverName))
                    continue;

                // Parse date from parts[2] and time from parts[3]
                if (DateTime.TryParseExact(
                    $"{parts[2]}_{parts[3]}",
                    "yyyy-MM-dd_HHmmss",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var crawledAt))
                {
                    // Read item count from file (quick parse)
                    var itemCount = GetItemCountFromFile(file);

                    result.Add(new CrawlFileInfo
                    {
                        FilePath = file,
                        ServerName = fileServerName,
                        CrawledAt = crawledAt,
                        TotalItems = itemCount
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CrawlDataService] Error parsing file {file}: {ex.Message}");
            }
        }

        return result.OrderByDescending(f => f.CrawledAt).ToList();
    }

    /// <summary>
    /// Search within a crawl session using filter criteria
    /// </summary>
    public List<DealItem> Search(CrawlSession session, CrawlSearchFilter filter)
    {
        if (session.Items == null || session.Items.Count == 0)
            return new List<DealItem>();

        IEnumerable<DealItem> results = session.Items;

        // Filter by item name
        if (!string.IsNullOrWhiteSpace(filter.ItemName))
        {
            results = results.Where(item =>
                item.ItemName?.Contains(filter.ItemName, StringComparison.OrdinalIgnoreCase) == true ||
                item.DisplayName?.Contains(filter.ItemName, StringComparison.OrdinalIgnoreCase) == true);
        }

        // Filter by card/enchant (search in SlotInfo)
        if (!string.IsNullOrWhiteSpace(filter.CardEnchant))
        {
            results = results.Where(item =>
                item.SlotInfo?.Any(s => s?.Contains(filter.CardEnchant, StringComparison.OrdinalIgnoreCase) == true) == true);
        }

        // Filter by price range
        if (filter.MinPrice.HasValue)
        {
            results = results.Where(item => item.Price >= filter.MinPrice.Value);
        }
        if (filter.MaxPrice.HasValue)
        {
            results = results.Where(item => item.Price <= filter.MaxPrice.Value);
        }

        return results.ToList();
    }

    /// <summary>
    /// Delete a crawl file
    /// </summary>
    public void DeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.WriteLine($"[CrawlDataService] Deleted {filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrawlDataService] Failed to delete {filePath}: {ex.Message}");
        }
    }

    #region Detail Cache

    /// <summary>
    /// Load SSI-based detail cache for a server.
    /// File: detail_cache_{serverId}.json  →  Dictionary&lt;ssi, ItemDetailInfo&gt;
    /// </summary>
    public Dictionary<string, ItemDetailInfo> LoadDetailCache(int serverId)
    {
        try
        {
            var filePath = GetDetailCachePath(serverId);
            if (!File.Exists(filePath)) return new();

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, ItemDetailInfo>>(json, _jsonReadOptions)
                ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrawlDataService] Failed to load detail cache: {ex.Message}");
            return new();
        }
    }

    /// <summary>
    /// Save SSI-based detail cache for a server.
    /// Only SSIs present in activeSsis are kept (prune sold/removed items).
    /// </summary>
    public void SaveDetailCache(int serverId, Dictionary<string, ItemDetailInfo> cache, HashSet<string>? activeSsis = null)
    {
        try
        {
            // Prune entries not in current session
            Dictionary<string, ItemDetailInfo> toSave;
            if (activeSsis != null)
            {
                toSave = new(activeSsis.Count);
                foreach (var ssi in activeSsis)
                {
                    if (cache.TryGetValue(ssi, out var detail))
                        toSave[ssi] = detail;
                }
            }
            else
            {
                toSave = cache;
            }

            var filePath = GetDetailCachePath(serverId);
            var json = JsonSerializer.Serialize(toSave, _jsonOptions);
            File.WriteAllText(filePath, json);
            Debug.WriteLine($"[CrawlDataService] Saved detail cache: {toSave.Count} entries for server {serverId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrawlDataService] Failed to save detail cache: {ex.Message}");
        }
    }

    private string GetDetailCachePath(int serverId)
        => Path.Combine(_crawlDir, $"detail_cache_{serverId}.json");

    #endregion

    private static string SanitizeFileName(string name)
    {
        // Remove characters that are invalid in file names
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static int GetItemCountFromFile(string filePath)
    {
        try
        {
            // Quick read just the totalItems field without deserializing entire file
            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("totalItems", out var prop))
            {
                return prop.GetInt32();
            }
        }
        catch { }
        return 0;
    }
}
