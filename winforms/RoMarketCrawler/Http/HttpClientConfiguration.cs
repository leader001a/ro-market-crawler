using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace RoMarketCrawler.Http;

/// <summary>
/// Named HttpClient constants and configuration for IHttpClientFactory.
/// Provides centralized HTTP client settings for all API clients.
/// </summary>
public static class HttpClientConfiguration
{
    /// <summary>
    /// HttpClient name for GNJOY API requests
    /// </summary>
    public const string GnjoyClient = "GnjoyClient";

    /// <summary>
    /// HttpClient name for Kafra.kr API requests
    /// </summary>
    public const string KafraClient = "KafraClient";

    /// <summary>
    /// HttpClient name for image downloads
    /// </summary>
    public const string ImageClient = "ImageClient";

    /// <summary>
    /// Configure all named HttpClients for the application
    /// </summary>
    public static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        // Configure GNJOY API client
        services.AddHttpClient(GnjoyClient, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html, application/json");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Referrer = new Uri("https://ro.gnjoy.com/");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            MaxConnectionsPerServer = 10,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            EnableMultipleHttp2Connections = true
        });

        // Configure Kafra.kr API client
        services.AddHttpClient(KafraClient, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 5
        });

        // Configure Image download client
        services.AddHttpClient(ImageClient, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 3
        });

        return services;
    }
}
