using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Service for checking and applying application updates from GitHub Releases
/// </summary>
public class UpdateService : IDisposable
{
    private const string GitHubApiUrl = "https://api.github.com/repos/leader001a/ro-market-crawler/releases/latest";
    private const string UserAgent = "RoMarketCrawler";
    private const string ExeFileName = "RoMarketCrawler.exe";

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Check for updates from GitHub Releases
    /// </summary>
    /// <returns>UpdateInfo if update available, null otherwise</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[UpdateService] GitHub API returned {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null)
            {
                Debug.WriteLine("[UpdateService] Failed to parse release JSON");
                return null;
            }

            var latestVersion = release.GetVersion();
            var currentVersion = GetCurrentVersion();

            if (latestVersion == null || currentVersion == null)
            {
                Debug.WriteLine($"[UpdateService] Version parse failed: latest={release.TagName}, current={currentVersion}");
                return null;
            }

            Debug.WriteLine($"[UpdateService] Current: {currentVersion}, Latest: {latestVersion}");

            if (latestVersion <= currentVersion)
            {
                Debug.WriteLine("[UpdateService] Already up to date");
                return null;
            }

            // Find exe asset
            var exeAsset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (exeAsset == null)
            {
                Debug.WriteLine("[UpdateService] No exe asset found in release");
                return null;
            }

            return new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                TagName = release.TagName,
                ReleaseName = release.Name,
                ReleaseNotes = release.Body,
                ReleaseUrl = release.HtmlUrl,
                DownloadUrl = exeAsset.BrowserDownloadUrl,
                FileSize = exeAsset.Size,
                FileName = exeAsset.Name
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download update file with progress reporting
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(
        UpdateInfo updateInfo,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"RoMarketCrawler_update_{updateInfo.TagName}.exe");

            using var response = await _httpClient.GetAsync(
                updateInfo.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.FileSize;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                progress?.Report(new DownloadProgress
                {
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = totalBytes,
                    Percentage = totalBytes > 0 ? (int)(downloadedBytes * 100 / totalBytes) : 0
                });
            }

            Debug.WriteLine($"[UpdateService] Downloaded to: {tempPath}");
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Download failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Apply update by creating a batch script that replaces the exe after app exits
    /// </summary>
    public bool ApplyUpdate(string downloadedFilePath)
    {
        try
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath))
            {
                currentExePath = Path.Combine(AppContext.BaseDirectory, ExeFileName);
            }

            var currentDir = Path.GetDirectoryName(currentExePath) ?? AppContext.BaseDirectory;
            var batchPath = Path.Combine(Path.GetTempPath(), "RoMarketCrawler_update.bat");

            // Create batch script that:
            // 1. Waits for current process to exit
            // 2. Copies new exe over old one
            // 3. Starts new exe
            // 4. Deletes itself
            var batchContent = $@"@echo off
chcp 65001 > nul
echo 업데이트 적용 중...
timeout /t 2 /nobreak > nul

:retry
tasklist /FI ""IMAGENAME eq {ExeFileName}"" 2>NUL | find /I /N ""{ExeFileName}"" > NUL
if ""%ERRORLEVEL%""==""0"" (
    echo 프로그램 종료 대기 중...
    timeout /t 1 /nobreak > nul
    goto retry
)

echo 파일 교체 중...
copy /Y ""{downloadedFilePath}"" ""{currentExePath}""
if errorlevel 1 (
    echo 업데이트 실패! 권한을 확인하세요.
    pause
    exit /b 1
)

echo 업데이트 완료! 프로그램을 시작합니다...
del ""{downloadedFilePath}"" 2>nul
start """" ""{currentExePath}""
del ""%~f0""
";

            File.WriteAllText(batchPath, batchContent, System.Text.Encoding.UTF8);

            // Start the batch script hidden
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);

            Debug.WriteLine("[UpdateService] Update batch script started");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Apply update failed: {ex.Message}");
            return false;
        }
    }

    private static Version? GetCurrentVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Information about an available update
/// </summary>
public class UpdateInfo
{
    public Version CurrentVersion { get; set; } = new();
    public Version LatestVersion { get; set; } = new();
    public string TagName { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileName { get; set; } = string.Empty;

    public string FileSizeFormatted
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024.0):F1} MB";
        }
    }
}

/// <summary>
/// Download progress information
/// </summary>
public class DownloadProgress
{
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int Percentage { get; set; }

    public string DownloadedFormatted => FormatBytes(DownloadedBytes);
    public string TotalFormatted => FormatBytes(TotalBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
