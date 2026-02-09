using System.Text.Json;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Forms;

/// <summary>
/// Form to display auto-search results in real-time with search history
/// </summary>
public class AutoSearchResultsForm : Form
{
    private readonly DataGridView _dgvResults;
    private readonly Label _lblStatus;
    private readonly Label _lblNotice;
    private readonly ComboBox _cboHistory;
    private readonly Button _btnClearHistory;
    private readonly BindingSource _bindingSource;
    private readonly List<AutoSearchResultItem> _currentResults = new();
    private readonly ThemeColors _colors;
    private readonly float _baseFontSize;
    private readonly string _historyFilePath;
    private readonly System.Windows.Forms.Timer _saveDebounceTimer;
    private readonly object _saveLock = new();

    private AutoSearchHistory _history = new();
    private string _currentSearchKey = string.Empty;
    private const int MaxHistoryCount = 10;
    private const int SaveDebounceMs = 1000; // Save at most once per second

    public AutoSearchResultsForm(ThemeColors colors, float baseFontSize, string dataDirectory)
    {
        _colors = colors;
        _baseFontSize = baseFontSize;
        _historyFilePath = Path.Combine(dataDirectory, "auto_search_history.json");
        _bindingSource = new BindingSource { DataSource = _currentResults };

        // Initialize debounce timer for file saving
        _saveDebounceTimer = new System.Windows.Forms.Timer { Interval = SaveDebounceMs };
        _saveDebounceTimer.Tick += (s, e) =>
        {
            _saveDebounceTimer.Stop();
            SaveHistoryToFileInternal();
        };

        Text = "자동검색 결과";
        Size = new Size(950, 550);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = colors.Background;
        ForeColor = colors.Text;
        Font = new Font(Font.FontFamily, baseFontSize);

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));  // Notice
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // History selector
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Grid
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Status

        // Warning notice panel
        var noticePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(60, 50, 30),
            Padding = new Padding(10, 5, 10, 5)
        };
        _lblNotice = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(255, 200, 100),
            Text = "⚠ 이 결과는 로컬에 저장된 과거 검색 기록입니다. 현재 해당 아이템이 노점에 존재하지 않을 수 있습니다.",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };
        noticePanel.Controls.Add(_lblNotice);

        // History selector panel
        var historyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = colors.Panel,
            Padding = new Padding(0, 3, 0, 3)
        };

        var lblHistory = new Label
        {
            Text = "검색 기록:",
            AutoSize = true,
            ForeColor = colors.TextMuted,
            Margin = new Padding(0, 5, 5, 0)
        };

        _cboHistory = new ComboBox
        {
            Width = 350,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = colors.Grid,
            ForeColor = colors.Text,
            Margin = new Padding(0, 2, 10, 0)
        };
        _cboHistory.SelectedIndexChanged += CboHistory_SelectedIndexChanged;

        _btnClearHistory = new Button
        {
            Text = "기록 삭제",
            Width = 80,
            Height = 25,
            BackColor = Color.FromArgb(150, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 1, 0, 0)
        };
        _btnClearHistory.FlatAppearance.BorderSize = 0;
        _btnClearHistory.Click += BtnClearHistory_Click;

        historyPanel.Controls.Add(lblHistory);
        historyPanel.Controls.Add(_cboHistory);
        historyPanel.Controls.Add(_btnClearHistory);

        // Results grid
        _dgvResults = CreateResultsGrid();

        // Status label
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = colors.TextMuted,
            Text = "검색 기록을 선택하세요."
        };

        mainPanel.Controls.Add(noticePanel, 0, 0);
        mainPanel.Controls.Add(historyPanel, 0, 1);
        mainPanel.Controls.Add(_dgvResults, 0, 2);
        mainPanel.Controls.Add(_lblStatus, 0, 3);

        Controls.Add(mainPanel);

        // Load history
        LoadHistoryFromFile();
        UpdateHistoryComboBox();

        // Save any pending changes when form closes
        FormClosing += (s, e) =>
        {
            SaveHistoryImmediate();
            _saveDebounceTimer.Dispose();
        };
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
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = _colors.Grid,
            GridColor = _colors.Border,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = _colors.Grid,
                ForeColor = _colors.Text,
                SelectionBackColor = _colors.Accent,
                SelectionForeColor = _colors.AccentText
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = _colors.Panel,
                ForeColor = _colors.Text,
                Alignment = DataGridViewContentAlignment.MiddleCenter
            },
            EnableHeadersVisualStyles = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn
            {
                Name = "FoundAt",
                HeaderText = "검색일시",
                DataPropertyName = "FoundAtDisplay",
                Width = 145
            },
            new DataGridViewTextBoxColumn
            {
                Name = "Page",
                HeaderText = "페이지",
                DataPropertyName = "FoundOnPage",
                Width = 55,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ServerName",
                HeaderText = "서버",
                DataPropertyName = "ServerName",
                Width = 70
            },
            new DataGridViewTextBoxColumn
            {
                Name = "DisplayName",
                HeaderText = "아이템명",
                DataPropertyName = "DisplayName",
                Width = 180
            },
            new DataGridViewTextBoxColumn
            {
                Name = "SlotAndOptionsDisplay",
                HeaderText = "카드/인챈트/랜덤옵션",
                DataPropertyName = "SlotAndOptionsDisplay",
                Width = 200,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "수량",
                DataPropertyName = "Quantity",
                Width = 45,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "PriceFormatted",
                HeaderText = "가격",
                DataPropertyName = "PriceFormatted",
                Width = 90,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ShopName",
                HeaderText = "상점명",
                DataPropertyName = "ShopName",
                Width = 100
            }
        });

        dgv.DataSource = _bindingSource;

        return dgv;
    }

    private void CboHistory_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboHistory.SelectedItem is AutoSearchHistoryEntry entry)
        {
            LoadResultsForEntry(entry);
        }
    }

    private void BtnClearHistory_Click(object? sender, EventArgs e)
    {
        if (_cboHistory.SelectedItem is AutoSearchHistoryEntry entry)
        {
            var result = MessageBox.Show(
                $"'{entry.DisplayName}' 검색 기록을 삭제하시겠습니까?",
                "기록 삭제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _history.Entries.Remove(entry);
                SaveHistoryToFile();
                UpdateHistoryComboBox();
                _currentResults.Clear();
                _bindingSource.ResetBindings(false);
                _lblStatus.Text = "검색 기록을 선택하세요.";
            }
        }
    }

    private void LoadResultsForEntry(AutoSearchHistoryEntry entry)
    {
        _currentSearchKey = entry.SearchKey;
        _currentResults.Clear();
        _currentResults.AddRange(entry.Results);
        _bindingSource.ResetBindings(false);
        _lblStatus.Text = $"'{entry.ItemName}' + '{entry.FilterText}' 검색 결과: {entry.Results.Count}건 (검색일: {entry.SearchedAt:yyyy-MM-dd HH:mm})";
    }

    /// <summary>
    /// Start a new search session
    /// </summary>
    public void StartNewSearch(string itemName, string filterText, int serverId, string serverName)
    {
        // Include server ID in search key so same item+filter on different servers are separate
        _currentSearchKey = $"{serverId}|{itemName}|{filterText}";

        // Check if entry already exists
        var existingEntry = _history.Entries.FirstOrDefault(e => e.SearchKey == _currentSearchKey);
        if (existingEntry != null)
        {
            // Move to front and clear old results
            _history.Entries.Remove(existingEntry);
            existingEntry.Results.Clear();
            existingEntry.SearchedAt = DateTime.Now;
            _history.Entries.Insert(0, existingEntry);
        }
        else
        {
            // Create new entry
            var newEntry = new AutoSearchHistoryEntry
            {
                SearchKey = _currentSearchKey,
                ServerId = serverId,
                ServerName = serverName,
                ItemName = itemName,
                FilterText = filterText,
                SearchedAt = DateTime.Now,
                Results = new List<AutoSearchResultItem>()
            };
            _history.Entries.Insert(0, newEntry);

            // Trim to max count
            while (_history.Entries.Count > MaxHistoryCount)
            {
                _history.Entries.RemoveAt(_history.Entries.Count - 1);
            }
        }

        _currentResults.Clear();
        _bindingSource.ResetBindings(false);
        SaveHistoryToFile();
        UpdateHistoryComboBox();

        // Select the current entry
        _cboHistory.SelectedIndex = 0;
    }

    /// <summary>
    /// Add a matching result item to the current search
    /// </summary>
    public void AddResult(DealItem item, int pageNumber)
    {
        var resultItem = new AutoSearchResultItem
        {
            FoundOnPage = pageNumber,
            ServerId = item.ServerId,
            ServerName = item.ServerName,
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            DisplayName = item.DisplayName,
            ItemImageUrl = item.ItemImageUrl,
            Refine = item.Refine,
            Grade = item.Grade,
            CardSlots = item.CardSlots,
            Quantity = item.Quantity,
            Price = item.Price,
            PriceFormatted = item.PriceFormatted,
            DealType = item.DealType,
            ShopName = item.ShopName,
            MapName = item.MapName,
            MapId = item.MapId,
            Ssi = item.Ssi,
            SlotInfo = new List<string>(item.SlotInfo),
            RandomOptions = new List<string>(item.RandomOptions),
            FoundAt = DateTime.Now
        };

        if (InvokeRequired)
        {
            Invoke(() => AddResultInternal(resultItem));
        }
        else
        {
            AddResultInternal(resultItem);
        }
    }

    private void AddResultInternal(AutoSearchResultItem item)
    {
        // Check for duplicate by Ssi (unique item identifier)
        if (!string.IsNullOrEmpty(item.Ssi))
        {
            bool isDuplicate = _currentResults.Any(r => r.Ssi == item.Ssi);
            if (isDuplicate)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoSearchResultsForm] Skipping duplicate item: {item.DisplayName} (Ssi: {item.Ssi})");
                return;
            }
        }

        // Add to current entry
        var entry = _history.Entries.FirstOrDefault(e => e.SearchKey == _currentSearchKey);
        if (entry != null)
        {
            entry.Results.Add(item);
        }

        _currentResults.Add(item);
        _bindingSource.ResetBindings(false);
        UpdateStatus();
        SaveHistoryToFile();
    }

    /// <summary>
    /// Update status message
    /// </summary>
    public void UpdateSearchStatus(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => _lblStatus.Text = message);
        }
        else
        {
            _lblStatus.Text = message;
        }
    }

    private void UpdateStatus()
    {
        var entry = _history.Entries.FirstOrDefault(e => e.SearchKey == _currentSearchKey);
        if (entry != null)
        {
            _lblStatus.Text = $"'{entry.ItemName}' + '{entry.FilterText}' 검색 결과: {_currentResults.Count}건";
        }
        else
        {
            _lblStatus.Text = $"검색 결과: {_currentResults.Count}건";
        }
    }

    private void UpdateHistoryComboBox()
    {
        _cboHistory.Items.Clear();
        foreach (var entry in _history.Entries)
        {
            _cboHistory.Items.Add(entry);
        }

        if (_cboHistory.Items.Count > 0 && _cboHistory.SelectedIndex < 0)
        {
            _cboHistory.SelectedIndex = 0;
        }

        _btnClearHistory.Enabled = _cboHistory.Items.Count > 0;
    }

    /// <summary>
    /// Clear current search results (not history)
    /// </summary>
    public void ClearResults()
    {
        _currentResults.Clear();
        _bindingSource.ResetBindings(false);
        UpdateStatus();
    }

    /// <summary>
    /// Get current result count
    /// </summary>
    public int ResultCount => _currentResults.Count;

    /// <summary>
    /// Schedule a debounced save to avoid file conflicts when adding items rapidly
    /// </summary>
    private void SaveHistoryToFile()
    {
        // Reset the timer - this creates a debounce effect
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    /// <summary>
    /// Save history immediately (called when form closes or explicitly needed)
    /// </summary>
    private void SaveHistoryImmediate()
    {
        _saveDebounceTimer.Stop();
        SaveHistoryToFileInternal();
    }

    /// <summary>
    /// Actually write the history to file
    /// </summary>
    private void SaveHistoryToFileInternal()
    {
        try
        {
            lock (_saveLock)
            {
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoSearchResultsForm] Failed to save history: {ex.Message}");
        }
    }

    private void LoadHistoryFromFile()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var loadedHistory = JsonSerializer.Deserialize<AutoSearchHistory>(json);
                if (loadedHistory != null)
                {
                    _history = loadedHistory;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoSearchResultsForm] Failed to load history: {ex.Message}");
        }
    }
}

/// <summary>
/// Auto-search history container
/// </summary>
public class AutoSearchHistory
{
    public List<AutoSearchHistoryEntry> Entries { get; set; } = new();
}

/// <summary>
/// Single search history entry
/// </summary>
public class AutoSearchHistoryEntry
{
    public string SearchKey { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string FilterText { get; set; } = string.Empty;
    public DateTime SearchedAt { get; set; }
    public List<AutoSearchResultItem> Results { get; set; } = new();

    public string DisplayName => $"[{ServerName}] {ItemName} - {FilterText} ({Results.Count}건, {SearchedAt:MM/dd HH:mm})";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Extended DealItem with auto-search metadata
/// </summary>
public class AutoSearchResultItem : DealItem
{
    public int FoundOnPage { get; set; }
    public DateTime FoundAt { get; set; }

    public string FoundAtDisplay => FoundAt.ToString("yyyy-MM-dd HH:mm:ss");
}
