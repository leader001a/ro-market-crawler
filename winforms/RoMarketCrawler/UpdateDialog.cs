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
    private readonly ThemeType _theme;

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

    public UpdateDialog(UpdateService updateService, UpdateInfo updateInfo, ThemeType theme)
    {
        _updateService = updateService;
        _updateInfo = updateInfo;
        _theme = theme;

        InitializeComponents();
        ApplyTheme();
    }

    private void InitializeComponents()
    {
        Text = "업데이트 확인";
        Size = new Size(480, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

        var padding = 20;
        var currentY = padding;

        // Title
        _lblTitle = new Label
        {
            Text = "새로운 버전이 있습니다!",
            Font = new Font("Malgun Gothic", 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 35;

        // Version info
        _lblVersion = new Label
        {
            Text = $"현재 버전: v{_updateInfo.CurrentVersion}  →  최신 버전: {_updateInfo.TagName}",
            Font = new Font("Malgun Gothic", 10),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 25;

        // File size
        _lblSize = new Label
        {
            Text = $"다운로드 크기: {_updateInfo.FileSizeFormatted}",
            Font = new Font("Malgun Gothic", 9),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 30;

        // Release notes label
        var lblNotesTitle = new Label
        {
            Text = "변경 사항:",
            Font = new Font("Malgun Gothic", 9, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(padding, currentY)
        };
        currentY += 20;

        // Release notes
        _txtReleaseNotes = new TextBox
        {
            Text = _updateInfo.ReleaseNotes,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(padding, currentY),
            Size = new Size(ClientSize.Width - padding * 2, 120),
            Font = new Font("Malgun Gothic", 9)
        };
        currentY += 130;

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
        currentY += 35;

        // Buttons
        var buttonWidth = 100;
        var buttonHeight = 32;
        var buttonSpacing = 10;
        var totalButtonWidth = buttonWidth * 3 + buttonSpacing * 2;
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

        _btnCancel = new Button
        {
            Text = "취소",
            Size = new Size(buttonWidth, buttonHeight),
            Location = new Point(buttonStartX + (buttonWidth + buttonSpacing) * 2, currentY),
            Font = new Font("Malgun Gothic", 9),
            Visible = false
        };
        _btnCancel.Click += BtnCancel_Click;

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblVersion, _lblSize, lblNotesTitle,
            _txtReleaseNotes, _progressBar, _lblProgress,
            _btnUpdate, _btnSkip, _btnCancel
        });
    }

    private void ApplyTheme()
    {
        if (_theme == ThemeType.Dark)
        {
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(220, 220, 220);

            _lblTitle.ForeColor = Color.FromArgb(100, 180, 255);
            _txtReleaseNotes.BackColor = Color.FromArgb(45, 45, 45);
            _txtReleaseNotes.ForeColor = Color.FromArgb(200, 200, 200);

            ApplyDarkButtonStyle(_btnUpdate, true);
            ApplyDarkButtonStyle(_btnSkip, false);
            ApplyDarkButtonStyle(_btnCancel, false);
        }
        else
        {
            BackColor = Color.FromArgb(250, 250, 250);
            ForeColor = Color.FromArgb(51, 51, 51);

            _lblTitle.ForeColor = Color.FromArgb(70, 130, 180);
            _txtReleaseNotes.BackColor = Color.FromArgb(255, 255, 255);
            _txtReleaseNotes.ForeColor = Color.FromArgb(51, 51, 51);

            ApplyLightButtonStyle(_btnUpdate, true);
            ApplyLightButtonStyle(_btnSkip, false);
            ApplyLightButtonStyle(_btnCancel, false);
        }
    }

    private void ApplyDarkButtonStyle(Button btn, bool isPrimary)
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
            btn.BackColor = Color.FromArgb(60, 60, 60);
            btn.ForeColor = Color.FromArgb(200, 200, 200);
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        }
    }

    private void ApplyLightButtonStyle(Button btn, bool isPrimary)
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
