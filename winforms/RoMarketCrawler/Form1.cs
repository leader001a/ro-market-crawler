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
    private ToolStripStatusLabel _lblTabNotification = null!;
    private System.Windows.Forms.Timer _notificationTimer = null!;
    private int _currentTabIndex = 0;
    private int _lastApiTabIndex = 0; // Last active GNJOY API tab (excludes Item tab)

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
    private DateTime? _apiLockoutUntil;
    private bool _hideUsageNotice = false;

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
        Shown += (s, e) =>
        {
            _dealTabController?.OnActivated();
            ShowUsageNoticeIfNeeded();
        };
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

        // Tab switch notification label
        _lblTabNotification = new ToolStripStatusLabel
        {
            Text = "",
            ForeColor = Color.FromArgb(255, 180, 80),
            Font = statusFont,
            Visible = false
        };

        // Auto-clear notification after 5 seconds
        _notificationTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _notificationTimer.Tick += (s2, e2) =>
        {
            _notificationTimer.Stop();
            _lblTabNotification.Visible = false;
            _lblTabNotification.Text = "";
        };

        if (!string.IsNullOrEmpty(expirationText))
        {
            _statusStrip.Items.Add(lblExpiration);
        }
        _statusStrip.Items.Add(_lblTabNotification);
        _statusStrip.Items.Add(_lblCreator);
    }

    private void ShowTabSwitchNotification(string message)
    {
        _lblTabNotification.Text = $"  {message}";
        _lblTabNotification.Visible = true;
        _notificationTimer.Stop();
        _notificationTimer.Start();
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
            Interval = 30000  // Check every 30 seconds (no countdown needed)
        };
        _rateLimitTimer.Tick += RateLimitTimer_Tick;
        _rateLimitTimer.Start();

        // Immediate check on startup (don't wait 30s for persisted lockout)
        RateLimitTimer_Tick(null, EventArgs.Empty);
    }

    #region Tab Switching

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selectedIndex = _tabControl.SelectedIndex;

        if (selectedIndex == 1)
        {
            // Moving to Item tab — don't deactivate anything, don't update _lastApiTabIndex
            _itemTabController?.OnActivated();
        }
        else
        {
            // Moving to a GNJOY API tab — deactivate the last active API tab
            if (selectedIndex != _lastApiTabIndex)
            {
                var notifications = new List<string>();
                AddNotification(notifications, _dealTabController?.OnDeactivated());
                AddNotification(notifications, _monitorTabController?.OnDeactivated());
                AddNotification(notifications, _costumeTabController?.OnDeactivated());

                if (notifications.Count > 0)
                    ShowTabSwitchNotification(string.Join(" / ", notifications));
            }

            // Activate the selected tab
            switch (selectedIndex)
            {
                case 0: _dealTabController?.OnActivated(); break;
                case 2: _monitorTabController?.OnActivated(); break;
                case 3: _costumeTabController?.OnActivated(); break;
            }

            _lastApiTabIndex = selectedIndex;
        }

        _currentTabIndex = selectedIndex;
    }

    private static void AddNotification(List<string> list, string? msg)
    {
        if (msg != null) list.Add(msg);
    }

    private void TabControl_Deselecting(object? sender, TabControlCancelEventArgs e)
    {
        // Not used — confirmation is handled in Selecting
    }

    private void TabControl_Selecting(object? sender, TabControlCancelEventArgs e)
    {
        // No confirmation needed when moving to Item tab
        if (e.TabPageIndex == 1) return;

        // No confirmation when returning to the same API tab (e.g. via Item tab pass-through)
        if (e.TabPageIndex == _lastApiTabIndex) return;

        // Check if last active API tab has active operations
        var lastApiController = GetTabController(_lastApiTabIndex);
        if (lastApiController?.HasActiveOperations == true)
        {
            var message = _lastApiTabIndex switch
            {
                0 => "노점조회 검색이 진행 중입니다.\n탭을 전환하면 검색이 중지됩니다.",
                2 => "모니터링 자동 갱신이 실행 중입니다.\n탭을 전환하면 자동 갱신이 중지됩니다.",
                3 => "의상 데이터 수집이 진행 중입니다.\n탭을 전환하면 수집이 중지됩니다.\n(다음에 이어서 수집할 수 있습니다)",
                _ => "작업이 진행 중입니다.\n탭을 전환하면 작업이 중지됩니다."
            };

            var result = MessageBox.Show(
                message + "\n(아이템 탭으로는 중지 없이 이동 가능합니다)\n\n탭을 전환하시겠습니까?",
                "작업 중지 확인",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.No)
            {
                e.Cancel = true;
                return;
            }
        }
    }

    private ITabController? GetTabController(int tabIndex)
    {
        return tabIndex switch
        {
            0 => _dealTabController,
            1 => _itemTabController,
            2 => _monitorTabController,
            3 => _costumeTabController,
            _ => null
        };
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
                GnjoyClient.ClearRateLimit();
                ClearPersistedLockout();
                Debug.WriteLine("[Form1] Rate limit UI deactivated (WebView2 mode)");
            }
            return;
        }

        var isRateLimited = GnjoyClient.IsRateLimited;

        if (isRateLimited && !_isRateLimitUIActive)
        {
            _isRateLimitUIActive = true;
            SetRateLimitUIState(true);
            PersistLockout();
            Debug.WriteLine("[Form1] Rate limit UI activated (24h lockout)");
        }
        else if (!isRateLimited && _isRateLimitUIActive)
        {
            _isRateLimitUIActive = false;
            SetRateLimitUIState(false);
            ClearPersistedLockout();
            Debug.WriteLine("[Form1] Rate limit UI deactivated (lockout expired)");
        }
    }

    private void SetRateLimitUIState(bool isRateLimited)
    {
        switch (_tabControl.SelectedIndex)
        {
            case 0:
                _dealTabController.SetRateLimitState(isRateLimited);
                break;
            case 2:
                _monitorTabController.SetRateLimitState(isRateLimited);
                break;
        }

        if (isRateLimited)
        {
            UpdateRateLimitStatusDisplay();
        }
    }

    private void UpdateRateLimitStatusDisplay()
    {
        var rateLimitedUntil = GnjoyClient.RateLimitedUntil;
        if (!rateLimitedUntil.HasValue) return;

        var statusMessage = $"API 요청이 제한되었습니다. {rateLimitedUntil.Value:yyyy-MM-dd HH:mm} 이후 이용 가능합니다.";

        switch (_tabControl.SelectedIndex)
        {
            case 0:
                _dealTabController.UpdateRateLimitStatus(statusMessage);
                break;
            case 2:
                _monitorTabController.UpdateRateLimitStatus(statusMessage);
                break;
        }
    }

    private void PersistLockout()
    {
        _apiLockoutUntil = GnjoyClient.RateLimitedUntil;
        SaveSettings();
        Debug.WriteLine($"[Form1] Lockout persisted until {_apiLockoutUntil:yyyy-MM-dd HH:mm}");
    }

    private void ClearPersistedLockout()
    {
        _apiLockoutUntil = null;
        SaveSettings();
        Debug.WriteLine("[Form1] Persisted lockout cleared");
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
                    _hideUsageNotice = settings.HideUsageNotice;
                    _apiLockoutUntil = settings.ApiLockoutUntil;
                    Debug.WriteLine($"[Form1] LoadSettings: Loaded {_dealSearchHistory.Count} search history items");

                    // Restore rate limit lockout from persisted state
                    if (_apiLockoutUntil.HasValue)
                    {
                        if (_apiLockoutUntil.Value > DateTime.Now)
                        {
                            GnjoyClient.SharedRateLimitManager.SetRateLimitUntil(_apiLockoutUntil.Value);
                            Debug.WriteLine($"[Form1] Restored lockout until {_apiLockoutUntil.Value:yyyy-MM-dd HH:mm}");
                        }
                        else
                        {
                            _apiLockoutUntil = null;
                            Debug.WriteLine("[Form1] Stored lockout expired, cleared");
                        }
                    }
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
                DealSearchHistory = _dealSearchHistory,
                HideUsageNotice = _hideUsageNotice,
                ApiLockoutUntil = _apiLockoutUntil
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

    #region Usage Notice

    private void ShowUsageNoticeIfNeeded()
    {
        if (_hideUsageNotice) return;

        Color bgColor, textColor, accentColor, panelColor;

        if (_currentTheme == ThemeType.Dark)
        {
            bgColor = Color.FromArgb(30, 30, 30);
            textColor = Color.FromArgb(220, 220, 220);
            accentColor = Color.FromArgb(0, 150, 200);
            panelColor = Color.FromArgb(45, 45, 45);
        }
        else
        {
            bgColor = Color.FromArgb(250, 250, 250);
            textColor = Color.FromArgb(30, 30, 30);
            accentColor = Color.FromArgb(0, 120, 180);
            panelColor = Color.FromArgb(240, 240, 240);
        }

        using var form = new Form
        {
            Text = "RO 마켓 크롤러 - 사용 안내",
            Size = new Size(650, 700),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = bgColor,
            ForeColor = textColor,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ShowIcon = true,
            KeyPreview = true
        };

        // Load icon
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("RoMarketCrawler.app.ico");
            if (stream != null)
            {
                form.Icon = new Icon(stream);
            }
        }
        catch { }

        form.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };

        const string noticeContent =
@"[1. 이 프로그램은 어떻게 동작하나요?]

이 프로그램은 여러분의 컴퓨터에서 직접 gnjoy 사이트(ro.gnjoy.com)에 검색 요청을 보내서 아이템 정보를 가져옵니다. 쉽게 말해, 여러분이 웹 브라우저에서 gnjoy 사이트를 검색하는 것과 같은 방식입니다.

[왜 이게 중요한가요?]

gnjoy 서버 입장에서는 이 프로그램이 보내는 요청이 여러분의 인터넷 주소(IP)에서 오는 것으로 인식됩니다. 따라서 짧은 시간에 너무 많은 검색을 하면, gnjoy 측에서 여러분의 인터넷 주소를 일시적으로 차단할 수 있습니다.

차단되면 이 프로그램뿐만 아니라, 같은 인터넷을 사용하는 웹 브라우저에서도 gnjoy 아이템 검색이 안 될 수 있습니다.

※ gnjoy 아이템 검색만 차단되며, 게임 접속이나 다른 서비스에는 영향이 없습니다.


[2. 차단을 방지하기 위해 프로그램이 하는 일]

[한 번에 하나의 기능만 동작]

노점조회, 모니터링, 의상검색 기능은 동시에 실행되지 않습니다. 다른 탭으로 이동하면 기존 작업이 자동으로 중지됩니다. 이는 동시에 여러 요청이 몰리는 것을 방지하기 위한 설계입니다.

※ 아이템 탭은 별도의 사이트(kafra.kr)를 사용하므로, 아이템 탭으로 이동할 때는 다른 작업이 중지되지 않습니다.

[검색 속도 조절]

프로그램은 gnjoy 사이트에 요청을 보낼 때 일정한 간격을 두고 보냅니다. 한꺼번에 빠르게 보내지 않고 천천히 보내서 차단 위험을 줄입니다.

[노점조회 예시]
- ""사과"" 검색 → 목록조회 1회로 검색 결과 목록을 가져옴
- 목록에 10개 결과가 있다면 → 각 아이템의 상세조회를 1초 간격으로 10회 요청
- 즉, 검색 1회당 총 11회 요청 (목록 1회 + 상세 10회), 약 10초 소요
- 결과가 30개면 총 31회 요청, 약 30초 소요

[의상 데이터 수집 예시]
- 전체 의상 목록조회를 한 페이지씩 가져옴 (페이지당 10개)
- 각 페이지 안의 아이템마다 상세조회를 1초 간격으로 요청
- 즉, 1페이지당 총 11회 요청 (목록 1회 + 상세 10회), 약 10초 소요
- 1000개의 의상이 있다면 약 100페이지, 총 약 1,100회 요청, 약 25분 소요

※ gnjoy 측에서 요청 제한 기준을 강화할 경우, 프로그램도 요청 간격을 늘려야 하므로 수집 시간이 더 길어질 수 있습니다. 또한 이러한 변경은 사전 공지 없이 이루어질 수 있어, 현재 정상 동작하더라도 갑자기 차단이 발생할 수 있습니다.

[차단 감지 시 24시간 자동 잠금]

gnjoy 사이트에서 요청을 거부하면, 프로그램은 24시간 동안 모든 gnjoy 관련 기능을 자동으로 잠급니다. 이는 추가 요청으로 차단이 더 길어지는 것을 방지하기 위한 조치입니다.

- 잠금 상태는 프로그램을 껐다 켜도 유지됩니다
- 잠금이 해제되는 시간이 화면에 표시됩니다
- 24시간이 지나면 자동으로 다시 사용할 수 있습니다

[왜 24시간이나 잠그나요?]

개발 과정에서 차단이 발생한 뒤 대기 시간이 지났다고 판단하여 다시 요청을 보냈더니, 그 요청 자체가 추가 시도로 인식되어 차단이 계속 연장된 경험이 있었습니다. 결국 24시간 동안 gnjoy 아이템 검색을 사용하지 못했습니다.

이런 상황을 방지하기 위해, 차단이 감지되면 충분한 시간 동안 요청을 완전히 멈추도록 설계했습니다. 조금 불편하더라도, 충분히 기다린 뒤 사용하는 것이 더 안전합니다.

[의상 데이터 ""이어하기"" 기능]

의상 데이터 수집은 시간이 오래 걸리기 때문에, 중간에 중단된 경우 이어서 수집할 수 있습니다. 단, 마지막 수집 이후 1시간 이상 지났다면 그 사이 노점 정보가 바뀌었을 수 있으므로 처음부터 다시 수집하는 것을 권장합니다.

※ 의상검색은 빠른 정보 갱신보다 안정적인 동작을 우선으로 설계되었습니다. 요청 속도를 높이면 차단 위험이 커지기 때문에, 시간이 걸리더라도 안전하게 수집합니다.

[왜 웹 사이트가 아닌 프로그램인가요?]

의상 노점 검색을 웹 사이트로 제공하려면, 서버 한 대가 gnjoy에 주기적으로 요청을 보내서 데이터를 수집해야 합니다. 그런데 위에서 설명한 것처럼 의상 1,000개 기준 한 번 수집에만 약 25분이 걸립니다.

만약 30분~1시간마다 최신 데이터로 갱신하려고 하면, 이전 수집이 끝나기도 전에 다음 수집을 시작해야 하는 상황이 됩니다. 요청 간격을 줄이면 차단당하고, 간격을 유지하면 갱신이 안 되는 딜레마에 빠지게 됩니다.

실제로 과거에 의상 노점 검색을 제공했던 웹 사이트들이 서비스를 중단한 것도 이런 요청 제한 문제가 원인이었을 가능성이 있습니다.

이 프로그램은 여러분의 컴퓨터에서 직접 요청을 보내는 방식이기 때문에, 서버 한 곳에 요청이 집중되는 문제를 피할 수 있습니다. 다만 그만큼 수집에 시간이 걸리고, 각자 차단 위험을 관리해야 하는 점은 양해 부탁드립니다.


[3. 사용 시 참고사항]

- 프로그램을 여러 개 동시에 실행하지 마세요. 요청이 두 배로 늘어나 차단 위험이 높아집니다.
- 모니터링 자동갱신 주기는 너무 짧게 설정하지 마세요. 5분 이상을 권장합니다.
- 의상 데이터 수집 중에는 다른 gnjoy 관련 기능 사용을 자제해 주세요. 수집이 끝난 뒤 사용하면 더 안전합니다.
- 차단이 발생하면 조급해하지 마세요. 24시간 후 자동으로 풀립니다. 차단 중에 반복 시도하면 차단이 더 길어질 수 있습니다.


[4. 배포 관련 안내]

본 프로그램은 곰곰2Q 님의 팬카페를 통해 공유되고 있으며, 게시 승인을 받았습니다. 단, 곰곰2Q 님은 본 프로그램의 개발 및 운영과 어떠한 연관도 없으며, 본 프로그램의 사용으로 인해 발생하는 문제에 대해 법적 책임을 포함한 어떠한 책임도 지지 않습니다.

프로그램 관련 문의는 개발자에게 직접 연락해 주세요.";

        // Main layout (same structure as HelpGuideForm)
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10),
            BackColor = bgColor
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = bgColor,
            ForeColor = textColor,
            Font = new Font("Malgun Gothic", _baseFontSize),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };

        // Format RichTextBox content (same pattern as HelpGuideForm.ShowQuickHelpDialog)
        var lines = noticeContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        using var normalFont = new Font("Malgun Gothic", _baseFontSize);
        using var boldFont = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold);
        using var headerFont = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!string.IsNullOrEmpty(line))
            {
                var trimmed = line.TrimStart();
                var leadingSpaces = line.Substring(0, line.Length - trimmed.Length);

                if (leadingSpaces.Length > 0)
                {
                    rtb.SelectionColor = textColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText(leadingSpaces);
                }

                // Section headers [...]
                if (trimmed.StartsWith('[') && trimmed.Contains(']'))
                {
                    int bracketEnd = trimmed.IndexOf(']');
                    var headerPart = trimmed.Substring(0, bracketEnd + 1);
                    var restPart = trimmed.Substring(bracketEnd + 1);

                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = headerFont;
                    rtb.AppendText(headerPart);

                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = textColor;
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Numbered items (1., 2., etc.)
                else if (trimmed.Length > 1 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
                {
                    var numberPart = trimmed.Substring(0, 2);
                    var restPart = trimmed.Substring(2);

                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = boldFont;
                    rtb.AppendText(numberPart);

                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = textColor;
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Note lines starting with ※
                else if (trimmed.StartsWith('※'))
                {
                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText("※");

                    var restPart = trimmed.Substring(1);
                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = _currentTheme == ThemeType.Dark
                            ? Color.FromArgb(180, 180, 180)
                            : Color.FromArgb(80, 80, 80);
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Bullet points with dash
                else if (trimmed.StartsWith('-'))
                {
                    rtb.SelectionColor = accentColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText("-");

                    var restPart = trimmed.Substring(1);
                    if (restPart.Length > 0)
                    {
                        rtb.SelectionColor = textColor;
                        rtb.SelectionFont = normalFont;
                        rtb.AppendText(restPart);
                    }
                }
                // Normal line
                else
                {
                    rtb.SelectionColor = textColor;
                    rtb.SelectionFont = normalFont;
                    rtb.AppendText(trimmed);
                }
            }

            if (i < lines.Length - 1)
            {
                rtb.SelectionColor = textColor;
                rtb.SelectionFont = normalFont;
                rtb.AppendText(Environment.NewLine);
            }
        }

        rtb.SelectionStart = 0;
        rtb.ScrollToCaret();

        // Bottom panel with checkbox and button (same style as HelpGuideForm)
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = bgColor
        };

        var chkDontShowAgain = new CheckBox
        {
            Text = "다시 보지 않기",
            Font = new Font("Malgun Gothic", _baseFontSize),
            ForeColor = _currentTheme == ThemeType.Dark
                ? Color.FromArgb(160, 160, 160)
                : Color.FromArgb(100, 100, 100),
            AutoSize = true,
            Location = new Point(5, 13)
        };

        var btnOk = new Button
        {
            Text = "확인",
            Size = new Size(100, 35),
            FlatStyle = FlatStyle.Flat,
            BackColor = panelColor,
            ForeColor = textColor,
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnOk.FlatAppearance.BorderColor = accentColor;
        btnOk.Anchor = AnchorStyles.Right;
        btnOk.Location = new Point(bottomPanel.Width - btnOk.Width - 5, 7);
        bottomPanel.Resize += (s, e) => btnOk.Location = new Point(bottomPanel.Width - btnOk.Width - 5, 7);

        btnOk.Click += (s, e) =>
        {
            if (chkDontShowAgain.Checked)
            {
                _hideUsageNotice = true;
                SaveSettings();
            }
            form.Close();
        };

        bottomPanel.Controls.Add(chkDontShowAgain);
        bottomPanel.Controls.Add(btnOk);

        mainPanel.Controls.Add(rtb, 0, 0);
        mainPanel.Controls.Add(bottomPanel, 0, 1);
        form.Controls.Add(mainPanel);
        form.AcceptButton = btnOk;

        // Apply dark theme scrollbar
        if (_currentTheme == ThemeType.Dark)
        {
            HelpGuideForm.ApplyScrollBarThemeToControl(form, true);
        }

        form.ShowDialog(this);
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
        _notificationTimer?.Stop();
        _notificationTimer?.Dispose();

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
