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

            // Start alarm timer automatically (always running, controlled by mute button)
            _alarmTimer.Interval = _alarmIntervalSeconds * 1000;
            _alarmTimer.Start();
            Debug.WriteLine($"[Form1] Alarm timer auto-started: {_alarmIntervalSeconds}s interval");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Monitoring load error: {ex.Message}");
        }
    }

    private void SetupMonitoringTab(TabPage tabPage)
    {
        // Main layout: ToolStrip on top, content below
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // ToolStrip
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content area
        ApplyTableLayoutPanelStyle(mainLayout);

        // Create ToolStrip toolbar
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = ThemePanel,
            ForeColor = ThemeText,
            Renderer = _currentTheme == ThemeType.Dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer(),
            Padding = new Padding(4, 0, 4, 0)
        };

        // Item name input
        var txtItemName = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 300,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            ToolTipText = "모니터링할 아이템명 입력"
        };
        txtItemName.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BtnMonitorAdd_Click(s, e); } };
        _txtMonitorItemName = txtItemName.TextBox;

        // Server selection
        var cboServer = new ToolStripComboBox
        {
            Width = 80,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ThemeGrid,
            ForeColor = ThemeText
        };
        foreach (var server in Server.GetAllServers())
            cboServer.Items.Add(server);
        cboServer.ComboBox.DisplayMember = "Name";
        cboServer.SelectedIndex = 0;
        _cboMonitorServer = cboServer.ComboBox;

        // Add/Remove buttons
        _btnMonitorAdd = new ToolStripButton("추가") { ToolTipText = "모니터링 목록에 추가" };
        _btnMonitorAdd.Click += BtnMonitorAdd_Click;

        _btnMonitorRemove = new ToolStripButton("선택삭제") { ToolTipText = "선택한 항목 삭제" };
        _btnMonitorRemove.Click += BtnMonitorRemove_Click;

        var btnClearAll = new ToolStripButton("전체삭제") { ToolTipText = "모든 항목 삭제" };
        btnClearAll.Click += BtnMonitorClearAll_Click;

        // Manual refresh button (right-aligned)
        _btnMonitorRefresh = new ToolStripButton("수동조회")
        {
            ToolTipText = "즉시 조회 실행",
            Alignment = ToolStripItemAlignment.Right
        };
        _btnMonitorRefresh.Click += BtnMonitorRefresh_Click;

        // Progress bar for refresh operation (right-aligned, hidden by default)
        _progressMonitor = new ToolStripProgressBar
        {
            Alignment = ToolStripItemAlignment.Right,
            Size = new Size(100, 16),
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };

        // Auto-refresh dropdown (right-aligned)
        var btnAutoRefresh = new ToolStripDropDownButton("자동조회")
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoToolTip = false
        };
        var autoRefreshPanel = CreateAutoRefreshPanel();
        var autoRefreshHost = new ToolStripControlHost(autoRefreshPanel) { AutoSize = false, Size = autoRefreshPanel.Size };
        btnAutoRefresh.DropDownItems.Add(autoRefreshHost);
        btnAutoRefresh.DropDown.BackColor = ThemePanel;

        // Alarm settings dropdown (right-aligned)
        var btnAlarmSettings = new ToolStripDropDownButton("알람설정")
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoToolTip = false
        };
        var alarmPanel = CreateAlarmSettingsPanel();
        var alarmHost = new ToolStripControlHost(alarmPanel) { AutoSize = false, Size = alarmPanel.Size };
        btnAlarmSettings.DropDownItems.Add(alarmHost);
        btnAlarmSettings.DropDown.BackColor = ThemePanel;

        // Mute button (right-aligned) - ToolStripButton for consistent design
        _btnSoundMute = new ToolStripButton
        {
            ToolTipText = "알람 음소거 토글",
            Alignment = ToolStripItemAlignment.Right
        };
        UpdateSoundMuteButton();
        _btnSoundMute.Click += BtnSoundMute_Click;

        // Auto-refresh status label (right-aligned, before 자동조회 button)
        _lblAutoRefreshStatus = new ToolStripLabel
        {
            Text = "[정지]",
            ForeColor = ThemeTextMuted,
            Alignment = ToolStripItemAlignment.Right
        };

        // Separators for right-aligned items
        var sepRight1 = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };
        var sepRight2 = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };
        var sepRight3 = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };

        // Hidden status label (for compatibility)
        _lblMonitorStatus = new Label { Text = "", Visible = false };
        _lblRefreshSetting = new Label { Text = "", Visible = false };

        // Add items to toolbar
        // Left side: server selection, item input and management
        toolStrip.Items.Add(cboServer);
        toolStrip.Items.Add(txtItemName);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_btnMonitorAdd);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_btnMonitorRemove);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnClearAll);

        // Right side: refresh and alarm controls (added in reverse display order)
        // Display order: [진행바] 수동조회 | [상태] 자동조회 | 알람설정 | 음소거
        toolStrip.Items.Add(_btnSoundMute);    // Rightmost
        toolStrip.Items.Add(sepRight1);        // |
        toolStrip.Items.Add(btnAlarmSettings); // Second from right
        toolStrip.Items.Add(sepRight2);        // |
        toolStrip.Items.Add(btnAutoRefresh);   // 자동조회 button
        toolStrip.Items.Add(_lblAutoRefreshStatus); // Status label [정지/동작중]
        toolStrip.Items.Add(sepRight3);        // |
        toolStrip.Items.Add(_btnMonitorRefresh);       // 수동조회 button
        toolStrip.Items.Add(_progressMonitor);         // Progress bar (hidden by default)

        mainLayout.Controls.Add(toolStrip, 0, 0);

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
        var lblItemList = new Label { Text = $"모니터링 목록 (최대 {MonitoringService.MaxItemCount}개)", Dock = DockStyle.Top, Height = 22 };
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

        // Force header center alignment by custom painting
        _dgvMonitorItems.CellPainting += (s, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null) // Header row
            {
                e.PaintBackground(e.ClipBounds, true);

                // Draw header text centered manually
                TextRenderer.DrawText(
                    e.Graphics,
                    e.FormattedValue?.ToString() ?? "",
                    e.CellStyle?.Font ?? _dgvMonitorItems.Font,
                    e.CellBounds,
                    e.CellStyle?.ForeColor ?? ThemeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );

                e.Handled = true; // Prevent default painting
            }
        };

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

        // Context menu for items grid
        var itemsContextMenu = new ContextMenuStrip();
        var resetItemsColumnsItem = new ToolStripMenuItem("컬럼 크기 초기화");
        resetItemsColumnsItem.Click += (s, e) => ResetMonitorItemsColumnSizes();
        itemsContextMenu.Items.Add(resetItemsColumnsItem);
        _dgvMonitorItems.ContextMenuStrip = itemsContextMenu;

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

        // Context menu for results grid
        var resultsContextMenu = new ContextMenuStrip();
        var resetColumnsItem = new ToolStripMenuItem("컬럼 크기 초기화");
        resetColumnsItem.Click += (s, e) => ResetMonitorResultsColumnSizes();
        resultsContextMenu.Items.Add(resetColumnsItem);
        _dgvMonitorResults.ContextMenuStrip = resultsContextMenu;

        // Force header center alignment by custom painting
        _dgvMonitorResults.CellPainting += (s, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null) // Header row
            {
                e.PaintBackground(e.ClipBounds, true);

                // Draw header text centered manually
                TextRenderer.DrawText(
                    e.Graphics,
                    e.FormattedValue?.ToString() ?? "",
                    e.CellStyle?.Font ?? _dgvMonitorResults.Font,
                    e.CellBounds,
                    e.CellStyle?.ForeColor ?? ThemeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );

                e.Handled = true; // Prevent default painting
            }
        };

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

        // Initialize alarm timer (independent timer for checking 득템 items)
        _alarmTimer = new System.Windows.Forms.Timer { Interval = _alarmIntervalSeconds * 1000 };
        _alarmTimer.Tick += AlarmTimer_Tick;
    }

    private void SetupMonitorItemsColumns()
    {
        _dgvMonitorItems.Columns.Clear();

        // Server column (ComboBox for editing) - first column
        _dgvMonitorItems.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "ServerId",
            HeaderText = "서버",
            DataPropertyName = "ServerId",
            Width = 95,
            MinimumWidth = 80,
            DataSource = Server.GetAllServers(),
            ValueMember = "Id",
            DisplayMember = "Name",
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });

        // Item name column (editable for renaming)
        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName",
            HeaderText = "아이템",
            DataPropertyName = "ItemName",
            Width = 150,
            MinimumWidth = 80,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ReadOnly = false
        });

        // Watch price column (editable text box for price threshold)
        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "WatchPrice",
            HeaderText = "감시가",
            DataPropertyName = "WatchPrice",
            Width = 90,
            MinimumWidth = 70,
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
            Width = 80,
            MinimumWidth = 65,
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

        // Column order: Server, Grade, Refine, ItemName, ...
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ServerName",
            HeaderText = "서버",
            Width = 75,
            MinimumWidth = 60,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Grade",
            HeaderText = "등급",
            Width = 65,
            MinimumWidth = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Refine",
            HeaderText = "제련",
            Width = 50,
            MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName",
            HeaderText = "아이템",
            Width = 200,
            MinimumWidth = 100,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DealCount",
            HeaderText = "수량",
            Width = 50,
            MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LowestPrice",
            HeaderText = "최저가",
            Width = 95,
            MinimumWidth = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "YesterdayAvg",
            HeaderText = "어제평균",
            Width = 95,
            MinimumWidth = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "WeekAvg",
            HeaderText = "주간평균",
            Width = 95,
            MinimumWidth = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PriceDiff",
            HeaderText = "%",
            Width = 55,
            MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "판정",
            Width = 55,
            MinimumWidth = 45,
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

        // Suspend layout for batch update performance
        _dgvMonitorResults.SuspendLayout();

        try
        {
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

            // Note: Sound alert is now handled by independent alarm timer (AlarmTimer_Tick)
            // This decouples the alarm from the refresh cycle for more consistent behavior

            // Clear default first row selection
            _dgvMonitorResults.ClearSelection();
        }
        finally
        {
            // Resume layout after batch update
            _dgvMonitorResults.ResumeLayout();
        }
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
            if (_lblAutoRefreshStatus != null)
            {
                _lblAutoRefreshStatus.Text = "[동작중]";
                _lblAutoRefreshStatus.ForeColor = ThemeSaleColor;
            }
        }
        else
        {
            _btnApplyInterval.Text = "자동갱신";
            _btnApplyInterval.ForeColor = ThemeText;
            if (_lblAutoRefreshStatus != null)
            {
                _lblAutoRefreshStatus.Text = "[정지]";
                _lblAutoRefreshStatus.ForeColor = ThemeTextMuted;
            }
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

    private void TabControl_Selecting(object? sender, TabControlCancelEventArgs e)
    {
        Debug.WriteLine($"[Form1] TabControl_Selecting: CurrentIndex={_tabControl.SelectedIndex}, TargetIndex={e.TabPageIndex}, TimerEnabled={_monitorTimer?.Enabled}");

        // Check if leaving Monitor tab (index 2) while auto-refresh is running
        if (_tabControl.SelectedIndex == 2 && e.TabPageIndex != 2)
        {
            if (_monitorTimer != null && _monitorTimer.Enabled)
            {
                var result = MessageBox.Show(
                    "다른 탭으로 이동하면 자동 갱신이 중지됩니다.\n이동하시겠습니까?",
                    "노점 모니터링",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                // User confirmed - stop auto-refresh
                StopMonitorTimer();
                _monitoringService.Config.RefreshIntervalSeconds = 0;
                UpdateMonitorRefreshLabel();
                Debug.WriteLine("[Form1] Auto-refresh stopped due to tab change");
            }
        }
    }

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Refresh Monitor tab UI when switching to it (index 2)
        if (_tabControl.SelectedIndex == 2)
        {
            UpdateMonitorItemList();
            UpdateMonitorResults();
            UpdateItemStatusColumn();
            Debug.WriteLine("[Form1] Monitor tab selected - UI refreshed");
        }
    }

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

        var (success, errorReason) = await _monitoringService.AddItemAsync(itemName, serverId);
        if (success)
        {
            _txtMonitorItemName.Clear();
            UpdateMonitorItemList();
            _lblMonitorStatus.Text = $"'{itemName}' 추가됨 ({_monitoringService.ItemCount}/{MonitoringService.MaxItemCount})";
        }
        else
        {
            var message = errorReason switch
            {
                "limit" => $"모니터링 목록은 최대 {MonitoringService.MaxItemCount}개까지만 등록할 수 있습니다.",
                "duplicate" => "이미 등록된 아이템입니다.",
                _ => "아이템을 추가할 수 없습니다."
            };
            MessageBox.Show(message, "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        // Show and initialize progress bar
        _progressMonitor.Value = 0;
        _progressMonitor.Visible = true;

        // Start timing
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var progress = new Progress<MonitorProgress>(p =>
            {
                _lblMonitorStatus.Text = $"{p.Phase}: {p.CurrentItem} ({p.CurrentIndex}/{p.TotalItems})";
                // Update progress bar
                if (p.TotalItems > 0)
                {
                    var percent = (int)(p.CurrentIndex * 100 / p.TotalItems);
                    _progressMonitor.Value = Math.Min(percent, 100);
                }
            });

            await _monitoringService.RefreshAllAsync(progress, _monitorCts.Token);

            stopwatch.Stop();
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            var resultCount = _monitoringService.Results.Count;
            Debug.WriteLine($"[Form1] RefreshMonitoringAsync complete, Results count={resultCount}, Elapsed={elapsedSeconds:F1}s");

            // Set processing state for all items
            var allItems = _monitoringService.Config.Items.ToList();
            foreach (var item in allItems)
            {
                item.IsProcessing = true;
            }
            _lblMonitorStatus.Text = $"결과 처리 중... ({resultCount}건)";
            UpdateItemStatusColumn();

            // Start minimum display timer (ensures "처리 중..." is visible)
            var minDisplayTask = Task.Delay(500);

            try
            {
                UpdateMonitorResults();

                // Wait for minimum display time to ensure user sees "처리 중..."
                await minDisplayTask;
            }
            finally
            {
                // Clear processing state for all items
                foreach (var item in allItems)
                {
                    item.IsProcessing = false;
                }

                // Schedule next refresh AFTER rendering is complete (if auto-refresh is enabled)
                // This ensures countdown starts when user can see the results
                if (_monitorTimer.Enabled)
                {
                    _monitoringService.ScheduleNextRefreshForAll();
                }

                UpdateItemStatusColumn();
            }

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
            _progressMonitor.Visible = false;
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
            if (seconds < 10) seconds = 10; // Minimum 10 seconds
            await _monitoringService.SetRefreshIntervalAsync(seconds);
            StartMonitorTimer(seconds);
            _lblMonitorStatus.Text = $"자동 갱신 시작: {seconds}초";
        }

        UpdateMonitorRefreshLabel();
    }


    private void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        // Get all items due for refresh, excluding items already being processed or refreshed
        var itemsDue = _monitoringService.GetAllItemsDueForRefresh()
            .Where(i => !i.IsRefreshing && !i.IsProcessing)
            .ToList();
        if (itemsDue.Count == 0)
        {
            return;
        }

        Debug.WriteLine($"[Form1] Starting {itemsDue.Count} items independently");

        // Mark all items as refreshing and start independent processing
        foreach (var item in itemsDue)
        {
            item.IsRefreshing = true;
            // Fire-and-forget: each item processes independently
            _ = ProcessSingleItemAsync(item);
        }

        UpdateItemStatusColumn();

        var itemNames = string.Join(", ", itemsDue.Select(i => i.ItemName).Take(3));
        if (itemsDue.Count > 3) itemNames += $" 외 {itemsDue.Count - 3}개";
        _lblMonitorStatus.Text = $"조회 중: {itemNames}...";
    }

    /// <summary>
    /// Process a single item independently: query → process → countdown
    /// Each item transitions through states without waiting for other items
    /// </summary>
    private async Task ProcessSingleItemAsync(MonitorItem item)
    {
        // Create a linked CancellationToken with individual timeout (15 seconds)
        // This prevents items from getting stuck in IsRefreshing state due to slow/blocked API calls
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            _monitorCts?.Token ?? CancellationToken.None);
        var cancellationToken = linkedCts.Token;

        try
        {
            // 1. API Query (조회 중...)
            Debug.WriteLine($"[Form1] Querying: {item.ItemName}");
            await _monitoringService.RefreshSingleItemAsync(item, cancellationToken);
            Interlocked.Increment(ref _refreshedItemCount);

            // 2. Transition to processing state (처리 중...)
            item.IsRefreshing = false;
            item.IsProcessing = true;

            // 3. Update UI on UI thread
            if (InvokeRequired)
            {
                Invoke(() =>
                {
                    UpdateItemStatusColumn();
                    UpdateMonitorResults();
                });
            }
            else
            {
                UpdateItemStatusColumn();
                UpdateMonitorResults();
            }

            // 4. Minimum display time for "처리 중..."
            await Task.Delay(500, cancellationToken);

            // 5. Transition to countdown state (N초 후)
            item.IsProcessing = false;
            _monitoringService.ScheduleNextRefresh(new[] { item });

            // 6. Update status column
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    UpdateItemStatusColumn();
                    UpdateStatusBarAfterItemComplete();
                });
            }

            Debug.WriteLine($"[Form1] Completed: {item.ItemName}");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // Individual item timeout - schedule next refresh so it retries later
            Debug.WriteLine($"[Form1] Timeout: {item.ItemName} (15s exceeded)");
            item.IsRefreshing = false;
            item.IsProcessing = false;
            _monitoringService.ScheduleNextRefresh(new[] { item });
        }
        catch (OperationCanceledException)
        {
            // User-initiated cancellation
            Debug.WriteLine($"[Form1] Cancelled: {item.ItemName}");
            item.IsRefreshing = false;
            item.IsProcessing = false;
        }
        catch (Exception ex)
        {
            // API error - schedule next refresh so it retries later
            Debug.WriteLine($"[Form1] Error: {item.ItemName}, {ex.Message}");
            item.IsRefreshing = false;
            item.IsProcessing = false;
            _monitoringService.ScheduleNextRefresh(new[] { item });
        }
        finally
        {
            if (!IsDisposed)
            {
                Invoke(() => UpdateItemStatusColumn());
            }
        }
    }

    /// <summary>
    /// Update status bar after an item completes processing
    /// </summary>
    private void UpdateStatusBarAfterItemComplete()
    {
        var processingCount = _monitoringService.Config.Items.Count(i => i.IsRefreshing || i.IsProcessing);
        var totalCount = _monitoringService.ItemCount;
        var resultCount = _monitoringService.Results.Count;

        if (processingCount > 0)
        {
            _lblMonitorStatus.Text = $"처리 중... ({processingCount}/{totalCount})";
        }
        else
        {
            _lblMonitorStatus.Text = $"갱신 완료 ({DateTime.Now:HH:mm:ss}) - {resultCount}건";

            // Check for good deals
            var goodDeals = _monitoringService.GetGoodDeals();
            if (goodDeals.Count > 0)
            {
                _lblMonitorStatus.Text = $"저렴한 매물 {goodDeals.Count}건! ({DateTime.Now:HH:mm:ss})";
            }
        }
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Update status column every second to show countdown
        UpdateItemStatusColumn();
    }

    /// <summary>
    /// Sets items to "처리 중..." status and forces immediate UI update.
    /// If refreshedItems is provided, only those items are updated; otherwise all items are updated.
    /// </summary>
    private void SetProcessingStatusVisible(bool isProcessing, List<MonitorItem>? refreshedItems = null)
    {
        _isProcessingResults = isProcessing;

        // Get the list of items to update
        var itemsToUpdate = refreshedItems ?? _monitoringService.Config.Items.ToList();

        for (int i = 0; i < _dgvMonitorItems.Rows.Count; i++)
        {
            var row = _dgvMonitorItems.Rows[i];
            var rowItem = row.DataBoundItem as MonitorItem;

            // Skip items not in the refresh list
            if (rowItem == null || !itemsToUpdate.Contains(rowItem))
            {
                continue;
            }

            var cell = row.Cells["RefreshStatus"];
            if (cell != null)
            {
                cell.Value = isProcessing ? "처리 중..." : "";
            }
        }

        // Force synchronous repaint
        _dgvMonitorItems.Invalidate();
        _dgvMonitorItems.Update();

        if (isProcessing)
        {
            Application.DoEvents();
        }
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

            // State priority: Refreshing > Processing > Countdown
            if (item.IsRefreshing)
            {
                statusCell.Value = "조회 중...";
            }
            else if (item.IsProcessing)
            {
                statusCell.Value = "처리 중...";
            }
            else if (!isAutoRefreshEnabled)
            {
                statusCell.Value = "-";
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

        if (cellValue == "처리 중...")
        {
            // Highlight processing state with blue background
            e.CellStyle!.BackColor = Color.FromArgb(40, 80, 140);
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        }
        else if (cellValue == "조회 중...")
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

    private void BtnSoundMute_Click(object? sender, EventArgs e)
    {
        _isSoundMuted = !_isSoundMuted;
        UpdateSoundMuteButton();
        SaveSettings();
    }

    private void UpdateSoundMuteButton()
    {
        if (_btnSoundMute == null) return;

        if (_isSoundMuted)
        {
            _btnSoundMute.Text = "음소거 해제";
            _btnSoundMute.ForeColor = ThemeSaleColor; // Red color for muted
        }
        else
        {
            _btnSoundMute.Text = "음소거";
            _btnSoundMute.ForeColor = ThemeText; // Normal color
        }
    }

    private void CboAlarmSound_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboAlarmSound?.SelectedItem is AlarmSoundItem item)
        {
            _selectedAlarmSound = item.SoundType;
            SaveSettings();
        }
    }

    /// <summary>
    /// Handle alarm interval change - update timer immediately
    /// </summary>
    private void NudAlarmInterval_ValueChanged(object? sender, EventArgs e)
    {
        _alarmIntervalSeconds = (int)_nudAlarmInterval.Value;
        _alarmTimer.Interval = _alarmIntervalSeconds * 1000;
        SaveSettings();
        Debug.WriteLine($"[Form1] Alarm interval changed: {_alarmIntervalSeconds}s");
    }

    /// <summary>
    /// Independent alarm timer tick - checks if any item is in 득템 state
    /// </summary>
    private void AlarmTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSoundMuted) return;

        // Check if any result has an item below watch price (득템 status)
        var results = _monitoringService.Results.Values.ToList();
        if (results.Count == 0) return;

        // Check each result for deals below watch price
        bool hasBargain = false;
        foreach (var result in results)
        {
            var watchPrice = result.Item.WatchPrice;
            if (!watchPrice.HasValue) continue;

            // Check if any deal in this result is at or below watch price
            if (result.Deals.Any(d => d.Price <= watchPrice.Value))
            {
                hasBargain = true;
                break;
            }
        }

        if (hasBargain)
        {
            Debug.WriteLine($"[Form1] Alarm timer: 득템 item detected, playing sound");
            PlayAlarmSound();
        }
    }

    private void PlayAlarmSound()
    {
        if (_selectedAlarmSound == AlarmSoundType.SystemSound)
        {
            // Windows system exclamation sound
            System.Media.SystemSounds.Exclamation.Play();
        }
        else
        {
            // NAudio-based synthesized sounds
            AlarmSoundService.PlaySound(_selectedAlarmSound);
        }
    }

    /// <summary>
    /// Creates the auto-refresh settings panel for dropdown
    /// </summary>
    private Panel CreateAutoRefreshPanel()
    {
        // Scale factor based on font size (base is 12pt)
        var scale = _baseFontSize / 12f;
        var font = new Font("Malgun Gothic", _baseFontSize - 3);
        var smallFont = new Font("Malgun Gothic", _baseFontSize - 3.5f);

        var panelWidth = (int)(230 * scale);
        var rowHeight = (int)(28 * scale);

        var panel = new Panel
        {
            Size = new Size(panelWidth, (int)(120 * scale)),
            BackColor = ThemePanel,
            Padding = new Padding(8)
        };

        var yPos = 8;

        // Title/Description
        var lblTitle = new Label
        {
            Text = "설정된 간격마다 모니터링 목록의\n아이템을 자동으로 조회합니다.",
            Location = new Point(8, yPos),
            AutoSize = false,
            Size = new Size(panelWidth - 20, (int)(40 * scale)),
            ForeColor = ThemeTextMuted,
            Font = smallFont
        };
        yPos += (int)(45 * scale);

        var lblInterval = new Label
        {
            Name = "lblInterval",
            Text = "새로고침 간격 (초)",
            Location = new Point(8, yPos + 2),
            AutoSize = true,
            ForeColor = ThemeText,
            Font = font
        };

        _nudRefreshInterval = new NumericUpDown
        {
            Name = "nudRefreshInterval",
            Minimum = 10, Maximum = 600, Value = 30,
            Location = new Point((int)(140 * scale), yPos),
            Size = new Size((int)(65 * scale), rowHeight),
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Font = font
        };
        yPos += rowHeight + 6;

        _btnApplyInterval = new Button
        {
            Name = "btnApplyInterval",
            Text = "자동갱신",
            Location = new Point(8, yPos),
            Size = new Size(panelWidth - 20, rowHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeAccent,
            ForeColor = ThemeAccentText,
            Font = font
        };
        _btnApplyInterval.FlatAppearance.BorderSize = 0;
        _btnApplyInterval.Click += BtnApplyInterval_Click;

        panel.Size = new Size(panelWidth, yPos + rowHeight + 10);
        panel.Controls.AddRange(new Control[] { lblTitle, lblInterval, _nudRefreshInterval, _btnApplyInterval });
        return panel;
    }

    /// <summary>
    /// Creates the alarm settings panel for dropdown
    /// </summary>
    private Panel CreateAlarmSettingsPanel()
    {
        // Scale factor based on font size (base is 12pt)
        var scale = _baseFontSize / 12f;
        var font = new Font("Malgun Gothic", _baseFontSize - 3);
        var smallFont = new Font("Malgun Gothic", _baseFontSize - 3.5f);

        var panelWidth = (int)(280 * scale);
        var rowHeight = (int)(28 * scale);

        var panel = new Panel
        {
            Size = new Size(panelWidth, (int)(180 * scale)),
            BackColor = ThemePanel,
            Padding = new Padding(8)
        };

        var yPos = 8;

        // Title/Description
        var lblTitle = new Label
        {
            Text = "감시가 이하 아이템(득템) 발견 시\n설정된 간격마다 알람을 재생합니다.",
            Location = new Point(8, yPos),
            AutoSize = false,
            Size = new Size(panelWidth - 20, (int)(40 * scale)),
            ForeColor = ThemeTextMuted,
            Font = smallFont
        };
        yPos += (int)(45 * scale);

        // Alarm sound selection row
        var lblSound = new Label
        {
            Name = "lblSound",
            Text = "알람 소리",
            Location = new Point(8, yPos + 2),
            AutoSize = true,
            ForeColor = ThemeText,
            Font = font
        };

        _cboAlarmSound = new ComboBox
        {
            Name = "cboAlarmSound",
            Location = new Point((int)(75 * scale), yPos),
            Size = new Size((int)(90 * scale), rowHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Font = font
        };
        _cboAlarmSound.Items.AddRange(new object[]
        {
            new AlarmSoundItem(AlarmSoundType.SystemSound, "시스템"),
            new AlarmSoundItem(AlarmSoundType.Chime, "차임벨"),
            new AlarmSoundItem(AlarmSoundType.DingDong, "딩동"),
            new AlarmSoundItem(AlarmSoundType.Rising, "상승음"),
            new AlarmSoundItem(AlarmSoundType.Alert, "알림음")
        });
        _cboAlarmSound.DisplayMember = "Name";
        for (int i = 0; i < _cboAlarmSound.Items.Count; i++)
        {
            if (_cboAlarmSound.Items[i] is AlarmSoundItem item && item.SoundType == _selectedAlarmSound)
            {
                _cboAlarmSound.SelectedIndex = i;
                break;
            }
        }
        if (_cboAlarmSound.SelectedIndex < 0) _cboAlarmSound.SelectedIndex = 0;
        _cboAlarmSound.SelectedIndexChanged += CboAlarmSound_SelectedIndexChanged;

        var btnTest = new Button
        {
            Name = "btnTest",
            Text = "테스트",
            Location = new Point((int)(170 * scale), yPos),
            Size = new Size((int)(65 * scale), rowHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Font = font
        };
        btnTest.FlatAppearance.BorderColor = ThemeBorder;
        btnTest.Click += (s, e) => PlayAlarmSound();
        yPos += rowHeight + 6;

        // Alarm interval row
        var lblInterval = new Label
        {
            Name = "lblInterval",
            Text = "알람 간격",
            Location = new Point(8, yPos + 2),
            AutoSize = true,
            ForeColor = ThemeText,
            Font = font
        };

        _nudAlarmInterval = new NumericUpDown
        {
            Name = "nudAlarmInterval",
            Minimum = 1, Maximum = 60, Value = _alarmIntervalSeconds,
            Location = new Point((int)(75 * scale), yPos),
            Size = new Size((int)(55 * scale), rowHeight),
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Font = font
        };
        _nudAlarmInterval.ValueChanged += NudAlarmInterval_ValueChanged;

        var lblSec = new Label
        {
            Name = "lblSec",
            Text = "초",
            Location = new Point((int)(135 * scale), yPos + 2),
            AutoSize = true,
            ForeColor = ThemeText,
            Font = font
        };
        yPos += rowHeight + 8;

        // Note about mute
        var lblNote = new Label
        {
            Text = "* 음소거 버튼으로 알람을 끌 수 있습니다.",
            Location = new Point(8, yPos),
            AutoSize = false,
            Size = new Size(panelWidth - 20, (int)(35 * scale)),
            ForeColor = ThemeTextMuted,
            Font = smallFont
        };

        panel.Size = new Size(panelWidth, yPos + (int)(30 * scale));
        panel.Controls.AddRange(new Control[] { lblTitle, lblSound, _cboAlarmSound, btnTest, lblInterval, _nudAlarmInterval, lblSec, lblNote });
        return panel;
    }

    /// <summary>
    /// Reset monitor items grid column sizes to default
    /// </summary>
    private void ResetMonitorItemsColumnSizes()
    {
        if (_dgvMonitorItems.Columns["ServerId"] != null)
            _dgvMonitorItems.Columns["ServerId"].Width = 95;
        if (_dgvMonitorItems.Columns["ItemName"] != null)
            _dgvMonitorItems.Columns["ItemName"].Width = 150;
        if (_dgvMonitorItems.Columns["WatchPrice"] != null)
            _dgvMonitorItems.Columns["WatchPrice"].Width = 90;
        if (_dgvMonitorItems.Columns["RefreshStatus"] != null)
            _dgvMonitorItems.Columns["RefreshStatus"].Width = 80;
    }

    /// <summary>
    /// Reset monitor results grid column sizes to default
    /// </summary>
    private void ResetMonitorResultsColumnSizes()
    {
        if (_dgvMonitorResults.Columns["ServerName"] != null)
            _dgvMonitorResults.Columns["ServerName"].Width = 75;
        if (_dgvMonitorResults.Columns["Grade"] != null)
            _dgvMonitorResults.Columns["Grade"].Width = 65;
        if (_dgvMonitorResults.Columns["Refine"] != null)
            _dgvMonitorResults.Columns["Refine"].Width = 50;
        if (_dgvMonitorResults.Columns["ItemName"] != null)
            _dgvMonitorResults.Columns["ItemName"].Width = 200;
        if (_dgvMonitorResults.Columns["DealCount"] != null)
            _dgvMonitorResults.Columns["DealCount"].Width = 50;
        if (_dgvMonitorResults.Columns["LowestPrice"] != null)
            _dgvMonitorResults.Columns["LowestPrice"].Width = 95;
        if (_dgvMonitorResults.Columns["YesterdayAvg"] != null)
            _dgvMonitorResults.Columns["YesterdayAvg"].Width = 95;
        if (_dgvMonitorResults.Columns["WeekAvg"] != null)
            _dgvMonitorResults.Columns["WeekAvg"].Width = 95;
        if (_dgvMonitorResults.Columns["PriceDiff"] != null)
            _dgvMonitorResults.Columns["PriceDiff"].Width = 55;
        if (_dgvMonitorResults.Columns["Status"] != null)
            _dgvMonitorResults.Columns["Status"].Width = 55;
    }

    #endregion
}

// Helper class for alarm sound combo box
internal class AlarmSoundItem
{
    public AlarmSoundType SoundType { get; }
    public string Name { get; }

    public AlarmSoundItem(AlarmSoundType soundType, string name)
    {
        SoundType = soundType;
        Name = name;
    }

    public override string ToString() => Name;
}

// Dark theme renderer for ToolStrip
internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkToolStripColorTable()) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Don't render border for cleaner look
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var btn = e.Item as ToolStripButton;
        if (btn != null)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            var color = e.Item.Selected ? Color.FromArgb(60, 60, 65) : Color.Transparent;
            if (e.Item.Pressed) color = Color.FromArgb(70, 70, 75);

            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, rect);
        }
        else
        {
            base.OnRenderButtonBackground(e);
        }
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var color = e.Item.Selected ? Color.FromArgb(60, 60, 65) : Color.Transparent;
        if (e.Item.Pressed) color = Color.FromArgb(70, 70, 75);

        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.FromArgb(220, 220, 220);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var center = rect.Width / 2;
        using var pen = new Pen(Color.FromArgb(70, 70, 75));
        e.Graphics.DrawLine(pen, center, 4, center, rect.Height - 4);
    }
}

// Dark color table for ToolStrip
internal class DarkToolStripColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 48);
    public override Color MenuBorder => Color.FromArgb(70, 70, 75);
    public override Color MenuItemBorder => Color.FromArgb(70, 70, 75);
    public override Color MenuItemSelected => Color.FromArgb(60, 60, 65);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 65);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 65);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(70, 70, 75);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(70, 70, 75);
    public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 48);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 48);
    public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 48);
    public override Color SeparatorDark => Color.FromArgb(70, 70, 75);
    public override Color SeparatorLight => Color.FromArgb(70, 70, 75);
}
