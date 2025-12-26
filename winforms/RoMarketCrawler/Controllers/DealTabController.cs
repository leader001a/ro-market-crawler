using System.Diagnostics;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Exceptions;
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
    private const int DetailRequestDelayMs = 500;

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
    private ToolStripButton _btnDealSearchToolStrip = null!;
    private ToolStripButton _btnDealCancelToolStrip = null!;
    private ToolStripProgressBar _progressDealSearch = null!;
    private DataGridView _dgvDeals = null!;
    private Label _lblDealStatus = null!;
    private FlowLayoutPanel _pnlSearchHistory = null!;
    private RoundedButton _btnDealPrev = null!;
    private RoundedButton _btnDealNext = null!;
    private Label _lblDealPage = null!;

    #endregion

    #region State

    private readonly List<DealItem> _searchResults = new();
    private readonly BindingSource _dealBindingSource;
    private CancellationTokenSource? _cts;
    private string _lastSearchTerm = string.Empty;
    private int _currentSearchId = 0;
    private List<string> _dealSearchHistory = new();
    private int _dealCurrentPage = 1;
    private bool _hasMorePages = false;

    // AutoComplete support (set from parent form)
    private AutoCompleteDropdown? _autoCompleteDropdown;
    private List<string>? _autoCompleteItems;

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
    /// Set autocomplete support from parent form
    /// </summary>
    public void SetAutoComplete(AutoCompleteDropdown dropdown, List<string> items)
    {
        _autoCompleteDropdown = dropdown;
        _autoCompleteItems = items;
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
        _btnDealPrev.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !isRateLimited && _hasMorePages;

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
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // Row 0: Toolbar
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // Row 1: Search history (dynamic)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Row 2: Results grid
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Row 3: Pagination
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));   // Row 4: Status
        ApplyTableLayoutPanelStyle(mainPanel);

        // ToolStrip-based toolbar
        var toolStrip = CreateToolStrip();

        // Search history panel
        _pnlSearchHistory = CreateSearchHistoryPanel();

        // Results grid
        _dgvDeals = CreateResultsGrid();

        // Pagination panel
        var paginationPanel = CreatePaginationPanel();

        // Status bar
        _lblDealStatus = CreateStatusLabel();
        _lblDealStatus.Text = "GNJOY에서 현재 노점 거래를 검색합니다.";

        mainPanel.Controls.Add(toolStrip, 0, 0);
        mainPanel.Controls.Add(_pnlSearchHistory, 0, 1);
        mainPanel.Controls.Add(_dgvDeals, 0, 2);
        mainPanel.Controls.Add(paginationPanel, 0, 3);
        mainPanel.Controls.Add(_lblDealStatus, 0, 4);

        _tabPage.Controls.Add(mainPanel);

        // Initial update of search history UI
        UpdateSearchHistoryPanel();
    }

    #region UI Creation

    private ToolStrip CreateToolStrip()
    {
        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        // Server combo
        var cboServer = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "서버 선택"
        };
        foreach (var server in Server.GetAllServers())
            cboServer.Items.Add(server);
        cboServer.ComboBox.DisplayMember = "Name";
        cboServer.SelectedIndex = 0;
        _cboDealServer = cboServer.ComboBox;

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
            if (e.KeyCode == Keys.Enter && _autoCompleteDropdown?.HasSelection != true)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _autoCompleteDropdown?.Hide();
                _ = SearchAsync();
            }
        };
        _txtDealSearch = txtSearch.TextBox;

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

        // Cancel button
        var btnCancel = new ToolStripButton
        {
            Text = "취소",
            Enabled = false,
            ToolTipText = "검색 취소"
        };
        btnCancel.Click += (s, e) => _cts?.Cancel();
        _btnDealCancelToolStrip = btnCancel;

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

        toolStrip.Items.Add(cboServer);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(txtSearch);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnSearch);
        toolStrip.Items.Add(btnCancel);
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

        // Force header center alignment by custom painting
        dgv.CellPainting += (s, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null)
            {
                e.PaintBackground(e.ClipBounds, true);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.FormattedValue?.ToString() ?? "",
                    e.CellStyle?.Font ?? dgv.Font,
                    e.CellBounds,
                    e.CellStyle?.ForeColor ?? _colors.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
                e.Handled = true;
            }
        };

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
        dgv.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                dgv.ClearSelection();
                dgv.Rows[e.RowIndex].Selected = true;
            }
        };

        dgv.CellDoubleClick += DgvDeals_CellDoubleClick;

        return dgv;
    }

    private void SetupDealGridColumns(DataGridView dgv)
    {
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn
            {
                Name = "ServerName",
                HeaderText = "서버",
                DataPropertyName = "ServerName",
                Width = 60,
                MinimumWidth = 50,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "DealTypeDisplay",
                HeaderText = "유형",
                DataPropertyName = "DealTypeDisplay",
                Width = 45,
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "DisplayName",
                HeaderText = "아이템",
                DataPropertyName = "DisplayName",
                Width = 200,
                MinimumWidth = 120,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 150
            },
            new DataGridViewTextBoxColumn
            {
                Name = "SlotAndOptionsDisplay",
                HeaderText = "카드/인챈트/랜덤옵션",
                DataPropertyName = "SlotAndOptionsDisplay",
                Width = 200,
                MinimumWidth = 120,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 140,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "수량",
                DataPropertyName = "Quantity",
                Width = 45,
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "PriceFormatted",
                HeaderText = "가격",
                DataPropertyName = "PriceFormatted",
                Width = 90,
                MinimumWidth = 75,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ShopName",
                HeaderText = "상점명",
                DataPropertyName = "ShopName",
                Width = 140,
                MinimumWidth = 80,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 80
            }
        });
    }

    private FlowLayoutPanel CreatePaginationPanel()
    {
        var paginationPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Panel,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        _btnDealPrev = new RoundedButton
        {
            Text = "<",
            Size = new Size(36, 28),
            CornerRadius = 6,
            Enabled = false
        };
        ApplyRoundedButtonStyle(_btnDealPrev, false);
        _btnDealPrev.Click += async (s, e) => await PreviousPageAsync();

        _lblDealPage = new Label
        {
            Text = "0페이지",
            AutoSize = true,
            ForeColor = _colors.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(10, 5, 10, 0)
        };

        _btnDealNext = new RoundedButton
        {
            Text = ">",
            Size = new Size(36, 28),
            CornerRadius = 6,
            Enabled = false
        };
        ApplyRoundedButtonStyle(_btnDealNext, false);
        _btnDealNext.Click += async (s, e) => await NextPageAsync();

        paginationPanel.Controls.Add(_btnDealPrev);
        paginationPanel.Controls.Add(_lblDealPage);
        paginationPanel.Controls.Add(_btnDealNext);

        // Center alignment by handling resize
        paginationPanel.Resize += (s, e) =>
        {
            var totalWidth = _btnDealPrev.Width + _lblDealPage.Width + _btnDealNext.Width + 20;
            paginationPanel.Padding = new Padding((paginationPanel.Width - totalWidth) / 2, 10, 0, 0);
        };

        return paginationPanel;
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
            // Fetch single page from API
            var items = await _gnjoyClient.SearchItemDealsAsync(searchText, serverId, _dealCurrentPage, _cts.Token);

            Debug.WriteLine($"[DealTabController] Page {_dealCurrentPage}: Got {items.Count} items");

            // Determine if there are more pages (API returns 10 items per page)
            _hasMorePages = items.Count >= 10;

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
            _lblDealStatus.Text = $"{_dealCurrentPage}페이지: {items.Count}개 결과 (더블클릭으로 상세정보 조회)";
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
        if (_hasMorePages)
        {
            _dealCurrentPage++;
            await FetchDealPageAsync();
        }
    }

    private void UpdateDealPaginationUI()
    {
        _lblDealPage.Text = $"{_dealCurrentPage}페이지";
        _btnDealPrev.Enabled = _dealCurrentPage > 1;
        _btnDealNext.Enabled = _hasMorePages;
    }

    private void SetDealSearchingState(bool searching)
    {
        _btnDealSearchToolStrip.Enabled = !searching;
        _btnDealCancelToolStrip.Enabled = searching;
        _txtDealSearch.Enabled = !searching;
        _cboDealServer.Enabled = !searching;
        _btnDealPrev.Enabled = !searching && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !searching && _hasMorePages;

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
                    _autoCompleteDropdown?.Hide();
                    _txtDealSearch.Text = searchTerm;
                    _autoCompleteDropdown?.Hide();
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

        // Add to autocomplete source if not already present
        if (_autoCompleteItems != null && !_autoCompleteItems.Contains(searchTerm))
        {
            _autoCompleteItems.Insert(0, searchTerm);
            _autoCompleteDropdown?.SetDataSource(_autoCompleteItems);
        }
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

        // Update DataGridView fonts
        if (_dgvDeals != null)
        {
            _dgvDeals.DefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize);
            _dgvDeals.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
        }

        // Update status label
        if (_lblDealStatus != null)
        {
            _lblDealStatus.Font = new Font("Malgun Gothic", baseFontSize);
        }

        // Update page label
        if (_lblDealPage != null)
        {
            _lblDealPage.Font = new Font("Malgun Gothic", baseFontSize);
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
        _btnDealPrev.Enabled = !isRateLimited && _dealCurrentPage > 1;
        _btnDealNext.Enabled = !isRateLimited && _hasMorePages;

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

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _dealBindingSource?.Dispose();
            _dgvDeals?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
