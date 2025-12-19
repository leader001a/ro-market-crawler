using System.Diagnostics;
using RoMarketCrawler.Exceptions;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Form1 partial class - Deal Search Tab (GNJOY)
/// Server-side pagination: API returns 10 items per page
/// </summary>
public partial class Form1
{
    #region Tab 1: Deal Search (GNJOY)

    // Server-side pagination state
    private bool _hasMorePages = false;

    // Item types that have enchant/card/random options (무기, 방어구, 쉐도우, 의상)
    private static readonly HashSet<int> EquipmentItemTypes = new() { 4, 5, 19, 20 };

    private void SetupDealTab(TabPage tab)
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
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // Row 3: Pagination
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));   // Row 4: Status
        ApplyTableLayoutPanelStyle(mainPanel);

        // ToolStrip-based toolbar
        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = ThemePanel,
            Renderer = new DarkToolStripRenderer()
        };

        // Server combo
        var cboServer = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
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
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            ToolTipText = "검색할 아이템명 입력"
        };
        txtSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter && !_autoCompleteDropdown.HasSelection)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _autoCompleteDropdown.Hide();
                BtnDealSearch_Click(s, e);
            }
        };
        _txtDealSearch = txtSearch.TextBox;

        // Search button
        var btnSearch = new ToolStripButton
        {
            Text = "검색",
            BackColor = ThemeAccent,
            ForeColor = ThemeAccentText,
            ToolTipText = "검색 실행"
        };
        btnSearch.Click += BtnDealSearch_Click;
        _btnDealSearch = new Button(); // Dummy for state management
        _btnDealSearchToolStrip = btnSearch;

        // Cancel button
        var btnCancel = new ToolStripButton
        {
            Text = "취소",
            Enabled = false,
            ToolTipText = "검색 취소"
        };
        btnCancel.Click += BtnDealCancel_Click;
        _btnDealCancel = new Button(); // Dummy for state management
        _btnDealCancelToolStrip = btnCancel;

        // Progress bar (right-aligned, hidden by default)
        _progressDealSearch = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visible = false,
            Alignment = ToolStripItemAlignment.Right,
            Size = new Size(120, 16)
        };

        // Add items to toolbar
        toolStrip.Items.Add(cboServer);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(txtSearch);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnSearch);
        toolStrip.Items.Add(btnCancel);
        toolStrip.Items.Add(_progressDealSearch);

        // Pagination panel (bottom, centered)
        var paginationPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemePanel,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, 3, 0, 0)
        };

        // Pagination buttons
        _btnDealPrev = new Button
        {
            Text = "<",
            Size = new Size(30, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemePanel,
            ForeColor = ThemeText,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _btnDealPrev.FlatAppearance.BorderColor = ThemeBorder;
        _btnDealPrev.Click += BtnDealPrev_Click;

        _lblDealPage = new Label
        {
            Text = "0페이지",
            AutoSize = true,
            ForeColor = ThemeText,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(10, 5, 10, 0)
        };

        _btnDealNext = new Button
        {
            Text = ">",
            Size = new Size(30, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemePanel,
            ForeColor = ThemeText,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _btnDealNext.FlatAppearance.BorderColor = ThemeBorder;
        _btnDealNext.Click += BtnDealNext_Click;

        paginationPanel.Controls.Add(_btnDealPrev);
        paginationPanel.Controls.Add(_lblDealPage);
        paginationPanel.Controls.Add(_btnDealNext);

        // Center alignment by handling resize
        paginationPanel.Resize += (s, e) =>
        {
            var totalWidth = _btnDealPrev.Width + _lblDealPage.Width + _btnDealNext.Width + 20;
            paginationPanel.Padding = new Padding((paginationPanel.Width - totalWidth) / 2, 3, 0, 0);
        };

        // Search history panel (horizontal flow of clickable labels)
        _pnlSearchHistory = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = ThemePanel,
            Padding = new Padding(5, 2, 5, 2),
            Margin = new Padding(0)
        };
        // Add "최근검색:" label
        var lblHistoryTitle = new Label
        {
            Text = "최근검색:",
            AutoSize = true,
            ForeColor = ThemeTextMuted,
            Margin = new Padding(0, 3, 5, 0)
        };
        _pnlSearchHistory.Controls.Add(lblHistoryTitle);
        UpdateSearchHistoryPanel();

        // Results grid
        _dgvDeals = new DataGridView
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
        ApplyDataGridViewStyle(_dgvDeals);

        // Force header center alignment by custom painting
        _dgvDeals.CellPainting += (s, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null)
            {
                e.PaintBackground(e.ClipBounds, true);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.FormattedValue?.ToString() ?? "",
                    e.CellStyle?.Font ?? _dgvDeals.Font,
                    e.CellBounds,
                    e.CellStyle?.ForeColor ?? ThemeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
                e.Handled = true;
            }
        };

        _dgvDeals.CellFormatting += DgvDeals_CellFormatting;
        SetupDealGridColumns();
        _dgvDeals.DataSource = _dealBindingSource;

        // Context menu for deals grid
        var dealContextMenu = new ContextMenuStrip();
        var addToMonitorItem = new ToolStripMenuItem("모니터링 추가");
        addToMonitorItem.Click += DealContextMenu_AddToMonitor;
        dealContextMenu.Items.Add(addToMonitorItem);
        _dgvDeals.ContextMenuStrip = dealContextMenu;

        // Handle right-click to select row before showing context menu
        _dgvDeals.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _dgvDeals.ClearSelection();
                _dgvDeals.Rows[e.RowIndex].Selected = true;
            }
        };

        // Status bar
        _lblDealStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "GNJOY에서 현재 노점 거래를 검색합니다."
        };
        ApplyStatusLabelStyle(_lblDealStatus);

        mainPanel.Controls.Add(toolStrip, 0, 0);
        mainPanel.Controls.Add(_pnlSearchHistory, 0, 1);
        mainPanel.Controls.Add(_dgvDeals, 0, 2);
        mainPanel.Controls.Add(paginationPanel, 0, 3);
        mainPanel.Controls.Add(_lblDealStatus, 0, 4);

        tab.Controls.Add(mainPanel);
    }

    private void SetupDealGridColumns()
    {
        _dgvDeals.Columns.AddRange(new DataGridViewColumn[]
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

        _dgvDeals.CellDoubleClick += DgvDeals_CellDoubleClick;
    }

    /// <summary>
    /// Search button click - fetch first page from API
    /// </summary>
    private async void BtnDealSearch_Click(object? sender, EventArgs e)
    {
        _dealCurrentPage = 1;  // API uses 1-based page numbers
        await FetchDealPageAsync();
    }

    /// <summary>
    /// Fetch a specific page from the API
    /// </summary>
    private async Task FetchDealPageAsync()
    {
        var searchText = _txtDealSearch.Text.Trim();
        if (string.IsNullOrEmpty(searchText))
        {
            MessageBox.Show("검색어를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // Fetch single page from API (API uses 1-based page numbers)
            var items = await _gnjoyClient.SearchItemDealsAsync(searchText, serverId, _dealCurrentPage, _cts.Token);

            Debug.WriteLine($"[Form1] Page {_dealCurrentPage}: Got {items.Count} items");

            // Determine if there are more pages (API returns 10 items per page)
            _hasMorePages = items.Count >= 10;

            // Compute display fields
            foreach (var item in items)
            {
                item.ComputeFields();
            }

            // Load details sequentially BEFORE showing results (prevents rate limiting)
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
            // Show rate limit status to user
            var remainingTime = rateLimitEx.RemainingTimeText;
            _lblDealStatus.Text = $"API 요청 제한됨: {remainingTime}";
            _lblDealStatus.ForeColor = ThemeSaleColor;  // Red color for warning

            Debug.WriteLine($"[Form1] Rate limited: {rateLimitEx.RetryAfterSeconds}s, until {rateLimitEx.RetryAfterTime}");

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
            Debug.WriteLine("[Form1] Search error: " + ex.ToString());
            MessageBox.Show("검색 중 오류가 발생했습니다.\n\n" + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progressDealSearch.Visible = false;
            SetDealSearchingState(false);
        }
    }

    /// <summary>
    /// Delay between detail requests to prevent rate limiting (in milliseconds)
    /// </summary>
    private const int DetailRequestDelayMs = 500;

    /// <summary>
    /// Load item details sequentially with delay to prevent rate limiting.
    /// Shows progress in status label and progress bar.
    /// </summary>
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
                if (!IsDisposed && IsCurrentSearch())
                {
                    var progress = (loaded * 100) / total;
                    Invoke(() =>
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
                // Re-throw rate limit exceptions to be handled by caller
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Form1] Failed to load detail for {item.ItemName}: {ex.Message}");
                loaded++;
            }
        }

        Debug.WriteLine($"[Form1] Loaded {loaded}/{total} item details sequentially");
    }

    /// <summary>
    /// Check if an item is equipment type that can have enchant/card info
    /// Types: 4=무기, 5=방어구, 19=쉐도우, 20=의상
    /// </summary>
    private bool IsEquipmentItem(DealItem item)
    {
        var effectiveItemId = item.GetEffectiveItemId();
        if (!effectiveItemId.HasValue) return true; // If can't determine, assume it's equipment

        if (!_itemIndexService.IsLoaded) return true; // If index not loaded, assume it's equipment

        var cachedItem = _itemIndexService.GetItemById(effectiveItemId.Value);
        if (cachedItem == null) return true; // If not in index, assume it's equipment

        var itemType = cachedItem.Type;
        var isEquipment = EquipmentItemTypes.Contains(itemType);

        Debug.WriteLine($"[Form1] Item '{item.ItemName}' (ID:{effectiveItemId}) Type:{itemType} IsEquipment:{isEquipment}");
        return isEquipment;
    }

    /// <summary>
    /// Previous page button - fetch previous page from API
    /// </summary>
    private async void BtnDealPrev_Click(object? sender, EventArgs e)
    {
        if (_dealCurrentPage > 1)
        {
            _dealCurrentPage--;
            await FetchDealPageAsync();
        }
    }

    /// <summary>
    /// Next page button - fetch next page from API
    /// </summary>
    private async void BtnDealNext_Click(object? sender, EventArgs e)
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

    private void BtnDealCancel_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
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
            _lblDealStatus.ForeColor = ThemeText;
        }

        // Disable search history links during search
        if (_pnlSearchHistory != null)
        {
            foreach (Control ctrl in _pnlSearchHistory.Controls)
            {
                if (ctrl is Label lbl && lbl.Tag is ValueTuple<string, string> tag && tag.Item1 == "SearchHistoryLink")
                {
                    lbl.Enabled = !searching;
                    lbl.ForeColor = searching ? ThemeTextMuted : ThemeAccent;
                    lbl.Cursor = searching ? Cursors.Default : Cursors.Hand;
                }
            }
        }
    }

    private void DgvDeals_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle == null || e.RowIndex < 0) return;

        var columnName = _dgvDeals.Columns[e.ColumnIndex].Name;

        // Deal type color
        if (columnName == "DealTypeDisplay" && e.Value != null)
        {
            var value = e.Value.ToString();
            if (value == "판매")
                e.CellStyle.ForeColor = ThemeSaleColor;
            else if (value == "구매")
                e.CellStyle.ForeColor = ThemeBuyColor;
        }
    }

    private void DgvDeals_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var dataSource = _dealBindingSource.DataSource as List<DealItem>;
        if (dataSource == null || e.RowIndex >= dataSource.Count) return;

        var selectedItem = dataSource[e.RowIndex];
        Debug.WriteLine($"[Form1] Opening detail for: {selectedItem.DisplayName}");

        using var detailForm = new ItemDetailForm(selectedItem, _itemIndexService, _currentTheme);
        detailForm.ShowDialog(this);
    }

    private void UpdateSearchHistoryPanel()
    {
        Debug.WriteLine($"[Form1] UpdateSearchHistoryPanel: _dealSearchHistory count = {_dealSearchHistory?.Count ?? -1}");
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
                ForeColor = ThemeAccent,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 3, 10, 0),
                Tag = ("SearchHistoryLink", term)  // Tuple tag to identify as search history link
            };
            btn.Click += (s, e) =>
            {
                if (s is Label lbl && lbl.Tag is (string _, string searchTerm))
                {
                    _autoCompleteDropdown?.Hide();  // Hide dropdown BEFORE setting text
                    _txtDealSearch.Text = searchTerm;
                    _autoCompleteDropdown?.Hide();  // Hide again AFTER to stop any triggered timer
                    BtnDealSearch_Click(s, e);
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

        // Update UI and save
        UpdateSearchHistoryPanel();
        SaveSettings();

        // Add to autocomplete source if not already present
        if (!_autoCompleteItems.Contains(searchTerm))
        {
            _autoCompleteItems.Insert(0, searchTerm);
            _autoCompleteDropdown?.SetDataSource(_autoCompleteItems);
        }
    }

    /// <summary>
    /// Context menu handler: Add selected deal item to monitoring list
    /// </summary>
    private async void DealContextMenu_AddToMonitor(object? sender, EventArgs e)
    {
        var selectedRow = _dgvDeals.CurrentRow;
        if (selectedRow == null) return;

        var dataSource = _dealBindingSource.DataSource as List<DealItem>;
        if (dataSource == null || selectedRow.Index >= dataSource.Count) return;

        var selectedItem = dataSource[selectedRow.Index];
        // Use DisplayName which includes Grade, Refine, CardSlots (e.g., "[UNIQUE]+11딤반다나[4]")
        var itemName = selectedItem.DisplayName ?? selectedItem.ItemName;
        var serverId = selectedItem.ServerId;

        Debug.WriteLine($"[DealTab] AddToMonitor: DisplayName='{selectedItem.DisplayName}', ItemName='{selectedItem.ItemName}'");
        Debug.WriteLine($"[DealTab] AddToMonitor: Grade='{selectedItem.Grade}', Refine={selectedItem.Refine}, CardSlots='{selectedItem.CardSlots}'");
        Debug.WriteLine($"[DealTab] AddToMonitor: Using itemName='{itemName}', serverId={serverId}");

        // Add to monitoring
        var (success, errorReason) = await _monitoringService.AddItemAsync(itemName, serverId);

        if (success)
        {
            _lblDealStatus.Text = $"'{itemName}' 모니터링 목록에 추가됨";

            // If auto-refresh is running on monitor tab, schedule immediate refresh
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
                "limit" => $"모니터링 목록은 최대 {Services.MonitoringService.MaxItemCount}개까지만 등록할 수 있습니다.",
                "duplicate" => "이미 등록된 아이템입니다.",
                _ => "아이템을 추가할 수 없습니다."
            };
            MessageBox.Show(message, "모니터링 추가", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    #endregion
}
