using System.Diagnostics;

namespace RoMarketCrawler;

/// <summary>
/// Application context that manages the startup splash and main form lifecycle.
/// </summary>
public class StartupApplicationContext : ApplicationContext
{
    private readonly StartupSplashForm _splashForm;

    public StartupApplicationContext()
    {
        _splashForm = new StartupSplashForm(this);
        _splashForm.Show();
    }

    public void SwitchToMainForm()
    {
        var mainForm = new Form1();
        MainForm = mainForm;
        mainForm.Show();
        _splashForm.Close();
    }

    public void ExitApplication()
    {
        _splashForm.Close();
        ExitThread();
    }
}

/// <summary>
/// Startup splash form that performs all validation checks before showing the main form.
/// This form runs inside Application.Run() so async/await works correctly.
/// </summary>
public partial class StartupSplashForm : Form
{
    private readonly Label _lblStatus;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblProgress;
    private readonly StartupApplicationContext? _appContext;

    public StartupSplashForm(StartupApplicationContext? appContext = null)
    {
        _appContext = appContext;

        // Form setup
        Text = "RO Market Crawler";
        Size = new Size(450, 180);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(250, 250, 250);
        ShowIcon = false;
        ControlBox = false;

        // Status label
        _lblStatus = new Label
        {
            Text = "프로그램을 시작하고 있습니다...",
            Font = new Font("Malgun Gothic", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 51, 51),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(15, 25),
            Size = new Size(405, 30)
        };

        // Progress bar
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(50, 70),
            Size = new Size(335, 25)
        };

        // Progress text
        _lblProgress = new Label
        {
            Text = "초기화 중...",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = Color.FromArgb(102, 102, 102),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(15, 105),
            Size = new Size(405, 25)
        };

        Controls.AddRange(new Control[] { _lblStatus, _progressBar, _lblProgress });

        // Start validation when form is shown
        Shown += OnFormShown;
    }

    private async void OnFormShown(object? sender, EventArgs e)
    {
        try
        {
            await RunValidationAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupSplashForm] Validation exception: {ex.Message}");
            _appContext?.ExitApplication();
        }
    }

    private async Task RunValidationAsync()
    {
        using var validator = new Services.StartupValidator();

        // 1. Single instance check
        UpdateProgress("중복 실행 확인 중...", 10);
        if (!validator.CheckSingleInstancePublic())
        {
            ShowError("프로그램이 이미 실행 중입니다.\n중복 실행은 허용되지 않습니다.", "중복 실행");
            _appContext?.ExitApplication();
            return;
        }

        // 2. Network check
        UpdateProgress("네트워크 연결 확인 중...", 25);
        if (!await validator.CheckNetworkPublicAsync())
        {
            ShowError("네트워크 연결이 없습니다.\n인터넷 연결을 확인한 후 다시 시도해주세요.", "네트워크 오류");
            _appContext?.ExitApplication();
            return;
        }

        // 3. Expiration check
        UpdateProgress("사용 기간 확인 중...", 40);
        var expirationResult = await validator.CheckExpirationPublicAsync();
        if (!expirationResult.IsValid)
        {
            var message = expirationResult.ErrorType switch
            {
                Services.ExpirationErrorType.Expired =>
                    $"프로그램 사용 기간이 만료되었습니다.\n\n만료일: {Services.StartupValidator.ExpirationDateKST}\n현재 서버 시간: {expirationResult.ServerTime:yyyy-MM-dd HH:mm:ss}\n\n새 버전을 요청해주세요.",
                Services.ExpirationErrorType.TimeCheckFailed =>
                    "서버 시간을 확인할 수 없습니다.\n네트워크 연결을 확인하거나 잠시 후 다시 시도해주세요.",
                _ => "사용 기간 확인에 실패했습니다."
            };
            ShowError(message, "사용 기간 만료");
            _appContext?.ExitApplication();
            return;
        }

        // 4. Consent dialog
        UpdateProgress("이용 동의 확인 중...", 55);
        Hide(); // Hide splash while showing consent dialog
        if (!validator.ShowConsentDialogPublic())
        {
            _appContext?.ExitApplication();
            return;
        }
        Show(); // Show splash again

        // 5. Item index check
        UpdateProgress("아이템 인덱스 확인 중...", 70);
        var indexResult = await CheckAndBuildItemIndexAsync();
        if (!indexResult)
        {
            _appContext?.ExitApplication();
            return;
        }

        // All checks passed
        UpdateProgress("완료!", 100);
        await Task.Delay(300); // Brief pause to show completion

        _appContext?.SwitchToMainForm();
    }

    private async Task<bool> CheckAndBuildItemIndexAsync()
    {
        try
        {
            var indexService = new Services.ItemIndexService();
            await indexService.LoadFromCacheAsync();

            if (indexService.TotalCount > 0)
            {
                Debug.WriteLine($"[StartupSplashForm] Item index exists with {indexService.TotalCount} items");
                return true;
            }

            Debug.WriteLine("[StartupSplashForm] No item index found, starting auto-indexing");

            // Update UI for indexing
            _lblStatus.Text = "아이템 인덱스 생성";
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 0;
            _lblProgress.Text = "인덱스를 생성하고 있습니다... (처음 실행 시 1-2분 소요)";

            // Create progress handler
            var progress = new Progress<Services.IndexProgress>(p =>
            {
                if (!IsDisposed)
                {
                    _lblProgress.Text = !string.IsNullOrEmpty(p.CurrentCategory)
                        ? $"{p.Phase} - {p.CurrentCategory} ({p.ItemsCollected:N0}개)"
                        : p.Phase;

                    // Calculate progress based on ItemsCollected
                    // TotalCategories > 0 means we have category-based progress (ID scan)
                    // Otherwise estimate from ItemsCollected (typical total ~22,000 items)
                    int percent;
                    if (p.TotalCategories > 0)
                    {
                        percent = (int)Math.Min(95, Math.Max(0, p.ProgressPercent));
                    }
                    else if (p.ItemsCollected > 0)
                    {
                        const int estimatedTotal = 22000;
                        percent = (int)Math.Min(95, (p.ItemsCollected * 100.0) / estimatedTotal);
                    }
                    else
                    {
                        percent = 0;
                    }
                    _progressBar.Value = percent;
                }
            });

            // Run indexing
            var success = await indexService.RebuildIndexAsync(progress);

            if (!success)
            {
                ShowError("인덱스 생성에 실패했습니다.", "인덱스 오류");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StartupSplashForm] Item index check failed: {ex.Message}");
            ShowError($"아이템 인덱스 확인 중 오류가 발생했습니다.\n\n{ex.Message}", "인덱스 오류");
            return false;
        }
    }

    private void UpdateProgress(string message, int percent)
    {
        if (!IsDisposed)
        {
            _lblProgress.Text = message;
            if (_progressBar.Style == ProgressBarStyle.Continuous)
            {
                _progressBar.Value = percent;
            }
        }
    }

    private static void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
