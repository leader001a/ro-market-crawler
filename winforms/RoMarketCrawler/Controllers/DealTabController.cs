using System.Diagnostics;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Exceptions;
using RoMarketCrawler.Forms;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Controllers;

/// <summary>
/// Controller for the Deal Search Tab (GNJOY API)
/// Server-side pagination: API returns 10 items per page
/// </summary>
public class DealTabController : BaseTabController
{
    #region Constants

    private const int MaxSearchHistoryCount = 10;
    private const int DetailRequestDelayMs = 1000;  // 1 second per item (to avoid rate limiting)

    // Item types that have enchant/card/random options (무기, 방어구, 쉐도우, 의상)
    private static readonly HashSet<int> EquipmentItemTypes = new() { 4, 5, 19, 20 };

    #endregion

    #region Services

    private readonly IGnjoyClient _gnjoyClient;
    private readonly IItemIndexService _itemIndexService;
    private readonly IMonitoringService _monitoringService;
    private readonly ISettingsService _settingsService;

    #endregion

    #region UI Controls

    private TextBox _txtDealSearch = null!;
    private ComboBox _cboDealServer = null!;
    private ToolStripComboBox _cboServerToolStrip = null!;
    private ToolStripButton _btnDealSearchToolStrip = null!;
    private ToolStripProgressBar _progressDealSearch = null!;
    private DataGridView _dgvDeals = null!;
    private Label _lblDealStatus = null!;
    private FlowLayoutPanel _pnlSearchHistory = null!;
    private RoundedButton _btnFirst = null!;      // <<
    private RoundedButton _btnPrev100 = null!;    // -50
    private RoundedButton _btnPrev10 = null!;     // -10
    private RoundedButton _btnDealPrev = null!;   // <
    private RoundedButton _btnDealNext = null!;   // >
    private RoundedButton _btnNext10 = null!;     // +10
    private RoundedButton _btnNext100 = null!;    // +50
    private RoundedButton _btnLast = null!;       // >>
    private Label _lblDealPage = null!;
    private FlowLayoutPanel _pnlPagination = null!;
    private TableLayoutPanel _mainPanel = null!;

    // Auto-search UI controls
    private FlowLayoutPanel _pnlAutoSearch = null!;
    private TextBox _txtAutoSearchFilter = null!;
    private TextBox _txtAutoSearchStartPage = null!;
    private TextBox _txtAutoSearchEndPage = null!;
    private RoundedButton _btnAutoSearch = null!;
    private RoundedButton _btnAutoSearchCancel = null!;
    private RoundedButton _btnAutoSearchResults = null!;
    private Label _lblAutoSearchStatus = null!;
    private ProgressBar _autoSearchProgressBar = null!;

    #endregion

    #region State

    private readonly List<DealItem> _searchResults = new();
    private readonly BindingSource _dealBindingSource;
    private CancellationTokenSource? _cts;
    private string _lastSearchTerm = string.Empty;
    private int _currentSearchId = 0;
    private List<string> _dealSearchHistory = new();
    private int _dealCurrentPage = 1;
    private int _totalCount = 0;
    private int _totalPages = 0;
    private const int ItemsPerPage = 10;
    private const int MaxVisiblePages = 10;

    // Link hit areas for SlotAndOptionsDisplay column
    private readonly Dictionary<(int row, int col), List<(Rectangle rect, string itemName)>> _linkHitAreas = new();
    private string? _hoveredLinkItem = null;

    // Auto-search state
    private const int AutoSearchItemDelayMs = 1000;  // 1 second per item (to avoid rate limiting)
    private const int AutoSearchPageDelayMs = 5000;  // 5 seconds between pages
    private const int AutoSearchMaxPages = 1000; // Safety limit
    private const string AdminAccessCode = "admin access";  // Secret code to unlock auto-search
    private CancellationTokenSource? _autoSearchCts;
    private bool _isAutoSearching = false;
    private bool _isAdminAccessEnabled = false;  // Hidden feature unlock state
    private AutoSearchResultsForm? _autoSearchResultsForm;
    private string _autoSearchDataDirectory = string.Empty;

    #endregion

    #region Events

    /// <summary>
    /// Raised when search history is modified
    /// </summary>
    public event EventHandler<List<string>>? SearchHistoryChanged;

    /// <summary>
    /// Raised when an item detail form needs to be shown
    /// </summary>
    public event EventHandler<DealItem>? ShowItemDetail;

    #endregion

    /// <inheritdoc/>
    public override string TabName => "노점조회";

    public DealTabController(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _gnjoyClient = GetService<IGnjoyClient>();
        _itemIndexService = GetService<IItemIndexService>();
        _monitoringService = GetService<IMonitoringService>();
        _settingsService = GetService<ISettingsService>();
        _dealBindingSource = new BindingSource { DataSource = _searchResults };
    }

    /// <summary>
    /// Set search history (loaded from settings)
    /// </summary>
    public void SetSearchHistory(List<string> history)
    {
        _dealSearchHistory = history ?? new List<string>();
        if (_pnlSearchHistory != null)
        {
            UpdateSearchHistoryPanel();
        }
    }

    /// <summary>
    /// Load search history from settings (alias for SetSearchHistory)
    /// </summary>
    public void LoadSearchHistory(List<string> history) => SetSearchHistory(history);

    /// <summary>
    /// Set watermark image for the DataGridView
    /// </summary>
    public void SetWatermark(Image watermark) => ApplyWatermark(_dgvDeals, watermark);

    /// <summary>
    /// Set rate limit UI state (enable/disable controls)
    /// </summary>
    public void SetRateLimitState(bool isRateLimited)
    {
        _btnDealSearchToolStrip.Enabled = !isRateLimited;
        _txtDealSearch.Enabled = !isRateLimited;
        _cboDealServer.Enabled = !isRateLimited;

        // Disable all navigation buttons
        _btnFirst.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnPrev100.Enabled = !isRateLimited && _dealCurrentPage > 50;
        _btnPrev10.Enabled = !isRateLimited && _dealCurrentPage > 10;
        _btnDealPrev.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !isRateLimited && _dealCurrentPage < _totalPages;
        _btnNext10.Enabled = !isRateLimited && _dealCurrentPage + 10 <= _totalPages;
        _btnNext100.Enabled = !isRateLimited && _dealCurrentPage + 50 <= _totalPages;
        _btnLast.Enabled = !isRateLimited && _dealCurrentPage < _totalPages;

        // Disable page number buttons
        foreach (Control ctrl in _pnlPagination.Controls)
        {
            if (ctrl.Tag is string s && (s == "PageButton" || s == "NavButton"))
            {
                if (isRateLimited) ctrl.Enabled = false;
            }
        }

        // Disable search history links
        if (_pnlSearchHistory != null)
        {
            foreach (Control ctrl in _pnlSearchHistory.Controls)
            {
                if (ctrl is Label lbl && lbl.Tag is ValueTuple<string, string> tag && tag.Item1 == "SearchHistoryLink")
                {
                    lbl.Enabled = !isRateLimited;
                    lbl.ForeColor = isRateLimited ? _colors.TextMuted : _colors.Accent;
                    lbl.Cursor = isRateLimited ? Cursors.Default : Cursors.Hand;
                }
            }
        }

        if (!isRateLimited)
        {
            ClearRateLimitStatus();
        }
    }

    /// <summary>
    /// Update rate limit status message
    /// </summary>
    public void UpdateRateLimitStatus(string message) => ShowRateLimitStatus(message);

    /// <summary>
    /// Get current search history
    /// </summary>
    public List<string> GetSearchHistory() => _dealSearchHistory;

    /// <summary>
    /// Get the search textbox for autocomplete attachment
    /// </summary>
    public TextBox GetSearchTextBox() => _txtDealSearch;

    /// <inheritdoc/>
    public override void Initialize()
    {
        var scale = _baseFontSize / 12f;

        // Set data directory for auto-search results
        _autoSearchDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RoMarketCrawler");
        Directory.CreateDirectory(_autoSearchDataDirectory);

        _mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10)
        };
        // Clear any default row styles and set our own
        _mainPanel.RowStyles.Clear();
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(32 * scale)));   // Row 0: Toolbar
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // Row 1: Search history (dynamic)
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(50 * scale)));   // Row 2: Auto-search panel
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Row 3: Results grid
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(55 * scale)));   // Row 4: Pagination
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(28 * scale)));   // Row 5: Status
        ApplyTableLayoutPanelStyle(_mainPanel);

        // ToolStrip-based toolbar
        var toolStrip = CreateToolStrip();

        // Search history panel
        _pnlSearchHistory = CreateSearchHistoryPanel();

        // Auto-search panel (hidden by default, unlocked with admin access code)
        _pnlAutoSearch = CreateAutoSearchPanel();
        _pnlAutoSearch.Visible = false;

        // Results grid
        _dgvDeals = CreateResultsGrid();

        // Pagination panel
        var paginationPanel = CreatePaginationPanel();

        // Status bar
        _lblDealStatus = CreateStatusLabel();
        _lblDealStatus.Text = "GNJOY에서 현재 노점 거래를 검색합니다.";

        _mainPanel.Controls.Add(toolStrip, 0, 0);
        _mainPanel.Controls.Add(_pnlSearchHistory, 0, 1);
        _mainPanel.Controls.Add(_pnlAutoSearch, 0, 2);
        _mainPanel.Controls.Add(_dgvDeals, 0, 3);
        _mainPanel.Controls.Add(paginationPanel, 0, 4);
        _mainPanel.Controls.Add(_lblDealStatus, 0, 5);

        _tabPage.Controls.Add(_mainPanel);

        // Initial update of search history UI
        UpdateSearchHistoryPanel();

        // Initially disable auto-search until a search is performed
        SetAutoSearchEnabled(false);
    }

    #region UI Creation

    private ToolStrip CreateToolStrip()
    {
        var scale = _baseFontSize / 12f;

        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        // Server combo - width scales with font size
        _cboServerToolStrip = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = (int)(100 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "서버 선택"
        };
        foreach (var server in Server.GetAllServers())
            _cboServerToolStrip.Items.Add(server);
        _cboServerToolStrip.ComboBox.DisplayMember = "Name";
        _cboServerToolStrip.SelectedIndex = 0;
        _cboDealServer = _cboServerToolStrip.ComboBox;
        _cboDealServer.SelectedIndexChanged += (s, e) =>
        {
            // Disable auto-search when server changes (results would be for different server)
            SetAutoSearchEnabled(false);
        };

        // Search text
        var txtSearch = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 250,
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "검색할 아이템명 입력"
        };
        txtSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = SearchAsync();
            }
        };
        _txtDealSearch = txtSearch.TextBox;
        _txtDealSearch.TextChanged += (s, e) =>
        {
            // Disable auto-search when search text is modified
            SetAutoSearchEnabled(false);
        };

        // Search button
        var btnSearch = new ToolStripButton
        {
            Text = "검색",
            BackColor = _colors.Accent,
            ForeColor = _colors.AccentText,
            ToolTipText = "검색 실행"
        };
        btnSearch.Click += async (s, e) => await SearchAsync();
        _btnDealSearchToolStrip = btnSearch;

        // Progress bar
        _progressDealSearch = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visible = false,
            Alignment = ToolStripItemAlignment.Right,
            Size = new Size(120, 16)
        };

        toolStrip.Items.Add(_cboServerToolStrip);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(txtSearch);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnSearch);
        toolStrip.Items.Add(_progressDealSearch);

        return toolStrip;
    }

    private FlowLayoutPanel CreateSearchHistoryPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = _colors.Panel,
            Padding = new Padding(5, 2, 5, 2),
            Margin = new Padding(0)
        };

        // Add "최근검색:" label
        var lblHistoryTitle = new Label
        {
            Text = "최근검색:",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            Margin = new Padding(0, 3, 5, 0)
        };
        panel.Controls.Add(lblHistoryTitle);

        return panel;
    }

    private FlowLayoutPanel CreateAutoSearchPanel()
    {
        var scale = _baseFontSize / 12f;

        var flowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            BackColor = _colors.Panel,
            Padding = new Padding(5, 8, 5, 5)
        };

        // Label: 자동검색
        var lblTitle = new Label
        {
            Text = "옵션검색:",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            Margin = new Padding(0, 3, 5, 0)
        };

        // Filter text input
        _txtAutoSearchFilter = new TextBox
        {
            Width = (int)(150 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 5, 0)
        };
        _txtAutoSearchFilter.PlaceholderText = "카드/인챈트/옵션";

        // Start page label and input
        var lblStartPage = new Label
        {
            Text = "시작:",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            Margin = new Padding(10, 3, 3, 0)
        };

        _txtAutoSearchStartPage = new TextBox
        {
            Width = (int)(45 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 5, 0),
            TextAlign = HorizontalAlignment.Center
        };
        _txtAutoSearchStartPage.PlaceholderText = "1";

        // End page label and input
        var lblEndPage = new Label
        {
            Text = "끝:",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            Margin = new Padding(5, 3, 3, 0)
        };

        _txtAutoSearchEndPage = new TextBox
        {
            Width = (int)(45 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 5, 0),
            TextAlign = HorizontalAlignment.Center
        };
        _txtAutoSearchEndPage.PlaceholderText = "∞";
        _txtAutoSearchEndPage.Leave += (s, e) =>
        {
            // Validate and clamp end page to total pages
            if (_totalPages > 0 && !string.IsNullOrWhiteSpace(_txtAutoSearchEndPage.Text))
            {
                if (int.TryParse(_txtAutoSearchEndPage.Text.Trim(), out int endPage))
                {
                    if (endPage > _totalPages)
                    {
                        _txtAutoSearchEndPage.Text = _totalPages.ToString();
                    }
                }
            }
        };

        // Auto-search button
        _btnAutoSearch = new RoundedButton
        {
            Text = "자동검색",
            BackColor = _colors.Accent,
            ForeColor = _colors.AccentText,
            Width = (int)(65 * scale),
            Height = (int)(22 * scale),
            Margin = new Padding(10, 0, 3, 0),
            CornerRadius = 3
        };
        _btnAutoSearch.Click += async (s, e) => await StartAutoSearchAsync();

        // Cancel button
        _btnAutoSearchCancel = new RoundedButton
        {
            Text = "중지",
            BackColor = Color.FromArgb(200, 80, 80),
            ForeColor = Color.White,
            Width = (int)(45 * scale),
            Height = (int)(22 * scale),
            Margin = new Padding(0, 0, 3, 0),
            CornerRadius = 3,
            Enabled = false
        };
        _btnAutoSearchCancel.Click += (s, e) => CancelAutoSearch();

        // Results button
        _btnAutoSearchResults = new RoundedButton
        {
            Text = "결과",
            BackColor = Color.FromArgb(80, 160, 100),
            ForeColor = Color.White,
            Width = (int)(45 * scale),
            Height = (int)(22 * scale),
            Margin = new Padding(0, 0, 5, 0),
            CornerRadius = 3
        };
        _btnAutoSearchResults.Click += (s, e) => ShowAutoSearchResults();

        // Progress bar
        _autoSearchProgressBar = new ProgressBar
        {
            Width = (int)(100 * scale),
            Height = (int)(18 * scale),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visible = false,
            Margin = new Padding(5, 2, 0, 0),
            Style = ProgressBarStyle.Continuous
        };

        // Status label
        _lblAutoSearchStatus = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            Margin = new Padding(5, 3, 0, 0)
        };

        flowPanel.Controls.Add(lblTitle);
        flowPanel.Controls.Add(_txtAutoSearchFilter);
        flowPanel.Controls.Add(lblStartPage);
        flowPanel.Controls.Add(_txtAutoSearchStartPage);
        flowPanel.Controls.Add(lblEndPage);
        flowPanel.Controls.Add(_txtAutoSearchEndPage);
        flowPanel.Controls.Add(_btnAutoSearch);
        flowPanel.Controls.Add(_btnAutoSearchCancel);
        flowPanel.Controls.Add(_btnAutoSearchResults);
        flowPanel.Controls.Add(_autoSearchProgressBar);
        flowPanel.Controls.Add(_lblAutoSearchStatus);

        return flowPanel;
    }

    private DataGridView CreateResultsGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        };
        ApplyDataGridViewStyle(dgv);

        // Custom cell painting for header and SlotAndOptionsDisplay column
        dgv.CellPainting += DgvDeals_CellPainting;

        dgv.CellFormatting += DgvDeals_CellFormatting;
        SetupDealGridColumns(dgv);
        dgv.DataSource = _dealBindingSource;

        // Context menu for deals grid
        var dealContextMenu = new ContextMenuStrip();
        var addToMonitorItem = new ToolStripMenuItem("모니터링 추가");
        addToMonitorItem.Click += DealContextMenu_AddToMonitor;
        dealContextMenu.Items.Add(addToMonitorItem);
        dgv.ContextMenuStrip = dealContextMenu;

        // Handle right-click to select row before showing context menu
        // Also prevent selection on SlotAndOptionsDisplay column
        dgv.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                dgv.ClearSelection();
                dgv.Rows[e.RowIndex].Selected = true;
            }
            // Prevent selection on SlotAndOptionsDisplay column (blue background makes links hard to read)
            else if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                var colName = dgv.Columns[e.ColumnIndex].Name;
                if (colName == "SlotAndOptionsDisplay")
                {
                    // Clear any existing selection of this cell after a brief delay
                    dgv.BeginInvoke(new Action(() =>
                    {
                        if (e.RowIndex < dgv.Rows.Count && e.ColumnIndex < dgv.Columns.Count)
                        {
                            dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = false;
                        }
                    }));
                }
            }
        };

        // Mouse move for link hover effect
        dgv.CellMouseMove += DgvDeals_CellMouseMove;
        dgv.CellMouseLeave += (s, e) =>
        {
            if (_hoveredLinkItem != null)
            {
                _hoveredLinkItem = null;
                dgv.Cursor = Cursors.Default;
            }
        };

        dgv.CellDoubleClick += DgvDeals_CellDoubleClick;
        dgv.CellMouseClick += DgvDeals_CellMouseClick;

        return dgv;
    }

    private void SetupDealGridColumns(DataGridView dgv)
    {
        // Column widths: 서버10%, 유형10%, 아이템30%, 카드/인챈트/랜덤옵션20%, 수량10%, 가격10%, 상점명10%
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn
            {
                Name = "ServerName",
                HeaderText = "서버",
                DataPropertyName = "ServerName",
                MinimumWidth = 50,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 10,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "DealTypeDisplay",
                HeaderText = "유형",
                DataPropertyName = "DealTypeDisplay",
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 10,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "DisplayName",
                HeaderText = "아이템",
                DataPropertyName = "DisplayName",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 30
            },
            new DataGridViewTextBoxColumn
            {
                Name = "SlotAndOptionsDisplay",
                HeaderText = "카드/인챈트/랜덤옵션",
                DataPropertyName = "SlotAndOptionsDisplay",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 20,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "수량",
                DataPropertyName = "Quantity",
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 10,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "PriceFormatted",
                HeaderText = "가격",
                DataPropertyName = "PriceFormatted",
                MinimumWidth = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 10,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ShopName",
                HeaderText = "상점명",
                DataPropertyName = "ShopName",
                MinimumWidth = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 10
            }
        });
    }

    private FlowLayoutPanel CreatePaginationPanel()
    {
        _pnlPagination = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Panel,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        // First page button (<<)
        _btnFirst = new RoundedButton
        {
            Text = "<<",
            Size = new Size(36, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 2, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnFirst, false);
        _btnFirst.Click += async (s, e) => await GoToPageAsync(1);

        // Previous 100 pages button (-50)
        _btnPrev100 = new RoundedButton
        {
            Text = "-50",
            Size = new Size(44, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 2, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnPrev100, false);
        _btnPrev100.Click += async (s, e) => await GoToPageAsync(_dealCurrentPage - 50);

        // Previous 10 pages button (-10)
        _btnPrev10 = new RoundedButton
        {
            Text = "-10",
            Size = new Size(40, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 2, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnPrev10, false);
        _btnPrev10.Click += async (s, e) => await GoToPageAsync(_dealCurrentPage - 10);

        // Previous page button (<)
        _btnDealPrev = new RoundedButton
        {
            Text = "<",
            Size = new Size(32, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 5, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnDealPrev, false);
        _btnDealPrev.Click += async (s, e) => await PreviousPageAsync();

        // Next page button (>)
        _btnDealNext = new RoundedButton
        {
            Text = ">",
            Size = new Size(32, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnDealNext, false);
        _btnDealNext.Click += async (s, e) => await NextPageAsync();

        // Next 10 pages button (+10)
        _btnNext10 = new RoundedButton
        {
            Text = "+10",
            Size = new Size(40, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(2, 0, 0, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnNext10, false);
        _btnNext10.Click += async (s, e) => await GoToPageAsync(_dealCurrentPage + 10);

        // Next 100 pages button (+50)
        _btnNext100 = new RoundedButton
        {
            Text = "+50",
            Size = new Size(44, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(2, 0, 0, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnNext100, false);
        _btnNext100.Click += async (s, e) => await GoToPageAsync(_dealCurrentPage + 50);

        // Last page button (>>)
        _btnLast = new RoundedButton
        {
            Text = ">>",
            Size = new Size(36, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(2, 0, 10, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnLast, false);
        _btnLast.Click += async (s, e) => await GoToPageAsync(_totalPages);

        // Total count label (will be updated dynamically)
        _lblDealPage = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(5, 5, 0, 0)
        };

        // Add controls in order: [<<] [-50] [-10] [<] [pages] [>] [+10] [+50] [>>] [label]
        _pnlPagination.Controls.Add(_btnFirst);
        _pnlPagination.Controls.Add(_btnPrev100);
        _pnlPagination.Controls.Add(_btnPrev10);
        _pnlPagination.Controls.Add(_btnDealPrev);
        // Page number buttons will be added dynamically here
        _pnlPagination.Controls.Add(_btnDealNext);
        _pnlPagination.Controls.Add(_btnNext10);
        _pnlPagination.Controls.Add(_btnNext100);
        _pnlPagination.Controls.Add(_btnLast);
        _pnlPagination.Controls.Add(_lblDealPage);

        // Center alignment by handling resize
        _pnlPagination.Resize += (s, e) => CenterPaginationPanel();

        return _pnlPagination;
    }

    private void ApplyRoundedButtonStyle(RoundedButton button, bool isPrimary)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            if (isPrimary)
            {
                button.ApplyPrimaryStyle(
                    _colors.Accent,
                    _colors.AccentHover,
                    ControlPaint.Dark(_colors.Accent, 0.1f),
                    _colors.AccentText);
            }
            else
            {
                button.ApplySecondaryStyle(
                    _colors.Panel,
                    _colors.GridAlt,
                    ControlPaint.Dark(_colors.Panel, 0.1f),
                    _colors.Text,
                    _colors.Border);
            }
        }
        else
        {
            // Classic theme styling
            button.ApplySecondaryStyle(
                SystemColors.Control,
                SystemColors.ControlLight,
                SystemColors.ControlDark,
                SystemColors.ControlText,
                SystemColors.ControlDark);
        }
    }

    #endregion

    #region Search Logic

    /// <summary>
    /// Execute search for first page
    /// </summary>
    public async Task SearchAsync()
    {
        // Check for admin access code to unlock auto-search feature
        var searchText = _txtDealSearch.Text.Trim();
        if (searchText.Equals(AdminAccessCode, StringComparison.OrdinalIgnoreCase))
        {
            _isAdminAccessEnabled = true;
            _pnlAutoSearch.Visible = true;
            _txtDealSearch.Text = "";
            _lblDealStatus.Text = "옵션검색 기능이 활성화되었습니다.";
            return;
        }

        _dealCurrentPage = 1;
        await FetchDealPageAsync();
    }

    /// <summary>
    /// Fetch a specific page from the API
    /// </summary>
    private async Task FetchDealPageAsync()
    {
        var searchText = _txtDealSearch.Text.Trim();
        if (string.IsNullOrEmpty(searchText) || searchText.Length < 2)
        {
            MessageBox.Show("검색어는 2글자 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedServer = _cboDealServer.SelectedItem as Server;
        var serverId = selectedServer?.Id ?? -1;

        // Cancel previous operations
        var oldCts = _cts;
        _cts = new CancellationTokenSource();

        if (oldCts != null)
        {
            try { oldCts.Cancel(); }
            catch { }
        }

        var searchId = ++_currentSearchId;
        _lastSearchTerm = searchText;

        // Add to search history (only on first page / new search)
        if (_dealCurrentPage == 1)
        {
            AddToSearchHistory(searchText);
        }

        SetDealSearchingState(true);
        _lblDealStatus.Text = $"{_dealCurrentPage}페이지 조회 중...";
        _progressDealSearch.Value = 0;
        _progressDealSearch.Visible = true;

        try
        {
            // Fetch single page from API with total count
            var result = await _gnjoyClient.SearchItemDealsWithCountAsync(searchText, serverId, _dealCurrentPage, _cts.Token);
            var items = result.Items ?? new List<DealItem>();

            Debug.WriteLine($"[DealTabController] Page {_dealCurrentPage}: Got {items.Count} items, Total: {result.TotalCount}");

            // Update pagination state
            _totalCount = result.TotalCount;
            _totalPages = result.TotalPages;

            // Compute display fields
            foreach (var item in items)
            {
                item.ComputeFields();
            }

            // Load details sequentially BEFORE showing results
            var equipmentItems = items.Where(i => i.HasDetailParams && IsEquipmentItem(i)).ToList();
            if (equipmentItems.Count > 0)
            {
                _lblDealStatus.Text = $"상세정보 로딩 중... (0/{equipmentItems.Count})";
                await LoadDetailsSequentiallyAsync(items, equipmentItems, searchId, _cts.Token);
            }

            // Show all results at once after loading is complete
            _searchResults.Clear();
            _searchResults.AddRange(items);
            _dealBindingSource.DataSource = items;
            _dealBindingSource.ResetBindings(false);

            // Update pagination UI
            UpdateDealPaginationUI();

            // Final status update
            _progressDealSearch.Visible = false;
            var statusText = _totalCount > 0
                ? $"총 {_totalCount:N0}건 중 {_dealCurrentPage}/{_totalPages}페이지 (더블클릭으로 상세정보 조회)"
                : "검색 결과가 없습니다.";
            _lblDealStatus.Text = statusText;

            // Enable auto-search after successful search with results (only if admin access enabled)
            if (_totalCount > 0 && _isAdminAccessEnabled)
            {
                SetAutoSearchEnabled(true);
                // Set end page to total pages
                _txtAutoSearchEndPage.Text = _totalPages.ToString();
            }
        }
        catch (OperationCanceledException)
        {
            _lblDealStatus.Text = "검색이 취소되었습니다.";
        }
        catch (RateLimitException rateLimitEx)
        {
            var remainingTime = rateLimitEx.RemainingTimeText;
            _lblDealStatus.Text = $"API 요청 제한됨: {remainingTime}";
            _lblDealStatus.ForeColor = _colors.SaleColor;

            Debug.WriteLine($"[DealTabController] Rate limited: {rateLimitEx.RetryAfterSeconds}s, until {rateLimitEx.RetryAfterTime}");

            MessageBox.Show(
                $"GNJOY API 요청 제한이 적용되었습니다.\n\n" +
                $"재시도 가능 시간: {remainingTime}\n\n" +
                "과도한 검색을 자제해 주세요.",
                "API 요청 제한",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _lblDealStatus.Text = "오류: " + ex.Message;
            Debug.WriteLine("[DealTabController] Search error: " + ex.ToString());
            MessageBox.Show("검색 중 오류가 발생했습니다.\n\n" + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progressDealSearch.Visible = false;
            SetDealSearchingState(false);
        }
    }

    private async Task LoadDetailsSequentiallyAsync(
        List<DealItem> allItems,
        List<DealItem> equipmentItems,
        int searchId,
        CancellationToken cancellationToken)
    {
        bool IsCurrentSearch() => searchId == _currentSearchId && !cancellationToken.IsCancellationRequested;

        var total = equipmentItems.Count;
        var loaded = 0;

        foreach (var item in equipmentItems)
        {
            if (!IsCurrentSearch()) break;

            try
            {
                var detail = await _gnjoyClient.FetchItemDetailAsync(
                    item.ServerId,
                    item.MapId!.Value,
                    item.Ssi!,
                    cancellationToken);

                if (detail != null && IsCurrentSearch())
                {
                    item.ApplyDetailInfo(detail);
                }

                loaded++;

                // Update progress
                if (IsCurrentSearch() && _tabPage.IsHandleCreated)
                {
                    var progress = (loaded * 100) / total;
                    _tabPage.Invoke(() =>
                    {
                        if (IsCurrentSearch())
                        {
                            _progressDealSearch.Value = progress;
                            _lblDealStatus.Text = $"상세정보 로딩 중... ({loaded}/{total})";
                        }
                    });
                }

                // Delay between requests to prevent rate limiting
                if (loaded < total && IsCurrentSearch())
                {
                    await Task.Delay(DetailRequestDelayMs, cancellationToken);
                }
            }
            catch (RateLimitException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DealTabController] Failed to load detail for {item.ItemName}: {ex.Message}");
                loaded++;
            }
        }

        Debug.WriteLine($"[DealTabController] Loaded {loaded}/{total} item details sequentially");
    }

    private bool IsEquipmentItem(DealItem item)
    {
        var effectiveItemId = item.GetEffectiveItemId();
        if (!effectiveItemId.HasValue) return true;

        if (!_itemIndexService.IsLoaded) return true;

        var cachedItem = _itemIndexService.GetItemById(effectiveItemId.Value);
        if (cachedItem == null) return true;

        var itemType = cachedItem.Type;
        var isEquipment = EquipmentItemTypes.Contains(itemType);

        Debug.WriteLine($"[DealTabController] Item '{item.ItemName}' (ID:{effectiveItemId}) Type:{itemType} IsEquipment:{isEquipment}");
        return isEquipment;
    }

    private async Task PreviousPageAsync()
    {
        if (_dealCurrentPage > 1)
        {
            _dealCurrentPage--;
            await FetchDealPageAsync();
        }
    }

    private async Task NextPageAsync()
    {
        if (_dealCurrentPage < _totalPages)
        {
            _dealCurrentPage++;
            await FetchDealPageAsync();
        }
    }

    private async Task GoToPageAsync(int page)
    {
        if (page >= 1 && page <= _totalPages && page != _dealCurrentPage)
        {
            _dealCurrentPage = page;
            await FetchDealPageAsync();
        }
    }

    private void UpdateDealPaginationUI()
    {
        // Update total count label with current page info
        _lblDealPage.Text = _totalCount > 0
            ? $"{_dealCurrentPage}/{_totalPages} ({_totalCount:N0}건)"
            : "";

        // Enable/disable navigation buttons based on current position
        // During auto-search, all navigation buttons should be disabled
        var canGoPrev = !_isAutoSearching && _dealCurrentPage > 1;
        var canGoNext = !_isAutoSearching && _dealCurrentPage < _totalPages;

        _btnFirst.Enabled = canGoPrev;
        _btnPrev100.Enabled = !_isAutoSearching && _dealCurrentPage > 50;
        _btnPrev10.Enabled = !_isAutoSearching && _dealCurrentPage > 10;
        _btnDealPrev.Enabled = canGoPrev;

        _btnDealNext.Enabled = canGoNext;
        _btnNext10.Enabled = !_isAutoSearching && _dealCurrentPage + 10 <= _totalPages;
        _btnNext100.Enabled = !_isAutoSearching && _dealCurrentPage + 50 <= _totalPages;
        _btnLast.Enabled = canGoNext;

        // Rebuild page number buttons
        RebuildPageNumberButtons();
        CenterPaginationPanel();
    }

    private void RebuildPageNumberButtons()
    {
        // Remove existing page number buttons (keep prev, next, and label)
        var toRemove = _pnlPagination.Controls.Cast<Control>()
            .Where(c => c.Tag is string s && s == "PageButton")
            .ToList();
        foreach (var ctrl in toRemove)
        {
            _pnlPagination.Controls.Remove(ctrl);
            ctrl.Dispose();
        }

        if (_totalPages <= 0) return;

        // Calculate which page numbers to show
        // Show up to MaxVisiblePages pages, centered around current page
        int startPage, endPage;
        if (_totalPages <= MaxVisiblePages)
        {
            startPage = 1;
            endPage = _totalPages;
        }
        else
        {
            // Try to center current page
            int half = MaxVisiblePages / 2;
            startPage = Math.Max(1, _dealCurrentPage - half);
            endPage = startPage + MaxVisiblePages - 1;

            if (endPage > _totalPages)
            {
                endPage = _totalPages;
                startPage = Math.Max(1, endPage - MaxVisiblePages + 1);
            }
        }

        // Insert page buttons after prev button
        int insertIndex = _pnlPagination.Controls.IndexOf(_btnDealPrev) + 1;

        // Calculate button size based on font scale
        var scale = _baseFontSize / 12f;
        var btnHeight = (int)(28 * scale);

        for (int page = startPage; page <= endPage; page++)
        {
            var pageNum = page;
            var isCurrentPage = page == _dealCurrentPage;

            // Adjust width based on number of digits
            var digitCount = page.ToString().Length;
            var btnWidth = (int)((24 + (digitCount * 8)) * scale);

            var btn = new RoundedButton
            {
                Text = page.ToString(),
                Size = new Size(btnWidth, btnHeight),
                CornerRadius = 6,
                Tag = "PageButton",
                Margin = new Padding(2, 0, 2, 0)
            };

            if (isCurrentPage)
            {
                // Current page - primary style (highlighted)
                ApplyRoundedButtonStyle(btn, true);
                btn.Enabled = false;
            }
            else
            {
                // Other pages - secondary style
                ApplyRoundedButtonStyle(btn, false);
                btn.Enabled = !_isAutoSearching;  // Disable during auto-search
                btn.Click += async (s, e) =>
                {
                    if (!_isAutoSearching)  // Double-check to prevent race condition
                        await GoToPageAsync(pageNum);
                };
            }

            _pnlPagination.Controls.Add(btn);
            _pnlPagination.Controls.SetChildIndex(btn, insertIndex++);
        }
    }

    private void CenterPaginationPanel()
    {
        if (_pnlPagination == null) return;

        // Calculate total width of all controls
        int totalWidth = 0;
        foreach (Control ctrl in _pnlPagination.Controls)
        {
            totalWidth += ctrl.Width + ctrl.Margin.Horizontal;
        }

        var leftPadding = Math.Max(0, (_pnlPagination.Width - totalWidth) / 2);
        _pnlPagination.Padding = new Padding(leftPadding, _pnlPagination.Padding.Top, 0, 0);
    }

    private void SetDealSearchingState(bool searching)
    {
        _btnDealSearchToolStrip.Enabled = !searching;
        _txtDealSearch.Enabled = !searching;
        _cboDealServer.Enabled = !searching;

        // Disable all navigation buttons during search
        _btnFirst.Enabled = !searching && _dealCurrentPage > 1;
        _btnPrev100.Enabled = !searching && _dealCurrentPage > 50;
        _btnPrev10.Enabled = !searching && _dealCurrentPage > 10;
        _btnDealPrev.Enabled = !searching && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !searching && _dealCurrentPage < _totalPages;
        _btnNext10.Enabled = !searching && _dealCurrentPage + 10 <= _totalPages;
        _btnNext100.Enabled = !searching && _dealCurrentPage + 50 <= _totalPages;
        _btnLast.Enabled = !searching && _dealCurrentPage < _totalPages;

        // Disable page number buttons during search
        foreach (Control ctrl in _pnlPagination.Controls)
        {
            if (ctrl.Tag is string s && (s == "PageButton" || s == "NavButton"))
            {
                if (searching) ctrl.Enabled = false;
            }
        }

        // Reset status label color when starting a new search
        if (searching)
        {
            _lblDealStatus.ForeColor = _colors.Text;
        }

        // Disable search history links during search
        if (_pnlSearchHistory != null)
        {
            foreach (Control ctrl in _pnlSearchHistory.Controls)
            {
                if (ctrl is Label lbl && lbl.Tag is ValueTuple<string, string> tag && tag.Item1 == "SearchHistoryLink")
                {
                    lbl.Enabled = !searching;
                    lbl.ForeColor = searching ? _colors.TextMuted : _colors.Accent;
                    lbl.Cursor = searching ? Cursors.Default : Cursors.Hand;
                }
            }
        }
    }

    #endregion

    #region Search History

    private void UpdateSearchHistoryPanel()
    {
        Debug.WriteLine($"[DealTabController] UpdateSearchHistoryPanel: _dealSearchHistory count = {_dealSearchHistory?.Count ?? -1}");
        if (_pnlSearchHistory == null) return;

        // Keep only the title label, remove all history buttons
        while (_pnlSearchHistory.Controls.Count > 1)
        {
            var ctrl = _pnlSearchHistory.Controls[1];
            _pnlSearchHistory.Controls.RemoveAt(1);
            ctrl.Dispose();
        }

        // Add history items as clickable labels
        foreach (var term in _dealSearchHistory ?? Enumerable.Empty<string>())
        {
            var btn = new Label
            {
                Text = term,
                AutoSize = true,
                ForeColor = _colors.Accent,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 3, 10, 0),
                Tag = ("SearchHistoryLink", term)
            };
            btn.Click += async (s, e) =>
            {
                if (s is Label lbl && lbl.Tag is (string _, string searchTerm))
                {
                    _txtDealSearch.Text = searchTerm;
                    await SearchAsync();
                }
            };
            // Hover effect
            btn.MouseEnter += (s, e) => { if (s is Label lbl) lbl.Font = new Font(lbl.Font, FontStyle.Underline); };
            btn.MouseLeave += (s, e) => { if (s is Label lbl) lbl.Font = new Font(lbl.Font, FontStyle.Regular); };
            _pnlSearchHistory.Controls.Add(btn);
        }

        // Update panel visibility and row height
        var hasHistory = _dealSearchHistory?.Count > 0;
        _pnlSearchHistory.Visible = hasHistory;

        // Update the row height in parent TableLayoutPanel
        if (_pnlSearchHistory.Parent is TableLayoutPanel tableLayout)
        {
            tableLayout.RowStyles[1].Height = hasHistory ? 28 : 0;
        }
    }

    private void AddToSearchHistory(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return;

        // Remove if already exists (to move it to the front)
        _dealSearchHistory.Remove(searchTerm);

        // Add to the beginning
        _dealSearchHistory.Insert(0, searchTerm);

        // Trim to max count
        while (_dealSearchHistory.Count > MaxSearchHistoryCount)
        {
            _dealSearchHistory.RemoveAt(_dealSearchHistory.Count - 1);
        }

        // Update UI
        UpdateSearchHistoryPanel();

        // Notify parent to save settings
        SearchHistoryChanged?.Invoke(this, _dealSearchHistory);
    }

    #endregion

    #region Grid Events

    private void DgvDeals_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle == null || e.RowIndex < 0) return;

        var columnName = _dgvDeals.Columns[e.ColumnIndex].Name;

        // Deal type color
        if (columnName == "DealTypeDisplay" && e.Value != null)
        {
            var value = e.Value.ToString();
            if (value == "판매")
                e.CellStyle.ForeColor = _colors.SaleColor;
            else if (value == "구매")
                e.CellStyle.ForeColor = _colors.BuyColor;
        }
    }

    private void DgvDeals_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var dataSource = _dealBindingSource.DataSource as List<DealItem>;
        if (dataSource == null || e.RowIndex >= dataSource.Count) return;

        var selectedItem = dataSource[e.RowIndex];
        Debug.WriteLine($"[DealTabController] Opening detail for: {selectedItem.DisplayName}");

        ShowItemDetail?.Invoke(this, selectedItem);
    }

    private void DgvDeals_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics == null) return;

        // Header painting
        if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            e.PaintBackground(e.ClipBounds, true);
            TextRenderer.DrawText(
                e.Graphics,
                e.FormattedValue?.ToString() ?? "",
                e.CellStyle?.Font ?? _dgvDeals.Font,
                e.CellBounds,
                e.CellStyle?.ForeColor ?? _colors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
            e.Handled = true;
            return;
        }

        // SlotAndOptionsDisplay column - draw links for SlotInfo, plain text for RandomOptions
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
        {
            var columnName = _dgvDeals.Columns[e.ColumnIndex].Name;
            if (columnName == "SlotAndOptionsDisplay")
            {
                var dataSource = _dealBindingSource.DataSource as List<DealItem>;
                if (dataSource == null || e.RowIndex >= dataSource.Count) return;

                var dealItem = dataSource[e.RowIndex];
                var slotItems = dealItem.SlotInfo ?? new List<string>();
                var randomOptions = dealItem.RandomOptions ?? new List<string>();

                if (slotItems.Count == 0 && randomOptions.Count == 0) return;

                // Paint background
                e.PaintBackground(e.ClipBounds, true);

                var font = e.CellStyle?.Font ?? _dgvDeals.DefaultCellStyle.Font ?? _dgvDeals.Font;
                var linkFont = new Font(font, FontStyle.Underline);
                var padding = e.CellStyle?.Padding ?? new Padding(2);
                var x = e.CellBounds.X + padding.Left + 3;
                var y = e.CellBounds.Y + padding.Top + 2;

                var hitAreas = new List<(Rectangle rect, string itemName)>();
                var key = (e.RowIndex, e.ColumnIndex);

                var lineHeight = TextRenderer.MeasureText(e.Graphics, "Test", font).Height;

                // Draw SlotInfo items as links (one per line)
                foreach (var item in slotItems)
                {
                    var textSize = TextRenderer.MeasureText(e.Graphics, item, linkFont);
                    var textRect = new Rectangle(x, y, textSize.Width, textSize.Height);

                    // Check if this is the hovered link
                    var isHovered = _hoveredLinkItem == item && key == _hoveredLinkKey;
                    var linkColor = isHovered ? _colors.Accent : _colors.LinkColor;

                    TextRenderer.DrawText(e.Graphics, item, linkFont, textRect, linkColor, TextFormatFlags.Left);

                    hitAreas.Add((textRect, item));
                    y += lineHeight + 1;
                }

                // Draw RandomOptions as plain text (one per line)
                foreach (var option in randomOptions)
                {
                    var textSize = TextRenderer.MeasureText(e.Graphics, option, font);
                    var textRect = new Rectangle(x, y, textSize.Width, textSize.Height);
                    TextRenderer.DrawText(e.Graphics, option, font, textRect, _colors.TextMuted, TextFormatFlags.Left);

                    y += lineHeight + 1;
                }

                // Store hit areas for click detection (only SlotInfo items)
                _linkHitAreas[key] = hitAreas;

                linkFont.Dispose();
                e.Handled = true;
            }
        }
    }

    private (int row, int col) _hoveredLinkKey;

    private void DgvDeals_CellMouseMove(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _dgvDeals.Columns[e.ColumnIndex].Name;
        if (columnName != "SlotAndOptionsDisplay")
        {
            if (_hoveredLinkItem != null)
            {
                _hoveredLinkItem = null;
                _dgvDeals.Cursor = Cursors.Default;
                _dgvDeals.InvalidateCell(e.ColumnIndex, e.RowIndex);
            }
            return;
        }

        var key = (e.RowIndex, e.ColumnIndex);
        if (!_linkHitAreas.TryGetValue(key, out var hitAreas)) return;

        var cellRect = _dgvDeals.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
        var mousePos = new Point(cellRect.X + e.X, cellRect.Y + e.Y);

        string? hoveredItem = null;
        foreach (var (rect, itemName) in hitAreas)
        {
            if (rect.Contains(mousePos))
            {
                hoveredItem = itemName;
                break;
            }
        }

        if (hoveredItem != _hoveredLinkItem)
        {
            _hoveredLinkItem = hoveredItem;
            _hoveredLinkKey = key;
            _dgvDeals.Cursor = hoveredItem != null ? Cursors.Hand : Cursors.Default;
            _dgvDeals.InvalidateCell(e.ColumnIndex, e.RowIndex);
        }
    }

    private void DgvDeals_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.Button != MouseButtons.Left) return;

        var columnName = _dgvDeals.Columns[e.ColumnIndex].Name;
        if (columnName != "SlotAndOptionsDisplay") return;

        // Prevent cell selection for this column (blue background makes text hard to read)
        if (_dgvDeals.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected)
        {
            _dgvDeals.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = false;
        }

        var key = (e.RowIndex, e.ColumnIndex);
        if (!_linkHitAreas.TryGetValue(key, out var hitAreas)) return;

        var cellRect = _dgvDeals.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
        var mousePos = new Point(cellRect.X + e.X, cellRect.Y + e.Y);

        foreach (var (rect, itemName) in hitAreas)
        {
            if (rect.Contains(mousePos))
            {
                ShowItemInfoByName(itemName);
                break;
            }
        }
    }


    private void ShowItemInfoByName(string itemName)
    {
        Debug.WriteLine($"[DealTabController] Looking up item: {itemName}");

        // Search for item in index
        var foundItem = _itemIndexService.SearchItems(itemName, new HashSet<int> { 999 }, 0, 1, false).FirstOrDefault();

        if (foundItem != null)
        {
            // Show item info popup
            var parentForm = _tabPage.FindForm();
            if (parentForm != null)
            {
                var indexService = _itemIndexService as ItemIndexService;
                var infoForm = new ItemInfoForm(foundItem, indexService, _currentTheme, _baseFontSize);
                infoForm.Show(parentForm);
            }
        }
        else
        {
            MessageBox.Show($"'{itemName}' 아이템을 찾을 수 없습니다.\n\n아이템 정보 수집(도구 > 아이템정보 수집)을 먼저 실행해 주세요.",
                "아이템 검색", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void DealContextMenu_AddToMonitor(object? sender, EventArgs e)
    {
        var selectedRow = _dgvDeals.CurrentRow;
        if (selectedRow == null) return;

        var dataSource = _dealBindingSource.DataSource as List<DealItem>;
        if (dataSource == null || selectedRow.Index >= dataSource.Count) return;

        var selectedItem = dataSource[selectedRow.Index];
        var itemName = selectedItem.DisplayName ?? selectedItem.ItemName;
        var serverId = selectedItem.ServerId;

        Debug.WriteLine($"[DealTabController] AddToMonitor: DisplayName='{selectedItem.DisplayName}', ItemName='{selectedItem.ItemName}'");
        Debug.WriteLine($"[DealTabController] AddToMonitor: Grade='{selectedItem.Grade}', Refine={selectedItem.Refine}, CardSlots='{selectedItem.CardSlots}'");
        Debug.WriteLine($"[DealTabController] AddToMonitor: Using itemName='{itemName}', serverId={serverId}");

        var (success, errorReason) = await _monitoringService.AddItemAsync(itemName, serverId);

        if (success)
        {
            _lblDealStatus.Text = $"'{itemName}' 모니터링 목록에 추가됨";

            if (_monitoringService.Config.RefreshIntervalSeconds > 0)
            {
                var newItem = _monitoringService.Config.Items
                    .FirstOrDefault(i => i.ItemName == itemName && i.ServerId == serverId);
                if (newItem != null)
                {
                    newItem.NextRefreshTime = DateTime.Now;
                }
            }
        }
        else
        {
            var message = errorReason switch
            {
                "limit" => $"모니터링 목록은 최대 {MonitoringService.MaxItemCountLimit}개까지만 등록할 수 있습니다.",
                "duplicate" => "이미 등록된 아이템입니다.",
                _ => "아이템을 추가할 수 없습니다."
            };
            MessageBox.Show(message, "모니터링 추가", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    #endregion

    #region Theme & Font

    /// <inheritdoc/>
    public override void ApplyTheme(ThemeColors colors)
    {
        base.ApplyTheme(colors);

        // Update status label
        if (_lblDealStatus != null)
        {
            _lblDealStatus.BackColor = colors.Panel;
            _lblDealStatus.ForeColor = colors.Text;
        }

        // Update DataGridView
        if (_dgvDeals != null)
        {
            ApplyDataGridViewStyle(_dgvDeals);
        }

        // Update pagination buttons
        if (_btnDealPrev != null) ApplyRoundedButtonStyle(_btnDealPrev, false);
        if (_btnDealNext != null) ApplyRoundedButtonStyle(_btnDealNext, false);
        if (_lblDealPage != null) _lblDealPage.ForeColor = colors.Text;

        // Update search history panel
        if (_pnlSearchHistory != null)
        {
            _pnlSearchHistory.BackColor = colors.Panel;
            foreach (Control ctrl in _pnlSearchHistory.Controls)
            {
                if (ctrl is Label lbl)
                {
                    if (lbl.Tag is ValueTuple<string, string> tag && tag.Item1 == "SearchHistoryLink")
                    {
                        lbl.ForeColor = colors.Accent;
                    }
                    else
                    {
                        lbl.ForeColor = colors.TextMuted;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void UpdateFontSize(float baseFontSize)
    {
        base.UpdateFontSize(baseFontSize);

        var scale = baseFontSize / 12f;

        // Update DataGridView fonts
        if (_dgvDeals != null)
        {
            _dgvDeals.DefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize);
            _dgvDeals.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
        }

        // Update status label height
        if (_lblDealStatus != null)
        {
            _lblDealStatus.Font = new Font("Malgun Gothic", baseFontSize);
            _lblDealStatus.Height = (int)(22 * scale);
        }

        // Update page label
        if (_lblDealPage != null)
        {
            _lblDealPage.Font = new Font("Malgun Gothic", baseFontSize);
            _lblDealPage.Padding = new Padding((int)(10 * scale), (int)(5 * scale), (int)(10 * scale), 0);
        }

        // Update pagination buttons size - all buttons have same height (28 * scale)
        var btnHeight = (int)(28 * scale);
        if (_btnFirst != null) _btnFirst.Size = new Size((int)(36 * scale), btnHeight);
        if (_btnPrev100 != null) _btnPrev100.Size = new Size((int)(44 * scale), btnHeight);
        if (_btnPrev10 != null) _btnPrev10.Size = new Size((int)(40 * scale), btnHeight);
        if (_btnDealPrev != null) _btnDealPrev.Size = new Size((int)(32 * scale), btnHeight);
        if (_btnDealNext != null) _btnDealNext.Size = new Size((int)(32 * scale), btnHeight);
        if (_btnNext10 != null) _btnNext10.Size = new Size((int)(40 * scale), btnHeight);
        if (_btnNext100 != null) _btnNext100.Size = new Size((int)(44 * scale), btnHeight);
        if (_btnLast != null) _btnLast.Size = new Size((int)(36 * scale), btnHeight);

        // Update pagination panel padding
        if (_pnlPagination != null)
        {
            var paddingTop = (int)(10 * scale);
            CenterPaginationPanel();
            _pnlPagination.Padding = new Padding(_pnlPagination.Padding.Left, paddingTop, 0, 0);
        }

        // Update combobox width
        if (_cboServerToolStrip != null)
        {
            _cboServerToolStrip.Width = (int)(100 * scale);
        }

        // Update row heights in main panel
        // Row indices: 0=Toolbar, 1=SearchHistory, 2=AutoSearch, 3=Grid, 4=Pagination, 5=Status
        if (_mainPanel != null && _mainPanel.RowStyles.Count >= 6)
        {
            _mainPanel.RowStyles[0].Height = (int)(32 * scale);  // Toolbar
            _mainPanel.RowStyles[2].Height = (int)(50 * scale);  // Auto-search panel
            _mainPanel.RowStyles[4].Height = (int)(55 * scale);  // Pagination
            _mainPanel.RowStyles[5].Height = (int)(28 * scale);  // Status
        }
    }

    #endregion

    #region Rate Limit UI

    /// <summary>
    /// Update UI based on rate limit state
    /// </summary>
    public void UpdateRateLimitUI(bool isRateLimited)
    {
        _btnDealSearchToolStrip.Enabled = !isRateLimited;
        _txtDealSearch.Enabled = !isRateLimited;
        _cboDealServer.Enabled = !isRateLimited;

        // Disable all navigation buttons
        _btnFirst.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnPrev100.Enabled = !isRateLimited && _dealCurrentPage > 50;
        _btnPrev10.Enabled = !isRateLimited && _dealCurrentPage > 10;
        _btnDealPrev.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !isRateLimited && _dealCurrentPage < _totalPages;
        _btnNext10.Enabled = !isRateLimited && _dealCurrentPage + 10 <= _totalPages;
        _btnNext100.Enabled = !isRateLimited && _dealCurrentPage + 50 <= _totalPages;
        _btnLast.Enabled = !isRateLimited && _dealCurrentPage < _totalPages;

        // Disable page number and nav buttons
        foreach (Control ctrl in _pnlPagination.Controls)
        {
            if (ctrl.Tag is string s && (s == "PageButton" || s == "NavButton"))
            {
                if (isRateLimited) ctrl.Enabled = false;
            }
        }

        // Update search history links
        if (_pnlSearchHistory != null)
        {
            foreach (Control ctrl in _pnlSearchHistory.Controls)
            {
                if (ctrl is Label lbl && lbl.Tag is ValueTuple<string, string> tag && tag.Item1 == "SearchHistoryLink")
                {
                    lbl.Enabled = !isRateLimited;
                    lbl.ForeColor = isRateLimited ? _colors.TextMuted : _colors.Accent;
                    lbl.Cursor = isRateLimited ? Cursors.Default : Cursors.Hand;
                }
            }
        }
    }

    /// <summary>
    /// Show rate limit message in status bar
    /// </summary>
    public void ShowRateLimitStatus(string message)
    {
        _lblDealStatus.Text = message;
        _lblDealStatus.ForeColor = _colors.SaleColor;
    }

    /// <summary>
    /// Clear rate limit status message
    /// </summary>
    public void ClearRateLimitStatus()
    {
        _lblDealStatus.Text = "검색어를 입력하고 [검색] 버튼을 클릭하세요.";
        _lblDealStatus.ForeColor = _colors.Text;
    }

    #endregion

    #region Tab Activation

    private bool _guideShownThisSession = false;

    /// <inheritdoc/>
    public override void OnActivated()
    {
        base.OnActivated();

        // Show guide dialog on first activation if not hidden
        if (!_guideShownThisSession && !_settingsService.HideDealSearchGuide)
        {
            _guideShownThisSession = true;
            ShowSearchGuideDialog();
        }
    }

    private void ShowSearchGuideDialog()
    {
        var parentForm = _tabPage.FindForm();
        if (parentForm == null) return;

        // Use BeginInvoke to show dialog after tab switch completes
        parentForm.BeginInvoke(() =>
        {
            using var guideForm = new Form
            {
                Text = "노점조회 안내",
                Size = new Size(500, 400),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = _colors.Panel,
                ForeColor = _colors.Text
            };

            var lblTitle = new Label
            {
                Text = "노점 거래 검색 안내",
                Font = new Font("Malgun Gothic", _baseFontSize + 2, FontStyle.Bold),
                ForeColor = _colors.Accent,
                AutoSize = false,
                Size = new Size(460, 30),
                Location = new Point(20, 15),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblContent = new Label
            {
                Text = "【 데이터 출처 】\n" +
                       "노점 거래 정보는 GNJOY 공식 홈페이지(ro.gnjoy.com)에서\n" +
                       "제공하는 데이터를 실시간으로 조회합니다.\n\n" +
                       "【 조회 방식 】\n" +
                       "1페이지(10건) 조회 시 아이템 목록 1회 + 상세정보 10회를\n" +
                       "순차적으로 요청하며, 상세정보 1건당 약 1초가 소요됩니다.\n" +
                       "따라서 한 페이지 조회에 약 10~15초가 소요될 수 있습니다.\n\n" +
                       "【 검색 제한 주의 】\n" +
                       "GNJOY 서버의 요청 제한 기준과 차단 기간은 공개되어 있지\n" +
                       "않아 정확히 알 수 없습니다.\n" +
                       "(개발자가 의상검색 개발 중 약 1일간 차단된 경험이 있습니다)\n" +
                       "과도한 연속 검색은 자제해 주시기 바랍니다.",
                Font = new Font("Malgun Gothic", _baseFontSize),
                ForeColor = _colors.Text,
                AutoSize = false,
                Size = new Size(460, 250),
                Location = new Point(20, 50)
            };

            var chkDontShowAgain = new CheckBox
            {
                Text = "다시 보지 않기",
                Font = new Font("Malgun Gothic", _baseFontSize),
                ForeColor = _colors.TextMuted,
                AutoSize = true,
                Location = new Point(20, 310)
            };

            var btnOk = new Button
            {
                Text = "확인",
                Size = new Size(100, 35),
                Location = new Point(380, 305),
                FlatStyle = _currentTheme == ThemeType.Dark ? FlatStyle.Flat : FlatStyle.Standard,
                BackColor = _colors.Accent,
                ForeColor = _colors.AccentText,
                Cursor = Cursors.Hand
            };
            if (_currentTheme == ThemeType.Dark)
            {
                btnOk.FlatAppearance.BorderColor = _colors.Accent;
            }

            btnOk.Click += (s, e) =>
            {
                if (chkDontShowAgain.Checked)
                {
                    _settingsService.HideDealSearchGuide = true;
                    _ = _settingsService.SaveAsync();
                }
                guideForm.Close();
            };

            guideForm.Controls.AddRange(new Control[] { lblTitle, lblContent, chkDontShowAgain, btnOk });
            guideForm.AcceptButton = btnOk;
            guideForm.ShowDialog(parentForm);
        });
    }

    #endregion

    #region Auto-Search

    private async Task StartAutoSearchAsync()
    {
        // Validate inputs - filter must have at least 2 characters (excluding special chars and whitespace)
        var filterText = _txtAutoSearchFilter.Text.Trim();
        var filterTextClean = System.Text.RegularExpressions.Regex.Replace(filterText, @"[^a-zA-Z0-9가-힣]", "");
        if (filterTextClean.Length < 2)
        {
            MessageBox.Show("검색할 카드/인챈트/옵션은 2글자 이상 입력해주세요.\n(특수문자, 공백 제외)", "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtAutoSearchFilter.Focus();
            return;
        }

        var searchTerm = _txtDealSearch.Text.Trim();
        if (string.IsNullOrEmpty(searchTerm))
        {
            MessageBox.Show("먼저 검색할 아이템명을 입력해주세요.", "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtDealSearch.Focus();
            return;
        }

        // Parse page range
        int startPage = 1;
        int? endPage = null;

        if (!string.IsNullOrWhiteSpace(_txtAutoSearchStartPage.Text))
        {
            if (!int.TryParse(_txtAutoSearchStartPage.Text.Trim(), out startPage) || startPage < 1)
            {
                MessageBox.Show("시작 페이지는 1 이상의 숫자여야 합니다.", "입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtAutoSearchStartPage.Focus();
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_txtAutoSearchEndPage.Text))
        {
            if (!int.TryParse(_txtAutoSearchEndPage.Text.Trim(), out int parsedEndPage) || parsedEndPage < 1)
            {
                MessageBox.Show("끝 페이지는 1 이상의 숫자여야 합니다.", "입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtAutoSearchEndPage.Focus();
                return;
            }
            endPage = parsedEndPage;
        }

        // Validate page range
        if (endPage.HasValue && startPage > endPage.Value)
        {
            MessageBox.Show("시작 페이지가 끝 페이지보다 클 수 없습니다.", "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Validate start page against total pages from last search
        if (_totalPages > 0 && startPage > _totalPages)
        {
            MessageBox.Show(
                $"시작 페이지({startPage})가 총 페이지 수({_totalPages})를 초과합니다.\n" +
                $"시작 페이지를 {_totalPages} 이하로 설정해주세요.",
                "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtAutoSearchStartPage.Focus();
            return;
        }

        // Apply safety limit if no end page specified
        int maxEndPage = endPage ?? (startPage + AutoSearchMaxPages - 1);

        // Get selected server first
        var selectedServer = _cboServerToolStrip.SelectedItem as Server;
        if (selectedServer == null)
        {
            return;
        }

        // Initialize form with new search session (creates or updates history entry)
        EnsureAutoSearchResultsForm();
        _autoSearchResultsForm!.StartNewSearch(searchTerm, filterText, selectedServer.Id, selectedServer.Name);

        // Set up cancellation
        _autoSearchCts = new CancellationTokenSource();
        var ct = _autoSearchCts.Token;

        // Update UI state
        SetAutoSearchingState(true);
        _lblAutoSearchStatus.Text = $"준비 중...";
        _autoSearchProgressBar.Value = 0;
        _autoSearchProgressBar.Visible = true;

        int matchCount = 0;
        int currentPage = startPage;
        var searchStartTime = DateTime.Now;
        int pagesCompleted = 0;

        try
        {
            while (currentPage <= maxEndPage)
            {
                ct.ThrowIfCancellationRequested();

                // Update progress bar
                int totalPagesToSearch = maxEndPage - startPage + 1;
                int progressPercent = totalPagesToSearch > 0
                    ? (int)((currentPage - startPage) * 100.0 / totalPagesToSearch)
                    : 0;
                _autoSearchProgressBar.Value = Math.Min(progressPercent, 100);

                // Calculate estimated remaining time
                string etaText = "";
                var remainingPages = maxEndPage - currentPage + 1;
                double avgTimePerPage;

                if (pagesCompleted > 0)
                {
                    // Use actual elapsed time for accuracy
                    var elapsed = DateTime.Now - searchStartTime;
                    avgTimePerPage = elapsed.TotalSeconds / pagesCompleted;
                }
                else
                {
                    // Initial estimate based on configured delays
                    // (items per page × item delay + page delay) / 1000 to convert ms to seconds
                    avgTimePerPage = (ItemsPerPage * AutoSearchItemDelayMs + AutoSearchPageDelayMs) / 1000.0;
                }

                var etaSeconds = (int)(avgTimePerPage * remainingPages);
                if (etaSeconds >= 60)
                    etaText = $" (남은 시간: {etaSeconds / 60}분 {etaSeconds % 60}초)";
                else if (etaSeconds > 0)
                    etaText = $" (남은 시간: {etaSeconds}초)";

                _lblAutoSearchStatus.Text = $"{currentPage}/{maxEndPage}페이지 검색 중... 발견: {matchCount}건{etaText}";

                // Search the page
                var (items, totalCount) = await SearchPageForAutoSearchAsync(
                    searchTerm, selectedServer.Id, currentPage, ct);

                // Update max end page based on actual total pages
                int actualTotalPages = (int)Math.Ceiling((double)totalCount / ItemsPerPage);
                if (actualTotalPages > 0)
                {
                    maxEndPage = Math.Min(endPage ?? actualTotalPages, actualTotalPages);
                }

                // Update main grid with current page results
                _searchResults.Clear();
                _searchResults.AddRange(items);
                _dealBindingSource.DataSource = null;
                _dealBindingSource.DataSource = _searchResults;
                _dgvDeals.Refresh();
                _dealCurrentPage = currentPage;
                _totalCount = totalCount;
                _totalPages = actualTotalPages;
                UpdateDealPaginationUI();  // Update pagination (buttons disabled during auto-search)
                _lblDealStatus.Text = $"옵션검색 중: {currentPage}/{actualTotalPages}페이지";

                // Check for matches in this page's results
                foreach (var item in items)
                {
                    if (IsMatchingItem(item, filterText))
                    {
                        matchCount++;
                        _autoSearchResultsForm?.AddResult(item, currentPage);
                        Debug.WriteLine($"[AutoSearch] Found match on page {currentPage}: {item.DisplayName}");
                    }
                }

                pagesCompleted++;

                // Move to next page
                currentPage++;

                // Wait before next page
                if (currentPage <= maxEndPage)
                {
                    _lblAutoSearchStatus.Text = $"다음 페이지 대기 중... ({AutoSearchPageDelayMs / 1000}초)";

                    try
                    {
                        await Task.Delay(AutoSearchPageDelayMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
            }

            // Search completed
            _autoSearchProgressBar.Value = 100;
            var totalElapsed = DateTime.Now - searchStartTime;
            var elapsedText = totalElapsed.TotalSeconds >= 60
                ? $"{(int)totalElapsed.TotalMinutes}분 {(int)totalElapsed.TotalSeconds % 60}초"
                : $"{(int)totalElapsed.TotalSeconds}초";
            _lblAutoSearchStatus.Text = $"검색 완료: {startPage}~{currentPage - 1}페이지, {matchCount}건 발견 (소요: {elapsedText})";
            _autoSearchResultsForm?.UpdateSearchStatus($"검색 완료: {matchCount}건 발견");

            if (matchCount > 0)
            {
                // Show results form automatically
                ShowAutoSearchResults();
            }
            else
            {
                MessageBox.Show(
                    $"'{filterText}' 항목을 {startPage}~{currentPage - 1}페이지에서 찾지 못했습니다.",
                    "자동검색 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            _lblAutoSearchStatus.Text = $"검색 중지됨: {currentPage - 1}페이지까지 검색, {matchCount}건 발견";
            _autoSearchResultsForm?.UpdateSearchStatus($"검색 중지됨: {matchCount}건 발견");
        }
        catch (RateLimitException rateLimitEx)
        {
            var remainingTime = rateLimitEx.RemainingTimeText;
            _lblAutoSearchStatus.Text = $"API 제한: {remainingTime} 후 재시도";
            _lblAutoSearchStatus.ForeColor = _colors.SaleColor;

            _autoSearchResultsForm?.UpdateSearchStatus($"API 제한으로 중단됨: {matchCount}건 발견");

            MessageBox.Show(
                $"GNJOY API 요청 제한이 적용되었습니다.\n\n" +
                $"제한 해제까지: {remainingTime}\n" +
                $"검색 진행: {currentPage - 1}페이지까지 완료\n" +
                $"발견된 항목: {matchCount}건\n\n" +
                $"제한이 해제된 후 다시 시도해주세요.\n" +
                $"(시작 페이지를 {currentPage}로 설정하면 이어서 검색 가능)",
                "API 요청 제한",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            // Set start page to continue from where we left off
            _txtAutoSearchStartPage.Text = currentPage.ToString();
        }
        catch (Exception ex)
        {
            _lblAutoSearchStatus.Text = $"오류 발생: {ex.Message}";
            Debug.WriteLine($"[AutoSearch] Error: {ex}");
        }
        finally
        {
            _autoSearchProgressBar.Visible = false;
            SetAutoSearchingState(false);
        }
    }

    private async Task<(List<DealItem> items, int totalCount)> SearchPageForAutoSearchAsync(
        string searchTerm, int serverId, int page, CancellationToken ct)
    {
        var items = new List<DealItem>();
        int totalCount = 0;

        try
        {
            var result = await _gnjoyClient.SearchItemDealsWithCountAsync(searchTerm, serverId, page, ct);

            if (result.TotalCount > 0 && result.Items != null)
            {
                totalCount = result.TotalCount;

                foreach (var item in result.Items)
                {
                    ct.ThrowIfCancellationRequested();

                    // Fetch detail info for equipment items
                    if (item.MapId.HasValue && !string.IsNullOrEmpty(item.Ssi))
                    {
                        try
                        {
                            var detail = await _gnjoyClient.FetchItemDetailAsync(
                                item.ServerId, item.MapId.Value, item.Ssi, ct);

                            if (detail != null)
                            {
                                item.ApplyDetailInfo(detail);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AutoSearch] Failed to get detail: {ex.Message}");
                        }
                    }

                    item.ComputeFields();  // Generate DisplayName and PriceFormatted
                    items.Add(item);

                    // 1 second delay per item (to avoid rate limiting)
                    await Task.Delay(AutoSearchItemDelayMs, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoSearch] Search error: {ex.Message}");
        }

        return (items, totalCount);
    }

    private bool IsMatchingItem(DealItem item, string filterText)
    {
        if (item == null || string.IsNullOrEmpty(filterText)) return false;

        // Check SlotInfo (cards, enchants)
        if (item.SlotInfo != null)
        {
            foreach (var slot in item.SlotInfo)
            {
                if (slot?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }

        // Check RandomOptions
        if (item.RandomOptions != null)
        {
            foreach (var option in item.RandomOptions)
            {
                if (option?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }

        return false;
    }

    private void CancelAutoSearch()
    {
        _autoSearchCts?.Cancel();
        _lblAutoSearchStatus.Text = "검색 중지 요청됨...";
    }

    private void ShowAutoSearchResults()
    {
        EnsureAutoSearchResultsForm();

        if (_autoSearchResultsForm!.Visible)
        {
            _autoSearchResultsForm.BringToFront();
            _autoSearchResultsForm.Focus();
        }
        else
        {
            var parentForm = _tabPage.FindForm();
            if (parentForm != null)
            {
                _autoSearchResultsForm.Show(parentForm);
            }
            else
            {
                _autoSearchResultsForm.Show();
            }
        }
    }

    private void EnsureAutoSearchResultsForm()
    {
        if (_autoSearchResultsForm == null || _autoSearchResultsForm.IsDisposed)
        {
            _autoSearchResultsForm = new AutoSearchResultsForm(_colors, _baseFontSize, _autoSearchDataDirectory);
            _autoSearchResultsForm.FormClosed += (s, e) => _autoSearchResultsForm = null;
        }
    }

    private void SetAutoSearchingState(bool searching)
    {
        _isAutoSearching = searching;

        _txtAutoSearchFilter.Enabled = !searching;
        _txtAutoSearchStartPage.Enabled = !searching;
        _txtAutoSearchEndPage.Enabled = !searching;
        _btnAutoSearch.Enabled = !searching;
        _btnAutoSearchCancel.Enabled = searching;

        // Also disable regular search controls during auto-search
        _btnDealSearchToolStrip.Enabled = !searching;
        _txtDealSearch.Enabled = !searching;
        _cboDealServer.Enabled = !searching;

        // Disable navigation buttons
        _btnFirst.Enabled = !searching && _dealCurrentPage > 1;
        _btnPrev100.Enabled = !searching && _dealCurrentPage > 50;
        _btnPrev10.Enabled = !searching && _dealCurrentPage > 10;
        _btnDealPrev.Enabled = !searching && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !searching && _dealCurrentPage < _totalPages;
        _btnNext10.Enabled = !searching && _dealCurrentPage + 10 <= _totalPages;
        _btnNext100.Enabled = !searching && _dealCurrentPage + 50 <= _totalPages;
        _btnLast.Enabled = !searching && _dealCurrentPage < _totalPages;
    }

    /// <summary>
    /// Enable or disable auto-search controls based on whether a search has been performed
    /// </summary>
    private void SetAutoSearchEnabled(bool enabled)
    {
        _txtAutoSearchFilter.Enabled = enabled;
        _txtAutoSearchStartPage.Enabled = enabled;
        _txtAutoSearchEndPage.Enabled = enabled;
        _btnAutoSearch.Enabled = enabled;
        // Results button is always enabled to view previous results
        // Cancel button is only enabled during auto-search
    }

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _autoSearchCts?.Cancel();
            _autoSearchCts?.Dispose();
            _autoSearchResultsForm?.Dispose();
            _dealBindingSource?.Dispose();
            _dgvDeals?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
