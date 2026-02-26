using System.Diagnostics;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Modal dialog that shows index rebuild progress.
/// Similar to StartupSplashForm but for manual index rebuilding.
/// </summary>
using RoMarketCrawler.Models;

public class IndexProgressDialog : Form
{
    private readonly Label _lblStatus;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblProgress;
    private readonly Button _btnCancel;
    private readonly Color _bgColor;
    private readonly Color _textColor;
    private readonly Color _accentColor;
    private readonly float _fontSize;
    private IWin32Window? _ownerWindow;

    private CancellationTokenSource? _cts;
    private bool _isComplete;
    private bool _isCancelled;
    private int _totalCount;

    public int TotalCount => _totalCount;
    public bool WasCancelled => _isCancelled;

    public IndexProgressDialog(ThemeType theme, float fontSize)
    {
        _fontSize = fontSize;

        // Set theme colors
        if (theme == ThemeType.Dark)
        {
            _bgColor = Color.FromArgb(30, 30, 30);
            _textColor = Color.FromArgb(220, 220, 220);
            _accentColor = Color.FromArgb(0, 150, 200);
        }
        else
        {
            _bgColor = Color.FromArgb(250, 250, 250);
            _textColor = Color.FromArgb(51, 51, 51);
            _accentColor = Color.FromArgb(0, 120, 180);
        }

        // Form setup - improved dimensions
        const int dialogWidth = 420;
        const int dialogHeight = 210;

        Text = "아이템 인덱스 수집";
        Size = new Size(dialogWidth, dialogHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual; // We'll set location manually
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = _bgColor;
        ShowIcon = false;
        ControlBox = false;

        // Layout constants
        const int horizontalPadding = 30;
        const int contentWidth = dialogWidth - (horizontalPadding * 2) - 16; // Account for form border

        // Status label - top message
        _lblStatus = new Label
        {
            Text = "아이템 정보를 수집하고 있습니다...",
            Font = new Font("Malgun Gothic", _fontSize, FontStyle.Bold),
            ForeColor = _textColor,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(horizontalPadding, 20),
            Size = new Size(contentWidth, 28)
        };

        // Progress bar - centered with good thickness
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Location = new Point(horizontalPadding, 58),
            Size = new Size(contentWidth, 26)
        };

        // Progress text - details below progress bar
        _lblProgress = new Label
        {
            Text = "준비 중...",
            Font = new Font("Malgun Gothic", _fontSize - 2),
            ForeColor = Color.FromArgb(120, 120, 120),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(horizontalPadding, 92),
            Size = new Size(contentWidth, 24)
        };

        // Cancel button - centered at bottom with padding
        _btnCancel = new Button
        {
            Text = "취소",
            Size = new Size(90, 32),
            Location = new Point((dialogWidth - 90) / 2 - 8, 130),
            FlatStyle = FlatStyle.Flat,
            BackColor = _accentColor,
            ForeColor = Color.White,
            Font = new Font("Malgun Gothic", _fontSize - 3, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnCancel.Click += BtnCancel_Click;

        Controls.AddRange(new Control[] { _lblStatus, _progressBar, _lblProgress, _btnCancel });

        // Center on owner when shown
        Load += (s, e) => CenterOnOwner();

        // Handle form closing
        FormClosing += (s, e) =>
        {
            if (!_isComplete && !_isCancelled)
            {
                // Prevent closing while in progress, cancel instead
                e.Cancel = true;
                BtnCancel_Click(this, EventArgs.Empty);
            }
        };
    }

    private void CenterOnOwner()
    {
        if (_ownerWindow is Form ownerForm)
        {
            // Center on owner form
            int x = ownerForm.Left + (ownerForm.Width - Width) / 2;
            int y = ownerForm.Top + (ownerForm.Height - Height) / 2;

            // Ensure it stays within screen bounds
            var screen = Screen.FromControl(ownerForm);
            x = Math.Max(screen.WorkingArea.Left, Math.Min(x, screen.WorkingArea.Right - Width));
            y = Math.Max(screen.WorkingArea.Top, Math.Min(y, screen.WorkingArea.Bottom - Height));

            Location = new Point(x, y);
        }
        else
        {
            // Fallback: center on primary screen
            var screen = Screen.PrimaryScreen;
            if (screen != null)
            {
                Location = new Point(
                    (screen.WorkingArea.Width - Width) / 2,
                    (screen.WorkingArea.Height - Height) / 2);
            }
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _isCancelled = true;
            _cts.Cancel();
            _btnCancel.Enabled = false;
            _lblProgress.Text = "취소 중...";
        }
    }

    /// <summary>
    /// Show the dialog and run the index rebuild operation.
    /// </summary>
    /// <returns>True if indexing completed successfully, false if cancelled or failed.</returns>
    public async Task<bool> ShowAndRunAsync(IWin32Window owner, ItemIndexService indexService)
    {
        _cts = new CancellationTokenSource();
        _isComplete = false;
        _isCancelled = false;
        _totalCount = 0;
        _ownerWindow = owner;

        // Show the dialog non-blocking
        Show(owner);

        bool success = false;
        try
        {
            var progress = new Progress<IndexProgress>(p =>
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    try
                    {
                        if (InvokeRequired)
                            BeginInvoke(() => UpdateProgressUI(p));
                        else
                            UpdateProgressUI(p);
                    }
                    catch { }
                }
            });

            success = await indexService.RebuildIndexAsync(progress, _cts.Token).ConfigureAwait(false);

            if (success)
            {
                _totalCount = indexService.TotalCount;
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[IndexProgressDialog] Operation cancelled");
            _isCancelled = true;
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IndexProgressDialog] Error: {ex.Message}");
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    MessageBox.Show(
                        $"인덱스 생성 중 오류가 발생했습니다.\n\n{ex.Message}",
                        "오류",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                });
            }
            return false;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;

            // Mark as complete to allow FormClosing
            _isComplete = success;
            if (!_isCancelled && !success)
            {
                _isComplete = true; // Allow closing even on failure
            }

            // Close on UI thread
            if (IsHandleCreated && !IsDisposed)
            {
                if (InvokeRequired)
                    BeginInvoke(() => { if (!IsDisposed) Close(); });
                else
                    Close();
            }
        }
    }

    private void UpdateProgressUI(IndexProgress p)
    {
        if (p.IsComplete)
        {
            _progressBar.Value = 100;
            _lblProgress.Text = $"완료: {p.ItemsCollected:N0}개 아이템";
        }
        else if (p.IsCancelled)
        {
            _lblProgress.Text = "취소됨";
        }
        else if (p.HasError)
        {
            _lblProgress.Text = p.Phase; // Phase contains error message when HasError is true
        }
        else
        {
            var progressText = !string.IsNullOrEmpty(p.CurrentCategory)
                ? $"{p.Phase} - {p.CurrentCategory} ({p.ItemsCollected:N0}개)"
                : p.Phase;
            _lblProgress.Text = progressText;

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
                // Estimate progress from collected items (estimated total ~22,000)
                const int estimatedTotal = 22000;
                percent = (int)Math.Min(95, (p.ItemsCollected * 100.0) / estimatedTotal);
            }
            else
            {
                percent = 0;
            }
            _progressBar.Value = percent;
        }
    }
}
