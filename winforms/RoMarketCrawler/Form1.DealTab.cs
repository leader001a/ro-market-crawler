using System.Diagnostics;
using RoMarketCrawler.Models;

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

    private void SetupDealTab(TabPage tab)
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // Row 0: Toolbar
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // Row 1: Search history (dynamic)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Row 2: Results grid
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // Row 3: Status
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
        txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BtnDealSearch_Click(s, e); } };
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

        // Pagination buttons
        var btnPrev = new ToolStripButton
        {
            Text = "<",
            Enabled = false,
            ToolTipText = "이전 페이지"
        };
        btnPrev.Click += BtnDealPrev_Click;
        _btnDealPrev = btnPrev;

        var lblPage = new ToolStripLabel
        {
            Text = "0페이지",
            ToolTipText = "현재 페이지"
        };
        _lblDealPage = lblPage;

        var btnNext = new ToolStripButton
        {
            Text = ">",
            Enabled = false,
            ToolTipText = "다음 페이지"
        };
        btnNext.Click += BtnDealNext_Click;
        _btnDealNext = btnNext;

        // Add items to toolbar
        toolStrip.Items.Add(cboServer);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(txtSearch);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnSearch);
        toolStrip.Items.Add(btnCancel);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnPrev);
        toolStrip.Items.Add(lblPage);
        toolStrip.Items.Add(btnNext);

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
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
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
        mainPanel.Controls.Add(_lblDealStatus, 0, 3);

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
                Name = "SlotInfoDisplay",
                HeaderText = "카드/인챈트",
                DataPropertyName = "SlotInfoDisplay",
                Width = 180,
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 120,
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

            // Update grid
            _searchResults.Clear();
            _searchResults.AddRange(items);
            _dealBindingSource.DataSource = items;
            _dealBindingSource.ResetBindings(false);

            // Update pagination UI
            UpdateDealPaginationUI();

            // Update status
            _lblDealStatus.Text = $"{_dealCurrentPage}페이지: {items.Count}개 결과 - 상세정보 로딩 중...";

            // Load details for current page items
            await LoadCurrentPageDetailsAsync(searchId, _cts.Token);

            // Final status update
            _lblDealStatus.Text = $"{_dealCurrentPage}페이지: {items.Count}개 결과 (더블클릭으로 상세정보 조회)";
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
    /// Load item details (card/enchant info) for current page items only
    /// </summary>
    private async Task LoadCurrentPageDetailsAsync(int searchId, CancellationToken cancellationToken)
    {
        bool IsCurrentSearch() => searchId == _currentSearchId && !cancellationToken.IsCancellationRequested;

        var currentPageItems = (_dealBindingSource.DataSource as List<DealItem>) ?? new List<DealItem>();
        // Check SlotInfo.Count == 0 (not SlotInfoDisplay which returns "-" when empty)
        var itemsWithDetails = currentPageItems.Where(i => i.HasDetailParams && i.SlotInfo.Count == 0).ToList();

        if (itemsWithDetails.Count == 0)
        {
            Debug.WriteLine($"[Form1] No items need detail loading on page {_dealCurrentPage}");
            return;
        }

        Debug.WriteLine($"[Form1] Loading details for {itemsWithDetails.Count} items on page {_dealCurrentPage}");
        var loadedCount = 0;
        var totalCount = itemsWithDetails.Count;

        // Update status to show loading started
        if (!IsDisposed && IsCurrentSearch())
        {
            Invoke(() =>
            {
                if (IsCurrentSearch())
                    _lblDealStatus.Text = $"{_dealCurrentPage}페이지: 상세정보 로딩 중... (0/{totalCount})";
            });
        }

        var tasks = itemsWithDetails.Select(async item =>
        {
            try
            {
                if (!IsCurrentSearch()) return;

                var detail = await _gnjoyClient.FetchItemDetailAsync(
                    item.ServerId,
                    item.MapId!.Value,
                    item.Ssi!,
                    cancellationToken);

                if (detail != null && IsCurrentSearch())
                {
                    item.ApplyDetailInfo(detail);
                    var count = Interlocked.Increment(ref loadedCount);

                    if (!IsDisposed && IsCurrentSearch())
                    {
                        Invoke(() =>
                        {
                            if (IsCurrentSearch())
                            {
                                _dealBindingSource.ResetBindings(false);
                                _lblDealStatus.Text = $"{_dealCurrentPage}페이지: 상세정보 로딩 중... ({count}/{totalCount})";
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Form1] Failed to load detail for {item.ItemName}: {ex.Message}");
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

        // Final update
        if (!IsDisposed && IsCurrentSearch())
        {
            Invoke(() =>
            {
                if (IsCurrentSearch())
                {
                    _dealBindingSource.ResetBindings(false);
                    var displayCount = (_dealBindingSource.DataSource as List<DealItem>)?.Count ?? 0;
                    _lblDealStatus.Text = $"{_dealCurrentPage}페이지: {displayCount}개 결과 (상세정보 {loadedCount}개 로딩됨, 더블클릭으로 상세정보 조회)";
                }
            });
        }
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

        using var detailForm = new ItemDetailForm(selectedItem, _itemIndexService);
        detailForm.ShowDialog(this);
    }

    private void UpdateSearchHistoryPanel()
    {
        if (_pnlSearchHistory == null) return;

        // Keep only the title label, remove all history buttons
        while (_pnlSearchHistory.Controls.Count > 1)
        {
            var ctrl = _pnlSearchHistory.Controls[1];
            _pnlSearchHistory.Controls.RemoveAt(1);
            ctrl.Dispose();
        }

        // Add history items as clickable labels
        foreach (var term in _dealSearchHistory)
        {
            var btn = new Label
            {
                Text = term,
                AutoSize = true,
                ForeColor = ThemeAccent,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 3, 10, 0),
                Tag = term
            };
            btn.Click += (s, e) =>
            {
                if (s is Label lbl && lbl.Tag is string searchTerm)
                {
                    _txtDealSearch.Text = searchTerm;
                    BtnDealSearch_Click(s, e);
                }
            };
            // Hover effect
            btn.MouseEnter += (s, e) => { if (s is Label lbl) lbl.Font = new Font(lbl.Font, FontStyle.Underline); };
            btn.MouseLeave += (s, e) => { if (s is Label lbl) lbl.Font = new Font(lbl.Font, FontStyle.Regular); };
            _pnlSearchHistory.Controls.Add(btn);
        }

        // Update panel visibility and row height
        var hasHistory = _dealSearchHistory.Count > 0;
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
    }

    #endregion
}
