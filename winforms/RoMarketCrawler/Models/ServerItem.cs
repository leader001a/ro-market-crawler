namespace RoMarketCrawler.Models;

internal class ServerItem
{
    public int Id { get; }
    public string Name { get; }

    public ServerItem(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}
