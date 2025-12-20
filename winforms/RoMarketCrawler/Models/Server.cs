namespace RoMarketCrawler.Models;

/// <summary>
/// RO Server information
/// </summary>
public class Server
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Primary server data - single source of truth
    private static readonly (int Id, string Name)[] _servers =
    {
        (-1, "전체"),
        (1, "바포메트"),
        (2, "이그드라실"),
        (3, "다크로드"),
        (4, "이프리트"),
    };

    // GNJOY internal ID mapping
    private static readonly Dictionary<int, int> _gnjoyToApiMap = new()
    {
        { 129, 1 },
        { 229, 2 },
        { 529, 3 },
        { 729, 4 },
    };

    // Lazy-initialized lookup dictionary
    private static Dictionary<int, string>? _serverNames;
    public static Dictionary<int, string> ServerNames => _serverNames ??= BuildServerNames();

    private static Dictionary<int, string> BuildServerNames()
    {
        var dict = new Dictionary<int, string>();
        foreach (var (id, name) in _servers)
        {
            dict[id] = name;
        }
        // Add GNJOY internal IDs pointing to same names
        foreach (var (gnjoyId, apiId) in _gnjoyToApiMap)
        {
            if (dict.TryGetValue(apiId, out var name))
            {
                dict[gnjoyId] = name;
            }
        }
        return dict;
    }

    public static List<Server> GetAllServers()
    {
        return _servers.Select(s => new Server { Id = s.Id, Name = s.Name }).ToList();
    }

    public static string GetServerName(int serverId)
    {
        return ServerNames.TryGetValue(serverId, out var name) ? name : "Unknown";
    }

    public static int MapGnjoyServerId(int gnjoyId)
    {
        return _gnjoyToApiMap.TryGetValue(gnjoyId, out var apiId) ? apiId : gnjoyId;
    }

    public override string ToString() => Name;
}
