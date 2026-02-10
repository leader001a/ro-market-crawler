using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RoMarketCrawler.Controllers;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Main form acting as coordinator for tab controllers
/// </summary>
public partial class Form1 : Form
{
    #region Theme Colors

    private Color ThemeBackground;
    private Color ThemePanel;
    private Color ThemeGrid;
    private Color ThemeGridAlt;
    private Color ThemeAccent;
    private Color ThemeAccentHover;
    private Color ThemeAccentText;
    private Color ThemeText;
    private Color ThemeTextMuted;
    private Color ThemeLinkColor;
    private Color ThemeBorder;
    private Color ThemeSaleColor;
    private Color ThemeBuyColor;
    private ThemeType _currentTheme = ThemeType.Dark;

    #endregion

    #region Tab Controllers

    private DealTabController _dealTabController = null!;
    private ItemTabController _itemTabController = null!;
    private MonitorTabController _monitorTabController = null!;
    private CostumeTabController _costumeTabController = null!;

    #endregion

    #region UI Components

    private BorderlessTabControl _tabControl = null!;
    private MenuStrip _menuStrip = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblCreator = null!;

    #endregion

    #region Watermark

    private Image? _watermarkImage = null;
    private Image? _watermarkFaded = null;

    #endregion

    #region Settings

    private float _baseFontSize = 12f;
    private readonly string _settingsFilePath;
    private List<string> _dealSearchHistory = new();
    private bool _isSoundMuted = false;
    private AlarmSoundType _selectedAlarmSound = AlarmSoundType.SystemSound;
    private int _alarmIntervalSeconds = 5;

    #endregion

    #region Rate Limit

    private System.Windows.Forms.Timer _rateLimitTimer = null!;
    private bool _isRateLimitUIActive = false;

    #endregion

    #region WebView2

    private WebView2Helper? _webView2Helper;
    private GnjoyClient? _gnjoyClient;

    #endregion

    public Form1()
    {
        // Initialize dark mode support before creating any controls
        ThemeManager.InitializeDarkModeSupport();

        InitializeComponent();

        // Initialize paths
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        _settingsFilePath = Path.Combine(dataDir, "settings.json");

        // Load settings and apply theme
        LoadSettings();
        ApplyThemeColors();

        // Create tab controllers using DI container
        CreateTabControllers();

        InitializeCustomComponents();

        // Wire up controller events
        WireControllerEvents();

        // Load data asynchronously
        _ = LoadDataAsync();

        // Activate initial tab after form is shown
        Shown += (s, e) => _dealTabController?.OnActivated();
    }

    private void CreateTabControllers()
    {
        // Resolve controllers from DI container
        _dealTabController = Program.Services.GetRequiredService<DealTabController>();
        _itemTabController = Program.Services.GetRequiredService<ItemTabController>();
        _monitorTabController = Program.Services.GetRequiredService<MonitorTabController>();
        _costumeTabController = Program.Services.GetRequiredService<CostumeTabController>();

        // Initialize settings in controllers
        _dealTabController.LoadSearchHistory(_dealSearchHistory);
        _monitorTabController.LoadAlarmSettings(_isSoundMuted, _selectedAlarmSound, _alarmIntervalSeconds);
    }

    private void WireControllerEvents()
    {
        // DealTabController events
        _dealTabController.SearchHistoryChanged += (s, history) =>
        {
            _dealSearchHistory = history;
            SaveSettings();
        };

        _dealTabController.ShowItemDetail += (s, dealItem) =>
        {
            ShowItemDetailForm(dealItem);
        };

        // MonitorTabController events
        _monitorTabController.SettingsChanged += (s, e) =>
        {
            // Get settings from controller and save
            var (muted, sound, interval) = _monitorTabController.GetAlarmSettings();
            _isSoundMuted = muted;
            _selectedAlarmSound = sound;
            _alarmIntervalSeconds = interval;
            SaveSettings();
        };

        // CostumeTabController - lock other tabs' API features during crawling
        _costumeTabController.CrawlStateChanged += (s, isCrawling) =>
        {
            _dealTabController.SetCrawlLockState(isCrawling);
            _monitorTabController.SetCrawlLockState(isCrawling);
        };
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Load item index
            await _itemTabController.LoadItemIndexAsync();

            // Load monitoring config and reset auto-refresh state
            await _monitorTabController.LoadMonitoringAsync();

            // Initialize WebView2 for Cloudflare bypass
            await InitializeWebView2Async();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Data load error: {ex.Message}");
        }
    }

    /// <summary>
    /// Initialize WebView2 helper for Cloudflare bypass
    /// </summary>
    private async Task InitializeWebView2Async()
    {
        try
        {
            // Check if WebView2 runtime is available
            if (!WebView2Helper.IsWebView2Available())
            {
                Debug.WriteLine("[Form1] WebView2 runtime not available");
                return;
            }

            Debug.WriteLine("[Form1] Initializing WebView2...");
            _webView2Helper = new WebView2Helper();
            await _webView2Helper.InitializeAsync();

            // Inject WebView2Helper into GnjoyClient
            _gnjoyClient = Program.Services.GetRequiredService<IGnjoyClient>() as GnjoyClient;
            if (_gnjoyClient != null && _webView2Helper.IsReady)
            {
                _gnjoyClient.SetWebView2Helper(_webView2Helper);
                _gnjoyClient.SetUseWebView2(true);  // Enable WebView2 mode by default
                Debug.WriteLine("[Form1] WebView2 enabled for GnjoyClient");
            }

            // Inject WebView2Helper into MonitoringService (it has its own GnjoyClient)
            var monitoringService = Program.Services.GetRequiredService<IMonitoringService>();
            if (_webView2Helper.IsReady)
            {
                monitoringService.SetWebView2Helper(_webView2Helper);
                Debug.WriteLine("[Form1] WebView2 enabled for MonitoringService");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] WebView2 initialization failed: {ex.Message}");
            // Continue without WebView2 - will use HttpClient fallback
        }
    }

    private void InitializeCustomComponents()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Text = $"RO Market Crawler - v{version?.Major}.{version?.Minor}.{version?.Build}";
        Size = new Size(1400, 800);
        StartPosition = FormStartPosition.Manual;
        // Start maximized on the screen where the mouse cursor is
        var currentScreen = Screen.FromPoint(Cursor.Position);
        Location = currentScreen.WorkingArea.Location;
        WindowState = FormWindowState.Maximized;
        BackColor = ThemeBackground;
        ForeColor = ThemeText;

        // Set window icon
        LoadWindowIcon();

        // Setup menu strip
        SetupMenuStrip();

        // Create TabControl
        _tabControl = new BorderlessTabControl
        {
            Dock = DockStyle.Fill
        };
        ApplyTabControlStyle(_tabControl);

        // Initialize controllers and add their TabPages
        var colors = ThemeColors.ForTheme(_currentTheme);

        _dealTabController.Initialize();
        _dealTabController.ApplyTheme(colors);
        _dealTabController.UpdateFontSize(_baseFontSize);

        _itemTabController.Initialize();
        _itemTabController.ApplyTheme(colors);
        _itemTabController.UpdateFontSize(_baseFontSize);

        _monitorTabController.Initialize();
        _monitorTabController.ApplyTheme(colors);
        _monitorTabController.UpdateFontSize(_baseFontSize);

        _costumeTabController.Initialize();
        _costumeTabController.ApplyTheme(colors);
        _costumeTabController.UpdateFontSize(_baseFontSize);

        // Add controller TabPages to TabControl
        _tabControl.TabPages.Add(_dealTabController.TabPage);
        _tabControl.TabPages.Add(_itemTabController.TabPage);
        _tabControl.TabPages.Add(_monitorTabController.TabPage);
        _tabControl.TabPages.Add(_costumeTabController.TabPage);

        // Attach IME half-width fix to all TextBox controls (fixes WebView2 IME corruption)
        ImeHelper.AttachToAllTextBoxes(_dealTabController.TabPage);
        ImeHelper.AttachToAllTextBoxes(_itemTabController.TabPage);
        ImeHelper.AttachToAllTextBoxes(_monitorTabController.TabPage);
        ImeHelper.AttachToAllTextBoxes(_costumeTabController.TabPage);

        // Wire up tab switching events
        _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
        _tabControl.Deselecting += TabControl_Deselecting;
        _tabControl.Selecting += TabControl_Selecting;

        // Setup status bar
        SetupStatusBar();

        // Add controls in reverse dock order
        Controls.Add(_tabControl);
        Controls.Add(_statusStrip);
        Controls.Add(_menuStrip);

        // Apply loaded settings to form-level controls
        ApplyFontSizeToAllControls(this);
        ApplyThemeToAllControls(this);

        // Apply dark mode to title bar
        ThemeManager.ApplyDarkModeToTitleBar(Handle, _currentTheme == ThemeType.Dark);

        // Load watermark
        LoadWatermarkImage();

        // Set watermark on controller grids
        SetWatermarkOnControllerGrids();

        // Initialize rate limit timer
        InitializeRateLimitTimer();
    }

    private void LoadWindowIcon()
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
            Debug.WriteLine($"[Form1] Failed to load icon: {ex.Message}");
        }
    }

    private void SetupStatusBar()
    {
        var statusTextColor = _currentTheme == ThemeType.Dark
            ? Color.FromArgb(200, 200, 200)
            : Color.FromArgb(60, 60, 60);
        var statusFont = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold);

        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = ThemePanel,
            ForeColor = statusTextColor,
            SizingGrip = false,
            Font = statusFont
        };

        // Expiration date label
        var expirationText = !string.IsNullOrEmpty(Services.StartupValidator.ExpirationDateKST)
            ? $"사용가능일: ~{Services.StartupValidator.ExpirationDateKST[..10]}"
            : "";
        var lblExpiration = new ToolStripStatusLabel
        {
            Text = expirationText,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = statusTextColor,
            Font = statusFont,
            Margin = new Padding(5, 0, 10, 0)
        };

        _lblCreator = new ToolStripStatusLabel
        {
            Text = "Created by 티포니",
            Spring = true,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = statusTextColor,
            Font = statusFont
        };

        if (!string.IsNullOrEmpty(expirationText))
        {
            _statusStrip.Items.Add(lblExpiration);
        }
        _statusStrip.Items.Add(_lblCreator);
    }

    private void LoadWatermarkImage()
    {
        try
        {
            _watermarkImage = Services.ResourceHelper.GetLogoImage();
            if (_watermarkImage == null) return;

            var fadedBitmap = new Bitmap(_watermarkImage.Width, _watermarkImage.Height);
            using (var g = Graphics.FromImage(fadedBitmap))
            {
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix
                {
                    Matrix33 = 0.04f
                };
                var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
                imageAttributes.SetColorMatrix(colorMatrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);

                g.DrawImage(_watermarkImage,
                    new Rectangle(0, 0, _watermarkImage.Width, _watermarkImage.Height),
                    0, 0, _watermarkImage.Width, _watermarkImage.Height,
                    GraphicsUnit.Pixel,
                    imageAttributes);
            }
            _watermarkFaded = fadedBitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Watermark load error: {ex.Message}");
        }
    }

    private void SetWatermarkOnControllerGrids()
    {
        if (_watermarkFaded == null) return;

        _dealTabController.SetWatermark(_watermarkFaded);
        _itemTabController.SetWatermark(_watermarkFaded);
        _monitorTabController.SetWatermark(_watermarkFaded);
        _costumeTabController.SetWatermark(_watermarkFaded);
    }

    private void InitializeRateLimitTimer()
    {
        _rateLimitTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };
        _rateLimitTimer.Tick += RateLimitTimer_Tick;
        _rateLimitTimer.Start();
    }

    #region Tab Switching

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selectedIndex = _tabControl.SelectedIndex;

        // Stop auto-refresh when switched to Deal Search tab (index 0)
        // Confirmation was already done in TabControl_Selecting
        // Skip if costume crawling is active (auto-refresh is already paused by SetCrawlLockState)
        var isCrawling = _costumeTabController?.IsCrawling == true;
        if (selectedIndex == 0 && !isCrawling
            && _monitorTabController != null && _monitorTabController.IsAutoRefreshConfigured)
        {
            _monitorTabController.StopAutoRefreshForTabChange();
        }

        // Notify controllers of activation/deactivation
        _dealTabController?.OnDeactivated();
        _itemTabController?.OnDeactivated();
        _monitorTabController?.OnDeactivated();
        _costumeTabController?.OnDeactivated();

        switch (selectedIndex)
        {
            case 0:
                _dealTabController?.OnActivated();
                break;
            case 1:
                _itemTabController?.OnActivated();
                break;
            case 2:
                _monitorTabController?.OnActivated();
                break;
            case 3:
                _costumeTabController?.OnActivated();
                break;
        }
    }

    private void TabControl_Deselecting(object? sender, TabControlCancelEventArgs e)
    {
        // Not used
    }

    // Selecting event - confirm before switching tabs if auto-refresh is running
    private void TabControl_Selecting(object? sender, TabControlCancelEventArgs e)
    {
        // Skip confirmation dialogs during costume data collection
        // (auto-refresh is already paused by SetCrawlLockState, API features are locked)
        if (_costumeTabController?.IsCrawling == true)
            return;

        // Show confirmation when going to Deal tab (index 0) and auto-refresh is configured
        // DealTab uses the same GNJOY API, so auto-refresh must be permanently stopped
        if (e.TabPageIndex == 0 && _monitorTabController != null && _monitorTabController.IsAutoRefreshConfigured)
        {
            var result = MessageBox.Show(
                "노점조회 탭으로 이동하면 자동 갱신이 중지됩니다.\n이동하시겠습니까?",
                "노점 모니터링",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.No)
            {
                e.Cancel = true;
                return;
            }
        }
    }

    #endregion

    #region Rate Limit Handling

    private void RateLimitTimer_Tick(object? sender, EventArgs e)
    {
        // Skip rate limit UI when WebView2 mode is active (bypasses Cloudflare)
        if (_gnjoyClient?.IsWebView2Enabled == true)
        {
            if (_isRateLimitUIActive)
            {
                _isRateLimitUIActive = false;
                SetRateLimitUIState(false);
                GnjoyClient.ClearRateLimit();  // Clear any stale rate limit
                Debug.WriteLine("[Form1] Rate limit UI deactivated (WebView2 mode)");
            }
            return;
        }

        var isRateLimited = GnjoyClient.IsRateLimited;

        if (isRateLimited && !_isRateLimitUIActive)
        {
            _isRateLimitUIActive = true;
            SetRateLimitUIState(true);
            Debug.WriteLine("[Form1] Rate limit UI activated");
        }
        else if (isRateLimited && _isRateLimitUIActive)
        {
            UpdateRateLimitStatusDisplay();
        }
        else if (!isRateLimited && _isRateLimitUIActive)
        {
            _isRateLimitUIActive = false;
            SetRateLimitUIState(false);
            Debug.WriteLine("[Form1] Rate limit UI deactivated");
        }
    }

    private void SetRateLimitUIState(bool isRateLimited)
    {
        _dealTabController.SetRateLimitState(isRateLimited);
        _monitorTabController.SetRateLimitState(isRateLimited);

        if (isRateLimited)
        {
            UpdateRateLimitStatusDisplay();
        }
    }

    private void UpdateRateLimitStatusDisplay()
    {
        var remainingSeconds = GnjoyClient.RemainingRateLimitSeconds;
        var minutes = remainingSeconds / 60;
        var seconds = remainingSeconds % 60;

        string timeText = minutes > 0 ? $"{minutes}분 {seconds}초" : $"{seconds}초";
        var statusMessage = $"⚠ API 요청 제한 중 - {timeText} 후 재시도 가능";

        _dealTabController.UpdateRateLimitStatus(statusMessage);
        _monitorTabController.UpdateRateLimitStatus(statusMessage);
    }

    #endregion

    #region Item Detail Form

    private void ShowItemDetailForm(DealItem dealItem)
    {
        try
        {
            // Get ItemIndexService from DI container for price history lookup
            var itemIndexService = Program.Services.GetService(typeof(IItemIndexService)) as ItemIndexService;
            using var detailForm = new ItemDetailForm(dealItem, itemIndexService, _currentTheme, _baseFontSize);
            detailForm.ShowDialog(this); // Modal - prevents duplicate popups and blocks main form
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Error showing item detail: {ex.Message}");
        }
    }

    #endregion

    #region Menu Strip

    private void SetupMenuStrip()
    {
        _menuStrip = new MenuStrip
        {
            BackColor = ThemePanel,
            ForeColor = ThemeText,
            Renderer = _currentTheme == ThemeType.Dark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer()
        };

        // View menu
        var viewMenu = new ToolStripMenuItem("보기(&V)") { ForeColor = ThemeText };

        // Font size submenu
        var fontSizeMenu = new ToolStripMenuItem("글꼴 크기") { ForeColor = ThemeText };
        var fontSizes = new[] { 8f, 9f, 10f, 11f, 12f, 14f };
        foreach (var size in fontSizes)
        {
            var item = new ToolStripMenuItem($"{size}pt")
            {
                Tag = size,
                Checked = Math.Abs(_baseFontSize - size) < 0.1f
            };
            item.Click += FontSizeMenuItem_Click;
            fontSizeMenu.DropDownItems.Add(item);
        }
        viewMenu.DropDownItems.Add(fontSizeMenu);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        var zoomIn = new ToolStripMenuItem("글꼴 크게 (Ctrl++)")
        {
            ShortcutKeys = Keys.Control | Keys.Oemplus
        };
        zoomIn.Click += (s, e) => ChangeFontSize(1f);
        viewMenu.DropDownItems.Add(zoomIn);

        var zoomOut = new ToolStripMenuItem("글꼴 작게 (Ctrl+-)")
        {
            ShortcutKeys = Keys.Control | Keys.OemMinus
        };
        zoomOut.Click += (s, e) => ChangeFontSize(-1f);
        viewMenu.DropDownItems.Add(zoomOut);

        var zoomReset = new ToolStripMenuItem("글꼴 기본 (Ctrl+0)")
        {
            ShortcutKeys = Keys.Control | Keys.D0
        };
        zoomReset.Click += (s, e) => SetFontSize(12f);
        viewMenu.DropDownItems.Add(zoomReset);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        // Theme submenu
        var themeMenu = new ToolStripMenuItem("테마") { ForeColor = ThemeText };

        var darkThemeItem = new ToolStripMenuItem("다크 테마")
        {
            Tag = ThemeType.Dark,
            Checked = _currentTheme == ThemeType.Dark
        };
        darkThemeItem.Click += ThemeMenuItem_Click;
        themeMenu.DropDownItems.Add(darkThemeItem);

        var classicThemeItem = new ToolStripMenuItem("클래식 테마")
        {
            Tag = ThemeType.Classic,
            Checked = _currentTheme == ThemeType.Classic
        };
        classicThemeItem.Click += ThemeMenuItem_Click;
        themeMenu.DropDownItems.Add(classicThemeItem);

        viewMenu.DropDownItems.Add(themeMenu);
        _menuStrip.Items.Add(viewMenu);

        // Tools menu
        var toolsMenu = new ToolStripMenuItem("도구(&T)") { ForeColor = ThemeText };

        var indexRebuildItem = new ToolStripMenuItem("아이템정보 수집")
        {
            ForeColor = ThemeText,
            ShortcutKeys = Keys.Control | Keys.R
        };
        indexRebuildItem.Click += (s, e) => _itemTabController.RebuildIndex();
        toolsMenu.DropDownItems.Add(indexRebuildItem);

        _menuStrip.Items.Add(toolsMenu);

        // Help menu
        var helpMenu = new ToolStripMenuItem("도움말(&H)") { ForeColor = ThemeText };

        var userGuideItem = new ToolStripMenuItem("사용 가이드")
        {
            ForeColor = ThemeText,
            ShortcutKeys = Keys.F1
        };
        userGuideItem.Click += (s, e) => ShowHelpGuide();
        helpMenu.DropDownItems.Add(userGuideItem);

        helpMenu.DropDownItems.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("정보") { ForeColor = ThemeText };
        aboutItem.Click += (s, e) => ShowAboutDialog();
        helpMenu.DropDownItems.Add(aboutItem);

        _menuStrip.Items.Add(helpMenu);

        // Close all popups
        var closeAllPopups = new ToolStripMenuItem("전체 팝업 닫기")
        {
            ForeColor = ThemeText,
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.W,
            Alignment = ToolStripItemAlignment.Right,
            Margin = new Padding(0, 0, 15, 0)
        };
        closeAllPopups.Click += (s, e) => CloseAllItemInfoForms();
        _menuStrip.Items.Add(closeAllPopups);
    }

    private void CloseAllItemInfoForms()
    {
        _itemTabController.CloseAllItemInfoForms();
    }

    private void ShowHelpGuide(HelpGuideForm.HelpSection section = HelpGuideForm.HelpSection.Overview)
    {
        using var helpForm = new HelpGuideForm(_currentTheme, _baseFontSize, section);
        helpForm.ShowDialog(this);
    }

    private void ShowAboutDialog()
    {
        const int formWidth = 520;
        const int leftMargin = 25;
        const int contentWidth = 460;

        // Dynamic line height based on font size
        int lineHeight = (int)(_baseFontSize * 1.8);
        int sectionGap = (int)(_baseFontSize * 0.8);

        Image? logoImage = null;
        int logoHeight = 0;
        try
        {
            logoImage = Services.ResourceHelper.GetLogoImage();
            if (logoImage != null)
            {
                var scale = (float)contentWidth / logoImage.Width;
                logoHeight = (int)(logoImage.Height * scale);
            }
        }
        catch { }

        var clrBackground = Color.FromArgb(250, 250, 250);
        var clrText = Color.FromArgb(51, 51, 51);
        var clrTextMuted = Color.FromArgb(102, 102, 102);
        var clrLink = Color.FromArgb(70, 130, 180);
        var clrLegalText = Color.FromArgb(60, 60, 60);
        var clrLegalBg = Color.FromArgb(245, 245, 245);

        // Track current Y position
        int currentY = 0;

        PictureBox? picLogo = null;
        if (logoImage != null)
        {
            picLogo = new PictureBox
            {
                Image = logoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(contentWidth, logoHeight),
                Location = new Point(leftMargin, currentY)
            };
            currentY += logoHeight;
        }

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.0.0";
        var lblTitle = new Label
        {
            Text = $"RO Market Crawler {versionStr}",
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold),
            ForeColor = clrLink,
            AutoSize = false,
            Size = new Size(contentWidth, lineHeight),
            Location = new Point(leftMargin, currentY)
        };
        currentY += lineHeight;

        var lblDesc = new Label
        {
            Text = "라그나로크 온라인 거래 정보 검색 및 모니터링 프로그램",
            Font = new Font("Malgun Gothic", _baseFontSize),
            ForeColor = clrTextMuted,
            AutoSize = false,
            Size = new Size(contentWidth, lineHeight),
            Location = new Point(leftMargin, currentY)
        };
        currentY += lineHeight + sectionGap;

        var lblSource = new Label
        {
            Text = "[데이터 출처]\n" +
                   "  - 아이템 정보: kafra.kr\n" +
                   "  - 노점 거래: ro.gnjoy.com",
            Font = new Font("Malgun Gothic", _baseFontSize),
            ForeColor = clrText,
            AutoSize = false,
            Size = new Size(contentWidth, lineHeight * 3),
            Location = new Point(leftMargin, currentY)
        };
        currentY += lineHeight * 3 + sectionGap;

        var lblCreator = new Label
        {
            Text = "Created by: 티포니",
            Font = new Font("Malgun Gothic", _baseFontSize),
            ForeColor = clrText,
            AutoSize = false,
            Size = new Size(contentWidth, lineHeight),
            Location = new Point(leftMargin, currentY)
        };
        currentY += lineHeight;

        var lblContact = new Label
        {
            Text = "문의:",
            Font = new Font("Malgun Gothic", _baseFontSize),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, currentY)
        };

        var linkKakao = new LinkLabel
        {
            Text = "카카오톡 오픈프로필",
            Font = new Font("Malgun Gothic", _baseFontSize),
            LinkColor = clrLink,
            ActiveLinkColor = clrLink,
            AutoSize = true,
            Location = new Point(leftMargin + (int)(_baseFontSize * 3.5), currentY)
        };
        linkKakao.LinkClicked += (s, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://open.kakao.com/o/sOfQ176h",
                    UseShellExecute = true
                });
            }
            catch { }
        };
        currentY += lineHeight + sectionGap;

        var lblPrivacy = new Label
        {
            Text = "** 본 프로그램은 개인정보 및 게임정보를 일체 수집하지 않습니다 **",
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 60),
            AutoSize = false,
            Size = new Size(contentWidth, lineHeight * 2),
            Location = new Point(leftMargin, currentY)
        };
        currentY += lineHeight * 2 + sectionGap;

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
            "실시간성, 정확성, 완전성을 보장하지 않습니다.\r\n\r\n" +
            "4. 거래 결정에 대한 책임\r\n" +
            "본 프로그램의 정보를 참고하여 이루어진 게임 내 거래 결정 및 그로 인한 손실에 대해 " +
            "저작권자는 어떠한 책임도 지지 않습니다.\r\n\r\n" +
            "5. 게임 이용약관 준수\r\n" +
            "본 프로그램의 사용으로 인해 발생할 수 있는 게임 이용약관 위반 및 그에 따른 " +
            "계정 제재 등의 불이익에 대해 저작권자는 책임을 지지 않습니다.\r\n\r\n" +
            "6. 면책 조항\r\n" +
            "본 프로그램은 어떠한 보증 없이 '있는 그대로(AS-IS)' 제공됩니다.\r\n\r\n" +
            "7. 개인정보 및 게임정보 보호\r\n" +
            "본 프로그램은 사용자의 개인정보, 게임 계정 정보, 게임 내 활동 정보 등 " +
            "어떠한 정보도 수집하거나 외부로 전송하지 않습니다.\r\n\r\n" +
            "8. 위반 시 책임\r\n" +
            "상기 사항을 위반하여 발생하는 모든 법적 책임은 위반자 본인에게 있습니다.";

        int legalBoxHeight = 240;
        var txtLegalNotice = new TextBox
        {
            Text = legalNoticeText,
            Font = new Font("Malgun Gothic", _baseFontSize),
            ForeColor = clrLegalText,
            BackColor = clrLegalBg,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(leftMargin, currentY),
            Size = new Size(contentWidth, legalBoxHeight)
        };
        currentY += legalBoxHeight + 10;

        // Calculate final form height (add title bar height ~40px)
        int formHeight = currentY + 45;

        using var aboutForm = new Form
        {
            Text = "프로그램 정보",
            Size = new Size(formWidth, formHeight),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = clrBackground,
            ShowIcon = true
        };

        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream("RoMarketCrawler.app.ico");
            if (iconStream != null)
            {
                aboutForm.Icon = new Icon(iconStream);
            }
        }
        catch { }

        if (picLogo != null)
        {
            aboutForm.Controls.Add(picLogo);
        }
        aboutForm.Controls.AddRange(new Control[] { lblTitle, lblDesc, lblSource, lblCreator, lblContact, linkKakao, lblPrivacy, txtLegalNotice });
        aboutForm.ShowDialog(this);
    }

    #endregion

    #region Theme and Font Size

    private void ThemeMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item && item.Tag is ThemeType theme)
        {
            SetTheme(theme);
        }
    }

    private void FontSizeMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item && item.Tag is float size)
        {
            SetFontSize(size);
        }
    }

    private void ChangeFontSize(float delta)
    {
        var newSize = _baseFontSize + delta;
        if (newSize >= 8f && newSize <= 14f)
        {
            SetFontSize(newSize);
        }
    }

    private void SetFontSize(float size)
    {
        _baseFontSize = size;
        ApplyFontSizeToAllControls(this);

        // Update controllers
        _dealTabController.UpdateFontSize(size);
        _itemTabController.UpdateFontSize(size);
        _monitorTabController.UpdateFontSize(size);
        _costumeTabController.UpdateFontSize(size);

        UpdateFontSizeMenuChecks();
        SaveSettings();
    }

    private void UpdateFontSizeMenuChecks()
    {
        if (_menuStrip?.Items[0] is ToolStripMenuItem viewMenu)
        {
            if (viewMenu.DropDownItems[0] is ToolStripMenuItem fontSizeMenu)
            {
                foreach (var item in fontSizeMenu.DropDownItems.OfType<ToolStripMenuItem>())
                {
                    if (item.Tag is float size)
                    {
                        item.Checked = Math.Abs(_baseFontSize - size) < 0.1f;
                    }
                }
            }
        }
    }

    #endregion

    #region Settings

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    _baseFontSize = settings.FontSize;
                    _currentTheme = settings.Theme;
                    _isSoundMuted = settings.IsSoundMuted;
                    _selectedAlarmSound = settings.AlarmSound;
                    _alarmIntervalSeconds = settings.AlarmIntervalSeconds;
                    _dealSearchHistory = settings.DealSearchHistory ?? new List<string>();
                    Debug.WriteLine($"[Form1] LoadSettings: Loaded {_dealSearchHistory.Count} search history items");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Failed to load settings: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                FontSize = _baseFontSize,
                Theme = _currentTheme,
                IsSoundMuted = _isSoundMuted,
                AlarmSound = _selectedAlarmSound,
                AlarmIntervalSeconds = _alarmIntervalSeconds,
                DealSearchHistory = _dealSearchHistory
            };
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            File.WriteAllText(_settingsFilePath, json);
            Debug.WriteLine($"[Form1] SaveSettings: Saved {_dealSearchHistory.Count} search history items");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Failed to save settings: {ex.Message}");
        }
    }

    #endregion

    #region Form Lifecycle

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle numpad +/- for font size adjustment
        if (keyData == (Keys.Control | Keys.Add))
        {
            ChangeFontSize(1f);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Subtract))
        {
            ChangeFontSize(-1f);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _rateLimitTimer?.Stop();
        _rateLimitTimer?.Dispose();

        _dealTabController?.Dispose();
        _itemTabController?.Dispose();
        _monitorTabController?.Dispose();
        _costumeTabController?.Dispose();

        _watermarkImage?.Dispose();
        _watermarkFaded?.Dispose();
        _webView2Helper?.Dispose();

        base.OnFormClosed(e);
    }

    #endregion
}
