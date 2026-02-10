using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RoMarketCrawler.Controllers;
using RoMarketCrawler.Http;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Startup;

/// <summary>
/// Extension methods for registering services with the DI container
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register all RoMarketCrawler services with the DI container
    /// </summary>
    public static IServiceCollection AddRoMarketCrawlerServices(this IServiceCollection services)
    {
        // Register named HttpClients via IHttpClientFactory
        services.AddHttpClients();

        // Core services - singleton for shared state
        // Use GnjoyClient's shared RateLimitManager for consistency across all API clients
        services.AddSingleton<IRateLimitManager>(sp => GnjoyClient.SharedRateLimitManager);
        services.AddSingleton<IAlarmSoundService, AlarmSoundServiceAdapter>();

        // API clients - singleton to reuse HttpClient connections
        // Note: These still create their own HttpClient instances for backward compatibility
        // Future: Migrate to IHttpClientFactory injection
        services.AddSingleton<IGnjoyClient, GnjoyClient>();
        services.AddSingleton<IKafraClient, KafraClient>();

        // Data directory - program execution folder/Data (portable)
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        Debug.WriteLine($"[DI] Data directory: {dataDir}");

        // Data services - singleton for caching
        // Note: Cache loading is done in StartupSplashForm.CheckAndBuildItemIndexAsync()
        // to avoid deadlock from GetAwaiter().GetResult() on UI thread
        services.AddSingleton<IItemIndexService>(sp => new ItemIndexService(dataDir));

        // Monitoring service - singleton with dedicated GnjoyClient
        services.AddSingleton<IMonitoringService>(sp => new MonitoringService(dataDir));

        // Settings service - singleton for application settings
        services.AddSingleton<ISettingsService>(sp => new SettingsService(dataDir));

        // Theme manager - singleton for centralized theme management
        services.AddSingleton<IThemeManager, ThemeManager>();

        // Crawl data service - singleton for shared data management
        services.AddSingleton<CrawlDataService>(sp => new CrawlDataService(dataDir));

        // Tab controllers - transient (one per form instance)
        // Controllers receive IServiceProvider to resolve their dependencies
        services.AddTransient<DealTabController>();
        services.AddTransient<ItemTabController>();
        services.AddTransient<MonitorTabController>();
        services.AddTransient<CostumeTabController>();

        return services;
    }
}

/// <summary>
/// Adapter to wrap the static AlarmSoundService as an instance-based service
/// </summary>
public class AlarmSoundServiceAdapter : IAlarmSoundService
{
    public void PlaySound(Models.AlarmSoundType soundType)
    {
        AlarmSoundService.PlaySound(soundType);
    }
}
