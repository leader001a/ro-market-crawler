using System.Diagnostics;
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
    private const int CrawlItemDelayMs = 1000;   // 1 second per item
    private const int CrawlPageDelayMs = 5000;    // 5 seconds between pages
    private const int CrawlMaxPages = 1000;       // Safety limit

    #endregion

    #region Services

    private readonly IGnjoyClient _gnjoyClient;
    private readonly CrawlDataService _crawlDataService;

    #endregion

    #region UI Controls

    // Toolbar
    private ToolStrip _toolStrip = null!;

    // Crawl bar controls
    private ToolStripComboBox _cboServer = null!;
    private ToolStripButton _btnCrawlStart = null!;
    private ToolStripButton _btnCrawlStop = null!;
    private ToolStripProgressBar _crawlProgressBar = null!;
    private ToolStripLabel _lblCrawlStatus = null!;

    // Search bar controls
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

    #endregion

    #region State

    private CancellationTokenSource? _crawlCts;
    private bool _isCrawling = false;
    private CrawlSession? _currentSession;
    private readonly List<DealItem> _searchResults = new();

    #endregion

    #region Events

    /// <summary>
    /// Raised when crawling state changes (true = started, false = stopped)
    /// </summary>
    public event EventHandler<bool>? CrawlStateChanged;

    #endregion

    /// <summary>
    /// Whether crawling is currently in progress
    /// </summary>
    public bool IsCrawling => _isCrawling;

    public override string TabName => "의상검색";

    public CostumeTabController(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _gnjoyClient = GetService<IGnjoyClient>();
        _crawlDataService = GetService<CrawlDataService>();
        _resultBindingSource = new BindingSource { DataSource = _searchResults };
    }

    /// <summary>
    /// Set watermark image for the DataGridView
    /// </summary>
    public void SetWatermark(Image watermark) => ApplyWatermark(_dgvResults, watermark);

    public override void Initialize()
    {
        var scale = _baseFontSize / 12f;

        _mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(5)
        };
        _mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max((int)(32 * scale), 28)));   // Row 0: Toolbar
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                                // Row 1: Grid
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max((int)(26 * scale), 22)));   // Row 2: Status

        _toolStrip = CreateToolStrip();
        _dgvResults = CreateResultsGrid();
        _lblStatus = CreateStatusLabel();
        _lblStatus.Text = "서버를 선택하고 데이터 수집을 시작하세요.";

        _mainPanel.Controls.Add(_toolStrip, 0, 0);
        _mainPanel.Controls.Add(_dgvResults, 0, 1);
        _mainPanel.Controls.Add(_lblStatus, 0, 2);

        _tabPage.Controls.Add(_mainPanel);
    }

    private ToolStrip CreateToolStrip()
    {
        var scale = _baseFontSize / 12f;

        var strip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        // === Crawl section ===

        // Server combo
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

        // Crawl start button
        _btnCrawlStart = new ToolStripButton
        {
            Text = "수집 시작",
            BackColor = _colors.Accent,
            ForeColor = _colors.AccentText,
            ToolTipText = "의상 데이터 전체 수집"
        };
        _btnCrawlStart.Click += async (s, e) => await StartCrawlAsync();

        // Crawl stop button
        _btnCrawlStop = new ToolStripButton
        {
            Text = "중지",
            Enabled = false,
            ToolTipText = "수집 중지"
        };
        _btnCrawlStop.Click += (s, e) => CancelCrawl();

        // === Search section ===

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
            Width = (int)(70 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "최소 가격"
        };
        _txtFilterMinPrice.KeyDown += SearchFilter_KeyDown;

        _txtFilterMaxPrice = new ToolStripTextBox
        {
            AutoSize = false,
            Width = (int)(70 * scale),
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "최대 가격"
        };
        _txtFilterMaxPrice.KeyDown += SearchFilter_KeyDown;

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

        // Progress bar (right-aligned)
        _crawlProgressBar = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visible = false,
            Alignment = ToolStripItemAlignment.Right,
            Size = new Size(120, 16)
        };

        // Status label (right-aligned)
        _lblCrawlStatus = new ToolStripLabel
        {
            Text = "",
            Alignment = ToolStripItemAlignment.Right
        };

        // Build strip: [Server▼] | 시작 중지 | 아이템:[__] 스톤:[__] 가격:[__]~[__] | 검색   [Progress][Status]→
        strip.Items.Add(_cboServer);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnCrawlStart);
        strip.Items.Add(_btnCrawlStop);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripLabel("아이템:"));
        strip.Items.Add(_txtFilterItemName);
        strip.Items.Add(new ToolStripLabel("스톤:"));
        strip.Items.Add(_txtFilterStone);
        strip.Items.Add(new ToolStripLabel("가격:"));
        strip.Items.Add(_txtFilterMinPrice);
        strip.Items.Add(new ToolStripLabel("~"));
        strip.Items.Add(_txtFilterMaxPrice);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnSearch);
        strip.Items.Add(_crawlProgressBar);
        strip.Items.Add(_lblCrawlStatus);

        return strip;
    }

    private DataGridView CreateResultsGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            DataSource = _resultBindingSource
        };
        ApplyDataGridViewStyle(dgv);

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

    #region Crawling

    private async Task StartCrawlAsync()
    {
        var selectedServer = _cboServer.SelectedItem as Server;
        if (selectedServer == null)
        {
            MessageBox.Show("서버를 선택해주세요.", "입력 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Check if we can resume from a previous incomplete session
        bool isResuming = false;
        var existingItems = new List<DealItem>();
        int resumeFromPage = 1;
        int resumeMaxEndPage = CrawlMaxPages;

        if (_currentSession != null && !_currentSession.IsComplete
            && _currentSession.ServerId == selectedServer.Id
            && _currentSession.LastCrawledPage > 0)
        {
            // Incomplete session for same server - offer resume
            var remaining = _currentSession.TotalServerPages > 0
                ? $" (남은 페이지: {_currentSession.TotalServerPages - _currentSession.LastCrawledPage})"
                : "";

            var btnResume = new TaskDialogButton("이어서 수집");
            var btnNew = new TaskDialogButton("처음부터 수집");
            var btnCancel = new TaskDialogButton("수집 취소");

            var page = new TaskDialogPage
            {
                Caption = "데이터 수집",
                Heading = "이전 수집을 이어서 진행하시겠습니까?",
                Text = $"이전 수집이 {_currentSession.LastCrawledPage}페이지에서 중단되었습니다.{remaining}\n" +
                       $"현재 {_currentSession.TotalItems}건이 수집된 상태입니다.",
                Buttons = { btnResume, btnNew, btnCancel }
            };

            var result = TaskDialog.ShowDialog(_tabPage.FindForm()!, page);

            if (result == btnCancel) return;

            if (result == btnResume)
            {
                isResuming = true;
                existingItems.AddRange(_currentSession.Items);
                resumeFromPage = _currentSession.LastCrawledPage + 1;
                if (_currentSession.TotalServerPages > 0)
                    resumeMaxEndPage = Math.Min(CrawlMaxPages, _currentSession.TotalServerPages);
            }
        }
        else if (_currentSession != null && _currentSession.IsComplete)
        {
            // Complete session exists - confirm overwrite
            var confirm = MessageBox.Show(
                $"이전 수집 데이터가 있습니다. ({_currentSession.TotalItems}건, {_currentSession.CrawledAt:yyyy-MM-dd HH:mm})\n" +
                $"새로 수집하시겠습니까?",
                "데이터 수집",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
        }

        _crawlCts = new CancellationTokenSource();
        var ct = _crawlCts.Token;
        SetCrawlingState(true);

        var allItems = new List<DealItem>(existingItems);
        int currentPage = resumeFromPage;
        int maxEndPage = resumeMaxEndPage;
        int totalCount = 0;
        var startTime = DateTime.Now;
        int pagesCompleted = 0;

        if (isResuming)
        {
            _lblCrawlStatus.Text = $"{resumeFromPage}페이지부터 이어서 수집...";
        }

        _crawlProgressBar.Value = 0;
        _crawlProgressBar.Visible = true;

        try
        {
            while (currentPage <= maxEndPage)
            {
                ct.ThrowIfCancellationRequested();

                // Update progress
                int totalPages = maxEndPage < CrawlMaxPages ? maxEndPage : 0;
                if (totalPages > 0)
                {
                    int progressPercent = (int)((currentPage - 1) * 100.0 / totalPages);
                    _crawlProgressBar.Value = Math.Min(progressPercent, 100);
                }

                // Calculate ETA
                string etaText = "";
                var remainingPages = maxEndPage - currentPage + 1;
                double avgTimePerPage;

                if (pagesCompleted > 0)
                {
                    var elapsed = DateTime.Now - startTime;
                    avgTimePerPage = elapsed.TotalSeconds / pagesCompleted;
                }
                else
                {
                    avgTimePerPage = (ItemsPerPage * CrawlItemDelayMs + CrawlPageDelayMs) / 1000.0;
                }

                var etaSeconds = (int)(avgTimePerPage * remainingPages);
                if (etaSeconds >= 60)
                    etaText = $" (약 {etaSeconds / 60}분 {etaSeconds % 60}초 남음)";
                else if (etaSeconds > 0)
                    etaText = $" (약 {etaSeconds}초 남음)";

                var pageDisplay = totalPages > 0 ? $"{currentPage}/{totalPages}" : $"{currentPage}/?";
                _lblCrawlStatus.Text = $"{pageDisplay} 수집 중... ({allItems.Count}건){etaText}";

                // Crawl page
                var (items, count) = await CrawlPageAsync(selectedServer.Id, currentPage, ct);
                totalCount = count;

                // Update max end page based on actual total
                int actualTotalPages = (int)Math.Ceiling((double)totalCount / ItemsPerPage);
                if (actualTotalPages > 0)
                {
                    maxEndPage = Math.Min(CrawlMaxPages, actualTotalPages);
                }

                // Tag items with page number
                foreach (var item in items)
                {
                    item.CrawledPage = currentPage;
                }

                allItems.AddRange(items);
                pagesCompleted++;

                // Update progress bar with actual total
                if (maxEndPage > 0 && maxEndPage < CrawlMaxPages)
                {
                    int progress = (int)(currentPage * 100.0 / maxEndPage);
                    _crawlProgressBar.Value = Math.Min(progress, 100);
                }

                currentPage++;

                // Wait before next page
                if (currentPage <= maxEndPage)
                {
                    _lblCrawlStatus.Text = $"대기 중... ({CrawlPageDelayMs / 1000}초)";
                    await Task.Delay(CrawlPageDelayMs, ct);
                }
            }

            // Crawl completed - save to JSON
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
                TotalServerPages = maxEndPage
            };

            await _crawlDataService.SaveAsync(session);
            _currentSession = session;
            _btnSearch.Enabled = true;

            var totalElapsed = DateTime.Now - startTime;
            var elapsedText = totalElapsed.TotalSeconds >= 60
                ? $"{(int)totalElapsed.TotalMinutes}분 {(int)totalElapsed.TotalSeconds % 60}초"
                : $"{(int)totalElapsed.TotalSeconds}초";

            _lblCrawlStatus.Text = $"완료: {allItems.Count}건 ({elapsedText})";
            UpdateStatusBar();
        }
        catch (OperationCanceledException)
        {
            _lblCrawlStatus.Text = $"중지됨: {allItems.Count}건";

            // Save partial results with resume info
            SavePartialSession(selectedServer, allItems, currentPage - 1, maxEndPage);
        }
        catch (RateLimitException rateLimitEx)
        {
            _lblCrawlStatus.Text = $"API 제한: {rateLimitEx.RemainingTimeText}";
            _lblCrawlStatus.ForeColor = _colors.SaleColor;

            // Save partial results with resume info
            SavePartialSession(selectedServer, allItems, currentPage - 1, maxEndPage);

            MessageBox.Show(
                $"GNJOY API 요청 제한이 적용되었습니다.\n\n" +
                $"제한 해제까지: {rateLimitEx.RemainingTimeText}\n" +
                $"수집 진행: {currentPage - 1}페이지, {allItems.Count}건\n\n" +
                $"다음 수집 시 이어서 진행할 수 있습니다.",
                "API 요청 제한",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _lblCrawlStatus.Text = $"오류: {ex.Message}";
            Debug.WriteLine($"[CostumeTab] Crawl error: {ex}");

            // Save partial results with resume info on error too
            SavePartialSession(selectedServer, allItems, currentPage - 1, maxEndPage);
        }
        finally
        {
            _crawlProgressBar.Visible = false;
            SetCrawlingState(false);
        }
    }

    private async Task<(List<DealItem> items, int totalCount)> CrawlPageAsync(
        int serverId, int page, CancellationToken ct)
    {
        var items = new List<DealItem>();
        int totalCount = 0;

        try
        {
            var result = await _gnjoyClient.SearchItemDealsWithCountAsync(CrawlSearchTerm, serverId, page, ct);

            if (result.TotalCount > 0 && result.Items != null)
            {
                totalCount = result.TotalCount;

                foreach (var item in result.Items)
                {
                    ct.ThrowIfCancellationRequested();

                    // Fetch detail info for items with detail params
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
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CostumeTab] Detail fetch failed: {ex.Message}");
                        }
                    }

                    item.ComputeFields();
                    items.Add(item);

                    // 1 second delay per item
                    await Task.Delay(CrawlItemDelayMs, ct);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (RateLimitException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CostumeTab] Page crawl error: {ex.Message}");
        }

        return (items, totalCount);
    }

    private void CancelCrawl()
    {
        _crawlCts?.Cancel();
        _lblCrawlStatus.Text = "중지 요청됨...";
    }

    private void SetCrawlingState(bool crawling)
    {
        _isCrawling = crawling;
        _cboServer.Enabled = !crawling;
        _btnCrawlStart.Enabled = !crawling;
        _btnCrawlStop.Enabled = crawling;
        _btnSearch.Enabled = !crawling && _currentSession != null;

        // Notify other tabs to lock/unlock API features
        CrawlStateChanged?.Invoke(this, crawling);
    }

    /// <summary>
    /// Save partial crawl results so the user can resume later
    /// </summary>
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

        // Update grid
        _searchResults.Clear();
        _searchResults.AddRange(results);
        _resultBindingSource.DataSource = null;
        _resultBindingSource.DataSource = _searchResults;
        _dgvResults.Refresh();

        _lblStatus.Text = $"검색 결과: {results.Count}건 / 전체 {_currentSession.TotalItems}건" +
            $"  |  {_currentSession.ServerName}, {_currentSession.CrawledAt:yyyy-MM-dd HH:mm} 수집";
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Try to load the latest crawl data on tab activation
    /// </summary>
    public override async void OnActivated()
    {
        base.OnActivated();

        if (_currentSession == null)
        {
            await TryLoadLatestDataAsync();
        }
    }

    private async Task TryLoadLatestDataAsync()
    {
        var selectedServer = _cboServer.SelectedItem as Server;
        if (selectedServer == null) return;

        var session = await _crawlDataService.LoadLatestAsync(CrawlSearchTerm, selectedServer.Name);
        if (session != null)
        {
            _currentSession = session;
            _btnSearch.Enabled = true;
            UpdateStatusBar();
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
        _lblStatus.Text = $"{_currentSession.ServerName}  |  {timeStr} 수집  |  {_currentSession.TotalItems}건";
    }

    #endregion

    #region Theme & Font

    public override void ApplyTheme(ThemeColors colors)
    {
        base.ApplyTheme(colors);

        if (_dgvResults != null) ApplyDataGridViewStyle(_dgvResults);

        // Toolbar
        if (_toolStrip != null)
        {
            _toolStrip.BackColor = colors.Panel;
        }
        if (_cboServer != null)
        {
            _cboServer.BackColor = colors.Grid;
            _cboServer.ForeColor = colors.Text;
        }
        if (_btnCrawlStart != null)
        {
            _btnCrawlStart.BackColor = colors.Accent;
            _btnCrawlStart.ForeColor = colors.AccentText;
        }
        if (_lblCrawlStatus != null)
        {
            _lblCrawlStatus.ForeColor = colors.Text;
        }
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
        if (_txtFilterItemName != null) _txtFilterItemName.Width = (int)(120 * scale);
        if (_txtFilterStone != null) _txtFilterStone.Width = (int)(120 * scale);
        if (_txtFilterMinPrice != null) _txtFilterMinPrice.Width = (int)(70 * scale);
        if (_txtFilterMaxPrice != null) _txtFilterMaxPrice.Width = (int)(70 * scale);

        // Row heights
        if (_mainPanel != null && _mainPanel.RowStyles.Count >= 3)
        {
            _mainPanel.RowStyles[0].Height = Math.Max((int)(32 * scale), 28);  // Toolbar
            _mainPanel.RowStyles[2].Height = Math.Max((int)(26 * scale), 22);  // Status
        }
    }

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _crawlCts?.Cancel();
            _crawlCts?.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}
