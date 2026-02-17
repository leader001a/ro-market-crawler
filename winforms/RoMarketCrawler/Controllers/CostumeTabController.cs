using System.Diagnostics;
using System.Text.Json;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Exceptions;
using RoMarketCrawler.Helpers;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Controllers;

/// <summary>
/// Controller for the Costume (의상) Tab
/// 2-step flow: (1) Crawl all pages → save to JSON, (2) Search locally on saved data
/// </summary>
public class CostumeTabController : BaseTabController
{
    #region Constants

    private const string CrawlSearchTerm = "의상";
    private const int ItemsPerPage = 10;
    private const int CrawlMaxPages = 1000;             // Safety limit
    private const int PageDelaySlowMs = 5000;            // 5s — 신규 비율 높을 때 (429 방지)
    private const int PageDelayFastMs = 1500;            // 1.5s — 신규 비율 낮을 때
    private const int NewItemDetailDelayMs = 1000;       // 1s — 신규 아이템 상세요청 후 딜레이
    private const int AutoCrawlIntervalMs = 300_000;     // 5분 자동 반복 주기

    #endregion

    #region Services

    private readonly IGnjoyClient _gnjoyClient;
    private readonly CrawlDataService _crawlDataService;
    private readonly IItemIndexService _itemIndexService;

    #endregion

    #region UI Controls

    // Toolbars

    // Crawl bar controls
    private ToolStripComboBox _cboServer = null!;
    private ToolStripButton _btnAutoCrawl = null!;
    private ToolStripButton _btnCrawlStop = null!;
    private ToolStripProgressBar _crawlProgressBar = null!;
    private ToolStripLabel _lblCrawlStatus = null!;

    // Crawl status strip (below search bar)
    private ToolStrip _crawlStatusStrip = null!;
    private ToolStrip _crawlStrip = null!;
    private TableLayoutPanel _topBarLayout = null!;

    // Search bar controls
    private ToolStrip _searchStrip = null!;
    private ToolStripTextBox _txtFilterItemName = null!;
    private ToolStripTextBox _txtFilterStone = null!;
    private ToolStripTextBox _txtFilterMinPrice = null!;
    private ToolStripTextBox _txtFilterMaxPrice = null!;
    private ToolStripButton _btnSearch = null!;

    // Results
    private DataGridView _dgvResults = null!;
    private readonly BindingSource _resultBindingSource;
    private Label _lblStatus = null!;
    private TableLayoutPanel _mainPanel = null!;
    private TableLayoutPanel _contentLayout = null!;
    private TableLayoutPanel _rightInnerLayout = null!;

    // Search history
    private FlowLayoutPanel _pnlSearchHistory = null!;

    // Monitor panel
    private Panel _monitorPanel = null!;
    private DataGridView _dgvWatch = null!;
    private ToolStripLabel _lblMonitorTitle = null!;
    private ToolStripButton _btnMute = null!;

    #endregion

    #region Events

    /// <summary>
    /// Raised when an item detail form needs to be shown
    /// </summary>
    public event EventHandler<DealItem>? ShowItemDetail;

    /// <summary>
    /// Raised when costume search history is modified
    /// </summary>
    public event EventHandler<List<CostumeSearchEntry>>? CostumeSearchHistoryChanged;

    #endregion

    #region Link Hit Areas

    private readonly Dictionary<(int row, int col), List<(Rectangle rect, string itemName)>> _linkHitAreas = new();
    private string? _hoveredLinkItem = null;
    private (int row, int col) _hoveredLinkKey;

    #endregion

    #region Pagination

    private List<DealItem> _allFilteredResults = new();
    private int _currentPage = 1;
    private int _totalCount = 0;
    private int _totalPages = 0;
    private const int PageSize = 50;
    private const int MaxVisiblePages = 10;

    private FlowLayoutPanel _pnlPagination = null!;
    private RoundedButton _btnFirst = null!;
    private RoundedButton _btnPrev10 = null!;
    private RoundedButton _btnPrev = null!;
    private RoundedButton _btnNext = null!;
    private RoundedButton _btnNext10 = null!;
    private RoundedButton _btnLast = null!;
    private Label _lblPageInfo = null!;

    #endregion

    #region State

    private CancellationTokenSource? _crawlCts;
    private bool _isCrawling = false;
    private CrawlSession? _currentSession;
    private readonly List<DealItem> _searchResults = new();

    // Auto-crawl state
    private System.Windows.Forms.Timer? _autoCrawlTimer;
    private bool _isAutoCrawling = false;
    private DateTime _lastCrawlFinishedAt;

    // Search history
    private const int MaxCostumeSearchHistoryCount = 10;
    private List<CostumeSearchEntry> _costumeSearchHistory = new();

    // Costume monitor
    private CostumeMonitorConfig _costumeMonitorConfig = new();
    private readonly string _costumeMonitorConfigPath;
    private List<(CostumeWatchItem Watch, List<DealItem> Matches)> _watchAlarmResults = new();
    private System.Windows.Forms.Timer? _alarmTimer;
    private bool _isSoundMuted = false;
    private AlarmSoundType _selectedAlarmSound = AlarmSoundType.SystemSound;
    private int _alarmIntervalSeconds = 5;

    #endregion

    /// <summary>
    /// Whether crawling is currently in progress
    /// </summary>
    public bool IsCrawling => _isCrawling;

    /// <inheritdoc/>
    public override bool HasActiveOperations => _isCrawling || _isAutoCrawling;

    public override string TabName => "의상검색";

    public CostumeTabController(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _gnjoyClient = GetService<IGnjoyClient>();
        _crawlDataService = GetService<CrawlDataService>();
        _itemIndexService = GetService<IItemIndexService>();
        _resultBindingSource = new BindingSource { DataSource = _searchResults };

        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        _costumeMonitorConfigPath = Path.Combine(dataDir, "CostumeMonitorConfig.json");
    }

    /// <summary>
    /// Set watermark image for the DataGridView
    /// </summary>
    public void SetWatermark(Image watermark) => ApplyWatermark(_dgvResults, watermark);

    public override void Initialize()
    {
        var scale = _baseFontSize / 12f;

        var stripHeight = Math.Max((int)(32 * scale), 28);

        // Create controls first so height constants are initialized
        _crawlStrip = CreateCrawlStrip();
        _searchStrip = CreateSearchStrip();
        _pnlSearchHistory = CreateSearchHistoryPanel();
        _monitorPanel = CreateMonitorPanel(); // Sets _monitorTitleBarHeight, _watchGridHeaderHeight, _watchGridRowHeight
        _dgvResults = CreateResultsGrid();
        var paginationPanel = CreatePaginationPanel();
        _lblStatus = CreateStatusLabel();
        _lblStatus.Text = "서버를 선택하고 데이터 수집을 시작하세요.";
        _lblStatus.Click += LblStatus_Click;

        // Crawl status strip (progress bar + status label)
        _crawlStatusStrip = new ToolStrip
        {
            Dock = DockStyle.Bottom,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };
        _crawlStatusStrip.Items.Add(_crawlProgressBar);
        _crawlStatusStrip.Items.Add(_lblCrawlStatus);

        // Top bar: 2 columns (crawl | search+history)
        // Left column panel: crawl strip (Top) + crawl status (Bottom)
        var crawlAreaPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 1, 0),
            Padding = new Padding(0),
            BackColor = _colors.Background
        };
        _crawlStrip.Dock = DockStyle.Top;
        crawlAreaPanel.Controls.Add(_crawlStatusStrip);  // Bottom dock
        crawlAreaPanel.Controls.Add(_crawlStrip);         // Top dock

        // Right column panel: search strip (Top) + search history (Top)
        var searchAreaPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = _colors.Background
        };
        _pnlSearchHistory.Dock = DockStyle.Top;
        _searchStrip.Dock = DockStyle.Top;
        searchAreaPanel.Controls.Add(_pnlSearchHistory);  // added first → docked below
        searchAreaPanel.Controls.Add(_searchStrip);        // added second → docked above

        _topBarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _topBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _topBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
        _topBarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ApplyTableLayoutPanelStyle(_topBarLayout);

        // Draw vertical grid line between columns (like DataGridView column divider)
        _topBarLayout.CellPaint += (s, e) =>
        {
            if (e.Column == 0)
            {
                var gridColor = _currentTheme == ThemeType.Dark ? _colors.Border : SystemColors.ControlDark;
                using var pen = new Pen(gridColor);
                var x = e.CellBounds.Right - 1;
                e.Graphics.DrawLine(pen, x, e.CellBounds.Top, x, e.CellBounds.Bottom);
            }
        };

        _topBarLayout.Controls.Add(crawlAreaPanel, 0, 0);
        _topBarLayout.Controls.Add(searchAreaPanel, 1, 0);

        // Content layout (1 col × 3 rows)
        var topBarHeight = stripHeight + 28;  // strip + search history max
        _contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, topBarHeight));                       // Row 0: Top bar (crawl+status | search+history)
        _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                                 // Row 1: Grid (Fill)
        _contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(55 * scale)));                  // Row 2: Pagination

        ApplyTableLayoutPanelStyle(_contentLayout);

        _contentLayout.Controls.Add(_topBarLayout, 0, 0);
        _contentLayout.Controls.Add(_dgvResults, 0, 1);
        _contentLayout.Controls.Add(paginationPanel, 0, 2);

        // Main panel: 1 col × 3 rows (monitor / spacer / search)
        _mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(5)
        };
        _mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 75));              // Row 0: Search
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));              // Row 1: Spacer
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));              // Row 2: Monitor

        ApplyTableLayoutPanelStyle(_mainPanel);

        _mainPanel.Controls.Add(_contentLayout, 0, 0);
        // Row 1 is spacer (empty)
        _mainPanel.Controls.Add(_monitorPanel, 0, 2);

        _tabPage.Controls.Add(_mainPanel);

        // Load costume monitor config
        _ = LoadCostumeMonitorConfigAsync();
    }

    /// <summary>
    /// Create crawl strip with server selector, auto-crawl button, stop button
    /// </summary>
    private ToolStrip CreateCrawlStrip()
    {
        var scale = _baseFontSize / 12f;

        var strip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        _cboServer = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = (int)(100 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "서버 선택"
        };
        foreach (var server in Server.GetAllServers())
        {
            if (server.Id > 0)
                _cboServer.Items.Add(server);
        }
        _cboServer.ComboBox.DisplayMember = "Name";
        if (_cboServer.Items.Count > 0)
            _cboServer.SelectedIndex = 0;
        _cboServer.SelectedIndexChanged += async (s, e) =>
        {
            if (_isAutoCrawling)
                StopAutoCrawl();
            _currentSession = null;
            _allFilteredResults.Clear();
            _btnSearch.Enabled = false;
            await TryLoadLatestDataAsync(autoDisplay: true);
        };

        _btnAutoCrawl = new ToolStripButton
        {
            Text = "자동 수집",
            BackColor = _colors.Accent,
            ForeColor = _colors.AccentText,
            ToolTipText = "자동 증분 수집 시작/중지 (5분 주기)"
        };
        _btnAutoCrawl.Click += (s, e) =>
        {
            if (_isAutoCrawling)
                StopAutoCrawl();
            else
                StartAutoCrawl();
        };

        _btnCrawlStop = new ToolStripButton
        {
            Text = "중지",
            Enabled = false,
            ToolTipText = "수집 중지"
        };
        _btnCrawlStop.Click += (s, e) => StopAutoCrawl();

        _crawlProgressBar = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visible = false,
            Size = new Size((int)(150 * scale), 16)
        };

        _lblCrawlStatus = new ToolStripLabel
        {
            Text = ""
        };

        strip.Items.Add(_cboServer);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnAutoCrawl);
        strip.Items.Add(_btnCrawlStop);

        return strip;
    }

    /// <summary>
    /// 검색 바 — 아이템:[____] 스톤:[____] 가격:[____]~[____] [검색]
    /// </summary>
    private ToolStrip CreateSearchStrip()
    {
        var scale = _baseFontSize / 12f;

        var strip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        // Item name filter
        _txtFilterItemName = new ToolStripTextBox
        {
            AutoSize = false,
            Width = (int)(120 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "아이템명 필터"
        };
        _txtFilterItemName.KeyDown += SearchFilter_KeyDown;

        // Stone filter
        _txtFilterStone = new ToolStripTextBox
        {
            AutoSize = false,
            Width = (int)(120 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "스톤 필터"
        };
        _txtFilterStone.KeyDown += SearchFilter_KeyDown;

        // Price range
        _txtFilterMinPrice = new ToolStripTextBox
        {
            AutoSize = false,
            Width = (int)(140 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "최소 가격"
        };
        _txtFilterMinPrice.KeyDown += SearchFilter_KeyDown;
        _txtFilterMinPrice.KeyPress += PriceTextBox_KeyPress;
        _txtFilterMinPrice.TextChanged += PriceTextBox_TextChanged;

        _txtFilterMaxPrice = new ToolStripTextBox
        {
            AutoSize = false,
            Width = (int)(140 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "최대 가격"
        };
        _txtFilterMaxPrice.KeyDown += SearchFilter_KeyDown;
        _txtFilterMaxPrice.KeyPress += PriceTextBox_KeyPress;
        _txtFilterMaxPrice.TextChanged += PriceTextBox_TextChanged;

        // Search button
        _btnSearch = new ToolStripButton
        {
            Text = "검색",
            BackColor = _colors.Accent,
            ForeColor = _colors.AccentText,
            Enabled = false,
            ToolTipText = "로컬 데이터 검색"
        };
        _btnSearch.Click += (s, e) => ExecuteLocalSearch();

        // Layout: 아이템:[____] 스톤:[____] 가격:[____]~[____] | [검색]
        var labelMargin = new Padding(6, 1, 0, 2);
        strip.Items.Add(new ToolStripLabel("아이템:") { Margin = labelMargin });
        strip.Items.Add(_txtFilterItemName);
        strip.Items.Add(new ToolStripLabel("스톤:") { Margin = labelMargin });
        strip.Items.Add(_txtFilterStone);
        strip.Items.Add(new ToolStripLabel("가격:") { Margin = labelMargin });
        strip.Items.Add(_txtFilterMinPrice);
        strip.Items.Add(new ToolStripLabel("~"));
        strip.Items.Add(_txtFilterMaxPrice);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnSearch);

        return strip;
    }

    private DataGridView CreateResultsGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            DataSource = _resultBindingSource
        };
        ApplyDataGridViewStyle(dgv);

        dgv.CellPainting += DgvResults_CellPainting;
        dgv.CellFormatting += DgvResults_CellFormatting;
        dgv.CellDoubleClick += DgvResults_CellDoubleClick;
        dgv.CellMouseClick += DgvResults_CellMouseClick;
        dgv.CellMouseMove += DgvResults_CellMouseMove;
        dgv.CellMouseLeave += (s, e) =>
        {
            if (_hoveredLinkItem != null)
            {
                _hoveredLinkItem = null;
                dgv.Cursor = Cursors.Default;
            }
        };
        dgv.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                var colName = dgv.Columns[e.ColumnIndex].Name;
                if (colName == "SlotAndOptionsDisplay")
                {
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

        SetupGridColumns(dgv);
        return dgv;
    }

    private void SetupGridColumns(DataGridView dgv)
    {
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn
            {
                Name = "DealTypeDisplay",
                HeaderText = "유형",
                DataPropertyName = "DealTypeDisplay",
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 7,
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
                HeaderText = "스톤",
                DataPropertyName = "SlotAndOptionsDisplay",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 25,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "수량",
                DataPropertyName = "Quantity",
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 7,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "PriceFormatted",
                HeaderText = "가격",
                DataPropertyName = "PriceFormatted",
                MinimumWidth = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 12,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ShopName",
                HeaderText = "상점명",
                DataPropertyName = "ShopName",
                MinimumWidth = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 12
            },
            new DataGridViewTextBoxColumn
            {
                Name = "CrawledPage",
                HeaderText = "P",
                DataPropertyName = "CrawledPage",
                MinimumWidth = 30,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 5,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ToolTipText = "수집 페이지 번호"
            }
        });
    }

    #region Grid Events

    private void DgvResults_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle == null || e.RowIndex < 0) return;

        var columnName = _dgvResults.Columns[e.ColumnIndex].Name;

        if (columnName == "DealTypeDisplay" && e.Value != null)
        {
            var value = e.Value.ToString();
            if (value == "판매")
                e.CellStyle.ForeColor = _colors.SaleColor;
            else if (value == "구매")
                e.CellStyle.ForeColor = _colors.BuyColor;
        }
    }

    private void DgvResults_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        if (e.RowIndex >= _searchResults.Count) return;

        var selectedItem = _searchResults[e.RowIndex];
        Debug.WriteLine($"[CostumeTab] Opening detail for: {selectedItem.DisplayName}");

        ShowItemDetail?.Invoke(this, selectedItem);
    }

    private void DgvResults_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics == null) return;

        // Header painting
        if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            e.PaintBackground(e.ClipBounds, true);
            TextRenderer.DrawText(
                e.Graphics,
                e.FormattedValue?.ToString() ?? "",
                e.CellStyle?.Font ?? _dgvResults.Font,
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
            var columnName = _dgvResults.Columns[e.ColumnIndex].Name;
            if (columnName == "SlotAndOptionsDisplay")
            {
                if (e.RowIndex >= _searchResults.Count) return;

                var dealItem = _searchResults[e.RowIndex];
                var slotItems = dealItem.SlotInfo ?? new List<string>();
                var randomOptions = dealItem.RandomOptions ?? new List<string>();

                if (slotItems.Count == 0 && randomOptions.Count == 0) return;

                e.PaintBackground(e.ClipBounds, true);

                var font = e.CellStyle?.Font ?? _dgvResults.DefaultCellStyle.Font ?? _dgvResults.Font;
                var linkFont = new Font(font, FontStyle.Underline);
                var padding = e.CellStyle?.Padding ?? new Padding(2);
                var x = e.CellBounds.X + padding.Left + 3;
                var y = e.CellBounds.Y + padding.Top + 2;

                var hitAreas = new List<(Rectangle rect, string itemName)>();
                var key = (e.RowIndex, e.ColumnIndex);

                var lineHeight = TextRenderer.MeasureText(e.Graphics, "Test", font).Height;

                // Draw SlotInfo items as links
                foreach (var item in slotItems)
                {
                    var textSize = TextRenderer.MeasureText(e.Graphics, item, linkFont);
                    var textRect = new Rectangle(x, y, textSize.Width, textSize.Height);

                    var isHovered = _hoveredLinkItem == item && key == _hoveredLinkKey;
                    var linkColor = isHovered ? _colors.Accent : _colors.LinkColor;

                    TextRenderer.DrawText(e.Graphics, item, linkFont, textRect, linkColor, TextFormatFlags.Left);

                    hitAreas.Add((textRect, item));
                    y += lineHeight + 1;
                }

                // Draw RandomOptions as plain text
                foreach (var option in randomOptions)
                {
                    var textSize = TextRenderer.MeasureText(e.Graphics, option, font);
                    var textRect = new Rectangle(x, y, textSize.Width, textSize.Height);
                    TextRenderer.DrawText(e.Graphics, option, font, textRect, _colors.TextMuted, TextFormatFlags.Left);

                    y += lineHeight + 1;
                }

                _linkHitAreas[key] = hitAreas;

                linkFont.Dispose();
                e.Handled = true;
            }
        }
    }

    private void DgvResults_CellMouseMove(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _dgvResults.Columns[e.ColumnIndex].Name;
        if (columnName != "SlotAndOptionsDisplay")
        {
            if (_hoveredLinkItem != null)
            {
                _hoveredLinkItem = null;
                _dgvResults.Cursor = Cursors.Default;
                _dgvResults.InvalidateCell(e.ColumnIndex, e.RowIndex);
            }
            return;
        }

        var key = (e.RowIndex, e.ColumnIndex);
        if (!_linkHitAreas.TryGetValue(key, out var hitAreas)) return;

        var cellRect = _dgvResults.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
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
            _dgvResults.Cursor = hoveredItem != null ? Cursors.Hand : Cursors.Default;
            _dgvResults.InvalidateCell(e.ColumnIndex, e.RowIndex);
        }
    }

    private void DgvResults_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.Button != MouseButtons.Left) return;

        var columnName = _dgvResults.Columns[e.ColumnIndex].Name;
        if (columnName != "SlotAndOptionsDisplay") return;

        if (_dgvResults.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected)
        {
            _dgvResults.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = false;
        }

        var key = (e.RowIndex, e.ColumnIndex);
        if (!_linkHitAreas.TryGetValue(key, out var hitAreas)) return;

        var cellRect = _dgvResults.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
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
        Debug.WriteLine($"[CostumeTab] Looking up item: {itemName}");

        var foundItem = _itemIndexService.SearchItems(itemName, new HashSet<int> { 999 }, 0, 1, false).FirstOrDefault();

        if (foundItem != null)
        {
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

    #endregion

    #region Pagination

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
        _btnFirst.Click += (s, e) => GoToPage(1);

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
        _btnPrev10.Click += (s, e) => GoToPage(_currentPage - 10);

        _btnPrev = new RoundedButton
        {
            Text = "<",
            Size = new Size(32, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 5, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnPrev, false);
        _btnPrev.Click += (s, e) => PreviousPage();

        _btnNext = new RoundedButton
        {
            Text = ">",
            Size = new Size(32, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0),
            Tag = "NavButton"
        };
        ApplyRoundedButtonStyle(_btnNext, false);
        _btnNext.Click += (s, e) => NextPage();

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
        _btnNext10.Click += (s, e) => GoToPage(_currentPage + 10);

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
        _btnLast.Click += (s, e) => GoToPage(_totalPages);

        _lblPageInfo = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(5, 5, 0, 0)
        };

        _pnlPagination.Controls.Add(_btnFirst);
        _pnlPagination.Controls.Add(_btnPrev10);
        _pnlPagination.Controls.Add(_btnPrev);
        // Page number buttons will be added dynamically
        _pnlPagination.Controls.Add(_btnNext);
        _pnlPagination.Controls.Add(_btnNext10);
        _pnlPagination.Controls.Add(_btnLast);
        _pnlPagination.Controls.Add(_lblPageInfo);

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
            button.ApplySecondaryStyle(
                SystemColors.Control,
                SystemColors.ControlLight,
                SystemColors.ControlDark,
                SystemColors.ControlText,
                SystemColors.ControlDark);
        }
    }

    private void GoToPage(int page)
    {
        if (page >= 1 && page <= _totalPages && page != _currentPage)
        {
            _currentPage = page;
            DisplayCurrentPage();
        }
    }

    private void NextPage()
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            DisplayCurrentPage();
        }
    }

    private void PreviousPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            DisplayCurrentPage();
        }
    }

    private void DisplayCurrentPage()
    {
        var skip = (_currentPage - 1) * PageSize;
        var pageItems = _allFilteredResults.Skip(skip).Take(PageSize).ToList();

        _searchResults.Clear();
        _searchResults.AddRange(pageItems);
        _resultBindingSource.DataSource = null;
        _resultBindingSource.DataSource = _searchResults;
        _dgvResults.Refresh();
        _linkHitAreas.Clear();

        UpdatePaginationUI();

        _lblStatus.Text = _totalCount > 0
            ? $"검색 결과: {_totalCount}건 중 {_currentPage}/{_totalPages}페이지" +
              $"  |  {_currentSession!.ServerName}, {_currentSession.CrawledAt:yyyy-MM-dd HH:mm} 수집"
            : "검색 결과가 없습니다.";
    }

    private void UpdatePaginationUI()
    {
        _lblPageInfo.Text = _totalCount > 0
            ? $"{_currentPage}/{_totalPages} ({_totalCount:N0}건)"
            : "";

        var canGoPrev = _currentPage > 1;
        var canGoNext = _currentPage < _totalPages;

        _btnFirst.Enabled = canGoPrev;
        _btnPrev10.Enabled = _currentPage > 10;
        _btnPrev.Enabled = canGoPrev;

        _btnNext.Enabled = canGoNext;
        _btnNext10.Enabled = _currentPage + 10 <= _totalPages;
        _btnLast.Enabled = canGoNext;

        RebuildPageNumberButtons();
        CenterPaginationPanel();
    }

    private void RebuildPageNumberButtons()
    {
        var toRemove = _pnlPagination.Controls.Cast<Control>()
            .Where(c => c.Tag is string s && s == "PageButton")
            .ToList();
        foreach (var ctrl in toRemove)
        {
            _pnlPagination.Controls.Remove(ctrl);
            ctrl.Dispose();
        }

        if (_totalPages <= 0) return;

        int startPage, endPage;
        if (_totalPages <= MaxVisiblePages)
        {
            startPage = 1;
            endPage = _totalPages;
        }
        else
        {
            int half = MaxVisiblePages / 2;
            startPage = Math.Max(1, _currentPage - half);
            endPage = startPage + MaxVisiblePages - 1;

            if (endPage > _totalPages)
            {
                endPage = _totalPages;
                startPage = Math.Max(1, endPage - MaxVisiblePages + 1);
            }
        }

        int insertIndex = _pnlPagination.Controls.IndexOf(_btnPrev) + 1;

        var scale = _baseFontSize / 12f;
        var btnHeight = (int)(28 * scale);

        for (int page = startPage; page <= endPage; page++)
        {
            var pageNum = page;
            var isCurrentPage = page == _currentPage;

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
                ApplyRoundedButtonStyle(btn, true);
                btn.Enabled = false;
            }
            else
            {
                ApplyRoundedButtonStyle(btn, false);
                btn.Click += (s, e) => GoToPage(pageNum);
            }

            _pnlPagination.Controls.Add(btn);
            _pnlPagination.Controls.SetChildIndex(btn, insertIndex++);
        }
    }

    private void CenterPaginationPanel()
    {
        if (_pnlPagination == null) return;

        int totalWidth = 0;
        foreach (Control ctrl in _pnlPagination.Controls)
        {
            totalWidth += ctrl.Width + ctrl.Margin.Horizontal;
        }

        var leftPadding = Math.Max(0, (_pnlPagination.Width - totalWidth) / 2);
        _pnlPagination.Padding = new Padding(leftPadding, _pnlPagination.Padding.Top, 0, 0);
    }

    #endregion

    #region Crawling

    private void SetCrawlingState(bool crawling)
    {
        _isCrawling = crawling;
        _cboServer.Enabled = !crawling && !_isAutoCrawling;
        _btnAutoCrawl.Enabled = !crawling || _isAutoCrawling;
        _btnCrawlStop.Enabled = crawling || _isAutoCrawling;
        _btnSearch.Enabled = !crawling && _currentSession != null;

        // Reset status label color only when starting crawl (preserves red color after rate limit error)
        if (crawling)
            _lblCrawlStatus.ForeColor = _colors.Text;

        // Update auto-crawl button appearance
        if (_isAutoCrawling)
        {
            _btnAutoCrawl.Text = "자동 수집 중";
            _btnAutoCrawl.BackColor = _colors.SaleColor;
            _btnAutoCrawl.ForeColor = Color.White;
        }
        else
        {
            _btnAutoCrawl.Text = "자동 수집";
            _btnAutoCrawl.BackColor = _colors.Accent;
            _btnAutoCrawl.ForeColor = _colors.AccentText;
        }
    }

    /// <summary>
    /// 증분 크롤링 중단 시, 아직 순회하지 않은 페이지의 기존 아이템을 allItems에 병합하여 데이터 손실 방지
    /// </summary>
    private static void MergeRemainingItems(bool isIncremental, List<DealItem> allItems, Dictionary<string, DealItem> existingBySsi)
    {
        if (!isIncremental || existingBySsi.Count == 0) return;

        var processedSsis = new HashSet<string>(
            allItems.Where(i => !string.IsNullOrEmpty(i.Ssi)).Select(i => i.Ssi!));

        foreach (var kvp in existingBySsi)
        {
            if (!processedSsis.Contains(kvp.Key))
                allItems.Add(kvp.Value);
        }
    }

    private void SavePartialSession(Server server, List<DealItem> items, int lastPage, int totalServerPages)
    {
        if (items.Count == 0) return;

        var session = new CrawlSession
        {
            SearchTerm = CrawlSearchTerm,
            ServerName = server.Name,
            ServerId = server.Id,
            CrawledAt = DateTime.Now,
            TotalPages = lastPage,
            TotalItems = items.Count,
            Items = items,
            LastCrawledPage = lastPage,
            TotalServerPages = totalServerPages
        };

        // Fire-and-forget save (UI thread, no await needed for partial save)
        _ = _crawlDataService.SaveAsync(session);

        _currentSession = session;
        _btnSearch.Enabled = true;
        UpdateStatusBar();
    }

    #region Auto / Incremental Crawl

    private void StartAutoCrawl()
    {
        var selectedServer = _cboServer.SelectedItem as Server;
        if (selectedServer == null)
        {
            MessageBox.Show("서버를 선택해주세요.", "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _isAutoCrawling = true;
        SetCrawlingState(_isCrawling);

        _autoCrawlTimer = new System.Windows.Forms.Timer { Interval = AutoCrawlIntervalMs };
        _autoCrawlTimer.Tick += async (s, e) =>
        {
            if (!_isCrawling)
                await RunIncrementalCrawlAsync();
        };

        // Immediately run the first crawl
        _ = RunIncrementalCrawlAsync();
        _autoCrawlTimer.Start();
    }

    private void StopAutoCrawl()
    {
        _isAutoCrawling = false;
        _autoCrawlTimer?.Stop();
        _autoCrawlTimer?.Dispose();
        _autoCrawlTimer = null;

        if (_isCrawling)
        {
            _crawlCts?.Cancel();
            _lblCrawlStatus.Text = "자동 수집 중지됨";
        }
        else
        {
            _lblCrawlStatus.Text = "자동 수집 중지됨";
        }

        SetCrawlingState(false);
        UpdateStatusBar();
    }

    private async Task RunIncrementalCrawlAsync()
    {
        var selectedServer = _cboServer.SelectedItem as Server;
        if (selectedServer == null) return;

        // If no existing session, fall back to full crawl mode
        bool isIncremental = _currentSession?.Items != null && _currentSession.Items.Count > 0
            && _currentSession.ServerId == selectedServer.Id;

        // Build SSI dictionary from existing session for incremental mode
        var existingBySsi = new Dictionary<string, DealItem>();
        if (isIncremental)
        {
            foreach (var item in _currentSession!.Items)
            {
                if (!string.IsNullOrEmpty(item.Ssi))
                    existingBySsi.TryAdd(item.Ssi, item);
            }
        }

        // Load persistent detail cache (survives app restart)
        var detailCache = _crawlDataService.LoadDetailCache(selectedServer.Id);

        _crawlCts?.Dispose();
        _crawlCts = new CancellationTokenSource();
        var ct = _crawlCts.Token;
        SetCrawlingState(true);

        var allItems = new List<DealItem>();
        var seenSsis = new HashSet<string>();
        int newCount = 0, updatedCount = 0, cacheHitCount = 0, pageItemCount = 0, pageNewCount = 0;
        int currentPage = 1;
        int maxEndPage = CrawlMaxPages;
        int totalCount = 0;
        bool detailCacheSaved = false;
        var startTime = DateTime.Now;

        _crawlProgressBar.Value = 0;
        _crawlProgressBar.Visible = true;

        // (modeLabel removed — user-facing status no longer shows crawl mode)

        try
        {
            while (currentPage <= maxEndPage)
            {
                ct.ThrowIfCancellationRequested();

                // Update progress
                if (maxEndPage < CrawlMaxPages && maxEndPage > 0)
                {
                    int progressPercent = (int)((currentPage - 1) * 100.0 / maxEndPage);
                    _crawlProgressBar.Value = Math.Min(progressPercent, 100);
                }

                _lblCrawlStatus.Text = $"{currentPage}/{(maxEndPage < CrawlMaxPages ? maxEndPage.ToString() : "?")} " +
                    $"수집 중... ({allItems.Count}건)";

                // Fetch page listing
                var result = await _gnjoyClient.SearchItemDealsWithCountAsync(
                    CrawlSearchTerm, selectedServer.Id, currentPage, ct);

                if (result.TotalCount > 0 && result.Items != null)
                {
                    totalCount = result.TotalCount;
                    int actualTotalPages = (int)Math.Ceiling((double)totalCount / ItemsPerPage);
                    if (actualTotalPages > 0)
                        maxEndPage = Math.Min(CrawlMaxPages, actualTotalPages);

                    pageItemCount = result.Items.Count;
                    pageNewCount = 0;

                    foreach (var item in result.Items)
                    {
                        ct.ThrowIfCancellationRequested();

                        // Skip duplicate SSI (items can shift between pages during long crawls)
                        if (!string.IsNullOrEmpty(item.Ssi) && !seenSsis.Add(item.Ssi))
                            continue;

                        if (isIncremental && !string.IsNullOrEmpty(item.Ssi)
                            && existingBySsi.TryGetValue(item.Ssi, out var existing))
                        {
                            // Known item: reuse detail info, update price/quantity
                            existing.Price = item.Price;
                            existing.PriceFormatted = null; // force recompute
                            existing.Quantity = item.Quantity;
                            existing.CrawledAt = DateTime.Now;
                            existing.CrawledPage = currentPage;
                            existing.ComputeFields();
                            allItems.Add(existing);
                            existingBySsi.Remove(item.Ssi); // remove so live session won't duplicate
                            updatedCount++;
                            // No delay — no detail request needed
                        }
                        else
                        {
                            // New item: check persistent detail cache first, then fetch from API
                            bool fetchedFromApi = false;
                            if (item.MapId.HasValue && !string.IsNullOrEmpty(item.Ssi))
                            {
                                if (detailCache.TryGetValue(item.Ssi, out var cachedDetail))
                                {
                                    // Cache hit: apply without API request
                                    item.ApplyDetailInfo(cachedDetail);
                                    cacheHitCount++;
                                }
                                else
                                {
                                    // Cache miss: fetch from API
                                    try
                                    {
                                        var detail = await _gnjoyClient.FetchItemDetailAsync(
                                            item.ServerId, item.MapId.Value, item.Ssi, ct);
                                        if (detail != null)
                                        {
                                            item.ApplyDetailInfo(detail);
                                            detailCache[item.Ssi] = detail;
                                        }
                                    }
                                    catch (OperationCanceledException) { throw; }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[CostumeTab] Incremental detail fetch failed: {ex.Message}");
                                    }

                                    fetchedFromApi = true;
                                    await Task.Delay(NewItemDetailDelayMs, ct);
                                }
                            }

                            item.CrawledPage = currentPage;
                            item.ComputeFields();
                            allItems.Add(item);
                            newCount++;
                            if (fetchedFromApi) pageNewCount++; // Only count API requests for adaptive delay
                        }
                    }
                }
                else if (currentPage == 1)
                {
                    // No results on first page — nothing to do
                    break;
                }

                // Update progress bar
                if (maxEndPage > 0 && maxEndPage < CrawlMaxPages)
                {
                    int progress = (int)(currentPage * 100.0 / maxEndPage);
                    _crawlProgressBar.Value = Math.Min(progress, 100);
                }

                // Update live session so search works during crawl
                // Include not-yet-crawled items from previous session
                var liveItems = allItems.ToList();
                if (isIncremental && existingBySsi.Count > 0)
                {
                    liveItems.AddRange(existingBySsi.Values);
                }
                _currentSession = new CrawlSession
                {
                    SearchTerm = CrawlSearchTerm,
                    ServerName = selectedServer.Name,
                    ServerId = selectedServer.Id,
                    CrawledAt = DateTime.Now,
                    TotalPages = currentPage,
                    TotalItems = liveItems.Count,
                    Items = liveItems,
                    LastCrawledPage = currentPage,
                    TotalServerPages = maxEndPage
                };
                _btnSearch.Enabled = true;

                currentPage++;

                // Adaptive page delay: high new-item ratio → slower (avoid 429)
                if (currentPage <= maxEndPage)
                {
                    bool highNewRatio = pageItemCount > 0 && pageNewCount * 2 >= pageItemCount;
                    var pageDelay = highNewRatio ? PageDelaySlowMs : PageDelayFastMs;
                    await Task.Delay(pageDelay, ct);
                }
            }

            // Save session — items not in allItems are automatically removed
            _crawlProgressBar.Value = 100;
            _lblCrawlStatus.Text = "저장 중...";

            var session = new CrawlSession
            {
                SearchTerm = CrawlSearchTerm,
                ServerName = selectedServer.Name,
                ServerId = selectedServer.Id,
                CrawledAt = DateTime.Now,
                TotalPages = currentPage - 1,
                TotalItems = allItems.Count,
                Items = allItems,
                LastCrawledPage = currentPage - 1,
                TotalServerPages = maxEndPage,
                IsIncremental = isIncremental
            };

            await _crawlDataService.SaveAsync(session);
            _currentSession = session;
            _btnSearch.Enabled = true;

            // Save detail cache — merge with disk to preserve entries added by other tabs during crawl
            var freshCache = _crawlDataService.LoadDetailCache(selectedServer.Id);
            foreach (var kvp in detailCache)
                freshCache[kvp.Key] = kvp.Value;
            _crawlDataService.SaveDetailCache(selectedServer.Id, freshCache);
            detailCacheSaved = true;

            // Clean up old timestamped files only after successful complete crawl
            _crawlDataService.CleanupOldTimestampedFiles(CrawlSearchTerm, selectedServer.Name);

            var totalElapsed = DateTime.Now - startTime;
            var elapsedText = totalElapsed.TotalSeconds >= 60
                ? $"{(int)totalElapsed.TotalMinutes}분 {(int)totalElapsed.TotalSeconds % 60}초"
                : $"{(int)totalElapsed.TotalSeconds}초";

            _lastCrawlFinishedAt = DateTime.Now;

            var nextText = "";
            if (_isAutoCrawling)
            {
                var nextTime = _lastCrawlFinishedAt.AddMilliseconds(AutoCrawlIntervalMs);
                nextText = $" | 다음 수집: {nextTime:HH:mm}";
            }
            _lblCrawlStatus.Text = $"완료: {allItems.Count}건 ({elapsedText}){nextText}";
            UpdateStatusBar();

            // Check watch conditions after crawl completes
            CheckWatchConditions();

            // Restart timer from now so next crawl is exactly 5 min after completion
            if (_isAutoCrawling && _autoCrawlTimer != null)
            {
                _autoCrawlTimer.Stop();
                _autoCrawlTimer.Start();
            }
        }
        catch (OperationCanceledException)
        {
            MergeRemainingItems(isIncremental, allItems, existingBySsi);
            _lblCrawlStatus.Text = $"중지됨: {allItems.Count}건";

            if (allItems.Count > 0)
                SavePartialSession(selectedServer, allItems, currentPage - 1, maxEndPage);
        }
        catch (RateLimitException rateLimitEx)
        {
            MergeRemainingItems(isIncremental, allItems, existingBySsi);

            if (allItems.Count > 0)
                SavePartialSession(selectedServer, allItems, currentPage - 1, maxEndPage);

            // Stop auto-crawl first (overwrites status text with "자동 수집 중지됨")
            if (_isAutoCrawling)
                StopAutoCrawl();

            // Then set rate limit status (overrides StopAutoCrawl's text)
            _lblCrawlStatus.Text = $"API 제한: {rateLimitEx.UnlockTimeText} 이후 이용 가능";
            _lblCrawlStatus.ForeColor = _colors.SaleColor;

            MessageBox.Show(
                $"GNJOY API 요청 제한이 적용되었습니다.\n\n" +
                $"이용 가능 시간: {rateLimitEx.UnlockTimeText}\n" +
                $"수집 진행: {currentPage - 1}페이지, {allItems.Count}건\n\n" +
                $"자동 수집이 중지되었습니다.",
                "API 요청 제한",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MergeRemainingItems(isIncremental, allItems, existingBySsi);
            _lblCrawlStatus.Text = $"오류: {ex.Message}";
            Debug.WriteLine($"[CostumeTab] Incremental crawl error: {ex}");

            if (allItems.Count > 0)
                SavePartialSession(selectedServer, allItems, currentPage - 1, maxEndPage);
        }
        finally
        {
            // Save detail cache on partial exit — merge with disk to preserve entries from other tabs
            if (!detailCacheSaved && detailCache.Count > 0)
            {
                var partialFreshCache = _crawlDataService.LoadDetailCache(selectedServer.Id);
                foreach (var kvp in detailCache)
                    partialFreshCache[kvp.Key] = kvp.Value;
                _crawlDataService.SaveDetailCache(selectedServer.Id, partialFreshCache);
            }

            _crawlProgressBar.Visible = false;
            SetCrawlingState(false);
        }
    }

    #endregion

    #endregion

    #region Local Search

    private void SearchFilter_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ExecuteLocalSearch();
        }
    }

    private void PriceTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        // Allow only digits, backspace, and control characters (Ctrl+A, Ctrl+C, Ctrl+V, etc.)
        if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
        {
            e.Handled = true;
        }
    }

    private bool _isPriceFormatting = false;
    private void PriceTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (_isPriceFormatting || sender is not ToolStripTextBox txt) return;
        _isPriceFormatting = true;
        try
        {
            var raw = txt.Text.Replace(",", "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                _isPriceFormatting = false;
                return;
            }
            // Keep only digits
            raw = new string(raw.Where(char.IsDigit).ToArray());
            if (raw.Length == 0)
            {
                txt.Text = "";
                _isPriceFormatting = false;
                return;
            }
            if (long.TryParse(raw, out var value))
            {
                var formatted = value.ToString("N0");
                if (txt.Text != formatted)
                {
                    txt.Text = formatted;
                    txt.SelectionStart = txt.Text.Length;
                }
            }
        }
        finally
        {
            _isPriceFormatting = false;
        }
    }

    private void ExecuteLocalSearch()
    {
        if (_currentSession == null)
        {
            MessageBox.Show("먼저 데이터 수집을 실행해주세요.", "검색 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Build filter
        var filter = new CrawlSearchFilter
        {
            ItemName = string.IsNullOrWhiteSpace(_txtFilterItemName.Text) ? null : _txtFilterItemName.Text.Trim(),
            CardEnchant = string.IsNullOrWhiteSpace(_txtFilterStone.Text) ? null : _txtFilterStone.Text.Trim()
        };

        // Parse price range
        if (!string.IsNullOrWhiteSpace(_txtFilterMinPrice.Text))
        {
            if (long.TryParse(_txtFilterMinPrice.Text.Trim().Replace(",", ""), out var minPrice))
                filter.MinPrice = minPrice;
        }
        if (!string.IsNullOrWhiteSpace(_txtFilterMaxPrice.Text))
        {
            if (long.TryParse(_txtFilterMaxPrice.Text.Trim().Replace(",", ""), out var maxPrice))
                filter.MaxPrice = maxPrice;
        }

        var hasFilter = !string.IsNullOrEmpty(filter.ItemName) ||
                       !string.IsNullOrEmpty(filter.CardEnchant) ||
                       filter.MinPrice.HasValue ||
                       filter.MaxPrice.HasValue;

        // Validate minimum character length for text filters
        if (!string.IsNullOrEmpty(filter.CardEnchant))
        {
            var clean = System.Text.RegularExpressions.Regex.Replace(filter.CardEnchant, @"[^a-zA-Z0-9가-힣]", "");
            if (clean.Length < 2)
            {
                MessageBox.Show("스톤 검색은 2글자 이상 입력해주세요.\n(특수문자, 공백 제외)", "입력 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtFilterStone.Focus();
                return;
            }
        }

        // Execute search
        List<DealItem> results;
        if (!hasFilter)
        {
            results = _currentSession.Items?.ToList() ?? new List<DealItem>();
        }
        else
        {
            results = _crawlDataService.Search(_currentSession, filter);
        }

        // Store all results for pagination
        _allFilteredResults.Clear();
        _allFilteredResults.AddRange(results);
        _totalCount = _allFilteredResults.Count;
        _totalPages = (int)Math.Ceiling((double)_totalCount / PageSize);
        _currentPage = 1;
        DisplayCurrentPage();

        // Add to search history if there's a meaningful filter
        if (!string.IsNullOrEmpty(filter.CardEnchant) || !string.IsNullOrEmpty(filter.ItemName))
        {
            AddToCostumeSearchHistory(filter.CardEnchant, filter.ItemName);
        }
    }

    #endregion

    #region Search History

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

    /// <summary>
    /// Set search history (loaded from settings)
    /// </summary>
    public void LoadCostumeSearchHistory(List<CostumeSearchEntry> history)
    {
        _costumeSearchHistory = history ?? new List<CostumeSearchEntry>();
        if (_pnlSearchHistory != null)
        {
            UpdateCostumeSearchHistoryPanel();
        }
    }

    private void AddToCostumeSearchHistory(string? stoneName, string? itemName)
    {
        if (string.IsNullOrWhiteSpace(stoneName) && string.IsNullOrWhiteSpace(itemName)) return;

        var entry = new CostumeSearchEntry
        {
            StoneName = string.IsNullOrWhiteSpace(stoneName) ? null : stoneName.Trim(),
            ItemName = string.IsNullOrWhiteSpace(itemName) ? null : itemName.Trim()
        };

        // Remove duplicate
        _costumeSearchHistory.RemoveAll(h =>
            string.Equals(h.StoneName, entry.StoneName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(h.ItemName, entry.ItemName, StringComparison.OrdinalIgnoreCase));

        _costumeSearchHistory.Insert(0, entry);

        while (_costumeSearchHistory.Count > MaxCostumeSearchHistoryCount)
            _costumeSearchHistory.RemoveAt(_costumeSearchHistory.Count - 1);

        UpdateCostumeSearchHistoryPanel();
        CostumeSearchHistoryChanged?.Invoke(this, _costumeSearchHistory);
    }

    private void UpdateCostumeSearchHistoryPanel()
    {
        if (_pnlSearchHistory == null) return;

        // Keep title label, remove history buttons
        while (_pnlSearchHistory.Controls.Count > 1)
        {
            var ctrl = _pnlSearchHistory.Controls[1];
            _pnlSearchHistory.Controls.RemoveAt(1);
            ctrl.Dispose();
        }

        foreach (var entry in _costumeSearchHistory)
        {
            var btn = new Label
            {
                Text = entry.DisplayText,
                AutoSize = true,
                ForeColor = _colors.Accent,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 3, 10, 0),
                Tag = entry
            };
            btn.Click += (s, e) =>
            {
                if (s is Label lbl && lbl.Tag is CostumeSearchEntry hist)
                {
                    _txtFilterStone.Text = hist.StoneName ?? "";
                    _txtFilterItemName.Text = hist.ItemName ?? "";
                    ExecuteLocalSearch();
                }
            };
            btn.MouseEnter += (s, e) => { if (s is Label lbl) lbl.Font = new Font(lbl.Font, FontStyle.Underline); };
            btn.MouseLeave += (s, e) => { if (s is Label lbl) lbl.Font = new Font(lbl.Font, FontStyle.Regular); };
            _pnlSearchHistory.Controls.Add(btn);
        }

        var hasHistory = _costumeSearchHistory.Count > 0;

        _pnlSearchHistory.Visible = hasHistory;

        // Update top bar height to accommodate search history
        if (_contentLayout != null && _contentLayout.RowStyles.Count > 0)
        {
            var scale = _baseFontSize / 12f;
            var stripHeight = Math.Max((int)(32 * scale), 28);
            _contentLayout.RowStyles[0].Height = stripHeight + (hasHistory ? 28 : 0);
        }
    }

    #endregion

    #region Costume Monitor

    private int _monitorTitleBarHeight;
    private int _watchGridHeaderHeight;
    private int _watchGridRowHeight;

    private Panel CreateMonitorPanel()
    {
        var scale = _baseFontSize / 12f;

        // Pre-calculate all heights used consistently everywhere
        _monitorTitleBarHeight = Math.Max((int)(34 * scale), 30);
        _watchGridHeaderHeight = Math.Max((int)(28 * scale), 24);
        _watchGridRowHeight = Math.Max((int)(28 * scale), 24);

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Panel,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        // Title bar (ToolStrip for consistent button style with crawl strip)
        var titleBar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        _lblMonitorTitle = new ToolStripLabel
        {
            Text = GetMonitorTitleText(),
            ForeColor = _colors.TextMuted
        };
        titleBar.Items.Add(_lblMonitorTitle);
        titleBar.Items.Add(new ToolStripSeparator());

        var btnAdd = new ToolStripButton
        {
            Text = "+ 추가",
            BackColor = _colors.Accent,
            ForeColor = _colors.AccentText,
            ToolTipText = "감시 항목 추가"
        };
        btnAdd.Click += (s, e) => AddWatchItem();
        titleBar.Items.Add(btnAdd);
        titleBar.Items.Add(new ToolStripSeparator());

        _btnMute = new ToolStripButton
        {
            Text = _isSoundMuted ? "음소거 해제" : "음소거",
            ForeColor = _isSoundMuted ? _colors.SaleColor : _colors.Text,
            ToolTipText = "알람 음소거 토글"
        };
        _btnMute.Click += (s, e) =>
        {
            _isSoundMuted = !_isSoundMuted;
            UpdateMuteButton();
            if (_isSoundMuted && _alarmTimer != null)
                _alarmTimer.Stop();
        };
        titleBar.Items.Add(_btnMute);

        // DataGridView for watch items — Fill to use all available space in left panel
        _dgvWatch = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            ScrollBars = ScrollBars.Vertical
        };
        ApplyDataGridViewStyle(_dgvWatch);
        // Set height properties AFTER ApplyDataGridViewStyle:
        // ApplyDarkTheme sets ColumnHeadersBorderStyle=Single which resets
        // ColumnHeadersHeightSizeMode, causing our height settings to be lost.
        _dgvWatch.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _dgvWatch.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _dgvWatch.ColumnHeadersHeight = _watchGridHeaderHeight;
        _dgvWatch.RowTemplate.Height = _watchGridRowHeight;

        _dgvWatch.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewButtonColumn
            {
                Name = "Status",
                HeaderText = "상태",
                Text = "-",
                UseColumnTextForButtonValue = false,
                MinimumWidth = 60,
                Width = 70,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ItemName",
                HeaderText = "의상명",
                DataPropertyName = "ItemName",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 30
            },
            new DataGridViewTextBoxColumn
            {
                Name = "StoneName",
                HeaderText = "스톤명",
                DataPropertyName = "StoneName",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 30
            },
            new DataGridViewTextBoxColumn
            {
                Name = "WatchPrice",
                HeaderText = "감시가",
                DataPropertyName = "WatchPrice",
                MinimumWidth = 80,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 18,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewButtonColumn
            {
                Name = "Delete",
                HeaderText = "",
                Text = "X",
                UseColumnTextForButtonValue = true,
                MinimumWidth = 35,
                Width = 35,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            }
        });

        _dgvWatch.CellEndEdit += DgvWatch_CellEndEdit;
        _dgvWatch.CellFormatting += DgvWatch_CellFormatting;
        _dgvWatch.CellClick += DgvWatch_CellClick;
        _dgvWatch.EditingControlShowing += DgvWatch_EditingControlShowing;
        _dgvWatch.RowLeave += DgvWatch_RowLeave;
        _dgvWatch.Leave += DgvWatch_Leave;
        _dgvWatch.DataError += (s, e) => e.ThrowException = false;

        // WinForms Dock order: last added is docked first.
        // Both are Dock=Top: titleBar (added last) goes to top, _dgvWatch below it.
        panel.Controls.Add(_dgvWatch);
        panel.Controls.Add(titleBar);

        return panel;
    }

    private string GetMonitorTitleText()
    {
        var count = _costumeMonitorConfig.Items.Count;
        var countText = count > 0 ? $" ({count}개)" : "";
        var alarmText = _watchAlarmResults.Count > 0
            ? $"  ⚠ {_watchAlarmResults.Sum(r => r.Matches.Count)}건 발견"
            : "";
        return $"의상 감시{countText}{alarmText}";
    }

    private void UpdateMonitorTitleText()
    {
        if (_lblMonitorTitle == null) return;
        _lblMonitorTitle.Text = GetMonitorTitleText();
        _lblMonitorTitle.ForeColor = _watchAlarmResults.Count > 0 ? _colors.SaleColor : _colors.TextMuted;
    }

    private void UpdateMuteButton()
    {
        if (_btnMute == null) return;
        if (_isSoundMuted)
        {
            _btnMute.Text = "음소거 해제";
            _btnMute.ForeColor = _colors.SaleColor;
        }
        else
        {
            _btnMute.Text = "음소거";
            _btnMute.ForeColor = _colors.Text;
        }
    }

    private bool _isAddingWatchItem = false;

    private void AddWatchItem()
    {
        _isAddingWatchItem = true;
        try
        {
            _costumeMonitorConfig.Items.Add(new CostumeWatchItem
            {
                StoneName = "",
                ItemName = "",
                WatchPrice = 0,
                AddedAt = DateTime.Now
            });

            RefreshWatchGrid();
            UpdateMonitorPanelHeight();
            UpdateMonitorTitleText();

            // Focus the new row for editing
            if (_dgvWatch.Rows.Count > 0)
            {
                var lastRow = _dgvWatch.Rows.Count - 1;
                _dgvWatch.CurrentCell = _dgvWatch.Rows[lastRow].Cells["ItemName"];
                _dgvWatch.BeginEdit(true);
            }
        }
        finally
        {
            _isAddingWatchItem = false;
        }
    }

    private void RefreshWatchGrid()
    {
        _dgvWatch.CancelEdit();
        _dgvWatch.DataSource = null;
        _dgvWatch.DataSource = new BindingSource { DataSource = _costumeMonitorConfig.Items };
        _dgvWatch.Refresh();
        UpdateWatchStatusColumn();
    }

    private void UpdateWatchStatusColumn()
    {
        for (int i = 0; i < _dgvWatch.Rows.Count && i < _costumeMonitorConfig.Items.Count; i++)
        {
            var watch = _costumeMonitorConfig.Items[i];
            var result = _watchAlarmResults.FirstOrDefault(r => r.Watch == watch);
            var cell = _dgvWatch.Rows[i].Cells["Status"];

            if (result.Matches != null && result.Matches.Count > 0)
            {
                cell.Value = $"{result.Matches.Count}건";
                cell.Style.ForeColor = _colors.SaleColor;
            }
            else
            {
                cell.Value = "-";
                cell.Style.ForeColor = _colors.TextMuted;
            }
        }
    }

    private void DgvWatch_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var colName = _dgvWatch.Columns[e.ColumnIndex].Name;

        if (colName == "WatchPrice" && e.Value is long price)
        {
            e.Value = price.ToString("N0");
            e.FormattingApplied = true;
        }
    }

    private void DgvWatch_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var colName = _dgvWatch.Columns[e.ColumnIndex].Name;

        if (colName == "Status")
        {
            if (e.RowIndex < _costumeMonitorConfig.Items.Count)
            {
                var watch = _costumeMonitorConfig.Items[e.RowIndex];
                var result = _watchAlarmResults.FirstOrDefault(r => r.Watch == watch);
                if (result.Matches != null && result.Matches.Count > 0)
                {
                    var form = new CostumeWatchMatchForm(watch, result.Matches, _currentTheme, _baseFontSize);
                    form.ShowItemDetail += (s, item) => ShowItemDetail?.Invoke(this, item);
                    form.ShowDialog(_tabPage.FindForm());
                }
            }
        }
        else if (colName == "Delete")
        {
            if (e.RowIndex < _costumeMonitorConfig.Items.Count)
            {
                _costumeMonitorConfig.Items.RemoveAt(e.RowIndex);
                RefreshWatchGrid();
                UpdateMonitorPanelHeight();
                UpdateMonitorTitleText();
                _ = SaveCostumeMonitorConfigAsync();

                if (_costumeMonitorConfig.Items.Count == 0)
                    StopAlarm();
            }
        }
    }

    private void DgvWatch_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _costumeMonitorConfig.Items.Count) return;

        var colName = _dgvWatch.Columns[e.ColumnIndex].Name;
        var item = _costumeMonitorConfig.Items[e.RowIndex];
        var cell = _dgvWatch.Rows[e.RowIndex].Cells[e.ColumnIndex];

        if (colName == "WatchPrice")
        {
            // Parse price from formatted string
            var rawText = cell.Value?.ToString()?.Replace(",", "").Trim() ?? "0";
            if (long.TryParse(rawText, out var price))
            {
                item.WatchPrice = price;
                cell.Value = price; // Store as long, CellFormatting will display it formatted
            }
        }

        _ = SaveCostumeMonitorConfigAsync();

        // Re-check watch conditions with updated criteria
        if (_currentSession?.Items != null)
            CheckWatchConditions();
        else
            UpdateWatchStatusColumn();
    }

    private void DgvWatch_RowLeave(object? sender, DataGridViewCellEventArgs e)
    {
        if (_isAddingWatchItem) return;
        CleanupEmptyWatchItems();
    }

    private void DgvWatch_Leave(object? sender, EventArgs e)
    {
        if (_isAddingWatchItem) return;
        CleanupEmptyWatchItems();
    }

    private void CleanupEmptyWatchItems()
    {
        var removed = _costumeMonitorConfig.Items.RemoveAll(item =>
            string.IsNullOrWhiteSpace(item.ItemName) && string.IsNullOrWhiteSpace(item.StoneName));

        if (removed > 0)
        {
            _dgvWatch.BeginInvoke(() =>
            {
                RefreshWatchGrid();
                UpdateMonitorPanelHeight();
                UpdateMonitorTitleText();

                if (_costumeMonitorConfig.Items.Count == 0)
                    StopAlarm();

                _ = SaveCostumeMonitorConfigAsync();
            });
        }
    }

    private void DgvWatch_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (_dgvWatch.CurrentCell == null) return;
        var colName = _dgvWatch.Columns[_dgvWatch.CurrentCell.ColumnIndex].Name;

        if (e.Control is TextBox txt)
        {
            txt.KeyPress -= WatchPriceKeyPress;
            txt.TextChanged -= WatchPriceTextChanged;

            if (colName == "WatchPrice")
            {
                txt.ImeMode = ImeMode.Disable;
                txt.KeyPress += WatchPriceKeyPress;
                txt.TextChanged += WatchPriceTextChanged;

                // Show formatted number when entering edit
                if (_dgvWatch.CurrentCell.Value is long price && price > 0)
                    txt.Text = price.ToString("N0");
            }
            else
            {
                txt.ImeMode = ImeMode.NoControl;
            }
        }
    }

    private void WatchPriceKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsDigit(e.KeyChar) && e.KeyChar != ',' && e.KeyChar != '\b' && !char.IsControl(e.KeyChar))
            e.Handled = true;
    }

    private bool _isFormattingWatchPrice = false;
    private void WatchPriceTextChanged(object? sender, EventArgs e)
    {
        if (_isFormattingWatchPrice || sender is not TextBox textBox) return;

        var rawDigits = new string(textBox.Text.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(rawDigits)) return;

        if (long.TryParse(rawDigits, out var value))
        {
            var formatted = value.ToString("N0");
            if (textBox.Text != formatted)
            {
                _isFormattingWatchPrice = true;
                var cursorPos = textBox.SelectionStart;
                var oldLength = textBox.Text.Length;
                textBox.Text = formatted;
                var newCursorPos = Math.Max(0, cursorPos + (formatted.Length - oldLength));
                textBox.SelectionStart = Math.Min(newCursorPos, formatted.Length);
                _isFormattingWatchPrice = false;
            }
        }
    }

    private void UpdateMonitorPanelHeight()
    {
        if (_dgvWatch == null) return;
        _dgvWatch.Visible = true;
        _dgvWatch.ScrollBars = ScrollBars.Vertical;
    }

    /// <summary>
    /// Check watch conditions against current crawl data
    /// </summary>
    private void CheckWatchConditions()
    {
        if (_costumeMonitorConfig.Items.Count == 0 || _currentSession?.Items == null) return;

        _watchAlarmResults.Clear();

        foreach (var watch in _costumeMonitorConfig.Items)
        {
            if (!watch.Enabled) continue;
            if (string.IsNullOrWhiteSpace(watch.StoneName) && string.IsNullOrWhiteSpace(watch.ItemName)) continue;
            if (watch.WatchPrice <= 0) continue;

            var matches = _currentSession.Items.Where(item =>
            {
                // Check stone name match (supports % wildcard)
                bool stoneMatch = string.IsNullOrWhiteSpace(watch.StoneName) ||
                    (item.SlotInfo != null && item.SlotInfo.Any(s =>
                        SearchHelper.WildcardContains(s, watch.StoneName)));

                // Check item name match (supports % wildcard)
                bool itemMatch = string.IsNullOrWhiteSpace(watch.ItemName) ||
                    SearchHelper.WildcardContains(item.ItemName, watch.ItemName);

                return stoneMatch && itemMatch && item.Price <= watch.WatchPrice;
            }).ToList();

            if (matches.Count > 0)
            {
                _watchAlarmResults.Add((watch, matches));
            }
        }

        UpdateMonitorTitleText();
        UpdateWatchStatusColumn();

        if (_watchAlarmResults.Count > 0)
        {
            var totalMatches = _watchAlarmResults.Sum(r => r.Matches.Count);
            _lblStatus.ForeColor = _colors.SaleColor;
            _lblStatus.Text = $"!! 감시 조건 일치: {totalMatches}건 발견";
            _lblStatus.Cursor = Cursors.Hand;
            StartAlarm();
        }
        else
        {
            _lblStatus.Cursor = Cursors.Default;
            StopAlarm();
        }
    }

    private void StartAlarm()
    {
        if (_isSoundMuted) return;

        if (_alarmTimer == null)
        {
            _alarmTimer = new System.Windows.Forms.Timer
            {
                Interval = _alarmIntervalSeconds * 1000
            };
            _alarmTimer.Tick += AlarmTimer_Tick;
        }

        // Play immediately on first trigger
        PlayAlarmSound();

        _alarmTimer.Interval = _alarmIntervalSeconds * 1000;
        _alarmTimer.Start();
    }

    private void StopAlarm()
    {
        _alarmTimer?.Stop();
    }

    private void AlarmTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSoundMuted || _watchAlarmResults.Count == 0)
        {
            StopAlarm();
            return;
        }
        PlayAlarmSound();
    }

    private void PlayAlarmSound()
    {
        if (_selectedAlarmSound == AlarmSoundType.SystemSound)
            System.Media.SystemSounds.Exclamation.Play();
        else
            AlarmSoundService.PlaySound(_selectedAlarmSound);
    }

    private void LblStatus_Click(object? sender, EventArgs e)
    {
        if (_watchAlarmResults.Count == 0) return;

        // Show all matched items from all watch conditions
        var allMatches = _watchAlarmResults.SelectMany(r => r.Matches).Distinct().ToList();

        _allFilteredResults.Clear();
        _allFilteredResults.AddRange(allMatches);
        _totalCount = _allFilteredResults.Count;
        _totalPages = (int)Math.Ceiling((double)_totalCount / PageSize);
        _currentPage = 1;
        DisplayCurrentPage();

        _lblStatus.ForeColor = _colors.SaleColor;
        _lblStatus.Text = $"!! 감시 조건 일치: {allMatches.Count}건 표시 중";
    }

    /// <summary>
    /// Load alarm settings from Form1 (shared global settings)
    /// </summary>
    public void LoadAlarmSettings(bool isMuted, AlarmSoundType sound, int intervalSeconds)
    {
        _isSoundMuted = isMuted;
        _selectedAlarmSound = sound;
        _alarmIntervalSeconds = intervalSeconds;

        // Update mute button UI
        UpdateMuteButton();
    }

    private async Task LoadCostumeMonitorConfigAsync()
    {
        try
        {
            if (!File.Exists(_costumeMonitorConfigPath)) return;

            var json = await File.ReadAllTextAsync(_costumeMonitorConfigPath);
            var config = JsonSerializer.Deserialize<CostumeMonitorConfig>(json);
            if (config != null)
            {
                _costumeMonitorConfig = config;
                RefreshWatchGrid();
                UpdateMonitorPanelHeight();
                UpdateMonitorTitleText();
                Debug.WriteLine($"[CostumeTab] Loaded {config.Items.Count} watch items");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CostumeTab] Failed to load costume monitor config: {ex.Message}");
        }
    }

    private async Task SaveCostumeMonitorConfigAsync()
    {
        try
        {
            // Save a filtered copy — don't modify the live list (avoids binding conflicts)
            var itemsToSave = _costumeMonitorConfig.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemName) || !string.IsNullOrWhiteSpace(item.StoneName))
                .ToList();

            var configToSave = new CostumeMonitorConfig { Items = itemsToSave };
            var json = JsonSerializer.Serialize(configToSave, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_costumeMonitorConfigPath, json);
            Debug.WriteLine($"[CostumeTab] Saved {itemsToSave.Count} watch items");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CostumeTab] Failed to save costume monitor config: {ex.Message}");
        }
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Try to load the latest crawl data on tab activation
    /// </summary>
    public override async void OnActivated()
    {
        base.OnActivated();

        // Ensure dynamic row heights are applied (they may not have taken effect
        // if set while the tab was not visible)
        UpdateMonitorPanelHeight();
        UpdateCostumeSearchHistoryPanel();

        // Always try to load and display data on tab activation
        await TryLoadLatestDataAsync(autoDisplay: true);

        // Resume auto-crawl: immediately trigger a crawl, then restart the timer
        if (_isAutoCrawling && _autoCrawlTimer != null && !_autoCrawlTimer.Enabled)
        {
            if (!_isCrawling)
                _ = RunIncrementalCrawlAsync();
            _autoCrawlTimer.Start();
        }
    }

    /// <inheritdoc/>
    public override string? OnDeactivated()
    {
        base.OnDeactivated();

        // Pause auto-crawl timer on tab switch (will resume on OnActivated)
        if (_isAutoCrawling)
        {
            _autoCrawlTimer?.Stop();
            if (_isCrawling)
            {
                try { _crawlCts?.Cancel(); } catch (ObjectDisposedException) { }
            }
            return "자동 수집이 일시정지되었습니다. 탭 복귀 시 재개됩니다.";
        }

        if (_isCrawling)
        {
            try { _crawlCts?.Cancel(); } catch (ObjectDisposedException) { }
            return "의상 데이터 수집이 중지되었습니다. 다음에 이어서 수집할 수 있습니다.";
        }
        return null;
    }

    private async Task TryLoadLatestDataAsync(bool autoDisplay = false)
    {
        var selectedServer = _cboServer.SelectedItem as Server;
        if (selectedServer == null) return;

        // Skip reload if same session is already loaded
        if (_currentSession != null && autoDisplay && _allFilteredResults.Count > 0
            && _currentSession.ServerName == selectedServer.Name)
        {
            return;
        }

        var session = await _crawlDataService.LoadLatestAsync(CrawlSearchTerm, selectedServer.Name);
        if (session != null)
        {
            _currentSession = session;
            _btnSearch.Enabled = true;

            var timeStr = session.CrawledAt.ToString("MM-dd HH:mm");
            _lblCrawlStatus.Text = $"저장된 데이터: {session.TotalItems}건 ({timeStr} 수집)";
            UpdateStatusBar();

            if (autoDisplay)
                ExecuteLocalSearch();
        }
    }

    private void UpdateStatusBar()
    {
        if (_currentSession == null)
        {
            _lblStatus.Text = "서버를 선택하고 데이터 수집을 시작하세요.";
            return;
        }

        var timeStr = _currentSession.CrawledAt.ToString("yyyy-MM-dd HH:mm");
        var autoCrawlText = _isAutoCrawling ? "  |  자동 수집 중" : "";
        _lblStatus.Text = $"{_currentSession.ServerName}  |  {timeStr} 수집  |  {_currentSession.TotalItems}건{autoCrawlText}";
    }

    #endregion

    #region Theme & Font

    public override void ApplyTheme(ThemeColors colors)
    {
        base.ApplyTheme(colors);

        if (_dgvResults != null) ApplyDataGridViewStyle(_dgvResults);
        if (_dgvWatch != null) ApplyDataGridViewStyle(_dgvWatch);

        // Search history panel
        if (_pnlSearchHistory != null)
        {
            _pnlSearchHistory.BackColor = colors.Panel;
            if (_pnlSearchHistory.Controls.Count > 0 && _pnlSearchHistory.Controls[0] is Label titleLbl)
                titleLbl.ForeColor = colors.TextMuted;
            for (int i = 1; i < _pnlSearchHistory.Controls.Count; i++)
            {
                if (_pnlSearchHistory.Controls[i] is Label histLbl)
                    histLbl.ForeColor = colors.Accent;
            }
        }

        // Monitor panel
        if (_monitorPanel != null)
        {
            _monitorPanel.BackColor = colors.Panel;
            if (_lblMonitorTitle != null)
                _lblMonitorTitle.ForeColor = _watchAlarmResults.Count > 0 ? colors.SaleColor : colors.TextMuted;
            UpdateMuteButton();
        }

        // Layout panels
        if (_mainPanel != null) ApplyTableLayoutPanelStyle(_mainPanel);
        if (_contentLayout != null) ApplyTableLayoutPanelStyle(_contentLayout);
        if (_rightInnerLayout != null) ApplyTableLayoutPanelStyle(_rightInnerLayout);
        if (_topBarLayout != null)
        {
            ApplyTableLayoutPanelStyle(_topBarLayout);
            _topBarLayout.Invalidate();  // Repaint grid line with updated theme color
        }

        // Toolbars
        if (_crawlStrip != null) _crawlStrip.BackColor = colors.Panel;
        if (_searchStrip != null) _searchStrip.BackColor = colors.Panel;
        if (_crawlStatusStrip != null) _crawlStatusStrip.BackColor = colors.Panel;
        if (_topBarLayout != null) ApplyTableLayoutPanelStyle(_topBarLayout);
        // Update search area panel background
        if (_searchStrip?.Parent != null) _searchStrip.Parent.BackColor = colors.Background;
        if (_crawlStrip?.Parent != null) _crawlStrip.Parent.BackColor = colors.Background;
        if (_cboServer != null)
        {
            _cboServer.BackColor = colors.Grid;
            _cboServer.ForeColor = colors.Text;
        }
        if (_btnAutoCrawl != null && !_isAutoCrawling)
        {
            _btnAutoCrawl.BackColor = colors.Accent;
            _btnAutoCrawl.ForeColor = colors.AccentText;
        }
        if (_lblCrawlStatus != null)
        {
            _lblCrawlStatus.ForeColor = colors.Text;
        }

        // Search bar
        if (_searchStrip != null) _searchStrip.BackColor = colors.Panel;
        if (_txtFilterItemName != null)
        {
            _txtFilterItemName.BackColor = colors.Grid;
            _txtFilterItemName.ForeColor = colors.Text;
        }
        if (_txtFilterStone != null)
        {
            _txtFilterStone.BackColor = colors.Grid;
            _txtFilterStone.ForeColor = colors.Text;
        }
        if (_txtFilterMinPrice != null)
        {
            _txtFilterMinPrice.BackColor = colors.Grid;
            _txtFilterMinPrice.ForeColor = colors.Text;
        }
        if (_txtFilterMaxPrice != null)
        {
            _txtFilterMaxPrice.BackColor = colors.Grid;
            _txtFilterMaxPrice.ForeColor = colors.Text;
        }
        if (_btnSearch != null)
        {
            _btnSearch.BackColor = colors.Accent;
            _btnSearch.ForeColor = colors.AccentText;
        }

        // Status
        if (_lblStatus != null)
        {
            _lblStatus.BackColor = colors.Panel;
            _lblStatus.ForeColor = colors.Text;
        }

        // Pagination
        if (_pnlPagination != null)
        {
            _pnlPagination.BackColor = colors.Panel;
        }
        if (_btnFirst != null) ApplyRoundedButtonStyle(_btnFirst, false);
        if (_btnPrev10 != null) ApplyRoundedButtonStyle(_btnPrev10, false);
        if (_btnPrev != null) ApplyRoundedButtonStyle(_btnPrev, false);
        if (_btnNext != null) ApplyRoundedButtonStyle(_btnNext, false);
        if (_btnNext10 != null) ApplyRoundedButtonStyle(_btnNext10, false);
        if (_btnLast != null) ApplyRoundedButtonStyle(_btnLast, false);
        if (_lblPageInfo != null) _lblPageInfo.ForeColor = colors.TextMuted;
    }

    public override void UpdateFontSize(float baseFontSize)
    {
        base.UpdateFontSize(baseFontSize);

        var scale = baseFontSize / 12f;
        var font = new Font("Malgun Gothic", baseFontSize);
        var boldFont = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);

        // DataGridView fonts
        if (_dgvResults != null)
        {
            _dgvResults.DefaultCellStyle.Font = font;
            _dgvResults.ColumnHeadersDefaultCellStyle.Font = boldFont;
            _dgvResults.ColumnHeadersHeight = Math.Max((int)(32 * scale), 28);
        }

        // Status label
        if (_lblStatus != null)
        {
            _lblStatus.Font = font;
            _lblStatus.Height = Math.Max((int)(24 * scale), 20);
        }

        // ToolStrip item widths
        if (_cboServer != null) _cboServer.Width = (int)(100 * scale);

        // ToolStrip item widths
        if (_txtFilterItemName != null) _txtFilterItemName.Width = (int)(120 * scale);
        if (_txtFilterStone != null) _txtFilterStone.Width = (int)(120 * scale);
        if (_txtFilterMinPrice != null) _txtFilterMinPrice.Width = (int)(140 * scale);
        if (_txtFilterMaxPrice != null) _txtFilterMaxPrice.Width = (int)(140 * scale);

        // Pagination controls
        var btnHeight = (int)(28 * scale);
        if (_btnFirst != null) _btnFirst.Size = new Size((int)(36 * scale), btnHeight);
        if (_btnPrev10 != null) _btnPrev10.Size = new Size((int)(40 * scale), btnHeight);
        if (_btnPrev != null) _btnPrev.Size = new Size((int)(32 * scale), btnHeight);
        if (_btnNext != null) _btnNext.Size = new Size((int)(32 * scale), btnHeight);
        if (_btnNext10 != null) _btnNext10.Size = new Size((int)(40 * scale), btnHeight);
        if (_btnLast != null) _btnLast.Size = new Size((int)(36 * scale), btnHeight);
        if (_lblPageInfo != null)
        {
            _lblPageInfo.Font = font;
            _lblPageInfo.Padding = new Padding((int)(10 * scale), (int)(5 * scale), (int)(10 * scale), 0);
        }
        if (_pnlPagination != null)
        {
            var paddingTop = (int)(10 * scale);
            CenterPaginationPanel();
            _pnlPagination.Padding = new Padding(_pnlPagination.Padding.Left, paddingTop, 0, 0);
        }

        // Content layout row heights (3 rows: top bar / grid / pagination)
        if (_contentLayout != null && _contentLayout.RowStyles.Count >= 3)
        {
            var stripHeight = Math.Max((int)(32 * scale), 28);
            var hasHistory = _pnlSearchHistory != null && _pnlSearchHistory.Visible;
            _contentLayout.RowStyles[0].Height = stripHeight + (hasHistory ? 28 : 0);  // Top bar
            _contentLayout.RowStyles[2].Height = (int)(55 * scale);                    // Pagination
        }

        // Refresh monitor panel height
        UpdateMonitorPanelHeight();
    }

    #endregion

    #region Dispose

    /// <summary>
    /// 종료 전 크롤링을 중지하고 부분 저장이 완료될 때까지 대기
    /// </summary>
    public void RequestGracefulStop()
    {
        if (!_isCrawling && !_isAutoCrawling) return;

        StopAutoCrawl();

        // Wait briefly for the catch block (MergeRemainingItems + SavePartialSession) to complete
        var sw = Stopwatch.StartNew();
        while (_isCrawling && sw.ElapsedMilliseconds < 3000)
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAutoCrawl();
            _crawlCts?.Cancel();
            _crawlCts?.Dispose();
            _alarmTimer?.Stop();
            _alarmTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}
