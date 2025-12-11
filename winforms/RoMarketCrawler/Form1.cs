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

    // Main TabControl
    private TabControl _tabControl = null!;

    // Tab 1: Deal Search (GNJOY)
    private TextBox _txtDealSearch = null!;
    private ComboBox _cboDealServer = null!;
    private ComboBox _cboDealType = null!;
    private Button _btnDealSearch = null!;
    private Button _btnDealCancel = null!;
    private DataGridView _dgvDeals = null!;
    private Label _lblDealStatus = null!;

    // Tab 2: Item Database (Kafra)
    private TextBox _txtItemSearch = null!;
    private ComboBox _cboItemType = null!;
    private Button _btnItemSearch = null!;
    private Button _btnIndexRebuild = null!;
    private Button _btnScanWeapons = null!;
    private DataGridView _dgvItems = null!;
    private TextBox _txtItemDetail = null!;
    private PictureBox _picItemImage = null!;
    private Label _lblItemName = null!;
    private Label _lblItemStatus = null!;
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
    private Button _btnMonitorAdd = null!;
    private Button _btnMonitorRemove = null!;
    private Button _btnMonitorRefresh = null!;
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

    // Font Size Settings
    private float _baseFontSize = 12f;
    private readonly string _settingsFilePath;
    private MenuStrip _menuStrip = null!;

    public Form1()
    {
        InitializeComponent();
        _gnjoyClient = new GnjoyClient();
        _kafraClient = new KafraClient();
        _itemIndexService = new ItemIndexService();
        _monitoringService = new MonitoringService(_gnjoyClient);
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
            _lblItemStatus.Text = "인덱스가 없습니다. [인덱스 생성] 버튼을 클릭하세요.";
        }
    }

    private void InitializeCustomComponents()
    {
        Text = "RO Market Crawler - kafra.kr Edition";
        Size = new Size(1400, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeBackground;
        ForeColor = ThemeText;

        // Setup menu strip
        SetupMenuStrip();

        _tabControl = new TabControl
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

        Controls.Add(_tabControl);
        Controls.Add(_menuStrip); // Add menu after tabControl so it appears on top

        // Apply loaded settings to all controls
        ApplyFontSizeToAllControls(this);
        ApplyThemeToAllControls(this);

        // Load servers for deal tab
        LoadServers();
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
                // Item name label is larger and bold
                control.Font = new Font("Segoe UI", _baseFontSize - 1, FontStyle.Bold);
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

            // Recurse into child controls
            if (control.HasChildren)
            {
                ApplyFontSizeToAllControls(control);
            }
        }
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
            var settings = new AppSettings { FontSize = _baseFontSize, Theme = _currentTheme };
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Failed to save settings: {ex.Message}");
        }
    }

    private void ApplyThemeColors()
    {
        if (_currentTheme == ThemeType.Dark)
        {
            // kafra.kr Dark Theme - optimized for visibility
            ThemeBackground = Color.FromArgb(30, 30, 35);
            ThemePanel = Color.FromArgb(45, 45, 55);           // Slightly brighter for headers
            ThemeGrid = Color.FromArgb(35, 35, 42);
            ThemeGridAlt = Color.FromArgb(45, 45, 55);         // More contrast with ThemeGrid
            ThemeAccent = Color.FromArgb(70, 130, 200);
            ThemeAccentHover = Color.FromArgb(90, 150, 220);
            ThemeAccentText = Color.White;
            ThemeText = Color.FromArgb(230, 230, 235);
            ThemeTextMuted = Color.FromArgb(160, 160, 170);
            ThemeLinkColor = Color.FromArgb(100, 180, 255);    // Light blue for links
            ThemeBorder = Color.FromArgb(70, 75, 90);          // Brighter grid lines
            ThemeSaleColor = Color.FromArgb(100, 200, 120);
            ThemeBuyColor = Color.FromArgb(255, 180, 80);
        }
        else // Classic
        {
            // Windows Classic Theme - use system colors for native look
            ThemeBackground = SystemColors.Control;
            ThemePanel = SystemColors.Control;
            ThemeGrid = SystemColors.Window;
            ThemeGridAlt = Color.FromArgb(240, 240, 245);    // Subtle alternating row
            ThemeAccent = SystemColors.Highlight;
            ThemeAccentHover = SystemColors.HotTrack;
            ThemeAccentText = SystemColors.HighlightText;
            ThemeText = SystemColors.WindowText;             // Use WindowText for better contrast
            ThemeTextMuted = SystemColors.GrayText;
            ThemeLinkColor = Color.FromArgb(0, 102, 204);    // Standard link blue
            ThemeBorder = SystemColors.ActiveBorder;         // More visible border
            ThemeSaleColor = Color.FromArgb(0, 128, 0);      // Darker green for light bg
            ThemeBuyColor = Color.FromArgb(180, 100, 0);     // Darker orange for light bg
        }
    }

    private void SetTheme(ThemeType theme)
    {
        if (_currentTheme == theme) return;

        _currentTheme = theme;
        ApplyThemeColors();
        ApplyThemeToAllControls(this);
        UpdateThemeMenuChecks();
        SaveSettings();
    }

    private void ApplyThemeToAllControls(Control parent)
    {
        // Apply to form
        if (parent == this)
        {
            BackColor = ThemeBackground;
            ForeColor = ThemeText;
        }

        foreach (Control control in parent.Controls)
        {
            // Apply based on control type
            if (control is MenuStrip menu)
            {
                if (_currentTheme == ThemeType.Dark)
                {
                    menu.BackColor = ThemePanel;
                    menu.ForeColor = ThemeText;
                    menu.Renderer = new DarkMenuRenderer();
                }
                else
                {
                    // Classic theme - use system default rendering
                    menu.BackColor = SystemColors.MenuBar;
                    menu.ForeColor = SystemColors.MenuText;
                    menu.RenderMode = ToolStripRenderMode.System;
                }
                // Update menu item colors
                foreach (ToolStripItem item in menu.Items)
                {
                    item.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.MenuText;
                    if (item is ToolStripMenuItem menuItem)
                    {
                        UpdateMenuItemColors(menuItem);
                    }
                }
            }
            else if (control is TabControl tab)
            {
                tab.BackColor = ThemeBackground;
                tab.ForeColor = ThemeText;
                tab.Refresh();  // Force immediate redraw of owner-drawn tabs
            }
            else if (control is TabPage tabPage)
            {
                tabPage.BackColor = ThemeBackground;
                tabPage.ForeColor = ThemeText;
            }
            else if (control is DataGridView dgv)
            {
                ApplyDataGridViewStyle(dgv);
            }
            else if (control is TextBox txt)
            {
                txt.BackColor = ThemeGrid;
                txt.ForeColor = ThemeText;
                txt.BorderStyle = _currentTheme == ThemeType.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
            }
            else if (control is ComboBox combo)
            {
                combo.BackColor = ThemeGrid;
                combo.ForeColor = ThemeText;
                combo.FlatStyle = _currentTheme == ThemeType.Dark ? FlatStyle.Flat : FlatStyle.Standard;
            }
            else if (control is Button btn)
            {
                btn.FlatStyle = _currentTheme == ThemeType.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                bool isPrimary = btn.Tag as string == "Primary";
                if (isPrimary)
                {
                    btn.BackColor = ThemeAccent;
                    btn.ForeColor = ThemeAccentText;
                    btn.FlatAppearance.BorderColor = ThemeAccent;
                    btn.FlatAppearance.MouseOverBackColor = ThemeAccentHover;
                }
                else
                {
                    btn.BackColor = ThemePanel;
                    btn.ForeColor = ThemeText;
                    btn.FlatAppearance.BorderColor = ThemeBorder;
                    btn.FlatAppearance.MouseOverBackColor = ThemeGridAlt;
                }
            }
            else if (control is Label lbl)
            {
                lbl.ForeColor = ThemeText;
                if (control == _lblItemName)
                {
                    lbl.ForeColor = ThemeLinkColor;
                    lbl.BackColor = ThemeGrid;
                }
                // Update status labels (those with non-transparent background)
                else if (lbl.BackColor != Color.Transparent && lbl.BackColor != lbl.Parent?.BackColor)
                {
                    lbl.BackColor = ThemePanel;
                }
            }
            else if (control is Panel panel)
            {
                // Check if it's a border panel
                if (panel.Padding == new Padding(1))
                {
                    panel.BackColor = ThemeBorder;
                }
                else
                {
                    panel.BackColor = ThemePanel;
                }
            }
            else if (control is TableLayoutPanel tlp)
            {
                tlp.BackColor = ThemePanel;
            }
            else if (control is FlowLayoutPanel flp)
            {
                flp.BackColor = ThemePanel;
            }
            else if (control is PictureBox pic)
            {
                pic.BackColor = ThemeGrid;
            }

            // Recurse
            if (control.HasChildren)
            {
                ApplyThemeToAllControls(control);
            }
        }
    }

    private void UpdateMenuItemColors(ToolStripMenuItem menuItem)
    {
        var textColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.MenuText;
        menuItem.ForeColor = textColor;
        foreach (ToolStripItem subItem in menuItem.DropDownItems)
        {
            subItem.ForeColor = textColor;
            if (subItem is ToolStripMenuItem subMenuItem)
            {
                UpdateMenuItemColors(subMenuItem);
            }
        }
    }

    private void UpdateThemeMenuChecks()
    {
        if (_menuStrip?.Items[0] is ToolStripMenuItem viewMenu)
        {
            // Find theme menu by searching for menu item with theme items
            foreach (var menuItem in viewMenu.DropDownItems.OfType<ToolStripMenuItem>())
            {
                // Check if this is the theme menu by looking for ThemeType tags
                var hasThemeItems = menuItem.DropDownItems.OfType<ToolStripMenuItem>()
                    .Any(sub => sub.Tag is ThemeType);

                if (hasThemeItems)
                {
                    foreach (var item in menuItem.DropDownItems.OfType<ToolStripMenuItem>())
                    {
                        if (item.Tag is ThemeType theme)
                        {
                            item.Checked = _currentTheme == theme;
                        }
                    }
                    break;
                }
            }
        }
    }

    // Custom menu renderer for dark theme
    private class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
    }

    private class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(60, 60, 70);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 70);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 70);
        public override Color MenuItemBorder => Color.FromArgb(70, 130, 200);
        public override Color MenuBorder => Color.FromArgb(55, 55, 65);
        public override Color ToolStripDropDownBackground => Color.FromArgb(40, 40, 48);
        public override Color ImageMarginGradientBegin => Color.FromArgb(40, 40, 48);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(40, 40, 48);
        public override Color ImageMarginGradientEnd => Color.FromArgb(40, 40, 48);
        public override Color SeparatorDark => Color.FromArgb(55, 55, 65);
        public override Color SeparatorLight => Color.FromArgb(55, 55, 65);
    }

    #region Dark Theme Styling Methods

    private void ApplyTabControlStyle(TabControl tabControl)
    {
        tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabControl.DrawItem += TabControl_DrawItem;
        tabControl.Paint += TabControl_Paint;
        tabControl.Padding = new Point(12, 5);
        tabControl.ItemSize = new Size(180, 30);
    }

    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        var tab = tabControl.TabPages[e.Index];
        var isSelected = e.Index == tabControl.SelectedIndex;
        var bounds = e.Bounds;
        var stripBgColor = _currentTheme == ThemeType.Dark ? ThemeBackground : SystemColors.Control;
        using var stripBgBrush = new SolidBrush(stripBgColor);

        // First, cover the ENTIRE top strip with background color
        e.Graphics.FillRectangle(stripBgBrush, 0, 0, tabControl.Width, 5);
        
        // Cover area to the LEFT of first tab
        if (e.Index == 0)
        {
            e.Graphics.FillRectangle(stripBgBrush, 0, 0, bounds.X + 5, tabControl.ItemSize.Height + 15);
        }

        // Draw the tab (includes extended border coverage)
        if (_currentTheme == ThemeType.Dark)
        {
            DrawDarkThemeTab(e.Graphics, tabControl, bounds, tab.Text, isSelected);
        }
        else
        {
            DrawClassicThemeTab(e.Graphics, tabControl, bounds, tab.Text, isSelected);
        }

        // After drawing tab, fill gap to the RIGHT of this tab
        if (e.Index < tabControl.TabCount - 1)
        {
            var nextBounds = tabControl.GetTabRect(e.Index + 1);
            var gapStart = bounds.Right - 2;
            var gapWidth = nextBounds.X - bounds.Right + 4;
            if (gapWidth > 0)
            {
                e.Graphics.FillRectangle(stripBgBrush, gapStart, 0, gapWidth, tabControl.ItemSize.Height + 15);
            }
        }

        // Fill empty strip area after last tab
        if (e.Index == tabControl.TabCount - 1)
        {
            var emptyAreaX = bounds.Right - 2;
            var emptyAreaWidth = tabControl.Width - bounds.Right + 5;
            if (emptyAreaWidth > 0)
            {
                e.Graphics.FillRectangle(stripBgBrush, emptyAreaX, 0, emptyAreaWidth, tabControl.ItemSize.Height + 15);
            }
        }
        
        // Cover bottom border of tab strip (line between tabs and content)
        e.Graphics.FillRectangle(stripBgBrush, 0, tabControl.ItemSize.Height, tabControl.Width, 15);
    }

    private void DrawDarkThemeTab(Graphics g, TabControl tabControl, Rectangle bounds, string text, bool isSelected)
    {
        Color tabColor = isSelected ? ThemeAccent : ThemeBackground;
        Color textColor = isSelected ? ThemeAccentText : ThemeTextMuted;
        
        // Step 1: Fill VERY LARGE extended area with background color (aggressive border removal)
        using var borderBrush = new SolidBrush(ThemeBackground);
        
        // Cover much larger area to ensure all system borders are hidden
        var extendedArea = new Rectangle(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 12);
        g.FillRectangle(borderBrush, extendedArea);
        
        // Step 2: Fill the actual tab content area with tab color
        using var tabBrush = new SolidBrush(tabColor);
        g.FillRectangle(tabBrush, bounds);

        // Step 3: Draw text
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, tabControl.Font, textBrush, bounds, sf);
    }

    private void DrawClassicThemeTab(Graphics g, TabControl tabControl, Rectangle bounds, string text, bool isSelected)
    {
        Color tabColor = isSelected ? SystemColors.Window : SystemColors.Control;
        Color textColor = SystemColors.ControlText;
        
        // Step 1: Fill VERY LARGE extended area with background color (aggressive border removal)
        using var borderBrush = new SolidBrush(SystemColors.Control);
        var extendedArea = new Rectangle(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 12);
        g.FillRectangle(borderBrush, extendedArea);
        
        // Step 2: Fill the actual tab content area with tab color
        using var tabBrush = new SolidBrush(tabColor);
        g.FillRectangle(tabBrush, bounds);

        // Step 3: Draw text
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, tabControl.Font, textBrush, bounds, sf);
    }


    private void TabControl_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        var borderCoverColor = _currentTheme == ThemeType.Dark ? ThemeBackground : SystemColors.Control;
        using var coverBrush = new SolidBrush(borderCoverColor);

        var tabStripHeight = tabControl.ItemSize.Height;

        // Aggressively cover all edge areas
        
        // Left edge (very wide coverage)
        e.Graphics.FillRectangle(coverBrush, 0, 0, 8, tabStripHeight + 15);

        // Top edge (full width, multiple pixels)
        e.Graphics.FillRectangle(coverBrush, 0, 0, tabControl.Width, 6);

        // Right edge (very wide coverage)
        e.Graphics.FillRectangle(coverBrush, tabControl.Width - 8, 0, 8, tabStripHeight + 15);

        // Bottom of tab strip (wide coverage for border between tabs and content)
        e.Graphics.FillRectangle(coverBrush, 0, tabStripHeight - 2, tabControl.Width, 18);
        
        // Also fill the area before first tab if there's any gap
        if (tabControl.TabCount > 0)
        {
            var firstTabRect = tabControl.GetTabRect(0);
            if (firstTabRect.X > 0)
            {
                e.Graphics.FillRectangle(coverBrush, 0, 0, firstTabRect.X + 3, tabStripHeight + 15);
            }
        }
    }

    private void ApplyTabPageStyle(TabPage tabPage)
    {
        tabPage.BackColor = ThemeBackground;
        tabPage.ForeColor = ThemeText;
    }

    private void ApplyButtonStyle(Button button, bool isPrimary = true)
    {
        button.FlatStyle = _currentTheme == ThemeType.Dark ? FlatStyle.Flat : FlatStyle.Standard;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Bold);
        button.Height = 30;
        button.Tag = isPrimary ? "Primary" : "Secondary";

        if (isPrimary)
        {
            button.BackColor = ThemeAccent;
            button.ForeColor = ThemeAccentText;
            button.FlatAppearance.BorderColor = ThemeAccent;
            button.FlatAppearance.MouseOverBackColor = ThemeAccentHover;
        }
        else
        {
            button.BackColor = ThemePanel;
            button.ForeColor = ThemeText;
            button.FlatAppearance.BorderColor = ThemeBorder;
            button.FlatAppearance.MouseOverBackColor = ThemeGridAlt;
        }
    }

    private void ApplyTextBoxStyle(TextBox textBox)
    {
        // Use slightly brighter background for better visibility in dark theme
        textBox.BackColor = _currentTheme == ThemeType.Dark
            ? Color.FromArgb(50, 50, 60)  // Brighter than grid for contrast
            : ThemeGrid;
        textBox.ForeColor = ThemeText;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Malgun Gothic", _baseFontSize - 2);
    }

    private void ApplyComboBoxStyle(ComboBox comboBox)
    {
        // Use slightly brighter background for better visibility in dark theme
        comboBox.BackColor = _currentTheme == ThemeType.Dark
            ? Color.FromArgb(50, 50, 60)  // Brighter than grid for contrast
            : ThemeGrid;
        comboBox.ForeColor = ThemeText;
        comboBox.FlatStyle = _currentTheme == ThemeType.Dark ? FlatStyle.Flat : FlatStyle.Standard;
        comboBox.Font = new Font("Malgun Gothic", _baseFontSize - 3);
    }

    private void ApplyLabelStyle(Label label, bool isHeader = false)
    {
        label.ForeColor = isHeader ? ThemeText : ThemeTextMuted;
        label.Font = new Font("Malgun Gothic", isHeader ? _baseFontSize - 2 : _baseFontSize - 3, isHeader ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ApplyDataGridViewStyle(DataGridView dgv)
    {
        dgv.BackgroundColor = ThemeGrid;
        dgv.ForeColor = ThemeText;
        dgv.GridColor = ThemeBorder;

        if (_currentTheme == ThemeType.Dark)
        {
            dgv.BorderStyle = BorderStyle.FixedSingle;  // Add outer border
            dgv.CellBorderStyle = DataGridViewCellBorderStyle.Single;  // Full cell borders
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        }
        else
        {
            // Classic theme - more traditional look
            dgv.BorderStyle = BorderStyle.FixedSingle;
            dgv.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgv.EnableHeadersVisualStyles = true;  // Use Windows visual styles for headers
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Raised;
        }

        // Header style - make distinct from data rows
        var headerBackColor = _currentTheme == ThemeType.Dark
            ? Color.FromArgb(55, 55, 68)  // Distinctly brighter than grid
            : ThemePanel;
        dgv.ColumnHeadersDefaultCellStyle.BackColor = headerBackColor;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = ThemeText;
        dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Bold);
        dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = headerBackColor;
        dgv.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        dgv.ColumnHeadersHeight = 35;

        // Cell style
        dgv.DefaultCellStyle.BackColor = ThemeGrid;
        dgv.DefaultCellStyle.ForeColor = ThemeText;
        dgv.DefaultCellStyle.SelectionBackColor = ThemeAccent;
        dgv.DefaultCellStyle.SelectionForeColor = ThemeAccentText;
        dgv.DefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 3);
        dgv.DefaultCellStyle.Padding = new Padding(5, 3, 5, 3);

        // Alternating row style
        dgv.AlternatingRowsDefaultCellStyle.BackColor = ThemeGridAlt;
        dgv.AlternatingRowsDefaultCellStyle.ForeColor = ThemeText;

        dgv.RowTemplate.Height = 28;
    }

    private void ApplyTableLayoutPanelStyle(TableLayoutPanel panel)
    {
        panel.BackColor = ThemeBackground;
    }

    private void ApplyFlowLayoutPanelStyle(FlowLayoutPanel panel)
    {
        panel.BackColor = ThemeBackground;
    }

    private void ApplyDetailTextBoxStyle(TextBox textBox)
    {
        textBox.BackColor = ThemePanel;
        textBox.ForeColor = ThemeText;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Consolas", _baseFontSize - 2);
    }

    private void ApplyStatusLabelStyle(Label label)
    {
        label.ForeColor = ThemeTextMuted;
        label.Font = new Font("Malgun Gothic", _baseFontSize - 3);
        label.BackColor = ThemeBackground;  // Match main background for darker look
        label.Padding = new Padding(10, 0, 0, 0);
    }

    #endregion

    #region Tab 1: Deal Search (GNJOY)

    private void SetupDealTab(TabPage tab)
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        ApplyTableLayoutPanelStyle(mainPanel);

        // Search panel
        var searchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ApplyFlowLayoutPanelStyle(searchPanel);

        var lblServer = new Label { Text = "서버:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) };
        ApplyLabelStyle(lblServer);

        _cboDealServer = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        ApplyComboBoxStyle(_cboDealServer);

        var lblDealType = new Label { Text = "유형:", AutoSize = true, Margin = new Padding(10, 8, 5, 0) };
        ApplyLabelStyle(lblDealType);

        _cboDealType = new ComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboDealType.Items.AddRange(new object[] { "전체", "판매", "구매" });
        _cboDealType.SelectedIndex = 0;
        _cboDealType.SelectedIndexChanged += CboDealType_SelectedIndexChanged;
        ApplyComboBoxStyle(_cboDealType);

        var lblSearch = new Label { Text = "아이템명:", AutoSize = true, Margin = new Padding(10, 8, 5, 0) };
        ApplyLabelStyle(lblSearch);

        _txtDealSearch = new TextBox { Width = 200 };
        _txtDealSearch.KeyDown += TxtDealSearch_KeyDown;
        ApplyTextBoxStyle(_txtDealSearch);

        _btnDealSearch = new Button { Text = "검색", Width = 80, Margin = new Padding(10, 0, 5, 0) };
        _btnDealSearch.Click += BtnDealSearch_Click;
        ApplyButtonStyle(_btnDealSearch, true);

        _btnDealCancel = new Button { Text = "취소", Width = 80, Enabled = false };
        _btnDealCancel.Click += BtnDealCancel_Click;
        ApplyButtonStyle(_btnDealCancel, false);

        searchPanel.Controls.AddRange(new Control[] { lblServer, _cboDealServer, lblDealType, _cboDealType, lblSearch, _txtDealSearch, _btnDealSearch, _btnDealCancel });

        // Results grid
        _dgvDeals = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        ApplyDataGridViewStyle(_dgvDeals);
        _dgvDeals.CellFormatting += DgvDeals_CellFormatting;
        SetupDealGridColumns();
        _dgvDeals.DataSource = _dealBindingSource;

        // Status bar
        _lblDealStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "GNJOY에서 현재 노점 거래를 검색합니다."
        };
        ApplyStatusLabelStyle(_lblDealStatus);

        mainPanel.Controls.Add(searchPanel, 0, 0);
        mainPanel.Controls.Add(_dgvDeals, 0, 1);
        mainPanel.Controls.Add(_lblDealStatus, 0, 2);

        tab.Controls.Add(mainPanel);
    }

    private void SetupDealGridColumns()
    {
        _dgvDeals.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ServerName", HeaderText = "서버", DataPropertyName = "ServerName", Width = 70 },
            new DataGridViewTextBoxColumn { Name = "DealTypeDisplay", HeaderText = "유형", DataPropertyName = "DealTypeDisplay", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "DisplayName", HeaderText = "아이템", DataPropertyName = "DisplayName", FillWeight = 150 },
            new DataGridViewTextBoxColumn { Name = "SlotInfoDisplay", HeaderText = "카드/인챈트", DataPropertyName = "SlotInfoDisplay", FillWeight = 150 },
            new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", DataPropertyName = "Quantity", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "PriceFormatted", HeaderText = "가격", DataPropertyName = "PriceFormatted", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "ShopName", HeaderText = "상점명", DataPropertyName = "ShopName", FillWeight = 100 }
        });

        _dgvDeals.CellDoubleClick += DgvDeals_CellDoubleClick;
    }

    private void LoadServers()
    {
        _cboDealServer.Items.Clear();
        foreach (var server in Server.GetAllServers())
        {
            _cboDealServer.Items.Add(server);
        }
        _cboDealServer.DisplayMember = "Name";
        _cboDealServer.SelectedIndex = 0;
    }

    private void TxtDealSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            BtnDealSearch_Click(sender, e);
        }
    }

    private async void BtnDealSearch_Click(object? sender, EventArgs e)
    {
        var searchText = _txtDealSearch.Text.Trim();
        if (string.IsNullOrEmpty(searchText))
        {
            MessageBox.Show("검색어를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedServer = _cboDealServer.SelectedItem as Server;
        var serverId = selectedServer?.Id ?? -1;

        _cts = new CancellationTokenSource();
        SetDealSearchingState(true);

        try
        {
            _lblDealStatus.Text = "검색 중...";
            _lastSearchTerm = searchText;
            Debug.WriteLine($"[Form1] Searching: '{searchText}', serverId={serverId}");
            var items = await _gnjoyClient.SearchAllItemDealsAsync(searchText, serverId, 10, _cts.Token);
            Debug.WriteLine($"[Form1] Got {items.Count} items (all pages)");

            foreach (var item in items)
            {
                item.ComputeFields();
            }

            _searchResults.Clear();
            _searchResults.AddRange(items);

            _dealBindingSource.ResetBindings(false);

            _lblDealStatus.Text = $"검색 완료: {items.Count}개 결과 - 카드/인챈트 정보 로딩 중...";

            // Background load item details for card/enchant info (like kafra.kr)
            _ = LoadItemDetailsAsync(items, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _lblDealStatus.Text = "검색이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            _lblDealStatus.Text = "오류: " + ex.Message;
            Debug.WriteLine("[Form1] Search error: " + ex.ToString());
            MessageBox.Show("검색 중 오류가 발생했습니다.\n\n" + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetDealSearchingState(false);
        }
    }

    /// <summary>
    /// Load item details (card/enchant info) in background for all items with detail params
    /// This mimics kafra.kr's behavior of showing card/enchant info in the grid
    /// </summary>
    private async Task LoadItemDetailsAsync(List<DealItem> items, CancellationToken cancellationToken)
    {
        var itemsWithDetails = items.Where(i => i.HasDetailParams).ToList();
        if (itemsWithDetails.Count == 0)
        {
            if (!IsDisposed)
            {
                Invoke(() => _lblDealStatus.Text = $"검색 완료: {items.Count}개 결과 (더블클릭으로 상세정보 조회)");
            }
            return;
        }

        Debug.WriteLine($"[Form1] Loading details for {itemsWithDetails.Count} items with detail params");
        var loadedCount = 0;

        // Load details in parallel with throttling (max 5 concurrent requests)
        var semaphore = new SemaphoreSlim(5);
        var tasks = itemsWithDetails.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (cancellationToken.IsCancellationRequested) return;

                var detail = await _gnjoyClient.FetchItemDetailAsync(
                    item.ServerId,
                    item.MapId!.Value,
                    item.Ssi!,
                    cancellationToken);

                if (detail != null)
                {
                    item.ApplyDetailInfo(detail);
                    Interlocked.Increment(ref loadedCount);

                    // Update UI periodically
                    if (loadedCount % 5 == 0 && !IsDisposed)
                    {
                        Invoke(() =>
                        {
                            _dealBindingSource.ResetBindings(false);
                            _lblDealStatus.Text = $"카드/인챈트 로딩: {loadedCount}/{itemsWithDetails.Count}...";
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Form1] Failed to load detail for {item.ItemName}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Form1] Detail loading cancelled");
        }

        // Final UI update
        if (!IsDisposed)
        {
            Invoke(() =>
            {
                _dealBindingSource.ResetBindings(false);
                _lblDealStatus.Text = $"검색 완료: {items.Count}개 결과 ({loadedCount}개 상세정보 로딩됨, 더블클릭으로 상세정보 조회)";
            });
        }
    }

    private void BtnDealCancel_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
    }

    private void SetDealSearchingState(bool searching)
    {
        _btnDealSearch.Enabled = !searching;
        _btnDealCancel.Enabled = searching;
        _txtDealSearch.Enabled = !searching;
        _cboDealServer.Enabled = !searching;
        _cboDealType.Enabled = !searching;
    }

    private void CboDealType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        ApplyDealTypeFilter();
    }

    private void ApplyDealTypeFilter()
    {
        if (_searchResults.Count == 0) return;

        var selectedDealType = _cboDealType.SelectedItem?.ToString();

        List<DealItem> filteredItems;

        if (selectedDealType == "전체" || string.IsNullOrEmpty(selectedDealType))
        {
            filteredItems = _searchResults;
        }
        else if (selectedDealType == "판매")
        {
            filteredItems = _searchResults.Where(i => i.DealType == "sale").ToList();
        }
        else if (selectedDealType == "구매")
        {
            filteredItems = _searchResults.Where(i => i.DealType == "buy").ToList();
        }
        else
        {
            filteredItems = _searchResults;
        }

        _dealBindingSource.DataSource = filteredItems;
        _dealBindingSource.ResetBindings(false);

        var totalCount = _searchResults.Count;
        var filteredCount = filteredItems.Count;

        if (selectedDealType == "전체" || string.IsNullOrEmpty(selectedDealType))
        {
            _lblDealStatus.Text = "검색 완료: " + totalCount.ToString() + "개 결과";
        }
        else
        {
            _lblDealStatus.Text = "검색 완료: " + filteredCount.ToString() + "개 결과 (전체 " + totalCount.ToString() + "개 중 " + selectedDealType + ")";
        }
    }

    private void DgvDeals_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle == null) return;

        var columnName = _dgvDeals.Columns[e.ColumnIndex].Name;

        if (columnName == "DealTypeDisplay" && e.Value != null)
        {
            var value = e.Value.ToString();
            if (value == "판매")
            {
                e.CellStyle.ForeColor = ThemeSaleColor;
            }
            else if (value == "구매")
            {
                e.CellStyle.ForeColor = ThemeBuyColor;
            }
        }
    }

    private void DgvDeals_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var dataSource = _dealBindingSource.DataSource as List<DealItem>;
        if (dataSource == null || e.RowIndex >= dataSource.Count) return;

        var selectedItem = dataSource[e.RowIndex];
        Debug.WriteLine($"[Form1] Opening detail for: {selectedItem.DisplayName}");

        using var detailForm = new ItemDetailForm(selectedItem, _gnjoyClient);
        detailForm.ShowDialog(this);
    }

    #endregion

    #region Tab 2: Item Database (Kafra)

    private void SetupItemTab(TabPage tab)
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Search panel
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content (left-right)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Status bar
        ApplyTableLayoutPanelStyle(mainPanel);

        // Search panel
        var searchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ApplyFlowLayoutPanelStyle(searchPanel);

        var lblType = new Label { Text = "아이템 타입:", AutoSize = true, Margin = new Padding(0, 8, 5, 0) };
        ApplyLabelStyle(lblType);

        _cboItemType = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboItemType.Items.AddRange(new object[] {
            new ItemTypeItem(999, "전체"),
            new ItemTypeItem(0, "힐링 아이템"),
            new ItemTypeItem(2, "사용 아이템"),
            new ItemTypeItem(3, "기타 아이템"),
            new ItemTypeItem(4, "무기"),
            new ItemTypeItem(5, "방어구"),
            new ItemTypeItem(6, "카드"),
            new ItemTypeItem(7, "펫 알"),
            new ItemTypeItem(10, "화살/탄환"),
            new ItemTypeItem(12, "쉐도우 장비")
        });
        _cboItemType.SelectedIndex = 0;
        ApplyComboBoxStyle(_cboItemType);

        var lblSearch = new Label { Text = "아이템명:", AutoSize = true, Margin = new Padding(10, 8, 5, 0) };
        ApplyLabelStyle(lblSearch);

        _txtItemSearch = new TextBox { Width = 200 };
        _txtItemSearch.KeyDown += TxtItemSearch_KeyDown;
        ApplyTextBoxStyle(_txtItemSearch);

        _btnItemSearch = new Button { Text = "검색", Width = 80, Margin = new Padding(10, 0, 0, 0) };
        _btnItemSearch.Click += BtnItemSearch_Click;
        ApplyButtonStyle(_btnItemSearch, true);

        _btnIndexRebuild = new Button { Text = "인덱스 생성", Width = 100, Margin = new Padding(20, 0, 0, 0) };
        _btnIndexRebuild.Click += BtnIndexRebuild_Click;
        ApplyButtonStyle(_btnIndexRebuild, false);

        _btnScanWeapons = new Button { Text = "누락 스캔", Width = 90, Margin = new Padding(5, 0, 0, 0) };
        _btnScanWeapons.Click += BtnScanWeapons_Click;
        ApplyButtonStyle(_btnScanWeapons, false);

        _progressIndex = new ProgressBar
        {
            Width = 150,
            Margin = new Padding(10, 5, 0, 0),
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };

        searchPanel.Controls.AddRange(new Control[] { lblType, _cboItemType, lblSearch, _txtItemSearch, _btnItemSearch, _btnIndexRebuild, _btnScanWeapons, _progressIndex });

        // Content area: Left-Right layout
        var contentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); // Left: Item list
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); // Right: Detail
        ApplyTableLayoutPanelStyle(contentPanel);

        // Left panel: Item list
        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemePanel,
            Padding = new Padding(3),
            Margin = new Padding(0, 0, 5, 0)
        };

        _dgvItems = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        ApplyDataGridViewStyle(_dgvItems);
        SetupItemGridColumns();
        _dgvItems.DataSource = _itemBindingSource;
        _dgvItems.SelectionChanged += DgvItems_SelectionChanged;

        leftPanel.Controls.Add(_dgvItems);

        // Right panel: Name -> Image -> Detail text (vertical layout)
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = ThemePanel,
            Padding = new Padding(0),
            Margin = new Padding(5, 0, 0, 0)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // Name area
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 115)); // Image area
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Detail text area

        // Item name container with border
        var nameContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeBorder,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 3)
        };
        var nameInner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid
        };
        _lblItemName = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            Font = new Font("Segoe UI", _baseFontSize - 1, FontStyle.Bold),
            ForeColor = ThemeLinkColor,
            BackColor = ThemeGrid,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(5, 0, 0, 0)
        };
        nameInner.Controls.Add(_lblItemName);
        nameContainer.Controls.Add(nameInner);

        // Item image container with border
        var imageContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeBorder,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 3)
        };
        var imageInner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid
        };
        _picItemImage = new PictureBox
        {
            Width = 100,
            Height = 100,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeGrid,
            Location = new Point(5, 5)
        };
        imageInner.Controls.Add(_picItemImage);
        imageContainer.Controls.Add(imageInner);

        // Detail text container with border
        var detailContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeBorder,
            Padding = new Padding(1),
            Margin = new Padding(0)
        };
        _txtItemDetail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None
        };
        ApplyDetailTextBoxStyle(_txtItemDetail);
        detailContainer.Controls.Add(_txtItemDetail);

        rightPanel.Controls.Add(nameContainer, 0, 0);
        rightPanel.Controls.Add(imageContainer, 0, 1);
        rightPanel.Controls.Add(detailContainer, 0, 2);

        contentPanel.Controls.Add(leftPanel, 0, 0);
        contentPanel.Controls.Add(rightPanel, 1, 0);

        // Status bar with pagination
        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ApplyFlowLayoutPanelStyle(statusPanel);

        _lblItemStatus = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "kafra.kr 아이템 데이터베이스에서 검색합니다.",
            Margin = new Padding(0, 5, 20, 0)
        };
        ApplyStatusLabelStyle(_lblItemStatus);

        _btnItemPrev = new Button
        {
            Text = "< 이전",
            Width = 70,
            Enabled = false,
            Margin = new Padding(0, 0, 5, 0)
        };
        _btnItemPrev.Click += BtnItemPrev_Click;
        ApplyButtonStyle(_btnItemPrev, false);

        _lblItemPage = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "",
            Margin = new Padding(5, 5, 5, 0)
        };
        ApplyLabelStyle(_lblItemPage);

        _btnItemNext = new Button
        {
            Text = "다음 >",
            Width = 70,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0)
        };
        _btnItemNext.Click += BtnItemNext_Click;
        ApplyButtonStyle(_btnItemNext, false);

        statusPanel.Controls.AddRange(new Control[] { _lblItemStatus, _btnItemPrev, _lblItemPage, _btnItemNext });

        mainPanel.Controls.Add(searchPanel, 0, 0);
        mainPanel.Controls.Add(contentPanel, 0, 1);
        mainPanel.Controls.Add(statusPanel, 0, 2);

        tab.Controls.Add(mainPanel);
    }

    private void SetupItemGridColumns()
    {
        _dgvItems.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ItemConst", HeaderText = "ID", DataPropertyName = "ItemConst", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "ScreenName", HeaderText = "아이템명", DataPropertyName = "ScreenName", FillWeight = 200 },
            new DataGridViewTextBoxColumn { Name = "TypeDisplay", HeaderText = "타입", DataPropertyName = "TypeDisplay", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Slots", HeaderText = "슬롯", DataPropertyName = "Slots", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "WeightDisplay", HeaderText = "무게", DataPropertyName = "WeightDisplay", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "NpcBuyPrice", HeaderText = "NPC구매가", DataPropertyName = "NpcBuyPrice", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "EquipJobsText", HeaderText = "장착 가능", DataPropertyName = "EquipJobsText", FillWeight = 150 }
        });
    }

    private void TxtItemSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            BtnItemSearch_Click(sender, e);
        }
    }

    private async void BtnItemSearch_Click(object? sender, EventArgs e)
    {
        // Reset to first page on new search
        _itemCurrentPage = 0;
        await LoadItemPageAsync();
    }

    private async Task LoadItemPageAsync()
    {
        var searchText = _txtItemSearch.Text.Trim();
        var selectedType = _cboItemType.SelectedItem as ItemTypeItem;
        var itemType = selectedType?.Id ?? 999;

        _btnItemSearch.Enabled = false;
        _btnItemPrev.Enabled = false;
        _btnItemNext.Enabled = false;
        _lblItemStatus.Text = "검색 중...";

        try
        {
            List<KafraItem> items;
            var skip = _itemCurrentPage * ItemPageSize;

            // Use index if loaded, otherwise fallback to API
            if (_itemIndexService.IsLoaded)
            {
                _itemTotalCount = _itemIndexService.CountItems(searchText, itemType);
                items = _itemIndexService.SearchItems(searchText, itemType, skip, ItemPageSize);
                Debug.WriteLine($"[Form1] Index search: '{searchText}' type={itemType} page={_itemCurrentPage} -> {items.Count}/{_itemTotalCount} results");
            }
            else
            {
                // API doesn't support pagination, just get first page
                items = await _kafraClient.SearchItemsAsync(searchText, itemType, ItemPageSize);
                _itemTotalCount = items.Count;
                Debug.WriteLine($"[Form1] API search: '{searchText}' type={itemType} -> {items.Count} results");
            }

            _itemResults.Clear();
            _itemResults.AddRange(items);
            _itemBindingSource.ResetBindings(false);

            // Update pagination UI
            UpdateItemPagination();

            var source = _itemIndexService.IsLoaded ? "(인덱스)" : "(API)";
            var pageInfo = _itemTotalCount > ItemPageSize ? $" (페이지 {_itemCurrentPage + 1})" : "";
            _lblItemStatus.Text = $"검색 완료 {source}: {_itemTotalCount}개 중 {items.Count}개 표시{pageInfo}";

            if (items.Count == 0 && string.IsNullOrEmpty(searchText))
            {
                _lblItemStatus.Text = "아이템 타입을 선택하면 해당 타입의 아이템 목록이 표시됩니다.";
            }
        }
        catch (Exception ex)
        {
            _lblItemStatus.Text = "오류: " + ex.Message;
            Debug.WriteLine($"[Form1] Item search error: {ex}");
        }
        finally
        {
            _btnItemSearch.Enabled = true;
            UpdateItemPaginationButtons();
        }
    }

    private void UpdateItemPagination()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        if (totalPages > 1)
        {
            _lblItemPage.Text = $"{_itemCurrentPage + 1} / {totalPages}";
        }
        else
        {
            _lblItemPage.Text = "";
        }
    }

    private void UpdateItemPaginationButtons()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        _btnItemPrev.Enabled = _itemCurrentPage > 0;
        _btnItemNext.Enabled = _itemCurrentPage < totalPages - 1;
    }

    private async void BtnItemPrev_Click(object? sender, EventArgs e)
    {
        if (_itemCurrentPage > 0)
        {
            _itemCurrentPage--;
            await LoadItemPageAsync();
        }
    }

    private async void BtnItemNext_Click(object? sender, EventArgs e)
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        if (_itemCurrentPage < totalPages - 1)
        {
            _itemCurrentPage++;
            await LoadItemPageAsync();
        }
    }

    private void DgvItems_SelectionChanged(object? sender, EventArgs e)
    {
        if (_dgvItems.SelectedRows.Count == 0) return;

        var selectedIndex = _dgvItems.SelectedRows[0].Index;
        if (selectedIndex < 0 || selectedIndex >= _itemResults.Count) return;

        var item = _itemResults[selectedIndex];

        // Set item name label
        _lblItemName.Text = $"{item.ScreenName} (ID: {item.ItemConst})";

        // Build detail text (without name, since it's shown in label)
        var details = new System.Text.StringBuilder();
        details.AppendLine($"타입: {item.GetTypeDisplayName()}");
        details.AppendLine($"무게: {item.GetFormattedWeight()}");
        details.AppendLine($"슬롯: {item.Slots}");
        details.AppendLine($"NPC 구매가: {item.GetFormattedNpcBuyPrice()}");
        details.AppendLine($"NPC 판매가: {item.GetFormattedNpcSellPrice()}");
        details.AppendLine();
        details.AppendLine("[효과]");
        var itemText = item.ItemText ?? "-";
        // Remove color codes and normalize line breaks for Windows TextBox
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"\^[0-9a-fA-F]{6}_?", "");
        itemText = itemText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        details.AppendLine(itemText);
        if (!string.IsNullOrEmpty(item.EquipJobsText))
        {
            details.AppendLine();
            details.AppendLine($"[장착 가능 직업]");
            var equipJobs = item.EquipJobsText;
            equipJobs = equipJobs.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            details.AppendLine(equipJobs);
        }

        _txtItemDetail.Text = details.ToString();

        // Load item image asynchronously
        _ = LoadItemImageAsync(item.ItemConst);
    }

    private async Task LoadItemImageAsync(int itemId)
    {
        try
        {
            // Clear previous image
            _picItemImage.Image?.Dispose();
            _picItemImage.Image = null;

            var cacheFilePath = Path.Combine(_imageCacheDir, $"{itemId}.png");
            byte[] imageBytes;

            // Check local cache first
            if (File.Exists(cacheFilePath))
            {
                imageBytes = await File.ReadAllBytesAsync(cacheFilePath);
                Debug.WriteLine($"[Form1] Image loaded from cache: {itemId}.png");
            }
            else
            {
                // Download from Divine Pride
                var imageUrl = $"https://static.divine-pride.net/images/items/item/{itemId}.png";
                imageBytes = await _imageHttpClient.GetByteArrayAsync(imageUrl);

                // Save to cache (fire and forget, don't block UI)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await File.WriteAllBytesAsync(cacheFilePath, imageBytes);
                        Debug.WriteLine($"[Form1] Image cached: {itemId}.png");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Form1] Failed to cache image {itemId}: {ex.Message}");
                    }
                });
            }

            using var ms = new MemoryStream(imageBytes);
            var image = Image.FromStream(ms);

            // Check if form is still alive and same item is still selected
            if (!IsDisposed && _dgvItems.SelectedRows.Count > 0)
            {
                var selectedIndex = _dgvItems.SelectedRows[0].Index;
                if (selectedIndex >= 0 && selectedIndex < _itemResults.Count)
                {
                    var selectedItem = _itemResults[selectedIndex];
                    if (selectedItem.ItemConst == itemId)
                    {
                        _picItemImage.Image = image;
                        return;
                    }
                }
            }

            // If not displayed, dispose
            image.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Failed to load image for item {itemId}: {ex.Message}");
            // Keep image blank on error
        }
    }

    private async void BtnIndexRebuild_Click(object? sender, EventArgs e)
    {
        if (_itemIndexService.IsLoading)
        {
            // Cancel current operation
            _indexCts?.Cancel();
            return;
        }

        _indexCts = new CancellationTokenSource();
        _btnIndexRebuild.Text = "취소";
        _btnItemSearch.Enabled = false;
        _progressIndex.Visible = true;
        _progressIndex.Value = 0;

        var progress = new Progress<IndexProgress>(p =>
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateProgress(p)));
            }
            else
            {
                UpdateProgress(p);
            }
        });

        try
        {
            var success = await _itemIndexService.RebuildIndexAsync(progress, _indexCts.Token);

            if (success)
            {
                UpdateIndexStatus();
                MessageBox.Show(
                    $"인덱스 생성 완료!\n\n총 {_itemIndexService.TotalCount:N0}개 아이템이 저장되었습니다.",
                    "완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            _lblItemStatus.Text = "인덱스 생성이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"인덱스 생성 중 오류가 발생했습니다:\n{ex.Message}",
                "오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Debug.WriteLine($"[Form1] Index rebuild error: {ex}");
        }
        finally
        {
            _btnIndexRebuild.Text = "인덱스 생성";
            _btnItemSearch.Enabled = true;
            _progressIndex.Visible = false;
            _indexCts = null;
        }
    }

    private void UpdateProgress(IndexProgress p)
    {
        if (p.IsComplete)
        {
            _progressIndex.Value = 100;
            _lblItemStatus.Text = $"완료: {p.ItemsCollected:N0}개 아이템";
        }
        else if (p.IsCancelled)
        {
            _lblItemStatus.Text = "취소됨";
        }
        else if (p.HasError)
        {
            _lblItemStatus.Text = p.Phase;
        }
        else
        {
            _progressIndex.Value = (int)Math.Min(p.ProgressPercent, 100);
            _lblItemStatus.Text = $"{p.Phase} ({p.CategoryIndex}/{p.TotalCategories}) - {p.ItemsCollected:N0}개 수집됨";
        }
    }

    private async void BtnScanWeapons_Click(object? sender, EventArgs e)
    {
        if (_itemIndexService.IsLoading)
        {
            _indexCts?.Cancel();
            return;
        }

        _indexCts = new CancellationTokenSource();
        _btnScanWeapons.Text = "취소";
        _btnIndexRebuild.Enabled = false;
        _btnItemSearch.Enabled = false;
        _progressIndex.Visible = true;
        _progressIndex.Value = 0;

        var progress = new Progress<IndexProgress>(p =>
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateProgress(p)));
            }
            else
            {
                UpdateProgress(p);
            }
        });

        try
        {
            // Scan weapon ID ranges: 1101-2000, 13000-14999
            _lblItemStatus.Text = "무기/누락 아이템 ID 범위 스캔 중...";

            var found1 = await _itemIndexService.ScanIdRangeAsync(1101, 2000, progress, _indexCts.Token);
            var found2 = 0;
            var found3 = 0;

            if (!_indexCts.Token.IsCancellationRequested)
            {
                found2 = await _itemIndexService.ScanIdRangeAsync(13000, 15000, progress, _indexCts.Token);
            }

            // Also scan some additional ranges for "기타" items
            if (!_indexCts.Token.IsCancellationRequested)
            {
                found3 = await _itemIndexService.ScanIdRangeAsync(21000, 25000, progress, _indexCts.Token);
            }

            var totalFound = found1 + found2 + found3;
            UpdateIndexStatus();

            MessageBox.Show(
                $"스캔 완료!\n\n새로 발견된 아이템: {totalFound:N0}개\n총 인덱스: {_itemIndexService.TotalCount:N0}개",
                "완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _lblItemStatus.Text = "스캔이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"스캔 중 오류가 발생했습니다:\n{ex.Message}",
                "오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Debug.WriteLine($"[Form1] Scan error: {ex}");
        }
        finally
        {
            _btnScanWeapons.Text = "누락 스캔";
            _btnIndexRebuild.Enabled = true;
            _btnItemSearch.Enabled = true;
            _progressIndex.Visible = false;
            _indexCts = null;
        }
    }

    #endregion

    #region Tab 3: Monitoring

    private async Task LoadMonitoringAsync()
    {
        try
        {
            await _monitoringService.LoadConfigAsync();
            UpdateMonitorItemList();
            UpdateMonitorRefreshLabel();

            // Start auto-refresh timer if configured
            if (_monitoringService.Config.RefreshIntervalSeconds > 0)
            {
                _nudRefreshInterval.Value = _monitoringService.Config.RefreshIntervalSeconds;
                StartMonitorTimer(_monitoringService.Config.RefreshIntervalSeconds);
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Monitoring load error: {ex.Message}");
        }
    }

    private void SetupMonitoringTab(TabPage tabPage)
    {
        // Main layout: 2 rows - input bar on top, content below
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); // Input bar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content area
        ApplyTableLayoutPanelStyle(mainLayout);

        // Row 0: Input bar - [아이템명] [서버] [+] [-] | [조회] [간격] [자동갱신] [♪] | [상태]
        var inputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10,
            RowCount = 1,
            Padding = new Padding(0)
        };
        // Layout: [아이템명] [서버] [+][-] | [조회] | [간격] [30] [자동갱신] | [♪] | [상태]
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // 0: Item name (fill remaining)
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));  // 1: Server combo
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));  // 2: Add
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));  // 3: Remove
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));  // 4: Refresh
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));  // 5: Interval label
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));  // 6: Interval NUD
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));  // 7: Auto button (wider for "자동갱신")
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));  // 8: Sound test
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); // 9: Status (wider for long text)
        ApplyTableLayoutPanelStyle(inputPanel);

        _txtMonitorItemName = new TextBox { Dock = DockStyle.Fill };
        ApplyTextBoxStyle(_txtMonitorItemName);
        _txtMonitorItemName.KeyDown += TxtMonitorItemName_KeyDown;

        _cboMonitorServer = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        ApplyComboBoxStyle(_cboMonitorServer);
        foreach (var server in Server.GetAllServers())
            _cboMonitorServer.Items.Add(server);
        _cboMonitorServer.DisplayMember = "Name";
        _cboMonitorServer.SelectedIndex = 0;

        _btnMonitorAdd = new Button { Text = "+", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        ApplyButtonStyle(_btnMonitorAdd);
        _btnMonitorAdd.Click += BtnMonitorAdd_Click;

        _btnMonitorRemove = new Button { Text = "-", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        ApplyButtonStyle(_btnMonitorRemove);
        _btnMonitorRemove.Click += BtnMonitorRemove_Click;

        // Context menu for Remove button - right-click to clear all
        var removeContextMenu = new ContextMenuStrip();
        var clearAllItem = new ToolStripMenuItem("전체 삭제");
        clearAllItem.Click += BtnMonitorClearAll_Click;
        removeContextMenu.Items.Add(clearAllItem);
        _btnMonitorRemove.ContextMenuStrip = removeContextMenu;

        _btnMonitorRefresh = new Button { Text = "조회", Dock = DockStyle.Fill };
        ApplyButtonStyle(_btnMonitorRefresh);
        _btnMonitorRefresh.Click += BtnMonitorRefresh_Click;

        // Interval label
        var lblIntervalLabel = new Label
        {
            Text = "간격",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = ThemeText,
            Padding = new Padding(0, 0, 2, 0)
        };

        _nudRefreshInterval = new NumericUpDown
        {
            Minimum = 5, Maximum = 600, Value = 30,
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid, ForeColor = ThemeText,
            BorderStyle = BorderStyle.FixedSingle
        };

        _btnApplyInterval = new Button { Text = "자동갱신", Dock = DockStyle.Fill };
        ApplyButtonStyle(_btnApplyInterval);
        _btnApplyInterval.Click += BtnApplyInterval_Click;

        // Timer label merged into auto button text
        _lblRefreshSetting = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false };


        var btnSoundTest = new Button { Text = "♪", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        ApplyButtonStyle(btnSoundTest);
        btnSoundTest.Click += (s, e) => System.Media.SystemSounds.Exclamation.Play();

        _lblMonitorStatus = new Label { Text = "대기 중", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        ApplyStatusLabelStyle(_lblMonitorStatus);

        inputPanel.Controls.Add(_txtMonitorItemName, 0, 0);
        inputPanel.Controls.Add(_cboMonitorServer, 1, 0);
        inputPanel.Controls.Add(_btnMonitorAdd, 2, 0);
        inputPanel.Controls.Add(_btnMonitorRemove, 3, 0);
        inputPanel.Controls.Add(_btnMonitorRefresh, 4, 0);
        inputPanel.Controls.Add(lblIntervalLabel, 5, 0);
        inputPanel.Controls.Add(_nudRefreshInterval, 6, 0);
        inputPanel.Controls.Add(_btnApplyInterval, 7, 0);
        inputPanel.Controls.Add(btnSoundTest, 8, 0);
        inputPanel.Controls.Add(_lblMonitorStatus, 9, 0);

        mainLayout.Controls.Add(inputPanel, 0, 0);

        // Row 1: Left-Right layout (30% Items | 70% Results)
        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); // Left: 30%
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); // Right: 70%
        ApplyTableLayoutPanelStyle(contentLayout);

        // Left panel: Item list
        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = ThemePanel, Padding = new Padding(3), Margin = new Padding(0, 0, 3, 0) };
        var lblItemList = new Label { Text = "모니터링 목록", Dock = DockStyle.Top, Height = 22 };
        ApplyLabelStyle(lblItemList);

        _dgvMonitorItems = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = false,  // Allow editing for server column ComboBox
            RowHeadersVisible = false,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            AllowUserToAddRows = false
        };
        ApplyDataGridViewStyle(_dgvMonitorItems);
        SetupMonitorItemsColumns();
        _dgvMonitorItems.DataSource = _monitorItemsBindingSource;

        // Clear default selection after data binding
        _dgvMonitorItems.DataBindingComplete += (s, e) => _dgvMonitorItems.ClearSelection();

        // Suppress DataError exceptions (STA thread issues with ComboBox AutoComplete)
        _dgvMonitorItems.DataError += (s, e) => { e.ThrowException = false; };

        // Commit ComboBox selection immediately when changed
        _dgvMonitorItems.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_dgvMonitorItems.IsCurrentCellDirty && _dgvMonitorItems.CurrentCell is DataGridViewComboBoxCell)
            {
                _dgvMonitorItems.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        // Handle server change
        _dgvMonitorItems.CellValueChanged += DgvMonitorItems_CellValueChanged;

        leftPanel.Controls.Add(_dgvMonitorItems);
        leftPanel.Controls.Add(lblItemList);
        contentLayout.Controls.Add(leftPanel, 0, 0);

        // Right panel: Results
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = ThemePanel, Padding = new Padding(3), Margin = new Padding(3, 0, 0, 0) };
        var lblResults = new Label { Text = "조회 결과", Dock = DockStyle.Top, Height = 22 };
        ApplyLabelStyle(lblResults);

        _dgvMonitorResults = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false
        };
        ApplyDataGridViewStyle(_dgvMonitorResults);
        SetupMonitorResultsColumns();
        _dgvMonitorResults.DataSource = _monitorResultsBindingSource;
        _dgvMonitorResults.CellFormatting += DgvMonitorResults_CellFormatting;

        rightPanel.Controls.Add(_dgvMonitorResults);
        rightPanel.Controls.Add(lblResults);
        contentLayout.Controls.Add(rightPanel, 1, 0);

        mainLayout.Controls.Add(contentLayout, 0, 1);
        tabPage.Controls.Add(mainLayout);

        // Initialize timer
        _monitorTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _monitorTimer.Tick += MonitorTimer_Tick;
    }

    private void SetupMonitorItemsColumns()
    {
        _dgvMonitorItems.Columns.Clear();

        // Apply header style to entire grid
        _dgvMonitorItems.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            Font = new Font(_dgvMonitorItems.Font.FontFamily, 9, FontStyle.Bold),
            BackColor = ThemePanel,
            ForeColor = ThemeText
        };
        _dgvMonitorItems.EnableHeadersVisualStyles = false;

        // Item name column (read-only)
        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName",
            HeaderText = "아이템",
            DataPropertyName = "ItemName",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });

        // Server column (ComboBox for editing)
        var serverColumn = new DataGridViewComboBoxColumn
        {
            Name = "ServerId",
            HeaderText = "서버",
            DataPropertyName = "ServerId",
            Width = 100,
            DataSource = Server.GetAllServers(),
            ValueMember = "Id",
            DisplayMember = "Name",
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        _dgvMonitorItems.Columns.Add(serverColumn);

        // Watch price column (editable text box for price threshold)
        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "WatchPrice",
            HeaderText = "감시가",
            DataPropertyName = "WatchPrice",
            Width = 100,
            DefaultCellStyle = new DataGridViewCellStyle 
            { 
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "N0",
                NullValue = ""
            }
        });
    }

    private async void DgvMonitorItems_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _dgvMonitorItems.Columns[e.ColumnIndex].Name;
        var row = _dgvMonitorItems.Rows[e.RowIndex];
        var itemName = row.Cells["ItemName"].Value?.ToString();

        if (string.IsNullOrEmpty(itemName)) return;

        // Find the MonitorItem
        var items = _monitoringService.Config.Items;
        var item = items.FirstOrDefault(i => i.ItemName == itemName);
        if (item == null) return;

        if (columnName == "ServerId")
        {
            var newServerId = row.Cells["ServerId"].Value as int?;
            if (newServerId == null) return;

            var oldServerId = item.ServerId;
            if (oldServerId == newServerId) return;

            // Update the server in the service
            await _monitoringService.UpdateItemServerAsync(itemName, oldServerId, newServerId.Value);

            // Refresh the binding to update display
            UpdateMonitorItemList();
        }
        else if (columnName == "WatchPrice")
        {
            // Handle WatchPrice change
            var cellValue = row.Cells["WatchPrice"].Value;
            long? newWatchPrice = null;

            if (cellValue != null && cellValue != DBNull.Value)
            {
                // Try to parse the value
                if (cellValue is long lv)
                    newWatchPrice = lv;
                else if (cellValue is int iv)
                    newWatchPrice = iv;
                else if (long.TryParse(cellValue.ToString()?.Replace(",", ""), out var parsed))
                    newWatchPrice = parsed;
            }

            // Update the watch price
            item.WatchPrice = newWatchPrice;

            // Save config
            await _monitoringService.SaveConfigAsync();
        }
    }

    private void SetupMonitorResultsColumns()
    {
        _dgvMonitorResults.Columns.Clear();

        // Apply header style to entire grid
        _dgvMonitorResults.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            Font = new Font(_dgvMonitorResults.Font.FontFamily, 9, FontStyle.Bold),
            BackColor = ThemePanel,
            ForeColor = ThemeText
        };
        _dgvMonitorResults.EnableHeadersVisualStyles = false;

        // Column order: Refine, Grade, ItemName, Server, ...
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Refine",
            HeaderText = "제련",
            Width = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Grade",
            HeaderText = "등급",
            Width = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName",
            HeaderText = "아이템",
            Width = 180
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ServerName",
            HeaderText = "서버",
            Width = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DealCount",
            HeaderText = "수량",
            Width = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LowestPrice",
            HeaderText = "최저가",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "YesterdayAvg",
            HeaderText = "어제평균",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "WeekAvg",
            HeaderText = "주간평균",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PriceDiff",
            HeaderText = "%",
            Width = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "판정",
            Width = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
    }

    private void UpdateMonitorItemList()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateMonitorItemList));
            return;
        }

        var items = _monitoringService.Config.Items.ToList();
        _monitorItemsBindingSource.DataSource = items;
        _monitorItemsBindingSource.ResetBindings(false);
        
        // Clear default first row selection
        _dgvMonitorItems.ClearSelection();
    }

    private void UpdateMonitorResults()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateMonitorResults));
            return;
        }

        var results = _monitoringService.Results.Values.ToList();

        // Unbind DataSource to allow manual row manipulation
        _dgvMonitorResults.DataSource = null;
        _dgvMonitorResults.Rows.Clear();

        var gradeOrder = new Dictionary<string, int> { { "S", 0 }, { "A", 1 }, { "B", 2 }, { "C", 3 }, { "D", 4 } };

        // Helper to get display name from cache or fallback to deal item name
        string GetDisplayName(DealItem deal)
        {
            var itemId = deal.GetEffectiveItemId();
            if (itemId.HasValue && _itemIndexService.IsLoaded)
            {
                var cachedItem = _itemIndexService.GetItemById(itemId.Value);
                if (cachedItem?.ScreenName != null)
                {
                    return cachedItem.ScreenName;
                }
            }
            // Fallback to original item name
            return deal.ItemName;
        }

        // Group by item ID + Refine + Grade + Server
        // This ensures same base item with different refine levels are shown separately
        var groupedDeals = results
            .SelectMany(r => r.Deals.Select(d => new { Deal = d, Result = r }))
            .GroupBy(x => new {
                // Use item ID for grouping if available, otherwise use item name hash
                GroupKey = x.Deal.GetEffectiveItemId()?.ToString() ?? $"name:{x.Deal.ItemName}",
                Refine = x.Deal.Refine ?? 0,
                Grade = x.Deal.Grade ?? "",
                x.Deal.ServerName
            })
            .Select(g => {
                var firstDeal = g.First().Deal;
                var monitorItem = g.First().Result.Item;
                return new
                {
                    // Display name from cache (base item name) or original
                    DisplayName = GetDisplayName(firstDeal),
                    OriginalName = firstDeal.ItemName,
                    ItemId = firstDeal.GetEffectiveItemId(),
                    Refine = g.Key.Refine,
                    Grade = g.Key.Grade,
                    ServerName = g.Key.ServerName,
                    DealCount = g.Count(),
                    LowestPrice = g.Min(x => x.Deal.Price),
                    YesterdayAvg = firstDeal.YesterdayAvgPrice,
                    WeekAvg = firstDeal.Week7AvgPrice,
                    WatchPrice = monitorItem.WatchPrice,
                    Deals = g.Select(x => x.Deal).ToList()
                };
            })
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Refine)
            .ThenBy(x => gradeOrder.TryGetValue(x.Grade, out var g) ? g : 99)
            .ThenBy(x => x.LowestPrice)
            .ToList();

        foreach (var group in groupedDeals)
        {
            var row = _dgvMonitorResults.Rows.Add();
            _dgvMonitorResults.Rows[row].Cells["Refine"].Value = group.Refine > 0 ? $"+{group.Refine}" : "-";
            _dgvMonitorResults.Rows[row].Cells["Grade"].Value = string.IsNullOrEmpty(group.Grade) ? "-" : group.Grade;
            _dgvMonitorResults.Rows[row].Cells["ItemName"].Value = group.DisplayName;
            _dgvMonitorResults.Rows[row].Cells["ServerName"].Value = group.ServerName;
            _dgvMonitorResults.Rows[row].Cells["DealCount"].Value = group.DealCount;
            _dgvMonitorResults.Rows[row].Cells["LowestPrice"].Value = group.LowestPrice.ToString("N0");

            // Check if item has grade - graded items don't have reliable price stats
            var hasGrade = !string.IsNullOrEmpty(group.Grade);

            // Check if price is below watch price threshold (only trigger for WatchPrice-based alerts)
            var belowWatchPrice = group.WatchPrice.HasValue && group.LowestPrice <= group.WatchPrice.Value;

            // For graded items: always show "-" for price averages (no reliable data)
            // For non-graded items: show actual values from API
            if (hasGrade)
            {
                _dgvMonitorResults.Rows[row].Cells["YesterdayAvg"].Value = "-";
                _dgvMonitorResults.Rows[row].Cells["WeekAvg"].Value = "-";
                _dgvMonitorResults.Rows[row].Cells["PriceDiff"].Value = "-";

                // Graded items can still trigger watch price alert
                if (belowWatchPrice)
                    _dgvMonitorResults.Rows[row].Cells["Status"].Value = "득템!";
                else
                    _dgvMonitorResults.Rows[row].Cells["Status"].Value = "-";

                // Store for formatting
                _dgvMonitorResults.Rows[row].Tag = new { 
                    Grade = group.Grade, 
                    Refine = group.Refine,
                    BelowYesterday = false, 
                    BelowWeek = false, 
                    IsBargain = belowWatchPrice 
                };
            }
            else
            {
                // Non-graded items: show price stats and calculate status
                _dgvMonitorResults.Rows[row].Cells["YesterdayAvg"].Value = group.YesterdayAvg?.ToString("N0") ?? "-";
                _dgvMonitorResults.Rows[row].Cells["WeekAvg"].Value = group.WeekAvg?.ToString("N0") ?? "-";

                // Price difference vs week average
                double? priceDiff = null;
                if (group.WeekAvg.HasValue && group.WeekAvg > 0)
                {
                    priceDiff = ((double)group.LowestPrice / group.WeekAvg.Value - 1) * 100;
                    _dgvMonitorResults.Rows[row].Cells["PriceDiff"].Value = priceDiff > 0 ? $"+{priceDiff:F0}%" : $"{priceDiff:F0}%";
                }
                else
                {
                    _dgvMonitorResults.Rows[row].Cells["PriceDiff"].Value = "-";
                }

                // Status based on watch price only
                var belowYesterday = group.YesterdayAvg.HasValue && group.LowestPrice < group.YesterdayAvg;
                var belowWeek = group.WeekAvg.HasValue && group.LowestPrice < group.WeekAvg;

                // Watch price triggers "득템!" status
                if (belowWatchPrice)
                    _dgvMonitorResults.Rows[row].Cells["Status"].Value = "득템!";
                else if (belowYesterday && belowWeek)
                    _dgvMonitorResults.Rows[row].Cells["Status"].Value = "저렴!";
                else if (belowYesterday || belowWeek)
                    _dgvMonitorResults.Rows[row].Cells["Status"].Value = "양호";
                else
                    _dgvMonitorResults.Rows[row].Cells["Status"].Value = "정상";

                // Store for formatting: grade, refine and price comparison info
                _dgvMonitorResults.Rows[row].Tag = new { 
                    Grade = group.Grade, 
                    Refine = group.Refine,
                    BelowYesterday = belowYesterday, 
                    BelowWeek = belowWeek, 
                    IsBargain = belowWatchPrice 
                };
            }
        }

        // Play sound alert only if any item is below watch price
        var hasBargain = groupedDeals.Any(g => g.WatchPrice.HasValue && g.LowestPrice <= g.WatchPrice.Value);
        if (hasBargain)
        {
            System.Media.SystemSounds.Exclamation.Play();
        }

        // Clear default first row selection
        _dgvMonitorResults.ClearSelection();
    }

    private void DgvMonitorResults_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvMonitorResults.Rows.Count) return;

        var row = _dgvMonitorResults.Rows[e.RowIndex];
        var tag = row.Tag;
        if (tag == null) return;

        // Use reflection to get anonymous type properties
        var tagType = tag.GetType();
        var grade = tagType.GetProperty("Grade")?.GetValue(tag) as string ?? "";
        var refine = (int)(tagType.GetProperty("Refine")?.GetValue(tag) ?? 0);
        var belowYesterday = (bool)(tagType.GetProperty("BelowYesterday")?.GetValue(tag) ?? false);
        var belowWeek = (bool)(tagType.GetProperty("BelowWeek")?.GetValue(tag) ?? false);
        var isBargain = (bool)(tagType.GetProperty("IsBargain")?.GetValue(tag) ?? false);

        var columnName = _dgvMonitorResults.Columns[e.ColumnIndex].Name;

        // Refine color coding
        if (columnName == "Refine" && refine > 0)
        {
            e.CellStyle!.ForeColor = refine switch
            {
                >= 15 => Color.FromArgb(255, 100, 100),  // Red for +15 and above
                >= 12 => Color.FromArgb(255, 180, 100),  // Orange for +12-14
                >= 10 => Color.FromArgb(255, 215, 0),    // Gold for +10-11
                >= 7 => Color.FromArgb(100, 180, 255),   // Blue for +7-9
                _ => Color.White                          // White for +1-6
            };
            if (refine >= 10)
            {
                e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
            }
        }

        // Grade color coding
        if (columnName == "Grade" && !string.IsNullOrEmpty(grade) && grade != "-")
        {
            e.CellStyle!.ForeColor = grade switch
            {
                "S" => Color.FromArgb(255, 215, 0),   // Gold
                "A" => Color.FromArgb(200, 150, 255), // Purple
                "B" => Color.FromArgb(100, 180, 255), // Blue
                "C" => Color.FromArgb(100, 255, 100), // Green
                "D" => Color.FromArgb(200, 200, 200), // Gray
                _ => Color.White
            };
            e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        }

        // Highlight price columns based on discount level
        if (columnName == "LowestPrice" || columnName == "PriceDiff" || columnName == "Status")
        {
            if (isBargain)
            {
                // Bargain (-10% or more) - bright red background
                e.CellStyle!.BackColor = Color.FromArgb(180, 40, 40);
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
            }
            else if (belowYesterday && belowWeek)
            {
                // Both below - green
                e.CellStyle!.BackColor = Color.FromArgb(40, 120, 60);
                e.CellStyle.ForeColor = Color.White;
            }
            else if (belowYesterday || belowWeek)
            {
                // One below - yellow
                e.CellStyle!.BackColor = Color.FromArgb(120, 100, 40);
                e.CellStyle.ForeColor = Color.White;
            }
        }
    }

    private void UpdateMonitorRefreshLabel()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateMonitorRefreshLabel));
            return;
        }

        var interval = _monitoringService.Config.RefreshIntervalSeconds;
        if (interval > 0)
        {
            _btnApplyInterval.Text = $"중지 ({interval}s)";
            _btnApplyInterval.ForeColor = ThemeSaleColor;
        }
        else
        {
            _btnApplyInterval.Text = "자동갱신";
            _btnApplyInterval.ForeColor = ThemeText;
        }
    }

    private void StartMonitorTimer(int seconds)
    {
        _monitorTimer.Stop();
        if (seconds > 0)
        {
            _monitorTimer.Interval = seconds * 1000;
            _monitorTimer.Start();
            Debug.WriteLine($"[Form1] Monitor timer started: {seconds}s interval");
        }
    }

    private void StopMonitorTimer()
    {
        _monitorTimer.Stop();
        Debug.WriteLine("[Form1] Monitor timer stopped");
    }

    // Event Handlers

    private void TxtMonitorItemName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            BtnMonitorAdd_Click(sender, e);
        }
    }

    private async void BtnMonitorAdd_Click(object? sender, EventArgs e)
    {
        var itemName = _txtMonitorItemName.Text.Trim();
        if (string.IsNullOrEmpty(itemName))
        {
            MessageBox.Show("아이템명을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var server = _cboMonitorServer.SelectedItem as Server;
        var serverId = server?.Id ?? -1;

        var added = await _monitoringService.AddItemAsync(itemName, serverId);
        if (added)
        {
            _txtMonitorItemName.Clear();
            UpdateMonitorItemList();
            _lblMonitorStatus.Text = $"'{itemName}' 추가됨";
        }
        else
        {
            MessageBox.Show("이미 등록된 아이템입니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void BtnMonitorRemove_Click(object? sender, EventArgs e)
    {
        if (_dgvMonitorItems.SelectedRows.Count == 0)
        {
            MessageBox.Show("삭제할 아이템을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var row = _dgvMonitorItems.SelectedRows[0];
        var item = row.DataBoundItem as MonitorItem;
        if (item == null) return;

        var result = MessageBox.Show(
            $"'{item.ItemName}'을(를) 삭제하시겠습니까?",
            "삭제 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            await _monitoringService.RemoveItemAsync(item.ItemName, item.ServerId);
            UpdateMonitorItemList();
            UpdateMonitorResults();
            _lblMonitorStatus.Text = $"'{item.ItemName}' 삭제됨";
        }
    }

    private async void BtnMonitorClearAll_Click(object? sender, EventArgs e)
    {
        var itemCount = _monitoringService.ItemCount;
        if (itemCount == 0)
        {
            MessageBox.Show("삭제할 아이템이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"모니터링 목록의 모든 아이템({itemCount}개)을 삭제하시겠습니까?",
            "전체 삭제 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            await _monitoringService.ClearAllItemsAsync();
            UpdateMonitorItemList();
            UpdateMonitorResults();
            _lblMonitorStatus.Text = $"모든 아이템({itemCount}개) 삭제됨";
        }
    }

    private async void BtnMonitorRefresh_Click(object? sender, EventArgs e)
    {
        await RefreshMonitoringAsync();
    }

    private async Task RefreshMonitoringAsync()
    {
        Debug.WriteLine($"[Form1] RefreshMonitoringAsync called, ItemCount={_monitoringService.ItemCount}");

        if (_monitoringService.ItemCount == 0)
        {
            _lblMonitorStatus.Text = "모니터링할 아이템이 없습니다";
            return;
        }

        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();

        _btnMonitorRefresh.Enabled = false;
        _btnMonitorAdd.Enabled = false;
        _btnMonitorRemove.Enabled = false;

        try
        {
            var progress = new Progress<MonitorProgress>(p =>
            {
                _lblMonitorStatus.Text = $"{p.Phase}: {p.CurrentItem} ({p.CurrentIndex}/{p.TotalItems})";
            });

            await _monitoringService.RefreshAllAsync(progress, _monitorCts.Token);

            var resultCount = _monitoringService.Results.Count;
            Debug.WriteLine($"[Form1] RefreshMonitoringAsync complete, Results count={resultCount}");

            UpdateMonitorResults();
            _lblMonitorStatus.Text = $"조회 완료 ({DateTime.Now:HH:mm:ss}) - {resultCount}건";

            // Check for good deals
            var goodDeals = _monitoringService.GetGoodDeals();
            if (goodDeals.Count > 0)
            {
                _lblMonitorStatus.Text = $"저렴한 매물 {goodDeals.Count}건 발견! ({DateTime.Now:HH:mm:ss})";
            }
        }
        catch (OperationCanceledException)
        {
            _lblMonitorStatus.Text = "조회 취소됨";
        }
        catch (Exception ex)
        {
            _lblMonitorStatus.Text = $"오류: {ex.Message}";
            Debug.WriteLine($"[Form1] Monitor refresh error: {ex}");
        }
        finally
        {
            _btnMonitorRefresh.Enabled = true;
            _btnMonitorAdd.Enabled = true;
            _btnMonitorRemove.Enabled = true;
        }
    }

    private async void BtnApplyInterval_Click(object? sender, EventArgs e)
    {
        var currentInterval = _monitoringService.Config.RefreshIntervalSeconds;

        // Toggle: if running, stop; otherwise start with new value
        if (currentInterval > 0)
        {
            // Stop auto-refresh
            await _monitoringService.SetRefreshIntervalAsync(0);
            StopMonitorTimer();
            _lblMonitorStatus.Text = "자동 갱신 중지";
        }
        else
        {
            // Start auto-refresh
            var seconds = (int)_nudRefreshInterval.Value;
            if (seconds < 5) seconds = 5; // Minimum 5 seconds
            await _monitoringService.SetRefreshIntervalAsync(seconds);
            StartMonitorTimer(seconds);
            _lblMonitorStatus.Text = $"자동 갱신 시작: {seconds}초";
        }

        UpdateMonitorRefreshLabel();
    }


    private async void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        Debug.WriteLine("[Form1] Monitor timer tick");
        await RefreshMonitoringAsync();
    }

    #endregion

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _indexCts?.Cancel();
        _monitorCts?.Cancel();
        _monitorTimer?.Stop();
        _monitorTimer?.Dispose();
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
}

// Theme types
public enum ThemeType
{
    Dark,
    Classic
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
