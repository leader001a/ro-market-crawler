namespace RoMarketCrawler.Models;

internal class ItemTypeItem
{
    public int Id { get; }
    public string Name { get; }

    public ItemTypeItem(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}
