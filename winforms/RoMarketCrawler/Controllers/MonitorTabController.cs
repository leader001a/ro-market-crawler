using System.Collections.Concurrent;
using System.Diagnostics;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Controllers;

/// <summary>
/// Controller for the Monitoring Tab
/// Handles auto-refresh, alarm settings, and queue-based item processing
/// </summary>
public class MonitorTabController : BaseTabController
{
    #region Constants

    private const int QueueProcessDelayMs = 500;

    #endregion

    #region Services

    private readonly IMonitoringService _monitoringService;
    private readonly IItemIndexService _itemIndexService;
    private readonly IAlarmSoundService _alarmSoundService;

    #endregion

    #region UI Controls

    private TextBox _txtMonitorItemName = null!;
    private ComboBox _cboMonitorServer = null!;
    private ToolStripButton _btnMonitorAdd = null!;
    private ToolStripButton _btnMonitorRemove = null!;
    private ToolStripButton _btnMonitorRefresh = null!;
    private ToolStripDropDownButton _btnAutoRefresh = null!;
    private ToolStripProgressBar _progressMonitor = null!;
    private DataGridView _dgvMonitorItems = null!;
    private DataGridView _dgvMonitorResults = null!;
    private NumericUpDown _nudRefreshInterval = null!;
    private Button _btnApplyInterval = null!;
    private Label _lblMonitorStatus = null!;
    private Label _lblRefreshSetting = null!;
    private ToolStripLabel _lblAutoRefreshStatus = null!;
    private ToolStripButton _btnSoundMute = null!;
    private ComboBox _cboAlarmSound = null!;
    private NumericUpDown _nudAlarmInterval = null!;

    #endregion

    #region Timers

    private System.Windows.Forms.Timer _monitorTimer = null!;
    private System.Windows.Forms.Timer _uiUpdateTimer = null!;
    private System.Windows.Forms.Timer _alarmTimer = null!;

    #endregion

    #region State

    private readonly BindingSource _monitorItemsBindingSource;
    private readonly BindingSource _monitorResultsBindingSource;
    private CancellationTokenSource? _monitorCts;

    // Refresh state
    private int _refreshedItemCount = 0;
    private readonly Stopwatch _refreshStopwatch = new();

    // Sort state for results grid
    private string _monitorResultsSortColumn = "ItemName";
    private bool _monitorResultsSortAscending = true;

    // Queue-based sequential processing
    private readonly ConcurrentQueue<MonitorItem> _refreshQueue = new();
    private Task? _queueProcessorTask;
    private CancellationTokenSource? _queueCts;

    // Alarm state
    private bool _isSoundMuted = false;
    private AlarmSoundType _selectedAlarmSound = AlarmSoundType.Chime;
    private int _alarmIntervalSeconds = 5;

    // AutoComplete support
    private AutoCompleteDropdown? _autoCompleteDropdown;

    // Track auto-refresh state before rate limit pause
    private bool _wasAutoRefreshRunningBeforeRateLimit = false;

    // Dropdown panel hosts for font size updates
    private ToolStripControlHost? _autoRefreshHost;
    private ToolStripControlHost? _alarmSettingsHost;
    private ToolStripDropDownButton? _btnAlarmSettings;

    #endregion

    #region Events

    /// <summary>
    /// Raised when settings need to be saved
    /// </summary>
    public event EventHandler? SettingsChanged;

    #endregion

    /// <inheritdoc/>
    public override string TabName => "모니터링";

    public MonitorTabController(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _monitoringService = GetService<IMonitoringService>();
        _itemIndexService = GetService<IItemIndexService>();
        _alarmSoundService = GetService<IAlarmSoundService>();

        _monitorItemsBindingSource = new BindingSource();
        _monitorResultsBindingSource = new BindingSource();
    }

    /// <summary>
    /// Set autocomplete dropdown for item name textbox
    /// </summary>
    public void SetAutoComplete(AutoCompleteDropdown dropdown)
    {
        _autoCompleteDropdown = dropdown;
    }

    /// <summary>
    /// Get the item name textbox for autocomplete attachment
    /// </summary>
    public TextBox GetItemNameTextBox() => _txtMonitorItemName;

    /// <summary>
    /// Get whether auto-refresh is currently active (timer running, paused by rate limit, or configured)
    /// </summary>
    public bool IsAutoRefreshRunning
    {
        get
        {
            // Check multiple conditions:
            // 1. Timer is actually enabled
            // 2. Timer was paused due to rate limit
            // 3. Config has interval set (fallback check)
            var timerEnabled = _monitorTimer?.Enabled == true;
            var pausedByRateLimit = _wasAutoRefreshRunningBeforeRateLimit;
            var configHasInterval = _monitoringService.Config.RefreshIntervalSeconds > 0;

            return timerEnabled || pausedByRateLimit || configHasInterval;
        }
    }

    /// <summary>
    /// Get whether auto-refresh is configured (interval > 0)
    /// </summary>
    public bool IsAutoRefreshConfigured => _monitoringService.Config.RefreshIntervalSeconds > 0;

    /// <summary>
    /// Get/Set alarm mute state
    /// </summary>
    public bool IsSoundMuted
    {
        get => _isSoundMuted;
        set
        {
            _isSoundMuted = value;
            UpdateSoundMuteButton();
        }
    }

    /// <summary>
    /// Get/Set selected alarm sound
    /// </summary>
    public AlarmSoundType SelectedAlarmSound
    {
        get => _selectedAlarmSound;
        set
        {
            _selectedAlarmSound = value;
            SelectAlarmSoundInCombo();
        }
    }

    /// <summary>
    /// Get/Set alarm interval in seconds
    /// </summary>
    public int AlarmIntervalSeconds
    {
        get => _alarmIntervalSeconds;
        set
        {
            _alarmIntervalSeconds = value;
            if (_nudAlarmInterval != null)
                _nudAlarmInterval.Value = value;
            if (_alarmTimer != null)
                _alarmTimer.Interval = value * 1000;
        }
    }

    /// <summary>
    /// Load alarm settings from Form1
    /// </summary>
    public void LoadAlarmSettings(bool isMuted, AlarmSoundType sound, int intervalSeconds)
    {
        _isSoundMuted = isMuted;
        _selectedAlarmSound = sound;
        _alarmIntervalSeconds = intervalSeconds;
    }

    /// <summary>
    /// Get current alarm settings
    /// </summary>
    public (bool IsMuted, AlarmSoundType Sound, int IntervalSeconds) GetAlarmSettings()
    {
        return (_isSoundMuted, _selectedAlarmSound, _alarmIntervalSeconds);
    }

    /// <summary>
    /// Load monitoring configuration asynchronously
    /// </summary>
    public async Task LoadMonitoringConfigAsync()
    {
        try
        {
            await _monitoringService.LoadConfigAsync();
            UpdateMonitorItemList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonitorTabController] Config load error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set watermark image for the DataGridView
    /// </summary>
    public void SetWatermark(Image watermark) => ApplyWatermark(_dgvMonitorResults, watermark);

    /// <summary>
    /// Set rate limit UI state (enable/disable controls)
    /// </summary>
    public void SetRateLimitState(bool isRateLimited)
    {
        _btnMonitorRefresh.Enabled = !isRateLimited;
        _btnAutoRefresh.Enabled = !isRateLimited;
        _btnMonitorAdd.Enabled = !isRateLimited;

        // Pause/Resume auto-refresh timer
        if (isRateLimited)
        {
            if (_monitorTimer != null && _monitorTimer.Enabled)
            {
                _wasAutoRefreshRunningBeforeRateLimit = true;
                _monitorTimer.Stop();
                _lblAutoRefreshStatus.Text = "[일시정지]";
                _lblAutoRefreshStatus.ForeColor = _colors.SaleColor;
                Debug.WriteLine("[MonitorTabController] Auto-refresh paused due to rate limit");
            }
        }
        else
        {
            if (_wasAutoRefreshRunningBeforeRateLimit && _monitorTimer != null)
            {
                _wasAutoRefreshRunningBeforeRateLimit = false;
                _monitorTimer.Start();
                _lblAutoRefreshStatus.Text = "[동작중]";
                _lblAutoRefreshStatus.ForeColor = Color.FromArgb(100, 200, 100);
                Debug.WriteLine("[MonitorTabController] Auto-refresh resumed after rate limit");
            }

            // Restore normal status
            _lblMonitorStatus.Text = "자동 갱신 설정 또는 [수동조회] 버튼으로 시세를 확인하세요.";
        }
    }

    /// <summary>
    /// Update rate limit status message
    /// </summary>
    public void UpdateRateLimitStatus(string message)
    {
        _lblMonitorStatus.Text = message;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ApplyTableLayoutPanelStyle(mainLayout);

        // Create ToolStrip
        var toolStrip = CreateToolStrip();
        mainLayout.Controls.Add(toolStrip, 0, 0);

        // Content layout
        var contentLayout = CreateContentLayout();
        mainLayout.Controls.Add(contentLayout, 0, 1);

        _tabPage.Controls.Add(mainLayout);

        // Initialize timers
        _monitorTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _monitorTimer.Tick += MonitorTimer_Tick;

        _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;

        _alarmTimer = new System.Windows.Forms.Timer { Interval = _alarmIntervalSeconds * 1000 };
        _alarmTimer.Tick += AlarmTimer_Tick;
    }

    #region UI Creation

    private ToolStrip CreateToolStrip()
    {
        var toolStrip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel,
            ForeColor = _colors.Text,
            Padding = new Padding(4, 0, 4, 0)
        };
        ApplyToolStripRenderer(toolStrip);

        // Server selection
        var cboServer = new ToolStripComboBox
        {
            Width = 80,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = _colors.Grid,
            ForeColor = _colors.Text
        };
        foreach (var server in Server.GetAllServers())
            cboServer.Items.Add(server);
        cboServer.ComboBox.DisplayMember = "Name";
        cboServer.SelectedIndex = 0;
        _cboMonitorServer = cboServer.ComboBox;

        // Item name input
        var txtItemName = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 300,
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "모니터링할 아이템명 입력"
        };
        txtItemName.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter && _autoCompleteDropdown?.HasSelection != true)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _autoCompleteDropdown?.Hide();
                BtnMonitorAdd_Click(s, e);
            }
        };
        _txtMonitorItemName = txtItemName.TextBox;

        // Add/Remove buttons
        _btnMonitorAdd = new ToolStripButton("추가") { ToolTipText = "모니터링 목록에 추가" };
        _btnMonitorAdd.Click += BtnMonitorAdd_Click;

        _btnMonitorRemove = new ToolStripButton("선택삭제") { ToolTipText = "선택한 항목 삭제" };
        _btnMonitorRemove.Click += BtnMonitorRemove_Click;

        var btnClearAll = new ToolStripButton("전체삭제") { ToolTipText = "모든 항목 삭제" };
        btnClearAll.Click += BtnMonitorClearAll_Click;

        // Manual refresh button
        _btnMonitorRefresh = new ToolStripButton("수동조회")
        {
            ToolTipText = "즉시 조회 실행",
            Alignment = ToolStripItemAlignment.Right
        };
        _btnMonitorRefresh.Click += BtnMonitorRefresh_Click;

        // Progress bar
        _progressMonitor = new ToolStripProgressBar
        {
            Alignment = ToolStripItemAlignment.Right,
            Size = new Size(100, 16),
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };

        // Auto-refresh dropdown
        _btnAutoRefresh = new ToolStripDropDownButton("자동조회")
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoToolTip = false
        };
        var autoRefreshPanel = CreateAutoRefreshPanel();
        _autoRefreshHost = new ToolStripControlHost(autoRefreshPanel) { AutoSize = false, Size = autoRefreshPanel.Size };
        _btnAutoRefresh.DropDownItems.Add(_autoRefreshHost);
        _btnAutoRefresh.DropDown.BackColor = _colors.Panel;

        // Alarm settings dropdown
        _btnAlarmSettings = new ToolStripDropDownButton("알람설정")
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoToolTip = false
        };
        var alarmPanel = CreateAlarmSettingsPanel();
        _alarmSettingsHost = new ToolStripControlHost(alarmPanel) { AutoSize = false, Size = alarmPanel.Size };
        _btnAlarmSettings.DropDownItems.Add(_alarmSettingsHost);
        _btnAlarmSettings.DropDown.BackColor = _colors.Panel;

        // Mute button
        _btnSoundMute = new ToolStripButton
        {
            ToolTipText = "알람 음소거 토글",
            Alignment = ToolStripItemAlignment.Right
        };
        UpdateSoundMuteButton();
        _btnSoundMute.Click += BtnSoundMute_Click;

        // Auto-refresh status label
        _lblAutoRefreshStatus = new ToolStripLabel
        {
            Text = "[정지]",
            ForeColor = _colors.TextMuted,
            Alignment = ToolStripItemAlignment.Right
        };

        // Hidden status labels (for compatibility)
        _lblMonitorStatus = new Label { Text = "", Visible = false };
        _lblRefreshSetting = new Label { Text = "", Visible = false };

        // Add items to toolbar (left side)
        toolStrip.Items.Add(cboServer);
        toolStrip.Items.Add(txtItemName);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_btnMonitorAdd);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_btnMonitorRemove);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnClearAll);

        // Add items to toolbar (right side)
        toolStrip.Items.Add(_btnSoundMute);
        toolStrip.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
        toolStrip.Items.Add(_btnAlarmSettings);
        toolStrip.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
        toolStrip.Items.Add(_btnAutoRefresh);
        toolStrip.Items.Add(_lblAutoRefreshStatus);
        toolStrip.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
        toolStrip.Items.Add(_btnMonitorRefresh);
        toolStrip.Items.Add(_progressMonitor);

        return toolStrip;
    }

    private void ApplyToolStripRenderer(ToolStrip toolStrip)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            toolStrip.Renderer = new DarkToolStripRenderer();
        }
        else
        {
            toolStrip.Renderer = new ToolStripProfessionalRenderer();
        }
    }

    private TableLayoutPanel CreateContentLayout()
    {
        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        ApplyTableLayoutPanelStyle(contentLayout);

        // Left panel: Item list
        var leftPanel = CreateLeftPanel();
        contentLayout.Controls.Add(leftPanel, 0, 0);

        // Right panel: Results
        var rightPanel = CreateRightPanel();
        contentLayout.Controls.Add(rightPanel, 1, 0);

        return contentLayout;
    }

    private Panel CreateLeftPanel()
    {
        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Panel,
            Padding = new Padding(3),
            Margin = new Padding(0, 0, 3, 0)
        };

        var lblItemList = new Label
        {
            Text = $"모니터링 목록 (최대 {MonitoringService.MaxItemCountLimit}개)",
            Dock = DockStyle.Top,
            Height = 22
        };
        ApplyLabelStyle(lblItemList);

        _dgvMonitorItems = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            ReadOnly = false,
            RowHeadersVisible = false,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            AllowUserToAddRows = false
        };
        ApplyDataGridViewStyle(_dgvMonitorItems);
        SetupMonitorItemsColumns();
        _dgvMonitorItems.DataSource = _monitorItemsBindingSource;

        _dgvMonitorItems.CellPainting += DgvMonitorItems_CellPainting;
        _dgvMonitorItems.DataBindingComplete += (s, e) => _dgvMonitorItems.ClearSelection();
        _dgvMonitorItems.DataError += (s, e) => e.ThrowException = false;
        _dgvMonitorItems.CurrentCellDirtyStateChanged += DgvMonitorItems_CurrentCellDirtyStateChanged;
        _dgvMonitorItems.CellValueChanged += DgvMonitorItems_CellValueChanged;
        _dgvMonitorItems.CellBeginEdit += DgvMonitorItems_CellBeginEdit;
        _dgvMonitorItems.CellFormatting += DgvMonitorItems_CellFormatting;

        // Context menu for items grid
        var itemsContextMenu = new ContextMenuStrip();
        var resetItemsColumnsItem = new ToolStripMenuItem("컬럼 크기 초기화");
        resetItemsColumnsItem.Click += (s, e) => ResetMonitorItemsColumnSizes();
        itemsContextMenu.Items.Add(resetItemsColumnsItem);
        _dgvMonitorItems.ContextMenuStrip = itemsContextMenu;

        // Warning labels
        var warningColor = _currentTheme == ThemeType.Dark
            ? Color.FromArgb(255, 100, 100)
            : Color.FromArgb(200, 50, 50);

        var lblWarning2 = new Label
        {
            Text = "  예: '빙화 마석' -> 매물이 많아 일부만 조회될 수 있음",
            Dock = DockStyle.Bottom,
            Height = 16,
            ForeColor = warningColor,
            Font = new Font("Malgun Gothic", 7.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0)
        };

        var lblWarning = new Label
        {
            Text = "* 아이템 이름을 자세히 입력하세요 (최대 30개 노점만 검색)",
            Dock = DockStyle.Bottom,
            Height = 16,
            ForeColor = warningColor,
            Font = new Font("Malgun Gothic", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0)
        };

        leftPanel.Controls.Add(lblWarning);
        leftPanel.Controls.Add(lblWarning2);
        leftPanel.Controls.Add(_dgvMonitorItems);
        leftPanel.Controls.Add(lblItemList);

        return leftPanel;
    }

    private Panel CreateRightPanel()
    {
        var rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Panel,
            Padding = new Padding(3),
            Margin = new Padding(3, 0, 0, 0)
        };

        var lblResults = new Label { Text = "조회 결과", Dock = DockStyle.Top, Height = 22 };
        ApplyLabelStyle(lblResults);

        _dgvMonitorResults = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false
        };
        ApplyDataGridViewStyle(_dgvMonitorResults);
        SetupMonitorResultsColumns();
        _dgvMonitorResults.DataSource = _monitorResultsBindingSource;
        _dgvMonitorResults.CellFormatting += DgvMonitorResults_CellFormatting;
        _dgvMonitorResults.ColumnHeaderMouseClick += DgvMonitorResults_ColumnHeaderMouseClick;
        _dgvMonitorResults.CellPainting += DgvMonitorResults_CellPainting;

        // Context menu for results grid
        var resultsContextMenu = new ContextMenuStrip();
        var resetColumnsItem = new ToolStripMenuItem("컬럼 크기 초기화");
        resetColumnsItem.Click += (s, e) => ResetMonitorResultsColumnSizes();
        resultsContextMenu.Items.Add(resetColumnsItem);
        _dgvMonitorResults.ContextMenuStrip = resultsContextMenu;

        rightPanel.Controls.Add(_dgvMonitorResults);
        rightPanel.Controls.Add(lblResults);

        return rightPanel;
    }

    private void SetupMonitorItemsColumns()
    {
        _dgvMonitorItems.Columns.Clear();

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

        _dgvMonitorItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "RefreshStatus",
            HeaderText = "상태",
            Width = 80,
            MinimumWidth = 65,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
    }

    private void SetupMonitorResultsColumns()
    {
        _dgvMonitorResults.Columns.Clear();

        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ServerName", HeaderText = "서버", Width = 75, MinimumWidth = 60,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Grade", HeaderText = "등급", Width = 65, MinimumWidth = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Refine", HeaderText = "제련", Width = 50, MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName", HeaderText = "아이템", Width = 200, MinimumWidth = 100,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DealCount", HeaderText = "수량", Width = 50, MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LowestPrice", HeaderText = "최저가", Width = 95, MinimumWidth = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "YesterdayAvg", HeaderText = "어제평균", Width = 95, MinimumWidth = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "WeekAvg", HeaderText = "주간평균", Width = 95, MinimumWidth = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PriceDiff", HeaderText = "%", Width = 55, MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _dgvMonitorResults.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status", HeaderText = "판정", Width = 55, MinimumWidth = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
    }

    private Panel CreateAutoRefreshPanel()
    {
        // Scale factor based on font size (base is 12pt)
        var scale = _baseFontSize / 12f;
        var font = new Font("Malgun Gothic", _baseFontSize);
        var smallFont = new Font("Malgun Gothic", _baseFontSize - 1);

        var panelWidth = (int)(260 * scale);
        var rowHeight = (int)(32 * scale);
        var padding = (int)(10 * scale);

        var panel = new Panel
        {
            BackColor = _colors.Panel
        };

        var yPos = padding;

        // Description - 2 lines
        var lblTitle = new Label
        {
            Text = "설정된 간격마다 모니터링 목록의\n아이템을 자동으로 조회합니다.",
            Location = new Point(padding, yPos),
            AutoSize = false,
            Size = new Size(panelWidth - padding * 2, (int)(48 * scale)),
            ForeColor = _colors.TextMuted,
            Font = smallFont
        };
        yPos += lblTitle.Height + (int)(8 * scale);

        var lblInterval = new Label
        {
            Text = "새로고침 간격 (분)",
            Location = new Point(padding, yPos + 4),
            AutoSize = true,
            ForeColor = _colors.Text,
            Font = font
        };

        _nudRefreshInterval = new NumericUpDown
        {
            Minimum = 1, Maximum = 60, Value = 1, Increment = 1,
            Location = new Point(panelWidth - padding - (int)(70 * scale), yPos),
            Size = new Size((int)(70 * scale), rowHeight),
            Font = font
        };
        ApplyNumericUpDownStyle(_nudRefreshInterval);
        yPos += rowHeight + (int)(8 * scale);

        _btnApplyInterval = new Button
        {
            Text = "자동갱신",
            Location = new Point(padding, yPos),
            Size = new Size(panelWidth - padding * 2, rowHeight),
            Font = font
        };
        ApplyButtonStyle(_btnApplyInterval, true);
        _btnApplyInterval.Click += BtnApplyInterval_Click;
        yPos += rowHeight + padding;

        panel.Size = new Size(panelWidth, yPos);
        panel.Controls.AddRange(new Control[] { lblTitle, lblInterval, _nudRefreshInterval, _btnApplyInterval });
        return panel;
    }

    private Panel CreateAlarmSettingsPanel()
    {
        // Scale factor based on font size (base is 12pt)
        var scale = _baseFontSize / 12f;
        var font = new Font("Malgun Gothic", _baseFontSize);
        var smallFont = new Font("Malgun Gothic", _baseFontSize - 1);

        var panelWidth = (int)(310 * scale);
        var rowHeight = (int)(32 * scale);
        var padding = (int)(10 * scale);
        var labelWidth = (int)(80 * scale);

        var panel = new Panel
        {
            BackColor = _colors.Panel
        };

        var yPos = padding;

        // Description - 2 lines
        var lblTitle = new Label
        {
            Text = "감시가 이하 아이템(득템) 발견 시\n설정된 간격마다 알람을 재생합니다.",
            Location = new Point(padding, yPos),
            AutoSize = false,
            Size = new Size(panelWidth - padding * 2, (int)(48 * scale)),
            ForeColor = _colors.TextMuted,
            Font = smallFont
        };
        yPos += lblTitle.Height + (int)(8 * scale);

        // Sound selection row
        var lblSound = new Label
        {
            Text = "알람 소리",
            Location = new Point(padding, yPos + 4),
            AutoSize = true,
            ForeColor = _colors.Text,
            Font = font
        };

        _cboAlarmSound = new ComboBox
        {
            Location = new Point(padding + labelWidth, yPos),
            Size = new Size((int)(100 * scale), rowHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = font
        };
        ApplyComboBoxStyle(_cboAlarmSound);
        _cboAlarmSound.Items.AddRange(new object[]
        {
            new AlarmSoundItem(AlarmSoundType.SystemSound, "시스템"),
            new AlarmSoundItem(AlarmSoundType.Chime, "차임벨"),
            new AlarmSoundItem(AlarmSoundType.DingDong, "딩동"),
            new AlarmSoundItem(AlarmSoundType.Rising, "상승음"),
            new AlarmSoundItem(AlarmSoundType.Alert, "알림음")
        });
        _cboAlarmSound.DisplayMember = "Name";
        SelectAlarmSoundInCombo();
        _cboAlarmSound.SelectedIndexChanged += CboAlarmSound_SelectedIndexChanged;

        var btnTest = new Button
        {
            Text = "테스트",
            Location = new Point(panelWidth - padding - (int)(70 * scale), yPos),
            Size = new Size((int)(70 * scale), rowHeight),
            Font = font
        };
        ApplyButtonStyle(btnTest, false);
        btnTest.Click += (s, e) => PlayAlarmSound();
        yPos += rowHeight + (int)(8 * scale);

        // Interval row
        var lblInterval = new Label
        {
            Text = "알람 간격",
            Location = new Point(padding, yPos + 4),
            AutoSize = true,
            ForeColor = _colors.Text,
            Font = font
        };

        _nudAlarmInterval = new NumericUpDown
        {
            Minimum = 1, Maximum = 60, Value = _alarmIntervalSeconds,
            Location = new Point(padding + labelWidth, yPos),
            Size = new Size((int)(60 * scale), rowHeight),
            Font = font
        };
        ApplyNumericUpDownStyle(_nudAlarmInterval);
        _nudAlarmInterval.ValueChanged += NudAlarmInterval_ValueChanged;

        var lblSec = new Label
        {
            Text = "초",
            Location = new Point(padding + labelWidth + (int)(65 * scale), yPos + 4),
            AutoSize = true,
            ForeColor = _colors.Text,
            Font = font
        };
        yPos += rowHeight + (int)(10 * scale);

        // Note
        var lblNote = new Label
        {
            Text = "* 음소거 버튼으로 알람을 끌 수 있습니다.",
            Location = new Point(padding, yPos),
            AutoSize = false,
            Size = new Size(panelWidth - padding * 2, (int)(24 * scale)),
            ForeColor = _colors.TextMuted,
            Font = smallFont
        };
        yPos += lblNote.Height + padding;

        panel.Size = new Size(panelWidth, yPos);
        panel.Controls.AddRange(new Control[] { lblTitle, lblSound, _cboAlarmSound, btnTest, lblInterval, _nudAlarmInterval, lblSec, lblNote });
        return panel;
    }

    private void ApplyNumericUpDownStyle(NumericUpDown nud)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            nud.BackColor = _colors.Grid;
            nud.ForeColor = _colors.Text;
        }
        else
        {
            nud.BackColor = SystemColors.Window;
            nud.ForeColor = SystemColors.WindowText;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Load monitoring configuration
    /// </summary>
    public async Task LoadMonitoringAsync()
    {
        try
        {
            await _monitoringService.LoadConfigAsync();
            UpdateMonitorItemList();

            if (_monitoringService.Config.RefreshIntervalSeconds > 0)
            {
                var savedMinutes = Math.Max(1, _monitoringService.Config.RefreshIntervalSeconds / 60);
                _nudRefreshInterval.Value = Math.Min(savedMinutes, 60);
            }
            _monitoringService.Config.RefreshIntervalSeconds = 0;
            UpdateMonitorRefreshLabel();

            _alarmTimer.Interval = _alarmIntervalSeconds * 1000;
            _alarmTimer.Start();
            Debug.WriteLine($"[MonitorTabController] Alarm timer auto-started: {_alarmIntervalSeconds}s interval");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonitorTabController] Monitoring load error: {ex.Message}");
        }
    }

    /// <summary>
    /// Update the monitor item list UI
    /// </summary>
    public void UpdateMonitorItemList()
    {
        if (_tabPage.InvokeRequired)
        {
            _tabPage.Invoke(new Action(UpdateMonitorItemList));
            return;
        }

        var items = _monitoringService.Config.Items.ToList();
        _monitorItemsBindingSource.DataSource = items;
        _monitorItemsBindingSource.ResetBindings(false);
        _dgvMonitorItems.ClearSelection();
    }

    /// <summary>
    /// Update the monitor results UI
    /// </summary>
    public void UpdateMonitorResults()
    {
        if (_tabPage.InvokeRequired)
        {
            _tabPage.Invoke(new Action(UpdateMonitorResults));
            return;
        }

        var results = _monitoringService.Results.Values.ToList();
        int savedScrollPosition = _dgvMonitorResults.FirstDisplayedScrollingRowIndex;

        _dgvMonitorResults.SuspendLayout();

        try
        {
            _dgvMonitorResults.DataSource = null;
            _dgvMonitorResults.Rows.Clear();

            var gradeOrder = new Dictionary<string, int> { { "S", 0 }, { "A", 1 }, { "B", 2 }, { "C", 3 }, { "D", 4 } };

            string GetDisplayName(DealItem deal)
            {
                string baseName;
                var itemId = deal.GetEffectiveItemId();
                if (itemId.HasValue && _itemIndexService.IsLoaded)
                {
                    var cachedItem = _itemIndexService.GetItemById(itemId.Value);
                    baseName = cachedItem?.ScreenName ?? deal.ItemName;
                }
                else
                {
                    baseName = deal.ItemName;
                }

                if (!string.IsNullOrEmpty(deal.CardSlots))
                {
                    return $"{baseName}[{deal.CardSlots}]";
                }
                return baseName;
            }

            var groupedDealsQuery = results
                .SelectMany(r => r.Deals.Select(d => new { Deal = d, Result = r }))
                .GroupBy(x => new
                {
                    GroupKey = x.Deal.GetEffectiveItemId()?.ToString() ?? $"name:{x.Deal.ItemName}",
                    Refine = x.Deal.Refine ?? 0,
                    Grade = x.Deal.Grade ?? "",
                    CardSlots = x.Deal.CardSlots ?? "",
                    x.Deal.ServerName
                })
                .Select(g =>
                {
                    var firstDeal = g.First().Deal;
                    var monitorItem = g.First().Result.Item;
                    return new
                    {
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
                });

            // Apply sorting
            var sortedDeals = ApplySorting(groupedDealsQuery, gradeOrder);
            var groupedDeals = sortedDeals.ToList();

            foreach (var group in groupedDeals)
            {
                var row = _dgvMonitorResults.Rows.Add();
                PopulateResultRow(row, group, gradeOrder);
            }

            _dgvMonitorResults.ClearSelection();

            if (savedScrollPosition >= 0 && savedScrollPosition < _dgvMonitorResults.RowCount)
            {
                _dgvMonitorResults.FirstDisplayedScrollingRowIndex = savedScrollPosition;
            }
        }
        finally
        {
            _dgvMonitorResults.ResumeLayout();
        }
    }

    private IEnumerable<dynamic> ApplySorting(IEnumerable<dynamic> query, Dictionary<string, int> gradeOrder)
    {
        // Helper to calculate price diff percentage
        static double GetPriceDiff(dynamic x)
        {
            if (x.WeekAvg == null || x.WeekAvg <= 0) return double.MaxValue;
            return ((double)x.LowestPrice / x.WeekAvg - 1) * 100;
        }

        // Helper to calculate status order (lower = better)
        static int GetStatusOrder(dynamic x)
        {
            if (x.WatchPrice != null && x.LowestPrice <= x.WatchPrice) return 0; // 득템!
            var priceDiff = GetPriceDiff(x);
            if (priceDiff <= -20) return 1; // 저렴!
            if (priceDiff < 0) return 2; // 양호
            if (priceDiff < double.MaxValue) return 3; // 정상
            return 4; // -
        }

        return _monitorResultsSortColumn switch
        {
            "ServerName" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.ServerName).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => x.ServerName).ThenBy(x => x.DisplayName),
            "Grade" => _monitorResultsSortAscending
                ? query.OrderBy(x => gradeOrder.TryGetValue((string)x.Grade, out int g) ? g : 99).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => gradeOrder.TryGetValue((string)x.Grade, out int g) ? g : 99).ThenBy(x => x.DisplayName),
            "Refine" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.Refine).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => x.Refine).ThenBy(x => x.DisplayName),
            "DealCount" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.DealCount).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => x.DealCount).ThenBy(x => x.DisplayName),
            "LowestPrice" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.LowestPrice).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => x.LowestPrice).ThenBy(x => x.DisplayName),
            "YesterdayAvg" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.YesterdayAvg ?? long.MaxValue).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => x.YesterdayAvg ?? 0L).ThenBy(x => x.DisplayName),
            "WeekAvg" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.WeekAvg ?? long.MaxValue).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => x.WeekAvg ?? 0L).ThenBy(x => x.DisplayName),
            "PriceDiff" => _monitorResultsSortAscending
                ? query.OrderBy(x => GetPriceDiff(x)).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => GetPriceDiff(x)).ThenBy(x => x.DisplayName),
            "Status" => _monitorResultsSortAscending
                ? query.OrderBy(x => GetStatusOrder(x)).ThenBy(x => x.DisplayName)
                : query.OrderByDescending(x => GetStatusOrder(x)).ThenBy(x => x.DisplayName),
            "ItemName" => _monitorResultsSortAscending
                ? query.OrderBy(x => x.DisplayName).ThenBy(x => x.Refine)
                : query.OrderByDescending(x => x.DisplayName).ThenBy(x => x.Refine),
            _ => _monitorResultsSortAscending
                ? query.OrderBy(x => x.DisplayName).ThenBy(x => x.Refine)
                : query.OrderByDescending(x => x.DisplayName).ThenBy(x => x.Refine)
        };
    }

    private void PopulateResultRow(int rowIndex, dynamic group, Dictionary<string, int> gradeOrder)
    {
        var row = _dgvMonitorResults.Rows[rowIndex];
        row.Cells["Refine"].Value = group.Refine > 0 ? $"+{group.Refine}" : "-";
        row.Cells["Grade"].Value = string.IsNullOrEmpty(group.Grade) ? "-" : group.Grade;
        row.Cells["ItemName"].Value = group.DisplayName;
        row.Cells["ServerName"].Value = group.ServerName;
        row.Cells["DealCount"].Value = group.DealCount;
        row.Cells["LowestPrice"].Value = group.LowestPrice.ToString("N0");

        var hasGrade = !string.IsNullOrEmpty(group.Grade);
        var belowWatchPrice = group.WatchPrice != null && group.LowestPrice <= group.WatchPrice;

        if (hasGrade)
        {
            row.Cells["YesterdayAvg"].Value = "-";
            row.Cells["WeekAvg"].Value = "-";
            row.Cells["PriceDiff"].Value = "-";
            row.Cells["Status"].Value = belowWatchPrice ? "득템!" : "-";

            row.Tag = new { Grade = group.Grade, Refine = group.Refine, BelowYesterday = false, BelowWeek = false, IsBargain = belowWatchPrice };
        }
        else
        {
            row.Cells["YesterdayAvg"].Value = group.YesterdayAvg?.ToString("N0") ?? "-";
            row.Cells["WeekAvg"].Value = group.WeekAvg?.ToString("N0") ?? "-";

            double? priceDiff = null;
            if (group.WeekAvg != null && group.WeekAvg > 0)
            {
                priceDiff = ((double)group.LowestPrice / group.WeekAvg - 1) * 100;
                row.Cells["PriceDiff"].Value = priceDiff > 0 ? $"+{priceDiff:F0}%" : $"{priceDiff:F0}%";
            }
            else
            {
                row.Cells["PriceDiff"].Value = "-";
            }

            string status;
            if (belowWatchPrice)
                status = "득템!";
            else if (priceDiff.HasValue && priceDiff <= -20)
                status = "저렴!";
            else if (priceDiff.HasValue && priceDiff < 0)
                status = "양호";
            else
                status = "정상";

            row.Cells["Status"].Value = status;

            var isBargain = priceDiff.HasValue && priceDiff <= -20;
            var isGood = priceDiff.HasValue && priceDiff < 0;
            row.Tag = new { Grade = group.Grade, Refine = group.Refine, BelowYesterday = isGood, BelowWeek = isBargain, IsBargain = belowWatchPrice };
        }
    }

    /// <summary>
    /// Update rate limit UI state
    /// </summary>
    public void UpdateRateLimitUI(bool isRateLimited)
    {
        _btnMonitorRefresh.Enabled = !isRateLimited;
        _btnMonitorAdd.Enabled = !isRateLimited;

        if (isRateLimited)
        {
            if (_monitorTimer != null && _monitorTimer.Enabled)
            {
                _wasAutoRefreshRunningBeforeRateLimit = true;
                _monitorTimer.Stop();
            }
        }
        else
        {
            if (_wasAutoRefreshRunningBeforeRateLimit && _monitorTimer != null)
            {
                _wasAutoRefreshRunningBeforeRateLimit = false;
                _monitorTimer.Start();
            }
        }
    }

    /// <summary>
    /// Stop auto-refresh when leaving monitor tab
    /// </summary>
    public void StopAutoRefreshForTabChange()
    {
        StopMonitorTimer();
        _wasAutoRefreshRunningBeforeRateLimit = false;
        _monitoringService.Config.RefreshIntervalSeconds = 0;
        UpdateMonitorRefreshLabel();
    }

    #endregion

    #region Event Handlers

    private async void BtnMonitorAdd_Click(object? sender, EventArgs e)
    {
        var itemName = _txtMonitorItemName.Text.Trim();
        if (string.IsNullOrEmpty(itemName) || itemName.Length < 2)
        {
            MessageBox.Show("아이템명은 2글자 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var server = _cboMonitorServer.SelectedItem as Server;
        var serverId = server?.Id ?? -1;

        var (success, errorReason) = await _monitoringService.AddItemAsync(itemName, serverId);
        if (success)
        {
            _txtMonitorItemName.Clear();
            UpdateMonitorItemList();

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
            MessageBox.Show(message, "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void BtnMonitorRemove_Click(object? sender, EventArgs e)
    {
        var selectedRowIndices = new HashSet<int>();
        foreach (DataGridViewCell cell in _dgvMonitorItems.SelectedCells)
        {
            if (cell.RowIndex >= 0)
                selectedRowIndices.Add(cell.RowIndex);
        }

        if (selectedRowIndices.Count == 0)
        {
            MessageBox.Show("삭제할 아이템을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var itemsToDelete = new List<MonitorItem>();
        foreach (var rowIndex in selectedRowIndices)
        {
            if (_dgvMonitorItems.Rows[rowIndex].DataBoundItem is MonitorItem item)
            {
                itemsToDelete.Add(item);
            }
        }

        if (itemsToDelete.Count == 0) return;

        var message = itemsToDelete.Count == 1
            ? $"'{itemsToDelete[0].ItemName}'을(를) 삭제하시겠습니까?"
            : $"선택한 {itemsToDelete.Count}개 아이템을 삭제하시겠습니까?";

        var result = MessageBox.Show(message, "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            foreach (var item in itemsToDelete)
            {
                await _monitoringService.RemoveItemAsync(item.ItemName, item.ServerId);
            }
            UpdateMonitorItemList();
            UpdateMonitorResults();
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
        }
    }

    private async void BtnMonitorRefresh_Click(object? sender, EventArgs e)
    {
        await RefreshMonitoringAsync();
    }

    private async void BtnApplyInterval_Click(object? sender, EventArgs e)
    {
        var currentInterval = _monitoringService.Config.RefreshIntervalSeconds;

        if (currentInterval > 0)
        {
            await _monitoringService.SetRefreshIntervalAsync(0);
            StopMonitorTimer();
        }
        else
        {
            var minutes = (int)_nudRefreshInterval.Value;
            if (minutes < 1) minutes = 1;
            var seconds = minutes * 60;
            await _monitoringService.SetRefreshIntervalAsync(seconds);
            StartMonitorTimer(seconds);
        }

        UpdateMonitorRefreshLabel();
    }

    private void BtnSoundMute_Click(object? sender, EventArgs e)
    {
        _isSoundMuted = !_isSoundMuted;
        UpdateSoundMuteButton();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CboAlarmSound_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboAlarmSound?.SelectedItem is AlarmSoundItem item)
        {
            _selectedAlarmSound = item.SoundType;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NudAlarmInterval_ValueChanged(object? sender, EventArgs e)
    {
        _alarmIntervalSeconds = (int)_nudAlarmInterval.Value;
        _alarmTimer.Interval = _alarmIntervalSeconds * 1000;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Timer Handlers

    private void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        var itemsDue = _monitoringService.GetAllItemsDueForRefresh()
            .Where(i => !i.IsQueued && !i.IsRefreshing && !i.IsProcessing)
            .ToList();
        if (itemsDue.Count == 0) return;

        foreach (var item in itemsDue)
        {
            item.IsQueued = true;
            item.IsRefreshing = false;
            _refreshQueue.Enqueue(item);
        }

        UpdateItemStatusColumn();
        EnsureQueueProcessorRunning();
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        UpdateItemStatusColumn();
    }

    private void AlarmTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSoundMuted) return;

        var results = _monitoringService.Results.Values.ToList();
        if (results.Count == 0) return;

        bool hasBargain = false;
        foreach (var result in results)
        {
            var watchPrice = result.Item.WatchPrice;
            if (!watchPrice.HasValue) continue;

            if (result.Deals.Any(d => d.Price <= watchPrice.Value))
            {
                hasBargain = true;
                break;
            }
        }

        if (hasBargain)
        {
            PlayAlarmSound();
        }
    }

    #endregion

    #region Private Methods

    private async Task RefreshMonitoringAsync()
    {
        if (_monitoringService.ItemCount == 0) return;

        var wasAutoRefreshRunning = _monitorTimer.Enabled;
        if (wasAutoRefreshRunning)
        {
            _monitorTimer.Stop();
        }

        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();

        _btnMonitorRefresh.Enabled = false;
        _btnMonitorAdd.Enabled = false;
        _btnMonitorRemove.Enabled = false;
        _btnAutoRefresh.Enabled = false;

        _progressMonitor.Value = 0;
        _progressMonitor.Visible = true;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var progress = new Progress<MonitorProgress>(p =>
            {
                if (p.TotalItems > 0)
                {
                    var percent = (int)(p.CurrentIndex * 100 / p.TotalItems);
                    _progressMonitor.Value = Math.Min(percent, 100);
                }
            });

            await _monitoringService.RefreshAllAsync(progress, _monitorCts.Token);
            stopwatch.Stop();

            var allItems = _monitoringService.Config.Items.ToList();
            foreach (var item in allItems)
            {
                item.IsProcessing = true;
            }
            UpdateItemStatusColumn();

            await Task.Delay(500);

            UpdateMonitorResults();

            foreach (var item in allItems)
            {
                item.IsProcessing = false;
            }

            if (_monitorTimer.Enabled)
            {
                _monitoringService.ScheduleNextRefreshForAll();
            }

            UpdateItemStatusColumn();
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Debug.WriteLine($"[MonitorTabController] Monitor refresh error: {ex}");
        }
        finally
        {
            if (wasAutoRefreshRunning)
            {
                _monitorTimer.Start();
            }
            else
            {
                _btnMonitorRefresh.Enabled = true;
            }
            _btnMonitorAdd.Enabled = true;
            _btnMonitorRemove.Enabled = true;
            _btnAutoRefresh.Enabled = true;
            _progressMonitor.Visible = false;
        }
    }

    private void StartMonitorTimer(int seconds)
    {
        _monitorTimer.Stop();
        _uiUpdateTimer.Stop();

        if (seconds > 0)
        {
            _monitoringService.InitializeRefreshSchedule();
            _refreshedItemCount = 0;
            _refreshStopwatch.Restart();

            _monitorTimer.Interval = 3000;
            _monitorTimer.Start();
            _uiUpdateTimer.Start();

            _btnMonitorRefresh.Enabled = false;
            UpdateItemStatusColumn();
        }
    }

    private void StopMonitorTimer()
    {
        _monitorTimer?.Stop();
        _uiUpdateTimer?.Stop();
        _refreshStopwatch.Stop();

        _queueCts?.Cancel();
        _queueCts?.Dispose();
        _queueCts = null;

        while (_refreshQueue.TryDequeue(out var queuedItem))
        {
            queuedItem.IsQueued = false;
            queuedItem.IsRefreshing = false;
        }

        foreach (var item in _monitoringService.Config.Items)
        {
            item.IsQueued = false;
            item.IsRefreshing = false;
            item.IsProcessing = false;
            item.NextRefreshTime = null;
        }

        _btnMonitorRefresh.Enabled = true;
        UpdateItemStatusColumn();
    }

    private void EnsureQueueProcessorRunning()
    {
        if (_queueProcessorTask == null || _queueProcessorTask.IsCompleted)
        {
            _queueCts?.Dispose();
            _queueCts = new CancellationTokenSource();
            _queueProcessorTask = ProcessQueueAsync(_queueCts.Token);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_refreshQueue.TryDequeue(out var item))
                {
                    item.IsQueued = false;
                    item.IsRefreshing = true;

                    if (!_tabPage.IsDisposed)
                    {
                        _tabPage.Invoke(() => UpdateItemStatusColumn());
                    }

                    await ProcessSingleItemAsync(item);

                    if (!_refreshQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(QueueProcessDelayMs, cancellationToken);
                    }
                }
                else
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonitorTabController] Queue processor error: {ex.Message}");
        }
    }

    private async Task ProcessSingleItemAsync(MonitorItem item)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            _monitorCts?.Token ?? CancellationToken.None);
        var cancellationToken = linkedCts.Token;

        try
        {
            await _monitoringService.RefreshSingleItemAsync(item, cancellationToken);
            Interlocked.Increment(ref _refreshedItemCount);

            var itemStillExists = _monitoringService.Config.Items
                .Any(i => i.ItemName == item.ItemName && i.ServerId == item.ServerId);
            if (!itemStillExists)
            {
                _monitoringService.ClearItemCache(item.ItemName, item.ServerId);
                return;
            }

            item.IsRefreshing = false;
            item.IsProcessing = true;

            if (_tabPage.InvokeRequired)
            {
                _tabPage.Invoke(() =>
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

            await Task.Delay(500, cancellationToken);

            item.IsProcessing = false;
            _monitoringService.ScheduleNextRefresh(new[] { item });

            if (!_tabPage.IsDisposed)
            {
                _tabPage.Invoke(() => UpdateItemStatusColumn());
            }
        }
        catch
        {
            item.IsRefreshing = false;
            item.IsProcessing = false;
            _monitoringService.ScheduleNextRefresh(new[] { item });
        }
        finally
        {
            if (!_tabPage.IsDisposed)
            {
                _tabPage.Invoke(() => UpdateItemStatusColumn());
            }
        }
    }

    private void UpdateItemStatusColumn()
    {
        if (_tabPage.InvokeRequired)
        {
            _tabPage.Invoke(new Action(UpdateItemStatusColumn));
            return;
        }

        var items = _monitoringService.Config.Items;
        var isAutoRefreshEnabled = _monitorTimer.Enabled;

        for (int i = 0; i < _dgvMonitorItems.Rows.Count && i < items.Count; i++)
        {
            var item = items[i];
            var row = _dgvMonitorItems.Rows[i];
            var statusCell = row.Cells["RefreshStatus"];

            if (item.IsRefreshing)
                statusCell.Value = "조회 중...";
            else if (item.IsQueued)
                statusCell.Value = "대기";
            else if (item.IsProcessing)
                statusCell.Value = "처리 중...";
            else if (!isAutoRefreshEnabled)
                statusCell.Value = "-";
            else if (item.NextRefreshTime.HasValue)
            {
                var remaining = (item.NextRefreshTime.Value - DateTime.Now).TotalSeconds;
                statusCell.Value = remaining > 0 ? $"{(int)remaining}초 후" : "대기";
            }
            else
                statusCell.Value = "-";
        }
    }

    private void UpdateMonitorRefreshLabel()
    {
        if (_tabPage.InvokeRequired)
        {
            _tabPage.Invoke(new Action(UpdateMonitorRefreshLabel));
            return;
        }

        var intervalSeconds = _monitoringService.Config.RefreshIntervalSeconds;
        var timerEnabled = _monitorTimer?.Enabled == true;

        if (intervalSeconds > 0 || timerEnabled)
        {
            var minutes = Math.Max(1, intervalSeconds / 60);
            _btnApplyInterval.Text = $"중지 ({minutes}분)";
            _btnApplyInterval.ForeColor = _colors.SaleColor;
            if (_lblAutoRefreshStatus != null)
            {
                _lblAutoRefreshStatus.Text = "[동작중]";
                _lblAutoRefreshStatus.ForeColor = Color.FromArgb(100, 200, 100);
            }
        }
        else
        {
            _btnApplyInterval.Text = "자동갱신";
            _btnApplyInterval.ForeColor = _colors.Text;
            if (_lblAutoRefreshStatus != null)
            {
                _lblAutoRefreshStatus.Text = "[정지]";
                _lblAutoRefreshStatus.ForeColor = _colors.TextMuted;
            }
        }
    }

    private void UpdateSoundMuteButton()
    {
        if (_btnSoundMute == null) return;

        if (_isSoundMuted)
        {
            _btnSoundMute.Text = "음소거 해제";
            _btnSoundMute.ForeColor = _colors.SaleColor;
        }
        else
        {
            _btnSoundMute.Text = "음소거";
            _btnSoundMute.ForeColor = _colors.Text;
        }
    }

    private void SelectAlarmSoundInCombo()
    {
        if (_cboAlarmSound == null) return;

        for (int i = 0; i < _cboAlarmSound.Items.Count; i++)
        {
            if (_cboAlarmSound.Items[i] is AlarmSoundItem item && item.SoundType == _selectedAlarmSound)
            {
                _cboAlarmSound.SelectedIndex = i;
                return;
            }
        }
        if (_cboAlarmSound.Items.Count > 0)
            _cboAlarmSound.SelectedIndex = 0;
    }

    private void PlayAlarmSound()
    {
        if (_selectedAlarmSound == AlarmSoundType.SystemSound)
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        else
        {
            _alarmSoundService.PlaySound(_selectedAlarmSound);
        }
    }

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

    private void ResetMonitorResultsColumnSizes()
    {
        foreach (var (name, width) in new[] { ("ServerName", 75), ("Grade", 65), ("Refine", 50), ("ItemName", 200),
            ("DealCount", 50), ("LowestPrice", 95), ("YesterdayAvg", 95), ("WeekAvg", 95), ("PriceDiff", 55), ("Status", 55) })
        {
            if (_dgvMonitorResults.Columns[name] != null)
                _dgvMonitorResults.Columns[name].Width = width;
        }
    }

    #endregion

    #region Grid Events

    private void DgvMonitorItems_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null)
        {
            e.PaintBackground(e.ClipBounds, true);
            TextRenderer.DrawText(
                e.Graphics,
                e.FormattedValue?.ToString() ?? "",
                e.CellStyle?.Font ?? _dgvMonitorItems.Font,
                e.CellBounds,
                e.CellStyle?.ForeColor ?? _colors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
            e.Handled = true;
        }
    }

    private void DgvMonitorItems_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_dgvMonitorItems.IsCurrentCellDirty && _dgvMonitorItems.CurrentCell is DataGridViewComboBoxCell)
        {
            _dgvMonitorItems.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void DgvMonitorItems_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        var row = _dgvMonitorItems.Rows[e.RowIndex];
        var columnName = _dgvMonitorItems.Columns[e.ColumnIndex].Name;

        var originalValues = row.Tag as Dictionary<string, object?> ?? new Dictionary<string, object?>();

        if (columnName == "ItemName")
            originalValues["ItemName"] = row.Cells["ItemName"].Value?.ToString();
        else if (columnName == "ServerId")
            originalValues["ServerId"] = row.Cells["ServerId"].Value;
        else if (columnName == "WatchPrice")
            originalValues["WatchPrice"] = row.Cells["WatchPrice"].Value;

        row.Tag = originalValues;
    }

    private async void DgvMonitorItems_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        var columnName = _dgvMonitorItems.Columns[e.ColumnIndex].Name;
        var row = _dgvMonitorItems.Rows[e.RowIndex];
        var originalValues = row.Tag as Dictionary<string, object?>;

        if (columnName == "ItemName")
        {
            var oldName = originalValues?.GetValueOrDefault("ItemName")?.ToString();
            var newName = row.Cells["ItemName"].Value?.ToString()?.Trim();

            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;

            if (newName.Length < 2)
            {
                row.Cells["ItemName"].Value = oldName;
                MessageBox.Show("아이템명은 2글자 이상 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                originalValues?.Remove("ItemName");
                return;
            }

            var items = _monitoringService.Config.Items;
            var item = items.FirstOrDefault(i => i.ItemName == newName);
            if (item == null) return;

            var success = await _monitoringService.RenameItemAsync(oldName, item.ServerId, newName);
            if (!success)
            {
                row.Cells["ItemName"].Value = oldName;
                MessageBox.Show("아이템 이름을 변경할 수 없습니다. 동일한 이름이 이미 존재합니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            originalValues?.Remove("ItemName");
            UpdateMonitorItemList();
            UpdateMonitorResults();
            return;
        }

        if (columnName == "ServerId")
        {
            var oldServerId = originalValues?.GetValueOrDefault("ServerId") as int?;
            var newServerId = row.Cells["ServerId"].Value as int?;

            if (oldServerId == null || newServerId == null || oldServerId == newServerId) return;

            var itemName = row.Cells["ItemName"].Value?.ToString();
            if (string.IsNullOrEmpty(itemName)) return;

            await _monitoringService.UpdateItemServerAsync(itemName, oldServerId.Value, newServerId.Value);
            originalValues?.Remove("ServerId");
            UpdateMonitorItemList();
            UpdateMonitorResults();
            return;
        }

        if (columnName == "WatchPrice")
        {
            var cellValue = row.Cells["WatchPrice"].Value;
            long? newWatchPrice = null;

            if (cellValue != null && cellValue != DBNull.Value)
            {
                if (cellValue is long lv) newWatchPrice = lv;
                else if (cellValue is int iv) newWatchPrice = iv;
                else if (long.TryParse(cellValue.ToString()?.Replace(",", ""), out var parsed)) newWatchPrice = parsed;
            }

            var itemName = row.Cells["ItemName"].Value?.ToString();
            var serverId = row.Cells["ServerId"].Value as int?;
            if (string.IsNullOrEmpty(itemName) || serverId == null) return;

            var monitorItem = _monitoringService.Config.Items.FirstOrDefault(i =>
                i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.ServerId == serverId);
            if (monitorItem == null) return;

            monitorItem.WatchPrice = newWatchPrice;
            originalValues?.Remove("WatchPrice");
            _monitoringService.ClearItemCache(itemName, serverId.Value);
            await _monitoringService.SaveConfigAsync();
            UpdateMonitorItemList();
            UpdateMonitorResults();
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
            e.CellStyle!.BackColor = Color.FromArgb(40, 80, 140);
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        }
        else if (cellValue == "조회 중...")
        {
            e.CellStyle!.BackColor = Color.FromArgb(120, 100, 40);
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        }
        else if (cellValue == "-")
        {
            e.CellStyle!.ForeColor = Color.Gray;
        }
        else if (cellValue == "대기")
        {
            e.CellStyle!.ForeColor = Color.FromArgb(100, 255, 100);
        }
    }

    private void DgvMonitorResults_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null)
        {
            e.PaintBackground(e.ClipBounds, true);

            var headerText = e.FormattedValue?.ToString() ?? "";
            var columnName = _dgvMonitorResults.Columns[e.ColumnIndex].Name;
            if (columnName == _monitorResultsSortColumn)
            {
                headerText += _monitorResultsSortAscending ? " ▲" : " ▼";
            }

            TextRenderer.DrawText(
                e.Graphics,
                headerText,
                e.CellStyle?.Font ?? _dgvMonitorResults.Font,
                e.CellBounds,
                e.CellStyle?.ForeColor ?? _colors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );

            e.Handled = true;
        }
    }

    private void DgvMonitorResults_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;

        var columnName = _dgvMonitorResults.Columns[e.ColumnIndex].Name;

        if (_monitorResultsSortColumn == columnName)
        {
            _monitorResultsSortAscending = !_monitorResultsSortAscending;
        }
        else
        {
            _monitorResultsSortColumn = columnName;
            _monitorResultsSortAscending = true;
        }

        UpdateMonitorResults();
    }

    private void DgvMonitorResults_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvMonitorResults.Rows.Count) return;

        var row = _dgvMonitorResults.Rows[e.RowIndex];
        var tag = row.Tag;
        if (tag == null) return;

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
            if (_currentTheme == ThemeType.Dark)
            {
                e.CellStyle!.ForeColor = refine switch
                {
                    >= 15 => Color.FromArgb(255, 80, 80),
                    >= 12 => Color.FromArgb(255, 160, 80),
                    >= 10 => Color.FromArgb(255, 200, 50),
                    >= 7 => Color.FromArgb(80, 180, 255),
                    _ => Color.FromArgb(180, 180, 180)
                };
            }
            else
            {
                e.CellStyle!.ForeColor = refine switch
                {
                    >= 15 => Color.FromArgb(200, 0, 0),
                    >= 12 => Color.FromArgb(180, 90, 0),
                    >= 10 => Color.FromArgb(160, 130, 0),
                    >= 7 => Color.FromArgb(0, 100, 180),
                    _ => Color.FromArgb(100, 100, 100)
                };
            }
            if (refine >= 7)
            {
                e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
            }
        }

        // Grade color coding
        if (columnName == "Grade" && !string.IsNullOrEmpty(grade) && grade != "-")
        {
            if (_currentTheme == ThemeType.Dark)
            {
                e.CellStyle!.ForeColor = grade.ToLower() switch
                {
                    "s" => Color.FromArgb(255, 200, 50),
                    "a" => Color.FromArgb(200, 130, 255),
                    "b" => Color.FromArgb(80, 180, 255),
                    "c" => Color.FromArgb(100, 220, 100),
                    "d" => Color.FromArgb(160, 160, 160),
                    "unique" => Color.FromArgb(255, 180, 0),
                    _ => Color.FromArgb(200, 200, 200)
                };
            }
            else
            {
                e.CellStyle!.ForeColor = grade.ToLower() switch
                {
                    "s" => Color.FromArgb(180, 140, 0),
                    "a" => Color.FromArgb(130, 0, 130),
                    "b" => Color.FromArgb(0, 100, 180),
                    "c" => Color.FromArgb(0, 130, 0),
                    "d" => Color.FromArgb(110, 110, 110),
                    "unique" => Color.FromArgb(180, 100, 0),
                    _ => Color.FromArgb(80, 80, 80)
                };
            }
            e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
        }

        // Highlight price columns
        if (columnName == "LowestPrice" || columnName == "PriceDiff" || columnName == "Status")
        {
            if (isBargain)
            {
                e.CellStyle!.BackColor = Color.FromArgb(180, 40, 40);
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.Font = new Font(e.CellStyle.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
            }
            else if (belowYesterday && belowWeek)
            {
                e.CellStyle!.BackColor = Color.FromArgb(40, 120, 60);
                e.CellStyle.ForeColor = Color.White;
            }
            else if (belowYesterday || belowWeek)
            {
                e.CellStyle!.BackColor = Color.FromArgb(120, 100, 40);
                e.CellStyle.ForeColor = Color.White;
            }
        }
    }

    #endregion

    #region Theme & Font

    /// <inheritdoc/>
    public override void ApplyTheme(ThemeColors colors)
    {
        base.ApplyTheme(colors);

        if (_dgvMonitorItems != null) ApplyDataGridViewStyle(_dgvMonitorItems);
        if (_dgvMonitorResults != null) ApplyDataGridViewStyle(_dgvMonitorResults);

        UpdateSoundMuteButton();
        UpdateMonitorRefreshLabel();
    }

    /// <inheritdoc/>
    public override void UpdateFontSize(float baseFontSize)
    {
        base.UpdateFontSize(baseFontSize);

        if (_dgvMonitorItems != null)
        {
            _dgvMonitorItems.DefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize);
            _dgvMonitorItems.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
        }

        if (_dgvMonitorResults != null)
        {
            _dgvMonitorResults.DefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize);
            _dgvMonitorResults.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
        }

        // Recreate dropdown panels with new font size
        UpdateDropdownPanels();
    }

    private void UpdateDropdownPanels()
    {
        // Update auto-refresh dropdown panel
        if (_btnAutoRefresh != null && _autoRefreshHost != null)
        {
            _autoRefreshHost.Control.Dispose();
            var newPanel = CreateAutoRefreshPanel();

            _btnAutoRefresh.DropDownItems.Clear();
            _autoRefreshHost = new ToolStripControlHost(newPanel) { AutoSize = false, Size = newPanel.Size };
            _btnAutoRefresh.DropDownItems.Add(_autoRefreshHost);
        }

        // Update alarm settings dropdown panel
        if (_btnAlarmSettings != null && _alarmSettingsHost != null)
        {
            _alarmSettingsHost.Control.Dispose();
            var newPanel = CreateAlarmSettingsPanel();

            _btnAlarmSettings.DropDownItems.Clear();
            _alarmSettingsHost = new ToolStripControlHost(newPanel) { AutoSize = false, Size = newPanel.Size };
            _btnAlarmSettings.DropDownItems.Add(_alarmSettingsHost);
        }
    }

    /// <inheritdoc/>
    public override void OnActivated()
    {
        base.OnActivated();
        UpdateMonitorItemList();
        UpdateMonitorResults();
        UpdateItemStatusColumn();
    }

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _monitorCts?.Cancel();
            _monitorCts?.Dispose();
            _queueCts?.Cancel();
            _queueCts?.Dispose();
            _monitorTimer?.Stop();
            _monitorTimer?.Dispose();
            _uiUpdateTimer?.Stop();
            _uiUpdateTimer?.Dispose();
            _alarmTimer?.Stop();
            _alarmTimer?.Dispose();
            _monitorItemsBindingSource?.Dispose();
            _monitorResultsBindingSource?.Dispose();
            _dgvMonitorItems?.Dispose();
            _dgvMonitorResults?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}

// AlarmSoundItem is now in Models/AlarmSoundItem.cs
// DarkToolStripRenderer is now in Controls/DarkToolStripRenderer.cs
