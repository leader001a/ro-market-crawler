#r "bin/Debug/net8.0-windows/RoMarketCrawler.dll"

using System;
using System.Threading.Tasks;
using RoMarketCrawler.Services;
using RoMarketCrawler.Models;

var gnjoyClient = new GnjoyClient();
var monitorService = new MonitoringService(gnjoyClient);

// Add a test item
await monitorService.AddItemAsync("포링 카드", -1);
Console.WriteLine($"Added item. ItemCount: {monitorService.ItemCount}");

// Refresh
var progress = new Progress<MonitorProgress>(p => {
    Console.WriteLine($"Progress: {p.Phase} - {p.CurrentItem} ({p.CurrentIndex}/{p.TotalItems})");
});

await monitorService.RefreshAllAsync(progress);

// Check results
Console.WriteLine($"Results count: {monitorService.Results.Count}");
foreach (var result in monitorService.Results.Values)
{
    Console.WriteLine($"  Item: {result.Item.ItemName}, Deals: {result.DealCount}, Error: {result.ErrorMessage ?? "none"}");
}

gnjoyClient.Dispose();
monitorService.Dispose();
