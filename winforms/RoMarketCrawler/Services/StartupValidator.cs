using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace RoMarketCrawler.Services;

/// <summary>
/// Expiration check error types
/// </summary>
public enum ExpirationErrorType
{
    None,
    Expired,
    TimeCheckFailed
}

/// <summary>
/// Result of expiration check
/// </summary>
public record ExpirationResult(bool IsValid, ExpirationErrorType ErrorType, DateTime? ServerTime);

/// <summary>
/// Handles all startup validation checks before the main application runs.
/// Checks are performed in order: Mutex -> Network -> Expiration -> Consent -> ItemIndex
/// </summary>
public class StartupValidator : IDisposable
{
    // ============================================================
    // CONFIGURATION - Modify these values for distribution
    // ============================================================

    /// <summary>
    /// Expiration date for the application (Korea Standard Time)
    /// Format: "yyyy-MM-dd HH:mm:ss"
    /// Set to null for no expiration
    /// </summary>
    public static readonly string? ExpirationDateKST = "2026-02-09 23:59:59";

    /// <summary>
    /// Application name for Mutex
    /// </summary>
    private const string MutexName = "RoMarketCrawler_SingleInstance_Mutex";

    // ============================================================

    private Mutex? _mutex;
    private bool _ownsMutex;
    private readonly HttpClient _httpClient;

    public StartupValidator()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)  // Shorter timeout for startup checks
        };
    }

    /// <summary>
    /// Run all startup validation checks in sequence.
    /// Returns true if all checks pass, false otherwise.
    /// </summary>
    public async Task<bool> ValidateAllAsync()
    {
        // 1. Check for duplicate instance
        if (!CheckSingleInstance())
        {
            ShowError("프로그램이 이미 실행 중입니다.\n중복 실행은 허용되지 않습니다.", "중복 실행");
            return false;
        }

        // 2. Check network connectivity
        if (!await CheckNetworkAsync())
        {
            ShowError("네트워크 연결이 없습니다.\n인터넷 연결을 확인한 후 다시 시도해주세요.", "네트워크 오류");
            ReleaseMutex();
            return false;
        }

        // 3. Check expiration date using external time API
        var expirationResult = await CheckExpirationAsync();
        if (!expirationResult.IsValid)
        {
            var message = expirationResult.ErrorType switch
            {
                ExpirationErrorType.Expired => $"프로그램 사용 기간이 만료되었습니다.\n\n만료일: {ExpirationDateKST}\n현재 서버 시간: {expirationResult.ServerTime:yyyy-MM-dd HH:mm:ss}\n\n새 버전을 요청해주세요.",
                ExpirationErrorType.TimeCheckFailed => "서버 시간을 확인할 수 없습니다.\n네트워크 연결을 확인하거나 잠시 후 다시 시도해주세요.",
                _ => "사용 기간 확인에 실패했습니다."
            };
            ShowError(message, "사용 기간 만료");
            ReleaseMutex();
            return false;
        }

        // 4. Show program info and get user consent
        if (!ShowConsentDialog())
        {
            ReleaseMutex();
            return false;
        }

        // 5. Check item index and auto-index if needed
        if (!await CheckAndBuildItemIndexAsync())
        {
            ReleaseMutex();
            return false;
        }

        return true;
    }

    #region 1. Single Instance Check (Mutex)

    /// <summary>
    /// Public wrapper for single instance check
    /// </summary>
    public bool CheckSingleInstancePublic() => CheckSingleInstance();

    private bool CheckSingleInstance()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);

            if (!_ownsMutex)
            {
                // Another instance is running
                Debug.WriteLine("[StartupValidator] Another instance is already running");
                return false;
            }

            Debug.WriteLine("[StartupValidator] Single instance check passed");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupValidator] Mutex creation failed: {ex.Message}");
            return false;
        }
    }

    private void ReleaseMutex()
    {
        if (_mutex != null && _ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
        }
    }

    #endregion

    #region 2. Network Connectivity Check

    /// <summary>
    /// Public wrapper for network check
    /// </summary>
    public Task<bool> CheckNetworkPublicAsync() => CheckNetworkAsync();

    private async Task<bool> CheckNetworkAsync()
    {
        var testUrls = new[]
        {
            "https://www.google.com",
            "https://www.microsoft.com",
            "https://ro.gnjoy.com"
        };

        // Run all checks in parallel, return true if any succeeds
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tasks = testUrls.Select(async url =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[StartupValidator] Network check passed via {url}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupValidator] Network check failed for {url}: {ex.Message}");
            }
            return false;
        }).ToList();

        try
        {
            // Wait for first success or all to complete
            while (tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks);
                if (await completedTask)
                {
                    return true;
                }
                tasks.Remove(completedTask);
            }
        }
        catch { }

        Debug.WriteLine("[StartupValidator] All network checks failed");
        return false;
    }

    #endregion

    #region 3. Expiration Check (External Time API)

    /// <summary>
    /// Public wrapper for expiration check
    /// </summary>
    public Task<ExpirationResult> CheckExpirationPublicAsync() => CheckExpirationAsync();

    private async Task<ExpirationResult> CheckExpirationAsync()
    {
        // If no expiration date set, always valid
        if (string.IsNullOrEmpty(ExpirationDateKST))
        {
            Debug.WriteLine("[StartupValidator] No expiration date configured, skipping check");
            return new ExpirationResult(true, ExpirationErrorType.None, null);
        }

        // Parse expiration date
        if (!DateTime.TryParse(ExpirationDateKST, out var expirationDate))
        {
            Debug.WriteLine($"[StartupValidator] Invalid expiration date format: {ExpirationDateKST}");
            return new ExpirationResult(false, ExpirationErrorType.TimeCheckFailed, null);
        }

        // Get current time from external API
        var serverTime = await GetServerTimeAsync();

        if (!serverTime.HasValue)
        {
            Debug.WriteLine("[StartupValidator] Failed to get server time from all APIs");
            return new ExpirationResult(false, ExpirationErrorType.TimeCheckFailed, null);
        }

        Debug.WriteLine($"[StartupValidator] Server time (KST): {serverTime.Value:yyyy-MM-dd HH:mm:ss}");
        Debug.WriteLine($"[StartupValidator] Expiration date: {expirationDate:yyyy-MM-dd HH:mm:ss}");

        if (serverTime.Value > expirationDate)
        {
            Debug.WriteLine("[StartupValidator] Application has expired");
            return new ExpirationResult(false, ExpirationErrorType.Expired, serverTime);
        }

        Debug.WriteLine("[StartupValidator] Expiration check passed");
        return new ExpirationResult(true, ExpirationErrorType.None, serverTime);
    }

    private async Task<DateTime?> GetServerTimeAsync()
    {
        // Method 1: Try getting time from HTTP Date headers (most reliable)
        var headerTime = await GetTimeFromHttpHeadersAsync();
        if (headerTime.HasValue)
        {
            return headerTime;
        }

        // Method 2: Try dedicated time APIs as fallback
        var apiTime = await GetTimeFromApisAsync();
        if (apiTime.HasValue)
        {
            return apiTime;
        }

        Debug.WriteLine("[StartupValidator] All time retrieval methods failed");
        return null;
    }

    private async Task<DateTime?> GetTimeFromHttpHeadersAsync()
    {
        // Get time from HTTP Date headers of major websites (very reliable)
        var urls = new[]
        {
            "https://www.google.com",
            "https://www.microsoft.com",
            "https://www.apple.com",
            "https://www.amazon.com",
            "https://www.cloudflare.com"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tasks = urls.Select(async url =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.Headers.Date.HasValue)
                {
                    // Convert UTC to KST (UTC+9)
                    var utcTime = response.Headers.Date.Value.UtcDateTime;
                    var kstTime = utcTime.AddHours(9);
                    Debug.WriteLine($"[StartupValidator] Got time from {url} header: {kstTime:yyyy-MM-dd HH:mm:ss} KST");
                    return (DateTime?)kstTime;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupValidator] HTTP header time failed ({url}): {ex.Message}");
            }
            return null;
        }).ToList();

        try
        {
            while (tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks);
                var result = await completedTask;
                if (result.HasValue)
                {
                    return result;
                }
                tasks.Remove(completedTask);
            }
        }
        catch { }

        Debug.WriteLine("[StartupValidator] All HTTP header time checks failed");
        return null;
    }

    private async Task<DateTime?> GetTimeFromApisAsync()
    {
        // Fallback: Try dedicated time APIs
        var timeApis = new (string Url, Func<string, DateTime?> Parser)[]
        {
            ("https://worldtimeapi.org/api/timezone/Asia/Seoul", ParseWorldTimeApi),
            ("https://www.timeapi.io/api/Time/current/zone?timeZone=Asia/Seoul", ParseTimeApiIo),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tasks = timeApis.Select(async api =>
        {
            try
            {
                Debug.WriteLine($"[StartupValidator] Trying time API: {api.Url}");
                var response = await _httpClient.GetStringAsync(api.Url, cts.Token);
                var time = api.Parser(response);

                if (time.HasValue)
                {
                    Debug.WriteLine($"[StartupValidator] Got time from {api.Url}: {time.Value}");
                    return time;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupValidator] Time API failed ({api.Url}): {ex.Message}");
            }
            return null;
        }).ToList();

        try
        {
            while (tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks);
                var result = await completedTask;
                if (result.HasValue)
                {
                    return result;
                }
                tasks.Remove(completedTask);
            }
        }
        catch { }

        return null;
    }

    private static DateTime? ParseWorldTimeApi(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try datetime field first
            if (root.TryGetProperty("datetime", out var datetimeProp))
            {
                var datetimeStr = datetimeProp.GetString();
                if (DateTime.TryParse(datetimeStr, out var dt))
                {
                    return dt;
                }
            }

            // Try unixtime as fallback
            if (root.TryGetProperty("unixtime", out var unixtimeProp))
            {
                var unixtime = unixtimeProp.GetInt64();
                // Convert to KST (UTC+9)
                return DateTimeOffset.FromUnixTimeSeconds(unixtime).ToOffset(TimeSpan.FromHours(9)).DateTime;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupValidator] WorldTimeAPI parse error: {ex.Message}");
        }

        return null;
    }

    private static DateTime? ParseTimeApiIo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // TimeAPI.io returns dateTime field
            if (root.TryGetProperty("dateTime", out var datetimeProp))
            {
                var datetimeStr = datetimeProp.GetString();
                if (DateTime.TryParse(datetimeStr, out var dt))
                {
                    return dt;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupValidator] TimeAPI.io parse error: {ex.Message}");
        }

        return null;
    }

    #endregion

    #region 4. Consent Dialog

    /// <summary>
    /// Public wrapper for consent dialog
    /// </summary>
    public bool ShowConsentDialogPublic() => ShowConsentDialog();

    private bool ShowConsentDialog()
    {
        // Layout constants (matching ShowAboutDialog in Form1.cs)
        const int formWidth = 500;
        const int baseFormHeight = 540;  // Base height without logo
        const int leftMargin = 25;
        const int contentWidth = 440;

        // Load logo first to calculate dynamic heights
        Image? logoImage = null;
        int logoHeight = 0;
        int logoOffset = 0;
        try
        {
            logoImage = ResourceHelper.GetLogoImage();
            if (logoImage != null)
            {
                // Scale to fit contentWidth while maintaining aspect ratio
                var scale = (float)contentWidth / logoImage.Width;
                logoHeight = (int)(logoImage.Height * scale);
                logoOffset = logoHeight + 20;  // Logo height + padding
            }
        }
        catch { /* Ignore image loading errors */ }

        int formHeight = baseFormHeight + logoOffset;

        // Classic theme colors for consistent readability (always light theme)
        var clrBackground = Color.FromArgb(250, 250, 250);
        var clrText = Color.FromArgb(51, 51, 51);
        var clrTextMuted = Color.FromArgb(102, 102, 102);
        var clrLink = Color.FromArgb(70, 130, 180);
        var clrLegalText = Color.FromArgb(60, 60, 60);
        var clrLegalBg = Color.FromArgb(245, 245, 245);
        var clrButtonBg = Color.FromArgb(70, 130, 180);
        var clrButtonText = Color.White;
        var clrButtonDisagreeBg = Color.FromArgb(180, 80, 80);

        var legalNoticeText =
            "[프로그램 이용 및 배포 제한 안내]\r\n\r\n" +
            "1. 복제 및 재배포 금지\r\n" +
            "본 프로그램은 저작권법의 보호를 받으며, 저작권자의 명시적인 서면 동의 없이 " +
            "프로그램의 전부 또는 일부를 복제, 수정, 개작하거나 타인에게 재배포하는 행위를 " +
            "엄격히 금지합니다.\r\n\r\n" +
            "2. 서비스 중단\r\n" +
            "본 프로그램은 외부 웹사이트의 데이터를 수집하여 제공합니다. 데이터 제공처, " +
            "게임 운영사 또는 관련 권리자의 요청이 있을 경우, 본 프로그램의 배포 및 " +
            "서비스는 별도의 사전 공지 없이 즉시 중단될 수 있습니다.\r\n\r\n" +
            "3. 데이터 정확성\r\n" +
            "본 프로그램에서 제공하는 아이템 정보 및 거래 시세는 참고 목적으로만 제공되며, " +
            "실시간성, 정확성, 완전성을 보장하지 않습니다. 실제 게임 내 가격 및 거래 조건과 " +
            "차이가 있을 수 있으므로 반드시 게임 내에서 직접 확인하시기 바랍니다.\r\n\r\n" +
            "4. 거래 결정에 대한 책임\r\n" +
            "본 프로그램의 정보를 참고하여 이루어진 게임 내 거래 결정 및 그로 인한 손실에 대해 " +
            "저작권자는 어떠한 책임도 지지 않습니다. 모든 거래 결정은 사용자 본인의 " +
            "판단과 책임 하에 이루어져야 합니다.\r\n\r\n" +
            "5. 게임 이용약관 준수\r\n" +
            "본 프로그램의 사용으로 인해 발생할 수 있는 게임 이용약관 위반 및 그에 따른 " +
            "계정 제재 등의 불이익에 대해 저작권자는 책임을 지지 않습니다. 사용자는 해당 게임의 " +
            "이용약관을 숙지하고 준수할 책임이 있습니다.\r\n\r\n" +
            "6. 면책 조항\r\n" +
            "본 프로그램은 어떠한 보증 없이 '있는 그대로(AS-IS)' 제공됩니다. 프로그램 사용으로 " +
            "인해 발생하는 직접적, 간접적 손해에 대해 저작권자는 책임을 지지 않습니다. " +
            "본 프로그램은 개인적인 참고 목적으로만 사용하시기 바랍니다.\r\n\r\n" +
            "7. 개인정보 및 게임정보 보호\r\n" +
            "본 프로그램은 사용자의 개인정보, 게임 계정 정보, 게임 내 활동 정보 등 " +
            "어떠한 정보도 수집하거나 외부로 전송하지 않습니다. 모든 데이터는 " +
            "사용자의 로컬 PC에만 저장됩니다.\r\n\r\n" +
            "8. 위반 시 책임\r\n" +
            "상기 사항을 위반하여 발생하는 모든 법적 책임은 위반자 본인에게 있으며, " +
            "저작권자는 관련 법령에 따라 법적 조치를 취할 수 있습니다.\r\n\r\n" +
            "[관련 법규 확인]\r\n" +
            "- 한국저작권위원회 / 국가법령정보센터";

        // Create custom dialog with consent (classic theme)
        using var consentForm = new Form
        {
            Text = "프로그램 정보 및 이용 동의",
            Size = new Size(formWidth, formHeight),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = clrBackground,
            ShowIcon = true
        };

        // Load icon
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream("RoMarketCrawler.app.ico");
            if (iconStream != null)
            {
                consentForm.Icon = new Icon(iconStream);
            }
        }
        catch { }

        // Logo image
        PictureBox? picLogo = null;
        if (logoImage != null)
        {
            picLogo = new PictureBox
            {
                Image = logoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(contentWidth, logoHeight),
                Location = new Point(leftMargin, 10)
            };
        }

        // Title with dynamic version
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.0.0";
        var lblTitle = new Label
        {
            Text = $"RO Market Crawler {versionStr}",
            Font = new Font("Malgun Gothic", 14, FontStyle.Bold),
            ForeColor = clrLink,
            AutoSize = true,
            Location = new Point(leftMargin, 18 + logoOffset)
        };

        // Description
        var lblDesc = new Label
        {
            Text = "라그나로크 온라인 거래 정보 검색 및 모니터링 프로그램",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = clrTextMuted,
            AutoSize = true,
            Location = new Point(leftMargin, 45 + logoOffset)
        };

        // Data source section
        var lblSource = new Label
        {
            Text = "[데이터 출처]\n" +
                   "  - 아이템 정보: kafra.kr\n" +
                   "  - 노점 거래: ro.gnjoy.com",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 72 + logoOffset)
        };

        // Creator
        var lblCreator = new Label
        {
            Text = "Created by: 티포니",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 130 + logoOffset)
        };

        // Contact
        var lblContact = new Label
        {
            Text = "문의: 메뉴 > 도움말 > 정보의 카카오톡 오픈프로필 참조",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 150 + logoOffset)
        };

        // Privacy notice (emphasized)
        var lblPrivacy = new Label
        {
            Text = "** 본 프로그램은 개인정보 및 게임정보를 일체 수집하지 않습니다 **",
            Font = new Font("Malgun Gothic", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 60),  // Green for trust
            AutoSize = true,
            Location = new Point(leftMargin, 175 + logoOffset)
        };

        // Legal notice (scrollable)
        var txtLegalNotice = new TextBox
        {
            Text = legalNoticeText,
            Font = new Font("Malgun Gothic", 8.5f),
            ForeColor = clrLegalText,
            BackColor = clrLegalBg,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(leftMargin, 200 + logoOffset),
            Size = new Size(contentWidth, 220)
        };

        // Confirm label
        var lblConfirm = new Label
        {
            Text = "위 내용을 모두 읽었으며, 이에 동의합니다.",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 430 + logoOffset)
        };

        // Button dimensions for centering calculation
        const int btnAgreeWidth = 90;
        const int btnDisagreeWidth = 110;  // Wider for longer Korean text
        const int btnGap = 15;
        const int totalBtnWidth = btnAgreeWidth + btnGap + btnDisagreeWidth;  // 215px

        // Center buttons within the content area
        int btnStartX = leftMargin + (contentWidth - totalBtnWidth) / 2;

        // Agree button (styled for classic theme)
        var btnAgree = new Button
        {
            Text = "동의",
            Width = btnAgreeWidth,
            Height = 30,
            Location = new Point(btnStartX, 455 + logoOffset),
            FlatStyle = FlatStyle.Flat,
            BackColor = clrButtonBg,
            ForeColor = clrButtonText,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        btnAgree.FlatAppearance.BorderSize = 0;

        // Disagree button
        var btnDisagree = new Button
        {
            Text = "동의하지 않음",
            Width = btnDisagreeWidth,
            Height = 30,
            Location = new Point(btnStartX + btnAgreeWidth + btnGap, 455 + logoOffset),
            FlatStyle = FlatStyle.Flat,
            BackColor = clrButtonDisagreeBg,
            ForeColor = clrButtonText,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel
        };
        btnDisagree.FlatAppearance.BorderSize = 0;

        // Add controls to form
        if (picLogo != null)
        {
            consentForm.Controls.Add(picLogo);
        }
        consentForm.Controls.AddRange(new Control[] {
            lblTitle, lblDesc, lblSource, lblCreator, lblContact, lblPrivacy,
            txtLegalNotice, lblConfirm, btnAgree, btnDisagree
        });
        consentForm.AcceptButton = btnAgree;
        consentForm.CancelButton = btnDisagree;

        // Deselect text and set focus to agree button when form is shown
        consentForm.Shown += (s, e) =>
        {
            txtLegalNotice.SelectionStart = 0;
            txtLegalNotice.SelectionLength = 0;
            btnAgree.Focus();
        };

        var result = consentForm.ShowDialog();

        if (result == DialogResult.OK)
        {
            Debug.WriteLine("[StartupValidator] User agreed to terms");
            return true;
        }

        Debug.WriteLine("[StartupValidator] User declined terms");
        return false;
    }

    #endregion

    #region 5. Item Index Check

    private async Task<bool> CheckAndBuildItemIndexAsync()
    {
        try
        {
            // Use the same data directory as DI container
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RoMarketCrawler");
            var indexService = new ItemIndexService(dataDir);
            await indexService.LoadFromCacheAsync();

            // Check if index exists and has items
            if (indexService.TotalCount > 0)
            {
                Debug.WriteLine($"[StartupValidator] Item index exists with {indexService.TotalCount} items");
                return true;
            }

            Debug.WriteLine("[StartupValidator] No item index found, starting auto-indexing");

            // Show indexing progress dialog
            return await ShowIndexingProgressAsync(indexService);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupValidator] Item index check failed: {ex.Message}");
            ShowError($"아이템 인덱스 확인 중 오류가 발생했습니다.\n\n{ex.Message}", "인덱스 오류");
            return false;
        }
    }

    private Task<bool> ShowIndexingProgressAsync(ItemIndexService indexService)
    {
        var tcs = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        var progressForm = new Form
        {
            Text = "아이템 인덱스 생성",
            Size = new Size(450, 200),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(30, 30, 30),
            ShowIcon = false,
            ControlBox = false
        };

        var lblStatus = new Label
        {
            Text = "아이템 인덱스를 생성하고 있습니다...\n처음 실행 시 약 1-2분 정도 소요됩니다.",
            Font = new Font("Malgun Gothic", 10),
            ForeColor = Color.FromArgb(220, 220, 220),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(15, 20),
            Size = new Size(405, 50)
        };

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Location = new Point(50, 85),
            Size = new Size(335, 25)
        };

        var lblProgress = new Label
        {
            Text = "준비 중...",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(15, 120),
            Size = new Size(405, 25)
        };

        var btnCancel = new Button
        {
            Text = "취소",
            Width = 80,
            Height = 28,
            Location = new Point(175, 155),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) =>
        {
            cts.Cancel();
            progressForm.DialogResult = DialogResult.Cancel;
        };

        progressForm.Controls.AddRange(new Control[] { lblStatus, progressBar, lblProgress, btnCancel });

        // Create a custom progress handler (will be used from ThreadPool thread)
        Action<IndexProgress> updateProgress = p =>
        {
            if (progressForm.IsHandleCreated && !progressForm.IsDisposed)
            {
                try
                {
                    // Use BeginInvoke for non-blocking UI updates
                    progressForm.BeginInvoke(() =>
                    {
                        if (!progressForm.IsDisposed)
                        {
                            lblProgress.Text = !string.IsNullOrEmpty(p.CurrentCategory)
                                ? $"{p.Phase} - {p.CurrentCategory} ({p.ItemsCollected}개)"
                                : p.Phase;

                            var percent = (int)Math.Min(100, Math.Max(0, p.ProgressPercent));
                            progressBar.Value = percent;
                        }
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        };

        // Custom IProgress implementation that directly invokes on UI thread
        var progress = new ActionProgress<IndexProgress>(updateProgress);

        // Use Timer to start indexing after form is fully rendered (avoids async deadlock)
        var startTimer = new System.Windows.Forms.Timer { Interval = 100 };
        startTimer.Tick += (s, e) =>
        {
            startTimer.Stop();
            startTimer.Dispose();

            // Start indexing on thread pool thread
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Run indexing synchronously on this thread pool thread
                    var success = indexService.RebuildIndexAsync(progress, cts.Token).GetAwaiter().GetResult();

                    // Update UI on form's thread
                    if (progressForm.IsHandleCreated && !progressForm.IsDisposed)
                    {
                        progressForm.BeginInvoke(() =>
                        {
                            if (success)
                            {
                                Debug.WriteLine("[StartupValidator] Indexing completed successfully");
                                progressForm.DialogResult = DialogResult.OK;
                            }
                            else
                            {
                                if (!cts.Token.IsCancellationRequested)
                                {
                                    MessageBox.Show(progressForm,
                                        "인덱스 생성에 실패했습니다.",
                                        "인덱스 오류",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Error);
                                }
                                progressForm.DialogResult = DialogResult.Abort;
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[StartupValidator] Indexing cancelled by user");
                    if (progressForm.IsHandleCreated && !progressForm.IsDisposed)
                    {
                        progressForm.BeginInvoke(() => progressForm.DialogResult = DialogResult.Cancel);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[StartupValidator] Indexing failed: {ex.Message}");
                    if (progressForm.IsHandleCreated && !progressForm.IsDisposed)
                    {
                        progressForm.BeginInvoke(() =>
                        {
                            MessageBox.Show(progressForm,
                                $"인덱스 생성 중 오류가 발생했습니다.\n\n{ex.Message}",
                                "인덱스 오류",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                            progressForm.DialogResult = DialogResult.Abort;
                        });
                    }
                }
            });
        };

        progressForm.Shown += (s, e) => startTimer.Start();

        // Handle form closed
        progressForm.FormClosed += (s, e) =>
        {
            var result = progressForm.DialogResult == DialogResult.OK;

            if (!result && progressForm.DialogResult == DialogResult.Cancel)
            {
                ShowError("인덱스 생성이 취소되었습니다.\n프로그램을 사용하려면 인덱스가 필요합니다.", "인덱스 필요");
            }

            cts.Dispose();
            progressForm.Dispose();
            tcs.SetResult(result);
        };

        progressForm.Show();

        return tcs.Task;
    }

    #endregion

    #region Helper Methods

    private static void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    #endregion

    public void Dispose()
    {
        _httpClient.Dispose();

        if (_mutex != null)
        {
            if (_ownsMutex)
            {
                try { _mutex.ReleaseMutex(); }
                catch { }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}

/// <summary>
/// Simple IProgress implementation that directly calls the provided action.
/// Unlike Progress&lt;T&gt;, this doesn't capture SynchronizationContext.
/// </summary>
internal class ActionProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public ActionProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value)
    {
        _handler(value);
    }
}
