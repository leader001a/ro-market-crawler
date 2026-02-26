using System.Diagnostics;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Dialog for showing update availability and download progress
/// </summary>
public class UpdateDialog : Form
{
    private readonly UpdateService _updateService;
    private readonly UpdateInfo _updateInfo;

    private PictureBox _picLogo = null!;
    private Label _lblTitle = null!;
    private Label _lblVersion = null!;
    private Label _lblSize = null!;
    private TextBox _txtReleaseNotes = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblProgress = null!;
    private Button _btnUpdate = null!;
    private Button _btnSkip = null!;
    private Button _btnCancel = null!;

    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading;

    public UpdateDialog(UpdateService updateService, UpdateInfo updateInfo)
    {
        _updateService = updateService;
        _updateInfo = updateInfo;

        InitializeComponents();
        ApplyTheme();
    }

    private void InitializeComponents()
    {
        Text = "업데이트 확인";
        Size = new Size(480, 520);  // Width original, Height 1.5x (380 -> 520 approx)
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;  // We'll center on the correct screen
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;  // Show icon in title bar
        ShowInTaskbar = false;

        // Load icon for title bar
        LoadTitleBarIcon();

        // Center on the screen where the cursor is (same monitor as the app)
        // Also deselect textbox on load
        Load += (s, e) =>
        {
            CenterOnCurrentScreen();
            // Remove focus from textbox to prevent blue selection
            ActiveControl = _btnUpdate;
        };

        var padding = 20;
        var currentY = padding;
        var logoSize = 128;  // 2x larger

        // Logo image (top right)
        _picLogo = new PictureBox
        {
            Size = new Size(logoSize, logoSize),
            Location = new Point(ClientSize.Width - padding - logoSize, padding),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        LoadLogoImage();

        // Title
        _lblTitle = new Label
        {
            Text = "새로운 버전이 있습니다!",
            Font = new Font("Malgun Gothic", 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 45;

        // Version info
        _lblVersion = new Label
        {
            Text = $"현재 버전: v{_updateInfo.CurrentVersion}  →  최신 버전: {_updateInfo.TagName}",
            Font = new Font("Malgun Gothic", 10),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 35;

        // File size
        _lblSize = new Label
        {
            Text = $"다운로드 크기: {_updateInfo.FileSizeFormatted}",
            Font = new Font("Malgun Gothic", 9),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 40;

        // Release notes label
        var lblNotesTitle = new Label
        {
            Text = "변경 사항:",
            Font = new Font("Malgun Gothic", 9, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 25;

        // Release notes (convert markdown to plain text) - Height increased for more content
        _txtReleaseNotes = new TextBox
        {
            Text = FormatReleaseNotes(_updateInfo.ReleaseNotes),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(padding, currentY),
            Size = new Size(ClientSize.Width - padding * 2, 220),  // Taller (120 -> 220)
            Font = new Font("Malgun Gothic", 9)
        };
        currentY += 235;

        // Progress bar (hidden initially)
        _progressBar = new ProgressBar
        {
            Location = new Point(padding, currentY),
            Size = new Size(ClientSize.Width - padding * 2, 23),
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };

        // Progress label (hidden initially)
        _lblProgress = new Label
        {
            Text = "다운로드 준비 중...",
            Font = new Font("Malgun Gothic", 9),
            AutoSize = true,
            Location = new Point(padding, currentY + 5),
            Visible = false
        };
        currentY += 40;

        // Buttons - center 2 visible buttons (업데이트, 나중에)
        var buttonWidth = 100;
        var buttonHeight = 32;
        var buttonSpacing = 10;
        var totalButtonWidth = buttonWidth * 2 + buttonSpacing;  // 2 buttons only
        var buttonStartX = (ClientSize.Width - totalButtonWidth) / 2;

        _btnUpdate = new Button
        {
            Text = "업데이트",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(buttonStartX, currentY),
            Font = new Font("Malgun Gothic", 9, FontStyle.Bold)
        };
        _btnUpdate.Click += BtnUpdate_Click;

        _btnSkip = new Button
        {
            Text = "나중에",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(buttonStartX + buttonWidth + buttonSpacing, currentY),
            Font = new Font("Malgun Gothic", 9)
        };
        _btnSkip.Click += (s, e) => DialogResult = DialogResult.Cancel;

        // Cancel button - centered alone (shown during download)
        var cancelButtonX = (ClientSize.Width - buttonWidth) / 2;
        _btnCancel = new Button
        {
            Text = "취소",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(cancelButtonX, currentY),
            Font = new Font("Malgun Gothic", 9),
            Visible = false
        };
        _btnCancel.Click += BtnCancel_Click;

        Controls.AddRange(new Control[]
        {
            _picLogo, _lblTitle, _lblVersion, _lblSize, lblNotesTitle,
            _txtReleaseNotes, _progressBar, _lblProgress,
            _btnUpdate, _btnSkip, _btnCancel
        });
    }

    /// <summary>
    /// Load icon for title bar from embedded resource
    /// </summary>
    private void LoadTitleBarIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("RoMarketCrawler.app.ico");
            if (stream != null)
            {
                Icon = new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateDialog] Failed to load icon: {ex.Message}");
        }
    }

    /// <summary>
    /// Load logo image from embedded resource
    /// </summary>
    private void LoadLogoImage()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("RoMarketCrawler.Data.logo.png");
            if (stream != null)
            {
                _picLogo.Image = Image.FromStream(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateDialog] Failed to load logo: {ex.Message}");
        }
    }

    /// <summary>
    /// Center the dialog on the screen where the cursor currently is
    /// </summary>
    private void CenterOnCurrentScreen()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var workingArea = screen.WorkingArea;
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Top + (workingArea.Height - Height) / 2
        );
    }

    /// <summary>
    /// Convert markdown release notes to plain text with proper line breaks
    /// </summary>
    private static string FormatReleaseNotes(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var text = markdown;

        // Remove markdown headers (## ### etc) but keep the text with line break
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);

        // Remove bold/italic markers
        text = text.Replace("**", "");
        text = text.Replace("__", "");
        text = text.Replace("*", "");
        text = text.Replace("_", "");

        // Remove inline code markers
        text = text.Replace("`", "");

        // Convert markdown links [text](url) to just text
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");

        // Normalize line endings
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Remove excessive blank lines (more than 2 consecutive)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        // Convert back to Windows line endings for TextBox
        text = text.Replace("\n", "\r\n");

        return text.Trim();
    }

    private void ApplyTheme()
    {
        // Always use Classic (Light) theme for update dialog
        BackColor = Color.FromArgb(250, 250, 250);
        ForeColor = Color.FromArgb(51, 51, 51);

        _lblTitle.ForeColor = Color.FromArgb(70, 130, 180);
        _txtReleaseNotes.BackColor = Color.FromArgb(255, 255, 255);
        _txtReleaseNotes.ForeColor = Color.FromArgb(51, 51, 51);

        ApplyButtonStyle(_btnUpdate, true);
        ApplyButtonStyle(_btnSkip, false);
        ApplyButtonStyle(_btnCancel, false);
    }

    private void ApplyButtonStyle(Button btn, bool isPrimary)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 1;

        if (isPrimary)
        {
            btn.BackColor = Color.FromArgb(70, 130, 180);
            btn.ForeColor = Color.White;
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 130, 180);
        }
        else
        {
            btn.BackColor = Color.FromArgb(240, 240, 240);
            btn.ForeColor = Color.FromArgb(51, 51, 51);
            btn.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
        }
    }

    private async void BtnUpdate_Click(object? sender, EventArgs e)
    {
        if (_isDownloading) return;

        _isDownloading = true;
        _downloadCts = new CancellationTokenSource();

        // Switch to download mode
        _btnUpdate.Visible = false;
        _btnSkip.Visible = false;
        _btnCancel.Visible = true;
        _progressBar.Visible = true;
        _lblProgress.Visible = true;
        _progressBar.Value = 0;
        _lblProgress.Text = "다운로드 준비 중...";
        ActiveControl = _btnCancel;  // Prevent textbox from getting focus

        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                _progressBar.Value = p.Percentage;
                _lblProgress.Text = $"다운로드 중... {p.DownloadedFormatted} / {p.TotalFormatted} ({p.Percentage}%)";
            });

            var downloadedPath = await _updateService.DownloadUpdateAsync(
                _updateInfo, progress, _downloadCts.Token);

            if (string.IsNullOrEmpty(downloadedPath))
            {
                MessageBox.Show(
                    "다운로드에 실패했습니다.\n네트워크 연결을 확인하고 다시 시도해주세요.",
                    "다운로드 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                ResetToInitialState();
                return;
            }

            _lblProgress.Text = "업데이트 적용 중...";
            _btnCancel.Enabled = false;

            // Apply update
            if (_updateService.ApplyUpdate(downloadedPath))
            {
                // Close the application - the batch script will restart it
                DialogResult = DialogResult.OK;
                Application.Exit();
            }
            else
            {
                MessageBox.Show(
                    "업데이트 적용에 실패했습니다.\n수동으로 업데이트해주세요.",
                    "업데이트 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                ResetToInitialState();
            }
        }
        catch (OperationCanceledException)
        {
            _lblProgress.Text = "다운로드 취소됨";
            ResetToInitialState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateDialog] Update failed: {ex.Message}");
            MessageBox.Show(
                $"업데이트 중 오류가 발생했습니다.\n{ex.Message}",
                "오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ResetToInitialState();
        }
        finally
        {
            _isDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private void ResetToInitialState()
    {
        _btnUpdate.Visible = true;
        _btnSkip.Visible = true;
        _btnCancel.Visible = false;
        _progressBar.Visible = false;
        _lblProgress.Visible = false;
        _btnCancel.Enabled = true;
        ActiveControl = _btnUpdate;  // Prevent textbox from getting focus
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isDownloading)
        {
            e.Cancel = true;
            _downloadCts?.Cancel();
        }
        base.OnFormClosing(e);
    }
}
