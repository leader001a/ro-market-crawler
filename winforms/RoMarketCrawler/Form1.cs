using System.Diagnostics;
using System.Net.Http;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

public partial class Form1 : Form
{
    // Theme Colors (set by ApplyThemeColors)
    private Color ThemeBackground;
    private Color ThemePanel;
    private Color ThemeGrid;
    private Color ThemeGridAlt;
    private Color ThemeAccent;
    private Color ThemeAccentHover;
    private Color ThemeAccentText;      // Text color on accent background
    private Color ThemeText;
    private Color ThemeTextMuted;
    private Color ThemeLinkColor;       // Emphasized text (item names, links)
    private Color ThemeBorder;
    private Color ThemeSaleColor;
    private Color ThemeBuyColor;
    private ThemeType _currentTheme = ThemeType.Dark;

    private readonly GnjoyClient _gnjoyClient;
    private readonly KafraClient _kafraClient;
    private readonly ItemIndexService _itemIndexService;
    private readonly UpdateService _updateService;
    private readonly List<DealItem> _searchResults;
    private readonly BindingSource _dealBindingSource;
    private readonly BindingSource _itemBindingSource;
    private readonly List<KafraItem> _itemResults;
    private CancellationTokenSource? _cts;
    private string _lastSearchTerm = string.Empty;
    private int _currentSearchId = 0;  // Increments with each search to prevent stale updates

    // Main TabControl (BorderlessTabControl hides border)
    private BorderlessTabControl _tabControl = null!;

    // Tab 1: Deal Search (GNJOY)
    private TextBox _txtDealSearch = null!;
    private ComboBox _cboDealServer = null!;
    private Button _btnDealSearch = null!;
    private Button _btnDealCancel = null!;
    private ToolStripButton _btnDealSearchToolStrip = null!;
    private ToolStripButton _btnDealCancelToolStrip = null!;
    private ToolStripProgressBar _progressDealSearch = null!;
    private DataGridView _dgvDeals = null!;
    private Label _lblDealStatus = null!;
    private FlowLayoutPanel _pnlSearchHistory = null!;
    private List<string> _dealSearchHistory = new();
    private const int MaxSearchHistoryCount = 10;

    // Deal Tab Pagination (server-side: API returns 10 items per page)
    private RoMarketCrawler.Controls.RoundedButton _btnDealPrev = null!;
    private RoMarketCrawler.Controls.RoundedButton _btnDealNext = null!;
    private Label _lblDealPage = null!;
    private int _dealCurrentPage = 1;  // API uses 1-based page numbers

    // Tab 2: Item Database (Kafra)
    private TextBox _txtItemSearch = null!;
    private readonly HashSet<int> _selectedItemTypes = new() { 999 }; // Default: all
    private Button _btnItemSearch = null!;
    private Button _btnIndexRebuild = null!;
    private ToolStripButton _btnItemSearchToolStrip = null!;
    private ToolStripButton _btnIndexRebuildToolStrip = null!;
    private DataGridView _dgvItems = null!;
    private RichTextBox _rtbItemDetail = null!;
    private PictureBox _picItemImage = null!;
    private Label _lblItemName = null!;
    private Label _lblItemBasicInfo = null!;
    private Label _lblItemStatus = null!;
    private readonly List<Form> _openItemInfoForms = new();
    private ProgressBar _progressIndex = null!;
    private readonly HttpClient _imageHttpClient = new();
    private readonly string _imageCacheDir;

    // Item Tab - Main Category Dropdown (single selection)
    private ComboBox _cboItemType = null!;

    // Item Tab - Sub-Category Checkboxes (dynamically generated)
    private FlowLayoutPanel _pnlSubCategories = null!;
    private FlowLayoutPanel _pnlJobFilters = null!;  // Job class filters (separate row)
    private readonly Dictionary<string, CheckBox> _subCategoryCheckBoxes = new();
    private readonly Dictionary<string, CheckBox> _jobFilterCheckBoxes = new();

    // Item Tab Pagination
    private RoMarketCrawler.Controls.RoundedButton _btnItemPrev = null!;
    private RoMarketCrawler.Controls.RoundedButton _btnItemNext = null!;
    private Label _lblItemPage = null!;
    private int _itemCurrentPage = 0;
    private int _itemTotalCount = 0;
    private const int ItemPageSize = 100;

    // Item Tab Search Options
    private CheckBox _chkSearchDescription = null!;  // Search in item description

    // Tab 3: Monitoring
    private readonly MonitoringService _monitoringService;
    private TextBox _txtMonitorItemName = null!;
    private ComboBox _cboMonitorServer = null!;
    private ToolStripButton _btnMonitorAdd = null!;
    private ToolStripButton _btnMonitorRemove = null!;
    private ToolStripButton _btnMonitorRefresh = null!;
    private ToolStripDropDownButton _btnAutoRefresh = null!;
    private ToolStripProgressBar _progressMonitor = null!;
    private DataGridView _dgvMonitorItems = null!;
    private DataGridView _dgvMonitorResults = null!;
    private NumericUpDown _nudRefreshInterval = null!;
    private Button _btnApplyInterval = null!;
    private Label _lblMonitorStatus = null!;
    private Label _lblRefreshSetting = null!;
    private System.Windows.Forms.Timer _monitorTimer = null!;
    private CancellationTokenSource? _monitorCts;
    private BindingSource _monitorItemsBindingSource = null!;
    private BindingSource _monitorResultsBindingSource = null!;

    // Monitor Results Processing State
    private bool _isProcessingResults = false;

    // Watermark image for DataGridView backgrounds
    private Image? _watermarkImage = null;
    private Image? _watermarkFaded = null;  // Pre-rendered faded version

    // Font Size Settings
    private float _baseFontSize = 12f;
    private readonly string _settingsFilePath;
    private MenuStrip _menuStrip = null!;

    // Sound Settings
    private bool _isSoundMuted = false;
    private AlarmSoundType _selectedAlarmSound = AlarmSoundType.SystemSound;
    private ToolStripButton _btnSoundMute = null!;
    private ToolStripLabel _lblAutoRefreshStatus = null!;
    private ComboBox _cboAlarmSound = null!;

    // Alarm Timer Settings (always running, controlled by mute button)
    private System.Windows.Forms.Timer _alarmTimer = null!;
    private NumericUpDown _nudAlarmInterval = null!;
    private int _alarmIntervalSeconds = 5;

    // Rate Limit UI Timer (checks and updates UI when rate limited)
    private System.Windows.Forms.Timer _rateLimitTimer = null!;
    private bool _isRateLimitUIActive = false;

    // Status Bar
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblCreator = null!;

    // Custom AutoComplete dropdown for search textboxes (handles Korean IME correctly)
    private AutoCompleteDropdown _autoCompleteDropdown = null!;
    private List<string> _autoCompleteItems = new();

    public Form1()
    {
        // Initialize dark mode support before creating any controls
        InitializeDarkModeSupport();

        InitializeComponent();
        _gnjoyClient = new GnjoyClient();  // Deal tab only
        _kafraClient = new KafraClient();
        _itemIndexService = new ItemIndexService();
        _updateService = new UpdateService();
        _monitoringService = new MonitoringService();  // Uses its own GnjoyClient for isolation
        _searchResults = new List<DealItem>();
        _itemResults = new List<KafraItem>();
        _dealBindingSource = new BindingSource { DataSource = _searchResults };
        _itemBindingSource = new BindingSource { DataSource = _itemResults };
        _monitorItemsBindingSource = new BindingSource();
        _monitorResultsBindingSource = new BindingSource();

        // Initialize paths
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        _imageCacheDir = Path.Combine(dataDir, "ItemImages");
        Directory.CreateDirectory(_imageCacheDir);
        _settingsFilePath = Path.Combine(dataDir, "settings.json");

        // Load settings and apply theme
        LoadSettings();
        ApplyThemeColors();

        InitializeCustomComponents();
        _ = LoadItemIndexAsync(); // Load index in background
        _ = LoadMonitoringAsync(); // Load monitoring config

        // Check for updates after form is shown
        Shown += async (s, e) => await CheckForUpdatesAsync();
    }

    private async Task LoadItemIndexAsync()
    {
        try
        {
            var loaded = await _itemIndexService.LoadFromCacheAsync();
            if (loaded)
            {
                UpdateIndexStatus();
                RefreshAutoCompleteSource();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Index load error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check for application updates from GitHub Releases
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            // Small delay to let the form fully render
            await Task.Delay(1000);

            var updateInfo = await _updateService.CheckForUpdateAsync();
            if (updateInfo == null)
            {
                Debug.WriteLine("[Form1] No updates available");
                return;
            }

            Debug.WriteLine($"[Form1] Update available: {updateInfo.TagName}");

            // Show update dialog
            using var dialog = new UpdateDialog(_updateService, updateInfo, _currentTheme);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Update check error: {ex.Message}");
            // Silently fail - don't interrupt user experience
        }
    }

    /// <summary>
    /// Initialize custom autocomplete dropdown
    /// </summary>
    private void InitializeAutoComplete()
    {
        _autoCompleteDropdown = new AutoCompleteDropdown();
        _autoCompleteDropdown.UpdateTheme(ThemeGrid, ThemeText, ThemeAccent, ThemeBorder);

        // Attach to all search textboxes
        _autoCompleteDropdown.AttachTo(_txtDealSearch);
        _autoCompleteDropdown.AttachTo(_txtItemSearch);
        _autoCompleteDropdown.AttachTo(_txtMonitorItemName);

        Debug.WriteLine("[Form1] AutoComplete dropdown initialized");
    }

    /// <summary>
    /// Refresh autocomplete source with item names from index and search history
    /// </summary>
    private void RefreshAutoCompleteSource()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(RefreshAutoCompleteSource));
            return;
        }

        try
        {
            _autoCompleteItems.Clear();

            // Add search history first (higher priority)
            if (_dealSearchHistory?.Count > 0)
            {
                _autoCompleteItems.AddRange(_dealSearchHistory);
            }

            // Add item names from index
            if (_itemIndexService.IsLoaded)
            {
                var itemNames = _itemIndexService.GetAllScreenNames();
                if (itemNames.Count > 0)
                {
                    _autoCompleteItems.AddRange(itemNames);
                    Debug.WriteLine($"[Form1] AutoComplete source refreshed: {itemNames.Count} items + {_dealSearchHistory?.Count ?? 0} history");
                }
            }

            // Update dropdown data source
            _autoCompleteDropdown?.SetDataSource(_autoCompleteItems);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] AutoComplete refresh error: {ex.Message}");
        }
    }

    private void UpdateIndexStatus()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateIndexStatus));
            return;
        }

        if (_itemIndexService.IsLoaded)
        {
            var lastUpdated = _itemIndexService.LastUpdated?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "알 수 없음";
            _lblItemStatus.Text = $"인덱스: {_itemIndexService.TotalCount:N0}개 아이템 (갱신: {lastUpdated})";
        }
        else
        {
            _lblItemStatus.Text = "인덱스가 없습니다. [아이템정보 수집] 버튼을 클릭하세요.";
        }
    }

    private void InitializeCustomComponents()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Text = $"RO Market Crawler - v{version?.Major}.{version?.Minor}.{version?.Build}";
        Size = new Size(1400, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeBackground;
        ForeColor = ThemeText;

        // Set window icon (title bar and taskbar) from embedded resource
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

        // Setup menu strip
        SetupMenuStrip();

        _tabControl = new BorderlessTabControl
        {
            Dock = DockStyle.Fill
        };
        ApplyTabControlStyle(_tabControl);

        // Create tabs
        var tabDeal = new TabPage("노점조회");
        var tabItem = new TabPage("아이템 정보 수집/조회");
        var tabMonitor = new TabPage("노점 모니터링");

        ApplyTabPageStyle(tabDeal);
        ApplyTabPageStyle(tabItem);
        ApplyTabPageStyle(tabMonitor);

        // Setup each tab
        SetupDealTab(tabDeal);
        SetupItemTab(tabItem);
        SetupMonitoringTab(tabMonitor);

        _tabControl.TabPages.Add(tabDeal);
        _tabControl.TabPages.Add(tabItem);
        _tabControl.TabPages.Add(tabMonitor);

        // Refresh Monitor tab UI when switching to it
        _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
        // Confirm before switching to Deal Search tab (if auto-refresh is running)
        _tabControl.Selecting += TabControl_Selecting;

        // Status bar at bottom with creator info
        // Use brighter/darker colors for better visibility
        var statusTextColor = _currentTheme == ThemeType.Dark
            ? Color.FromArgb(200, 200, 200)  // Brighter for dark theme
            : Color.FromArgb(60, 60, 60);     // Darker for light theme
        var statusFont = new Font("Malgun Gothic", _baseFontSize - 2, FontStyle.Bold);

        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = ThemePanel,
            ForeColor = statusTextColor,
            SizingGrip = false,
            Font = statusFont
        };

        // Expiration date label (far left)
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

        // Add controls in reverse dock order (last added = first docked)
        Controls.Add(_tabControl);
        Controls.Add(_statusStrip);
        Controls.Add(_menuStrip);

        // Apply loaded settings to all controls
        ApplyFontSizeToAllControls(this);
        ApplyThemeToAllControls(this);

        // Apply dark mode to title bar
        ApplyDarkModeToTitleBar(_currentTheme == ThemeType.Dark);

        // Refresh search history panel after layout is complete
        UpdateSearchHistoryPanel();

        // Initialize custom autocomplete dropdown for Korean IME support
        InitializeAutoComplete();

        // Load watermark image for DataGridView backgrounds
        LoadWatermarkImage();

        // Initialize rate limit UI timer (checks every second when rate limited)
        _rateLimitTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000  // Check every second
        };
        _rateLimitTimer.Tick += RateLimitTimer_Tick;
        _rateLimitTimer.Start();
    }

    /// <summary>
    /// Load and prepare the watermark image for DataGridView backgrounds
    /// </summary>
    private void LoadWatermarkImage()
    {
        try
        {
            _watermarkImage = Services.ResourceHelper.GetLogoImage();
            if (_watermarkImage == null) return;

            // Create a faded (semi-transparent) version of the watermark
            var fadedBitmap = new Bitmap(_watermarkImage.Width, _watermarkImage.Height);
            using (var g = Graphics.FromImage(fadedBitmap))
            {
                // Use color matrix to apply transparency (opacity: 0.04 for subtle watermark)
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix
                {
                    Matrix33 = 0.04f  // Alpha channel
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

            // Attach paint handlers to all DataGridViews
            AttachWatermarkPaintHandlers();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Watermark load error: {ex.Message}");
        }
    }

    /// <summary>
    /// Attach watermark paint handlers to all DataGridViews
    /// </summary>
    private void AttachWatermarkPaintHandlers()
    {
        if (_watermarkFaded == null) return;

        _dgvDeals.Paint += DataGridView_PaintWatermark;
        _dgvItems.Paint += DataGridView_PaintWatermark;
        _dgvMonitorResults.Paint += DataGridView_PaintWatermark;
    }

    /// <summary>
    /// Paint watermark on DataGridView background
    /// </summary>
    private void DataGridView_PaintWatermark(object? sender, PaintEventArgs e)
    {
        if (_watermarkFaded == null || sender is not DataGridView dgv) return;

        // Calculate watermark size (scale to fit 80% of grid height)
        var targetHeight = dgv.ClientSize.Height * 0.8;
        var scale = (float)targetHeight / _watermarkFaded.Height;
        var watermarkWidth = (int)(_watermarkFaded.Width * scale);
        var watermarkHeight = (int)(_watermarkFaded.Height * scale);

        // Center the watermark in the grid (below header)
        var x = (dgv.ClientSize.Width - watermarkWidth) / 2;
        var contentTop = dgv.ColumnHeadersHeight;
        var contentHeight = dgv.ClientSize.Height - contentTop;
        var y = contentTop + (contentHeight - watermarkHeight) / 2;

        // Draw the watermark
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_watermarkFaded, x, y, watermarkWidth, watermarkHeight);
    }

    private void SetupMenuStrip()
    {
        _menuStrip = new MenuStrip
        {
            BackColor = ThemePanel,
            ForeColor = ThemeText,
            Renderer = _currentTheme == ThemeType.Dark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer()
        };

        // View menu
        var viewMenu = new ToolStripMenuItem("보기(&V)");
        viewMenu.ForeColor = ThemeText;

        // Font size submenu
        var fontSizeMenu = new ToolStripMenuItem("글꼴 크기");
        fontSizeMenu.ForeColor = ThemeText;

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

        // Add separator and zoom shortcuts
        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        var zoomIn = new ToolStripMenuItem("글꼴 크게 (Ctrl++)");
        zoomIn.ShortcutKeys = Keys.Control | Keys.Oemplus;
        zoomIn.Click += (s, e) => ChangeFontSize(1f);
        viewMenu.DropDownItems.Add(zoomIn);

        var zoomOut = new ToolStripMenuItem("글꼴 작게 (Ctrl+-)");
        zoomOut.ShortcutKeys = Keys.Control | Keys.OemMinus;
        zoomOut.Click += (s, e) => ChangeFontSize(-1f);
        viewMenu.DropDownItems.Add(zoomOut);

        var zoomReset = new ToolStripMenuItem("글꼴 기본 (Ctrl+0)");
        zoomReset.ShortcutKeys = Keys.Control | Keys.D0;
        zoomReset.Click += (s, e) => SetFontSize(12f);
        viewMenu.DropDownItems.Add(zoomReset);

        // Theme submenu
        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        var themeMenu = new ToolStripMenuItem("테마");
        themeMenu.ForeColor = ThemeText;

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
        var toolsMenu = new ToolStripMenuItem("도구(&T)");
        toolsMenu.ForeColor = ThemeText;

        var indexRebuildItem = new ToolStripMenuItem("아이템정보 수집")
        {
            ForeColor = ThemeText,
            ShortcutKeys = Keys.Control | Keys.R
        };
        indexRebuildItem.Click += BtnIndexRebuild_Click;
        toolsMenu.DropDownItems.Add(indexRebuildItem);

        _menuStrip.Items.Add(toolsMenu);

        // Help menu
        var helpMenu = new ToolStripMenuItem("도움말(&H)");
        helpMenu.ForeColor = ThemeText;

        var userGuideItem = new ToolStripMenuItem("사용 가이드")
        {
            ForeColor = ThemeText,
            ShortcutKeys = Keys.F1
        };
        userGuideItem.Click += (s, e) => ShowHelpGuide();
        helpMenu.DropDownItems.Add(userGuideItem);

        helpMenu.DropDownItems.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("정보")
        {
            ForeColor = ThemeText
        };
        aboutItem.Click += (s, e) => ShowAboutDialog();
        helpMenu.DropDownItems.Add(aboutItem);

        _menuStrip.Items.Add(helpMenu);

        // Close all popups - right-aligned button with margin
        var closeAllPopups = new ToolStripMenuItem("전체 팝업 닫기")
        {
            ForeColor = ThemeText,
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.W,
            Alignment = ToolStripItemAlignment.Right,
            Margin = new Padding(0, 0, 15, 0)  // Right margin
        };
        closeAllPopups.Click += (s, e) => CloseAllItemInfoForms();
        _menuStrip.Items.Add(closeAllPopups);
    }

    private void ShowHelpGuide(HelpGuideForm.HelpSection section = HelpGuideForm.HelpSection.Overview)
    {
        using var helpForm = new HelpGuideForm(_currentTheme, _baseFontSize, section);
        helpForm.ShowDialog(this);
    }

    private void ShowAboutDialog()
    {
        const int formWidth = 500;
        const int baseFormHeight = 540;
        const int leftMargin = 25;
        const int contentWidth = 440;

        // Load logo first to calculate dynamic heights
        Image? logoImage = null;
        int logoHeight = 0;
        int logoOffset = 0;
        try
        {
            logoImage = Services.ResourceHelper.GetLogoImage();
            if (logoImage != null)
            {
                var scale = (float)contentWidth / logoImage.Width;
                logoHeight = (int)(logoImage.Height * scale);
                logoOffset = logoHeight + 20;
            }
        }
        catch { }

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

        using var aboutForm = new Form
        {
            Text = "프로그램 정보",
            Size = new Size(formWidth, formHeight),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = clrBackground,
            ShowIcon = false
        };

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

        // Font sizes consistent with Form1 control sizing scheme
        var titleFontSize = _baseFontSize + 1;      // Large title (was +4, too big)
        var normalFontSize = _baseFontSize - 2;     // Same as TextBox/ComboBox
        var smallFontSize = _baseFontSize - 3;      // Same as Label

        // Title
        var lblTitle = new Label
        {
            Text = "RO Market Crawler v1.0.0",
            Font = new Font("Malgun Gothic", titleFontSize, FontStyle.Bold),
            ForeColor = clrLink,
            AutoSize = true,
            Location = new Point(leftMargin, 18 + logoOffset)
        };

        // Description
        var lblDesc = new Label
        {
            Text = "라그나로크 온라인 거래 정보 검색 및 모니터링 프로그램",
            Font = new Font("Malgun Gothic", normalFontSize),
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
            Font = new Font("Malgun Gothic", normalFontSize),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 72 + logoOffset)
        };

        // Creator
        var lblCreator = new Label
        {
            Text = "Created by: 티포니",
            Font = new Font("Malgun Gothic", normalFontSize),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 130 + logoOffset)
        };

        // Contact: separate Label + LinkLabel for proper styling
        var lblContact = new Label
        {
            Text = "문의:",
            Font = new Font("Malgun Gothic", normalFontSize),
            ForeColor = clrText,
            AutoSize = true,
            Location = new Point(leftMargin, 150 + logoOffset)
        };

        var linkKakao = new LinkLabel
        {
            Text = "카카오톡 오픈프로필",
            Font = new Font("Malgun Gothic", normalFontSize),
            LinkColor = clrLink,
            ActiveLinkColor = clrLink,
            AutoSize = true,
            Location = new Point(leftMargin + 38, 150 + logoOffset)
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

        // Privacy notice (emphasized)
        var lblPrivacy = new Label
        {
            Text = "** 본 프로그램은 개인정보 및 게임정보를 일체 수집하지 않습니다 **",
            Font = new Font("Malgun Gothic", normalFontSize, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 60),  // Green for trust
            AutoSize = true,
            Location = new Point(leftMargin, 175 + logoOffset)
        };

        // Legal notice (scrollable)
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

        var txtLegalNotice = new TextBox
        {
            Text = legalNoticeText,
            Font = new Font("Malgun Gothic", smallFontSize),
            ForeColor = clrLegalText,
            BackColor = clrLegalBg,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(leftMargin, 200 + logoOffset),
            Size = new Size(contentWidth, 240)
        };

        // OK button (centered at bottom) - styled manually for classic theme
        var btnOk = new Button
        {
            Text = "확인",
            Width = 80,
            Height = 28,
            DialogResult = DialogResult.OK,
            Location = new Point((formWidth - 80) / 2 - 8, 455 + logoOffset),
            FlatStyle = FlatStyle.Flat,
            BackColor = clrButtonBg,
            ForeColor = clrButtonText,
            Cursor = Cursors.Hand
        };
        btnOk.FlatAppearance.BorderSize = 0;

        // Add controls to form
        if (picLogo != null)
        {
            aboutForm.Controls.Add(picLogo);
        }
        aboutForm.Controls.AddRange(new Control[] { lblTitle, lblDesc, lblSource, lblCreator, lblContact, linkKakao, lblPrivacy, txtLegalNotice, btnOk });
        aboutForm.AcceptButton = btnOk;
        aboutForm.ShowDialog(this);
    }

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
        if (newSize >= 8f && newSize <= 16f)
        {
            SetFontSize(newSize);
        }
    }

    private void SetFontSize(float size)
    {
        _baseFontSize = size;
        ApplyFontSizeToAllControls(this);
        UpdateFontSizeMenuChecks();
        SaveSettings();
    }

    private void ApplyFontSizeToAllControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            // Skip menu strip (it has its own styling)
            if (control is MenuStrip) continue;

            // Apply font based on control type with appropriate offsets
            if (control is DataGridView dgv)
            {
                dgv.DefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 3);
                dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Bold);
            }
            else if (control == _lblItemName)
            {
                // Item name label is larger and bold (matching ItemInfoForm)
                control.Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold);
            }
            else if (control == _lblItemBasicInfo)
            {
                // Basic info label smaller and muted
                control.Font = new Font("Malgun Gothic", _baseFontSize - 2);
            }
            else if (control is RichTextBox rtb)
            {
                rtb.Font = new Font("Malgun Gothic", _baseFontSize - 1.5f);
            }
            else if (control is Button btn)
            {
                btn.Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Bold);
            }
            else if (control is TextBox)
            {
                control.Font = new Font("Malgun Gothic", _baseFontSize - 2);
            }
            else if (control is ComboBox)
            {
                control.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is Label)
            {
                control.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is NumericUpDown nud)
            {
                nud.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is CheckBox chk)
            {
                chk.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is RadioButton rb)
            {
                rb.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is LinkLabel ll)
            {
                ll.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is GroupBox gb)
            {
                gb.Font = new Font("Malgun Gothic", _baseFontSize - 3);
            }
            else if (control is TabControl tc)
            {
                tc.Font = new Font("Malgun Gothic", _baseFontSize - 2);
            }
            else if (control is StatusStrip ss)
            {
                var statusFont = new Font("Malgun Gothic", _baseFontSize - 2, FontStyle.Bold);
                ss.Font = statusFont;
                foreach (ToolStripItem item in ss.Items)
                {
                    item.Font = statusFont;
                }
            }
            else if (control is ToolStrip toolStrip)
            {
                // Apply font to ToolStrip and all its items
                toolStrip.Font = new Font("Malgun Gothic", _baseFontSize - 3);
                ApplyFontSizeToToolStripItems(toolStrip.Items);
            }

            // Recurse into child controls
            if (control.HasChildren)
            {
                ApplyFontSizeToAllControls(control);
            }
        }
    }

    private void ApplyFontSizeToToolStripItems(ToolStripItemCollection items)
    {
        var font = new Font("Malgun Gothic", _baseFontSize - 3);
        var scale = _baseFontSize / 12f;

        foreach (ToolStripItem item in items)
        {
            item.Font = font;

            // Handle dropdown items recursively
            if (item is ToolStripDropDownButton dropDown)
            {
                ApplyFontSizeToToolStripItems(dropDown.DropDownItems);

                // Apply font and resize controls inside ToolStripControlHost
                foreach (ToolStripItem dropItem in dropDown.DropDownItems)
                {
                    if (dropItem is ToolStripControlHost host && host.Control is Panel panel)
                    {
                        // Apply font to panel controls
                        ApplyFontSizeToAllControls(panel);

                        // Recalculate panel size based on font scale
                        RecalculatePanelLayout(panel, scale);

                        // Update host size to match panel
                        host.Size = panel.Size;
                    }
                }
            }
            else if (item is ToolStripMenuItem menuItem)
            {
                ApplyFontSizeToToolStripItems(menuItem.DropDownItems);
            }
        }
    }

    private void RecalculatePanelLayout(Panel panel, float scale)
    {
        var font = new Font("Malgun Gothic", _baseFontSize - 3);
        var smallFont = new Font("Malgun Gothic", _baseFontSize - 3.5f);
        var rowHeight = (int)(28 * scale);
        var panelWidth = (int)(280 * scale);

        // Update panel width
        panel.Width = panelWidth;

        // Update font for all controls
        foreach (Control ctrl in panel.Controls)
        {
            if (ctrl is Label lbl)
            {
                lbl.Font = lbl.ForeColor == ThemeTextMuted ? smallFont : font;
            }
            else if (ctrl is Button btn)
            {
                btn.Font = font;
            }
            else if (ctrl is NumericUpDown nud)
            {
                nud.Font = font;
            }
            else if (ctrl is ComboBox cbo)
            {
                cbo.Font = font;
            }
        }

        // Check if this is the alarm settings panel (has cboAlarmSound)
        var isAlarmPanel = panel.Controls.Cast<Control>().Any(c => c.Name == "cboAlarmSound");

        if (isAlarmPanel)
        {
            RecalculateAlarmPanelLayout(panel, scale, font, smallFont, rowHeight, panelWidth);
        }
        else
        {
            RecalculateAutoRefreshPanelLayout(panel, scale, font, smallFont, rowHeight, panelWidth);
        }
    }

    private void RecalculateAlarmPanelLayout(Panel panel, float scale, Font font, Font smallFont, int rowHeight, int panelWidth)
    {
        var yPos = 8;

        // Row 1: Description label (multi-line)
        var lblTitle = panel.Controls.Cast<Control>().FirstOrDefault(c => c is Label lbl && lbl.Text.Contains("\n"));
        if (lblTitle != null)
        {
            lblTitle.Location = new Point(8, yPos);
            lblTitle.Size = new Size(panelWidth - 20, (int)(40 * scale));
            yPos += (int)(45 * scale);
        }

        // Row 2: Alarm sound selection (label + combo + test button)
        var lblSound = panel.Controls["lblSound"];
        var cboAlarmSound = panel.Controls["cboAlarmSound"];
        var btnTest = panel.Controls["btnTest"];

        if (lblSound != null)
            lblSound.Location = new Point(8, yPos + 2);
        if (cboAlarmSound != null)
        {
            cboAlarmSound.Location = new Point((int)(75 * scale), yPos);
            cboAlarmSound.Size = new Size((int)(90 * scale), rowHeight);
        }
        if (btnTest != null)
        {
            btnTest.Location = new Point((int)(170 * scale), yPos);
            btnTest.Size = new Size((int)(65 * scale), rowHeight);
        }
        yPos += rowHeight + 6;

        // Row 3: Alarm interval (label + numericupdown + "초")
        var lblInterval = panel.Controls["lblInterval"];
        var nudAlarmInterval = panel.Controls["nudAlarmInterval"];
        var lblSec = panel.Controls["lblSec"];

        if (lblInterval != null)
            lblInterval.Location = new Point(8, yPos + 2);
        if (nudAlarmInterval != null)
        {
            nudAlarmInterval.Location = new Point((int)(75 * scale), yPos);
            nudAlarmInterval.Size = new Size((int)(55 * scale), rowHeight);
        }
        if (lblSec != null)
            lblSec.Location = new Point((int)(135 * scale), yPos + 2);
        yPos += rowHeight + 8;

        // Row 4: Note label (single line)
        var noteLabel = panel.Controls.Cast<Control>().FirstOrDefault(c => c is Label && c.Text.StartsWith("*"));
        if (noteLabel != null)
        {
            noteLabel.Location = new Point(8, yPos);
            noteLabel.Size = new Size(panelWidth - 16, (int)(22 * scale));
            yPos = noteLabel.Bottom + 8;
        }

        panel.Size = new Size(panelWidth, yPos);
    }

    private void RecalculateAutoRefreshPanelLayout(Panel panel, float scale, Font font, Font smallFont, int rowHeight, int panelWidth)
    {
        var yPos = 8;

        // Row 1: Description label (multi-line)
        var lblTitle = panel.Controls.Cast<Control>().FirstOrDefault(c => c is Label lbl && lbl.Text.Contains("\n"));
        if (lblTitle != null)
        {
            lblTitle.Location = new Point(8, yPos);
            lblTitle.Size = new Size(panelWidth - 20, (int)(40 * scale));
            yPos += (int)(45 * scale);
        }

        // Row 2: Refresh interval (label + numericupdown)
        var lblInterval = panel.Controls["lblInterval"];
        var nudInterval = panel.Controls["nudRefreshInterval"];

        if (lblInterval != null)
            lblInterval.Location = new Point(8, yPos + 2);
        if (nudInterval != null)
        {
            nudInterval.Location = new Point((int)(140 * scale), yPos);
            nudInterval.Size = new Size((int)(65 * scale), rowHeight);
        }
        yPos += rowHeight + 6;

        // Row 3: Apply button
        var btnApply = panel.Controls["btnApplyInterval"];
        if (btnApply != null)
        {
            btnApply.Location = new Point(8, yPos);
            btnApply.Size = new Size(panelWidth - 20, rowHeight);
            yPos = btnApply.Bottom + 10;
        }

        panel.Size = new Size(panelWidth, yPos);
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
            else
            {
                Debug.WriteLine($"[Form1] LoadSettings: Settings file not found at {_settingsFilePath}");
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _monitorCts?.Cancel();
        _queueCts?.Cancel();
        _queueCts?.Dispose();
        _monitorTimer?.Stop();
        _monitorTimer?.Dispose();
        _alarmTimer?.Stop();
        _alarmTimer?.Dispose();
        _rateLimitTimer?.Stop();
        _rateLimitTimer?.Dispose();
        _gnjoyClient.Dispose();
        _kafraClient.Dispose();
        _itemIndexService.Dispose();
        _updateService.Dispose();
        _monitoringService.Dispose();
        _imageHttpClient.Dispose();
        _picItemImage?.Image?.Dispose();
        _autoCompleteDropdown?.Dispose();
        _watermarkImage?.Dispose();
        _watermarkFaded?.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>
    /// Timer tick handler for rate limit UI updates.
    /// Checks if rate limited and updates UI accordingly.
    /// </summary>
    private void RateLimitTimer_Tick(object? sender, EventArgs e)
    {
        var isRateLimited = GnjoyClient.IsRateLimited;

        if (isRateLimited && !_isRateLimitUIActive)
        {
            // Just became rate limited - disable UI and show message
            _isRateLimitUIActive = true;
            SetRateLimitUIState(true);
            Debug.WriteLine("[Form1] Rate limit UI activated");
        }
        else if (isRateLimited && _isRateLimitUIActive)
        {
            // Still rate limited - update remaining time display
            UpdateRateLimitStatusDisplay();
        }
        else if (!isRateLimited && _isRateLimitUIActive)
        {
            // Rate limit expired - re-enable UI
            _isRateLimitUIActive = false;
            SetRateLimitUIState(false);
            Debug.WriteLine("[Form1] Rate limit UI deactivated");
        }
    }

    // Track if auto-refresh was running before rate limit
    private bool _wasAutoRefreshRunningBeforeRateLimit = false;

    /// <summary>
    /// Enable or disable UI controls based on rate limit state.
    /// </summary>
    private void SetRateLimitUIState(bool isRateLimited)
    {
        // Deal Tab controls
        _btnDealSearchToolStrip.Enabled = !isRateLimited;
        _txtDealSearch.Enabled = !isRateLimited;
        _cboDealServer.Enabled = !isRateLimited;
        _btnDealPrev.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !isRateLimited && _hasMorePages;

        // Monitor Tab controls
        _btnMonitorRefresh.Enabled = !isRateLimited;
        _btnAutoRefresh.Enabled = !isRateLimited;
        _btnMonitorAdd.Enabled = !isRateLimited;

        // Pause/Resume auto-refresh timer based on rate limit
        if (isRateLimited)
        {
            if (_monitorTimer != null && _monitorTimer.Enabled)
            {
                _wasAutoRefreshRunningBeforeRateLimit = true;
                _monitorTimer.Stop();
                _lblAutoRefreshStatus.Text = "[일시정지]";
                _lblAutoRefreshStatus.ForeColor = ThemeSaleColor;
                Debug.WriteLine("[Form1] Auto-refresh paused due to rate limit");
            }
        }
        else
        {
            // Resume auto-refresh if it was running before rate limit
            if (_wasAutoRefreshRunningBeforeRateLimit && _monitorTimer != null)
            {
                _wasAutoRefreshRunningBeforeRateLimit = false;
                _monitorTimer.Start();
                _lblAutoRefreshStatus.Text = "[동작중]";
                _lblAutoRefreshStatus.ForeColor = Color.FromArgb(100, 200, 100);
                Debug.WriteLine("[Form1] Auto-refresh resumed after rate limit expired");
            }
        }

        // Disable search history links in Deal tab
        if (_pnlSearchHistory != null)
        {
            foreach (Control ctrl in _pnlSearchHistory.Controls)
            {
                if (ctrl is Label lbl && lbl.Tag is ValueTuple<string, string> tag && tag.Item1 == "SearchHistoryLink")
                {
                    lbl.Enabled = !isRateLimited;
                    lbl.ForeColor = isRateLimited ? ThemeTextMuted : ThemeAccent;
                    lbl.Cursor = isRateLimited ? Cursors.Default : Cursors.Hand;
                }
            }
        }

        // Update status messages
        if (isRateLimited)
        {
            UpdateRateLimitStatusDisplay();
        }
        else
        {
            // Restore normal status messages
            _lblDealStatus.Text = "검색어를 입력하고 [검색] 버튼을 클릭하세요.";
            _lblDealStatus.ForeColor = ThemeText;
            _lblMonitorStatus.Text = "자동 갱신 설정 또는 [수동조회] 버튼으로 시세를 확인하세요.";
        }
    }

    /// <summary>
    /// Update the rate limit status display with remaining time.
    /// </summary>
    private void UpdateRateLimitStatusDisplay()
    {
        var remainingSeconds = GnjoyClient.RemainingRateLimitSeconds;
        var minutes = remainingSeconds / 60;
        var seconds = remainingSeconds % 60;

        string timeText;
        if (minutes > 0)
        {
            timeText = $"{minutes}분 {seconds}초";
        }
        else
        {
            timeText = $"{seconds}초";
        }

        var statusMessage = $"⚠ API 요청 제한 중 - {timeText} 후 재시도 가능";

        // Update both tabs' status labels
        _lblDealStatus.Text = statusMessage;
        _lblDealStatus.ForeColor = ThemeSaleColor;  // Red color for warning

        _lblMonitorStatus.Text = statusMessage;
    }
}

// Helper class for item type combo box

// App settings for persistence
// Theme types

