using System.Diagnostics;
using RoMarketCrawler.Models;

namespace RoMarketCrawler;

/// <summary>
/// Form1 partial class - Deal Search Tab (GNJOY)
/// </summary>
public partial class Form1
{
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
}
