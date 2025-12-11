// Temporary test file - delete after debugging
using System;
using System.Net.Http;
using System.Threading.Tasks;
using RoMarketCrawler.Services;
using RoMarketCrawler.Models;

namespace RoMarketCrawler
{
    public class TestParser
    {
        public static async Task RunTest()
        {
            Console.WriteLine("=== Testing Server ID Filtering ===");

            using var gnjoyClient = new GnjoyClient();

            var itemName = "포링 카드";

            // Test with ServerId=1 (바포메트)
            Console.WriteLine($"\nTest 1: ServerId=1 (바포메트)");
            var deals1 = await gnjoyClient.SearchItemDealsAsync(itemName, 1);
            Console.WriteLine($"  Deals count: {deals1.Count}");
            var servers1 = deals1.Select(d => d.ServerName).Distinct().ToList();
            Console.WriteLine($"  Servers in results: {string.Join(", ", servers1)}");

            // Test with ServerId=-1 (전체)
            Console.WriteLine($"\nTest 2: ServerId=-1 (전체)");
            var deals2 = await gnjoyClient.SearchItemDealsAsync(itemName, -1);
            Console.WriteLine($"  Deals count: {deals2.Count}");
            var servers2 = deals2.Select(d => d.ServerName).Distinct().ToList();
            Console.WriteLine($"  Servers in results: {string.Join(", ", servers2)}");

            Console.WriteLine("\nIf ServerId=1 shows only 바포메트, server filtering is working correctly.");
        }
    }
}
