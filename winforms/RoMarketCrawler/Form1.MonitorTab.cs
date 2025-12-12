using System.Diagnostics;
using System.Threading;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Form1 partial class - Monitoring Tab
/// </summary>
public partial class Form1
{
    #region Tab 3: Monitoring

    // Individual item refresh state (thread-safe counter for parallel processing)
    private int _refreshedItemCount = 0;
    private Stopwatch _refreshStopwatch = new();

    // UI update timer for status column countdown
    private System.Windows.Forms.Timer _uiUpdateTimer = null!;

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
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); // 9: Status (wider for elapsed time)
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

        // Commit ComboBox selection immediately when changed (TextBox commits on Enter/focus out)
        _dgvMonitorItems.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_dgvMonitorItems.IsCurrentCellDirty && _dgvMonitorItems.CurrentCell is DataGridViewComboBoxCell)
            {
                _dgvMonitorItems.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        // Handle server, item name, and watch price changes
        _dgvMonitorItems.CellValueChanged += DgvMonitorItems_CellValueChanged;
        // Store original values before editing (for change tracking)
        // Data binding updates the bound object BEFORE CellValueChanged fires,
        // so we must capture original values in CellBeginEdit
        _dgvMonitorItems.CellBeginEdit += (s, e) =>
        {
            var row = _dgvMonitorItems.Rows[e.RowIndex];
            var columnName = _dgvMonitorItems.Columns[e.ColumnIndex].Name;

            // Get or create the dictionary for storing original values
            var originalValues = row.Tag as Dictionary<string, object?> ?? new Dictionary<string, object?>();

            // Store the original value for the column being edited
            if (columnName == "ItemName")
                originalValues["ItemName"] = row.Cells["ItemName"].Value?.ToString();
            else if (columnName == "ServerId")
                originalValues["ServerId"] = row.Cells["ServerId"].Value;
            else if (columnName == "WatchPrice")
                originalValues["WatchPrice"] = row.Cells["WatchPrice"].Value;

            row.Tag = originalValues;
        };

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

        // Initialize refresh timer (checks for items due to refresh)
        _monitorTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _monitorTimer.Tick += MonitorTimer_Tick;

        // Initialize UI update timer (updates status column every second)
        _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
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

        // Item name column (editable for renaming)
        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName",
            HeaderText = "아이템",
            DataPropertyName = "ItemName",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = false
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

        // Status column (read-only, shows refresh countdown)
        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "RefreshStatus",
            HeaderText = "상태",
            Width = 70,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });

        // Add cell formatting for status column
        _dgvMonitorItems.CellFormatting += DgvMonitorItems_CellFormatting;
    }

    private async void DgvMonitorItems_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _dgvMonitorItems.Columns[e.ColumnIndex].Name;
        var row = _dgvMonitorItems.Rows[e.RowIndex];

        // Get stored original values from CellBeginEdit
        var originalValues = row.Tag as Dictionary<string, object?>;

        if (columnName == "ItemName")
        {
            // Handle ItemName change (rename)
            var oldName = originalValues?.GetValueOrDefault("ItemName")?.ToString();
            var newName = row.Cells["ItemName"].Value?.ToString();

            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;

            // Find the MonitorItem by NEW name (data binding already updated ItemName before this event fires)
            var items = _monitoringService.Config.Items;
            var item = items.FirstOrDefault(i => i.ItemName == newName);
            if (item == null) return;

            var serverId = item.ServerId;

            // Rename the item
            var success = await _monitoringService.RenameItemAsync(oldName, serverId, newName);
            if (!success)
            {
                // Revert the change if rename failed
                row.Cells["ItemName"].Value = oldName;
                MessageBox.Show($"아이템 이름을 변경할 수 없습니다. 동일한 이름이 이미 존재합니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Clear the stored original values
            originalValues?.Remove("ItemName");

            // Refresh the binding to update display
            UpdateMonitorItemList();
            UpdateMonitorResults();  // Also refresh results to remove old cached data
            return;
        }

        if (columnName == "ServerId")
        {
            // Handle ServerId change
            var oldServerId = originalValues?.GetValueOrDefault("ServerId") as int?;
            var newServerId = row.Cells["ServerId"].Value as int?;

            if (oldServerId == null || newServerId == null || oldServerId == newServerId) return;

            // Get item name from the row (this is still the same, only ServerId changed)
            var itemName = row.Cells["ItemName"].Value?.ToString();
            if (string.IsNullOrEmpty(itemName)) return;

            // Update the server in the service using the ORIGINAL serverId
            await _monitoringService.UpdateItemServerAsync(itemName, oldServerId.Value, newServerId.Value);

            // Clear the stored original value
            originalValues?.Remove("ServerId");

            // Refresh the binding to update display
            UpdateMonitorItemList();
            UpdateMonitorResults();
            return;
        }

        if (columnName == "WatchPrice")
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

            // Get item name and server from the row to find exact item
            var itemName = row.Cells["ItemName"].Value?.ToString();
            var serverId = row.Cells["ServerId"].Value as int?;
            if (string.IsNullOrEmpty(itemName) || serverId == null) return;

            // Find the MonitorItem by both name and server (for exact match)
            var configItems = _monitoringService.Config.Items;
            var monitorItem = configItems.FirstOrDefault(i =>
                i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId);
            if (monitorItem == null) return;

            monitorItem.WatchPrice = newWatchPrice;

            // Clear the stored original value
            originalValues?.Remove("WatchPrice");

            // Clear cache for this item (defensive: ensure fresh results on next refresh)
            _monitoringService.ClearItemCache(itemName, serverId.Value);

            // Save config and refresh UI
            await _monitoringService.SaveConfigAsync();
            UpdateMonitorItemList();
            UpdateMonitorResults();
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

        // Column order: Grade, Refine, ItemName, Server, ...
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Grade",
            HeaderText = "등급",
            Width = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Refine",
            HeaderText = "제련",
            Width = 45,
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
        _uiUpdateTimer.Stop();

        if (seconds > 0)
        {
            // Initialize individual item refresh schedule
            _monitoringService.InitializeRefreshSchedule();
            _refreshedItemCount = 0;
            _refreshStopwatch.Restart();

            // Timer checks every 3 seconds for items due to refresh
            _monitorTimer.Interval = 3000;
            _monitorTimer.Start();

            // UI timer updates status column every second
            _uiUpdateTimer.Start();

            // Immediately update status column to show initial countdowns
            UpdateItemStatusColumn();

            Debug.WriteLine($"[Form1] Monitor timer started: checking every 3s, item interval {seconds}s");
        }
    }

    private void StopMonitorTimer()
    {
        _monitorTimer.Stop();
        _uiUpdateTimer.Stop();
        _refreshStopwatch.Stop();

        // Clear all item refresh states
        foreach (var item in _monitoringService.Config.Items)
        {
            item.IsRefreshing = false;
            item.NextRefreshTime = null;
        }

        // Update status column to show "-"
        UpdateItemStatusColumn();

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

        // Start timing
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var progress = new Progress<MonitorProgress>(p =>
            {
                _lblMonitorStatus.Text = $"{p.Phase}: {p.CurrentItem} ({p.CurrentIndex}/{p.TotalItems})";
            });

            await _monitoringService.RefreshAllAsync(progress, _monitorCts.Token);

            stopwatch.Stop();
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            var resultCount = _monitoringService.Results.Count;
            Debug.WriteLine($"[Form1] RefreshMonitoringAsync complete, Results count={resultCount}, Elapsed={elapsedSeconds:F1}s");

            UpdateMonitorResults();
            _lblMonitorStatus.Text = $"조회 완료 ({DateTime.Now:HH:mm:ss}) - {resultCount}건, {elapsedSeconds:F1}초";

            // Check for good deals
            var goodDeals = _monitoringService.GetGoodDeals();
            if (goodDeals.Count > 0)
            {
                _lblMonitorStatus.Text = $"저렴한 매물 {goodDeals.Count}건 발견! ({DateTime.Now:HH:mm:ss}) - {elapsedSeconds:F1}초";
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _lblMonitorStatus.Text = $"조회 취소됨 ({stopwatch.Elapsed.TotalSeconds:F1}초)";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
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
        // Get all items due for refresh (supports unlimited parallel processing)
        var itemsDue = _monitoringService.GetAllItemsDueForRefresh();
        if (itemsDue.Count == 0)
        {
            // No items due yet, wait for next tick
            return;
        }

        Debug.WriteLine($"[Form1] Processing {itemsDue.Count} items in parallel");

        // Mark all items as refreshing
        foreach (var item in itemsDue)
        {
            item.IsRefreshing = true;
        }

        var itemNames = string.Join(", ", itemsDue.Select(i => i.ItemName).Take(3));
        if (itemsDue.Count > 3) itemNames += $" 외 {itemsDue.Count - 3}개";
        _lblMonitorStatus.Text = $"조회 중: {itemNames}...";
        UpdateItemStatusColumn();

        var cancellationToken = _monitorCts?.Token ?? CancellationToken.None;

        // Process all items in parallel (unlimited concurrency)
        var tasks = itemsDue.Select(async item =>
        {
            try
            {
                await _monitoringService.RefreshSingleItemAsync(item, cancellationToken);
                Interlocked.Increment(ref _refreshedItemCount);
                return (item, success: true, error: (string?)null);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[Form1] Item refresh cancelled: {item.ItemName}");
                return (item, success: false, error: "취소됨");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Form1] Item refresh error: {item.ItemName}, {ex.Message}");
                return (item, success: false, error: ex.Message);
            }
            finally
            {
                item.IsRefreshing = false;
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Update UI after all items complete
        if (InvokeRequired)
        {
            Invoke(new Action(() => AfterParallelRefreshComplete(itemsDue.Count)));
        }
        else
        {
            AfterParallelRefreshComplete(itemsDue.Count);
        }
    }

    private void AfterParallelRefreshComplete(int processedCount)
    {
        UpdateMonitorResults();
        UpdateItemStatusColumn();

        var elapsed = _refreshStopwatch.Elapsed.TotalSeconds;
        var totalItems = _monitoringService.ItemCount;
        _lblMonitorStatus.Text = $"갱신 완료 ({DateTime.Now:HH:mm:ss}) - {_refreshedItemCount}/{totalItems}건, {elapsed:F0}초";

        // Check for good deals
        var goodDeals = _monitoringService.GetGoodDeals();
        if (goodDeals.Count > 0)
        {
            _lblMonitorStatus.Text = $"저렴한 매물 {goodDeals.Count}건! ({DateTime.Now:HH:mm:ss})";
        }
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Update status column every second to show countdown
        UpdateItemStatusColumn();
    }

    private void UpdateItemStatusColumn()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateItemStatusColumn));
            return;
        }

        var items = _monitoringService.Config.Items;
        var isAutoRefreshEnabled = _monitorTimer.Enabled;

        for (int i = 0; i < _dgvMonitorItems.Rows.Count && i < items.Count; i++)
        {
            var item = items[i];
            var row = _dgvMonitorItems.Rows[i];
            var statusCell = row.Cells["RefreshStatus"];

            if (!isAutoRefreshEnabled)
            {
                statusCell.Value = "-";
            }
            else if (item.IsRefreshing)
            {
                statusCell.Value = "조회 중...";
            }
            else if (item.NextRefreshTime.HasValue)
            {
                var remaining = (item.NextRefreshTime.Value - DateTime.Now).TotalSeconds;
                if (remaining > 0)
                    statusCell.Value = $"{(int)remaining}초 후";
                else
                    statusCell.Value = "대기";
            }
            else
            {
                statusCell.Value = "-";
            }
        }
    }

    private void DgvMonitorItems_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _dgvMonitorItems.Columns[e.ColumnIndex].Name;
        if (columnName != "RefreshStatus") return;

        var cellValue = e.Value?.ToString() ?? "";

        if (cellValue == "조회 중...")
        {
            // Highlight refreshing item with yellow background
            e.CellStyle!.BackColor = Color.FromArgb(120, 100, 40);
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        }
        else if (cellValue == "-")
        {
            // Gray for disabled
            e.CellStyle!.ForeColor = Color.Gray;
        }
        else if (cellValue == "대기")
        {
            // Green for ready to refresh
            e.CellStyle!.ForeColor = Color.FromArgb(100, 255, 100);
        }
    }

    #endregion
}
