using System.Diagnostics;
using System.Net.Http;
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
    private readonly List<DealItem> _searchResults;
    private readonly BindingSource _dealBindingSource;
    private readonly BindingSource _itemBindingSource;
    private readonly List<KafraItem> _itemResults;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _indexCts;
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
    private DataGridView _dgvDeals = null!;
    private Label _lblDealStatus = null!;
    private FlowLayoutPanel _pnlSearchHistory = null!;
    private List<string> _dealSearchHistory = new();
    private const int MaxSearchHistoryCount = 10;

    // Deal Tab Pagination (server-side: API returns 10 items per page)
    private Button _btnDealPrev = null!;
    private Button _btnDealNext = null!;
    private Label _lblDealPage = null!;
    private int _dealCurrentPage = 1;  // API uses 1-based page numbers

    // Tab 2: Item Database (Kafra)
    private TextBox _txtItemSearch = null!;
    private ToolStripDropDownButton _ddItemTypes = null!;
    private readonly HashSet<int> _selectedItemTypes = new() { 999 }; // Default: all
    private ToolStripComboBox _cboSubFilter1 = null!;
    private ToolStripComboBox _cboSubFilter2 = null!;
    private Button _btnItemSearch = null!;
    private Button _btnIndexRebuild = null!;
    private ToolStripButton _btnItemSearchToolStrip = null!;
    private ToolStripButton _btnIndexRebuildToolStrip = null!;
    private ToolStripControlHost _progressIndexHost = null!;
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

    // Item Tab Pagination
    private Button _btnItemPrev = null!;
    private Button _btnItemNext = null!;
    private Label _lblItemPage = null!;
    private int _itemCurrentPage = 0;
    private int _itemTotalCount = 0;
    private const int ItemPageSize = 100;

    // Tab 3: Monitoring
    private readonly MonitoringService _monitoringService;
    private TextBox _txtMonitorItemName = null!;
    private ComboBox _cboMonitorServer = null!;
    private ToolStripButton _btnMonitorAdd = null!;
    private ToolStripButton _btnMonitorRemove = null!;
    private ToolStripButton _btnMonitorRefresh = null!;
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

    // Status Bar
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblCreator = null!;

    public Form1()
    {
        // Initialize dark mode support before creating any controls
        InitializeDarkModeSupport();

        InitializeComponent();
        _gnjoyClient = new GnjoyClient();  // Deal tab only
        _kafraClient = new KafraClient();
        _itemIndexService = new ItemIndexService();
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
    }

    private async Task LoadItemIndexAsync()
    {
        try
        {
            var loaded = await _itemIndexService.LoadFromCacheAsync();
            if (loaded)
            {
                UpdateIndexStatus();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Index load error: {ex.Message}");
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
        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = ThemePanel,
            ForeColor = ThemeTextMuted,
            SizingGrip = false
        };

        _lblCreator = new ToolStripStatusLabel
        {
            Text = "Created by 티포니",
            Spring = true,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = ThemeTextMuted
        };

        _statusStrip.Items.Add(_lblCreator);

        // Add controls in reverse dock order (last added = first docked)
        // 1. TabControl (Fill) - added first, docked last - fills remaining space
        // 2. StatusStrip (Bottom) - added second, docked second - takes bottom
        // 3. MenuStrip (Top) - added third, docked first - takes top
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
        _indexCts?.Cancel();
        _monitorCts?.Cancel();
        _monitorTimer?.Stop();
        _monitorTimer?.Dispose();
        _alarmTimer?.Stop();
        _alarmTimer?.Dispose();
        _gnjoyClient.Dispose();
        _kafraClient.Dispose();
        _itemIndexService.Dispose();
        _monitoringService.Dispose();
        _imageHttpClient.Dispose();
        _picItemImage?.Image?.Dispose();
        base.OnFormClosed(e);
    }
}

// Helper class for item type combo box
internal class ItemTypeItem
{
    public int Id { get; }
    public string Name { get; }

    public ItemTypeItem(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}

// App settings for persistence
internal class AppSettings
{
    public float FontSize { get; set; } = 12f;
    public ThemeType Theme { get; set; } = ThemeType.Dark;
    public bool IsSoundMuted { get; set; } = false;
    public AlarmSoundType AlarmSound { get; set; } = AlarmSoundType.SystemSound;
    public int AlarmIntervalSeconds { get; set; } = 5;
    public List<string> DealSearchHistory { get; set; } = new();
}

// Theme types
public enum ThemeType
{
    Dark,
    Classic
}

// Alarm sound types
public enum AlarmSoundType
{
    SystemSound,  // 시스템 경고음 (기본값)
    Chime,        // 차임벨
    DingDong,     // 딩동
    Rising,       // 상승음
    Alert         // 알림음
}

// Helper class for server combo box
internal class ServerItem
{
    public int Id { get; }
    public string Name { get; }

    public ServerItem(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}
