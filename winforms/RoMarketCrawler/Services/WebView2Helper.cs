using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RoMarketCrawler.Services;

/// <summary>
/// Helper class for making HTTP requests using WebView2 browser engine
/// This bypasses Cloudflare protection by using a real browser
/// </summary>
public class WebView2Helper : IDisposable
{
    private WebView2? _webView;
    private Form? _hiddenForm;
    private bool _isInitialized;
    private bool _disposed;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    /// <summary>
    /// Check if WebView2 is initialized and ready
    /// </summary>
    public bool IsReady => _isInitialized && _webView?.CoreWebView2 != null;

    /// <summary>
    /// Initialize WebView2 in a hidden form
    /// Must be called from UI thread
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            Debug.WriteLine("[WebView2Helper] Initializing...");

            // Create hidden form to host WebView2
            _hiddenForm = new Form
            {
                Width = 1,
                Height = 1,
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-10000, -10000),
                Opacity = 0
            };

            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };

            _hiddenForm.Controls.Add(_webView);
            _hiddenForm.Show();
            _hiddenForm.Hide();

            // Initialize WebView2 with options
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(Path.GetTempPath(), "RoMarketCrawler_WebView2"));

            await _webView.EnsureCoreWebView2Async(env);

            // Configure WebView2 settings
            var settings = _webView.CoreWebView2.Settings;
            settings.IsScriptEnabled = true;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            // Set user agent to match regular Chrome
            _webView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

            _isInitialized = true;
            Debug.WriteLine("[WebView2Helper] Initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2Helper] Initialization failed: {ex.Message}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Fetch HTML content from a URL using WebView2
    /// </summary>
    /// <param name="url">URL to fetch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="timeoutSeconds">Timeout in seconds (default 30)</param>
    /// <returns>HTML content of the page</returns>
    public async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default, int timeoutSeconds = 30)
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("WebView2 is not initialized. Call InitializeAsync first.");
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            Debug.WriteLine($"[WebView2Helper] Fetching: {url}");

            var tcs = new TaskCompletionSource<string>();
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            void NavigationCompletedHandler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess)
                {
                    Debug.WriteLine($"[WebView2Helper] Navigation completed successfully");
                }
                else
                {
                    Debug.WriteLine($"[WebView2Helper] Navigation failed: {e.WebErrorStatus}");
                }
            }

            _webView!.CoreWebView2.NavigationCompleted += NavigationCompletedHandler;

            try
            {
                // Navigate to URL
                _webView.CoreWebView2.Navigate(url);

                // Wait for page to load and get HTML
                // We need to wait a bit for Cloudflare challenge to complete
                await Task.Delay(500, linkedCts.Token);

                // Check if we're on a Cloudflare challenge page and wait
                for (int i = 0; i < 20; i++) // Max 10 seconds waiting for challenge
                {
                    linkedCts.Token.ThrowIfCancellationRequested();

                    var title = await _webView.CoreWebView2.ExecuteScriptAsync("document.title");
                    title = title.Trim('"');
                    Debug.WriteLine($"[WebView2Helper] Page title: {title}");

                    if (title.Contains("Just a moment") || title.Contains("Checking"))
                    {
                        Debug.WriteLine("[WebView2Helper] Cloudflare challenge detected, waiting...");
                        await Task.Delay(500, linkedCts.Token);
                    }
                    else
                    {
                        break;
                    }
                }

                // Get page HTML
                var html = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "document.documentElement.outerHTML");

                // Remove JSON escaping from the result
                if (html.StartsWith("\"") && html.EndsWith("\""))
                {
                    html = html[1..^1];
                    html = System.Text.RegularExpressions.Regex.Unescape(html);
                }

                Debug.WriteLine($"[WebView2Helper] Got HTML, length: {html.Length}");
                return html;
            }
            finally
            {
                _webView.CoreWebView2.NavigationCompleted -= NavigationCompletedHandler;
                timeoutCts.Dispose();
                linkedCts.Dispose();
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// Check if WebView2 runtime is available on the system
    /// </summary>
    public static bool IsWebView2Available()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Debug.WriteLine($"[WebView2Helper] WebView2 version: {version}");
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _webView?.Dispose();
        _hiddenForm?.Dispose();
        _initLock.Dispose();
        _requestLock.Dispose();

        Debug.WriteLine("[WebView2Helper] Disposed");
    }
}
