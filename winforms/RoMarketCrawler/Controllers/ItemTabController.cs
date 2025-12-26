using System.Diagnostics;
using System.Text.RegularExpressions;
using RoMarketCrawler.Controls;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Controllers;

/// <summary>
/// Controller for the Item Search Tab (Kafra.kr database)
/// </summary>
public class ItemTabController : BaseTabController
{
    #region Constants

    private const int ItemPageSize = 100;

    #endregion

    #region Services

    private readonly IKafraClient _kafraClient;
    private readonly IItemIndexService _itemIndexService;

    #endregion

    #region UI Controls

    private TextBox _txtItemSearch = null!;
    private ComboBox _cboItemType = null!;
    private ToolStripButton _btnItemSearch = null!;
    private CheckBox _chkSearchDescription = null!;
    private ToolStripProgressBar _progressIndex = null!;
    private ToolStrip _toolStrip = null!;
    private DataGridView _dgvItems = null!;
    private RichTextBox _rtbItemDetail = null!;
    private PictureBox _picItemImage = null!;
    private Label _lblItemName = null!;
    private Label _lblItemBasicInfo = null!;
    private Label _lblItemStatus = null!;
    private FlowLayoutPanel _pnlSubCategories = null!;
    private FlowLayoutPanel _pnlJobFilters = null!;
    private RoundedButton _btnItemPrev = null!;
    private RoundedButton _btnItemNext = null!;
    private Label _lblItemPage = null!;

    #endregion

    #region State

    private readonly List<KafraItem> _itemResults = new();
    private readonly BindingSource _itemBindingSource;
    private readonly HashSet<int> _selectedItemTypes = new() { 999 };
    private readonly Dictionary<string, CheckBox> _subCategoryCheckBoxes = new();
    private readonly Dictionary<string, CheckBox> _jobFilterCheckBoxes = new();
    private readonly List<Form> _openItemInfoForms = new();
    private readonly HttpClient _imageHttpClient = new();
    private readonly string _imageCacheDir;
    private int _itemCurrentPage = 0;
    private int _itemTotalCount = 0;

    // AutoComplete support
    private AutoCompleteDropdown? _autoCompleteDropdown;

    #endregion

    #region Events

    /// <summary>
    /// Raised when index status changes (for updating menu items)
    /// </summary>
    public event EventHandler? IndexStatusChanged;

    /// <summary>
    /// Raised when autocomplete source needs refresh
    /// </summary>
    public event EventHandler? AutoCompleteRefreshNeeded;

    #endregion

    /// <inheritdoc/>
    public override string TabName => "아이템";

    public ItemTabController(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _kafraClient = GetService<IKafraClient>();
        _itemIndexService = GetService<IItemIndexService>();
        _itemBindingSource = new BindingSource { DataSource = _itemResults };

        // Initialize image cache directory
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        _imageCacheDir = Path.Combine(dataDir, "ItemImages");
        Directory.CreateDirectory(_imageCacheDir);
    }

    /// <summary>
    /// Set autocomplete dropdown for search textbox
    /// </summary>
    public void SetAutoComplete(AutoCompleteDropdown dropdown)
    {
        _autoCompleteDropdown = dropdown;
    }

    /// <summary>
    /// Get the search textbox for autocomplete attachment
    /// </summary>
    public TextBox GetSearchTextBox() => _txtItemSearch;

    /// <summary>
    /// Load item index from cache asynchronously
    /// </summary>
    public async Task LoadItemIndexAsync()
    {
        try
        {
            var loaded = await _itemIndexService.LoadFromCacheAsync();
            if (loaded)
            {
                UpdateIndexStatus();
                IndexStatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemTabController] Index load error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all item names for autocomplete
    /// </summary>
    public List<string> GetAllItemNames()
    {
        if (!_itemIndexService.IsLoaded) return new List<string>();
        return _itemIndexService.GetAllScreenNames().ToList();
    }

    /// <summary>
    /// Set watermark image for the DataGridView
    /// </summary>
    public void SetWatermark(Image watermark) => ApplyWatermark(_dgvItems, watermark);

    /// <summary>
    /// Rebuild the item index (called from menu)
    /// </summary>
    public void RebuildIndex()
    {
        // Find the parent form for the progress dialog
        var parentForm = _tabPage.FindForm();
        if (parentForm != null)
        {
            _ = RebuildIndexAsync(parentForm);
        }
    }

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
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // Row 0: Dropdown + Search
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // Row 1: Sub-filters
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // Row 2: Job filters
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // Row 3: Content
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));    // Row 4: Status bar
        ApplyTableLayoutPanelStyle(mainPanel);

        // Row 0: Category dropdown + Search controls
        var row0Panel = CreateSearchToolbar();

        // Row 1: Sub-category checkboxes panel
        _pnlSubCategories = CreateFilterPanel();

        // Row 2: Job class filters panel
        _pnlJobFilters = CreateFilterPanel();

        // Content area: Left-Right layout
        var contentPanel = CreateContentPanel();

        // Status bar
        var statusPanel = CreateStatusPanel();

        mainPanel.Controls.Add(row0Panel, 0, 0);
        mainPanel.Controls.Add(_pnlSubCategories, 0, 1);
        mainPanel.Controls.Add(_pnlJobFilters, 0, 2);
        mainPanel.Controls.Add(contentPanel, 0, 3);
        mainPanel.Controls.Add(statusPanel, 0, 4);

        _tabPage.Controls.Add(mainPanel);
    }

    #region UI Creation

    private ToolStrip CreateSearchToolbar()
    {
        _toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = _colors.Panel
        };

        // Category dropdown
        var cboCategory = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            ToolTipText = "카테고리 선택"
        };

        var categoryItems = new (int id, string name)[]
        {
            (999, "전체"),
            (4, "무기"),
            (5, "방어구"),
            (6, "카드"),
            (7, "펫"),
            (10, "화살/탄약"),
            (19, "쉐도우"),
            (20, "의상"),
            (998, "기타")
        };

        foreach (var (id, name) in categoryItems)
        {
            cboCategory.Items.Add(new CategoryItem(id, name));
        }
        cboCategory.SelectedIndex = 0;
        _cboItemType = cboCategory.ComboBox;
        _cboItemType.SelectedIndexChanged += CboItemType_SelectedIndexChanged;

        // Search textbox
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
        _txtItemSearch = txtSearch.TextBox;

        // Search button
        _btnItemSearch = new ToolStripButton
        {
            Text = "검색",
            ToolTipText = "검색 실행"
        };
        _btnItemSearch.Click += async (s, e) => await SearchAsync();

        // Description search checkbox
        _chkSearchDescription = new CheckBox
        {
            Text = "설명 포함",
            AutoSize = true,
            ForeColor = _colors.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(5, 0, 0, 0)
        };
        var chkHost = new ToolStripControlHost(_chkSearchDescription)
        {
            ToolTipText = "아이템 설명에서도 검색",
            Margin = new Padding(5, 0, 0, 0)
        };

        // Progress bar
        _progressIndex = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Visible = false,
            Alignment = ToolStripItemAlignment.Right,
            Size = new Size(120, 16)
        };

        _toolStrip.Items.Add(cboCategory);
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(txtSearch);
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(_btnItemSearch);
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(chkHost);
        _toolStrip.Items.Add(_progressIndex);

        return _toolStrip;
    }

    private FlowLayoutPanel CreateFilterPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            MinimumSize = new Size(0, 0),
            Margin = new Padding(0),
            BackColor = _colors.Panel
        };
        return panel;
    }

    private TableLayoutPanel CreateContentPanel()
    {
        var contentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        ApplyTableLayoutPanelStyle(contentPanel);

        // Left wrapper: Grid + Pagination
        var leftWrapper = CreateLeftWrapper();

        // Right panel: Item detail
        var rightPanel = CreateRightPanel();

        contentPanel.Controls.Add(leftWrapper, 0, 0);
        contentPanel.Controls.Add(rightPanel, 1, 0);

        return contentPanel;
    }

    private TableLayoutPanel CreateLeftWrapper()
    {
        var leftWrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 5, 0)
        };
        leftWrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftWrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        ApplyTableLayoutPanelStyle(leftWrapper);

        // Left panel: Item list
        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Panel,
            Padding = new Padding(3),
            Margin = new Padding(0)
        };

        _dgvItems = CreateItemGrid();
        leftPanel.Controls.Add(_dgvItems);

        // Pagination panel
        var paginationWrapper = CreatePaginationPanel();

        leftWrapper.Controls.Add(leftPanel, 0, 0);
        leftWrapper.Controls.Add(paginationWrapper, 0, 1);

        return leftWrapper;
    }

    private DataGridView CreateItemGrid()
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
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        ApplyDataGridViewStyle(dgv);

        // Force header center alignment
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

        SetupItemGridColumns(dgv);
        dgv.DataSource = _itemBindingSource;
        dgv.SelectionChanged += DgvItems_SelectionChanged;
        dgv.CellDoubleClick += DgvItems_CellDoubleClick;

        return dgv;
    }

    private void SetupItemGridColumns(DataGridView dgv)
    {
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ItemConst", HeaderText = "ID", DataPropertyName = "ItemConst", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "ScreenName", HeaderText = "아이템명", DataPropertyName = "ScreenName", FillWeight = 200 },
            new DataGridViewTextBoxColumn { Name = "Slots", HeaderText = "슬롯", DataPropertyName = "Slots", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "WeightDisplay", HeaderText = "무게", DataPropertyName = "WeightDisplay", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "NpcBuyPrice", HeaderText = "NPC구매가", DataPropertyName = "NpcBuyPrice", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "EquipJobsText", HeaderText = "장착 가능", DataPropertyName = "EquipJobsText", FillWeight = 150 }
        });
    }

    private TableLayoutPanel CreatePaginationPanel()
    {
        var paginationWrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1
        };
        ApplyTableLayoutPanelStyle(paginationWrapper);

        var paginationPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = _colors.Panel
        };

        _btnItemPrev = new RoundedButton
        {
            Text = "< 이전",
            Size = new Size(80, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 5, 0)
        };
        ApplyRoundedButtonStyle(_btnItemPrev, false);
        _btnItemPrev.Click += async (s, e) => await PreviousPageAsync();

        _lblItemPage = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "",
            Margin = new Padding(10, 5, 10, 0),
            ForeColor = _colors.Text,
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Regular)
        };

        _btnItemNext = new RoundedButton
        {
            Text = "다음 >",
            Size = new Size(80, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0)
        };
        ApplyRoundedButtonStyle(_btnItemNext, false);
        _btnItemNext.Click += async (s, e) => await NextPageAsync();

        paginationPanel.Controls.AddRange(new Control[] { _btnItemPrev, _lblItemPage, _btnItemNext });
        paginationWrapper.Controls.Add(paginationPanel, 0, 0);
        paginationPanel.Anchor = AnchorStyles.None;

        return paginationWrapper;
    }

    private TableLayoutPanel CreateRightPanel()
    {
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _colors.Panel,
            Padding = new Padding(5),
            Margin = new Padding(5, 0, 0, 0)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Header card
        var headerCard = CreateHeaderCard();
        rightPanel.Controls.Add(headerCard, 0, 0);

        // Description card
        var descCard = CreateDescriptionCard();
        rightPanel.Controls.Add(descCard, 0, 1);

        return rightPanel;
    }

    private Panel CreateHeaderCard()
    {
        var headerCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Grid,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 5)
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _picItemImage = new PictureBox
        {
            Size = new Size(75, 100),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = _colors.Grid,
            Margin = new Padding(0, 0, 5, 0)
        };
        headerLayout.Controls.Add(_picItemImage, 0, 0);

        var infoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };

        _lblItemName = new Label
        {
            Text = "",
            Font = new Font("Malgun Gothic", 11, FontStyle.Bold),
            ForeColor = _colors.LinkColor,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft
        };
        infoPanel.Controls.Add(_lblItemName);

        _lblItemBasicInfo = new Label
        {
            Text = "",
            Font = new Font("Malgun Gothic", 9),
            ForeColor = _colors.TextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft
        };
        infoPanel.Controls.Add(_lblItemBasicInfo);
        _lblItemBasicInfo.BringToFront();

        headerLayout.Controls.Add(infoPanel, 1, 0);
        headerCard.Controls.Add(headerLayout);

        return headerCard;
    }

    private Panel CreateDescriptionCard()
    {
        var descCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _colors.Grid,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };

        var descTitle = new Label
        {
            Text = "아이템 설명",
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            ForeColor = _colors.Text,
            Dock = DockStyle.Top,
            Height = 22
        };
        descCard.Controls.Add(descTitle);

        _rtbItemDetail = new RichTextBox
        {
            BackColor = _colors.Grid,
            ForeColor = _colors.Text,
            Font = new Font("Malgun Gothic", 9.5f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        descCard.Controls.Add(_rtbItemDetail);
        _rtbItemDetail.BringToFront();

        return descCard;
    }

    private FlowLayoutPanel CreateStatusPanel()
    {
        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _colors.Panel
        };

        _lblItemStatus = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "kafra.kr 아이템 데이터베이스에서 검색합니다.",
            Margin = new Padding(0, 5, 0, 0),
            ForeColor = _colors.Text
        };

        statusPanel.Controls.Add(_lblItemStatus);
        return statusPanel;
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

    #endregion

    #region Category Filtering

    private class CategoryItem
    {
        public int Id { get; }
        public string Name { get; }

        public CategoryItem(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }

    private void CboItemType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboItemType.SelectedItem is not CategoryItem selected)
            return;

        _selectedItemTypes.Clear();
        _selectedItemTypes.Add(selected.Id);
        UpdateSubCategoryCheckboxes();
    }

    private void UpdateSubCategoryCheckboxes()
    {
        _pnlSubCategories.SuspendLayout();
        _pnlJobFilters.SuspendLayout();
        _pnlSubCategories.Controls.Clear();
        _pnlJobFilters.Controls.Clear();
        _subCategoryCheckBoxes.Clear();
        _jobFilterCheckBoxes.Clear();

        if (_selectedItemTypes.Contains(999))
        {
            _pnlSubCategories.ResumeLayout();
            _pnlJobFilters.ResumeLayout();
            return;
        }

        var allFilters = new List<(FilterCategory category, int sourceType)>();
        foreach (var typeId in _selectedItemTypes)
        {
            var filters = ItemFilters.GetFiltersForType(typeId);
            foreach (var filter in filters)
            {
                allFilters.Add((filter, typeId));
            }
        }

        var uniqueFilters = allFilters
            .GroupBy(f => f.category.Name)
            .Select(g => g.First())
            .ToList();

        var jobFilter = uniqueFilters.FirstOrDefault(f => f.category.Name == "직업군");
        var otherFilters = uniqueFilters.Where(f => f.category.Name != "직업군").ToList();

        // Create sub-category checkboxes
        foreach (var (filterCategory, sourceType) in otherFilters)
        {
            AddFilterCategoryToPanel(_pnlSubCategories, filterCategory, _subCategoryCheckBoxes);
        }

        // Create job filter checkboxes
        if (jobFilter.category != null)
        {
            AddFilterCategoryToPanel(_pnlJobFilters, jobFilter.category, _jobFilterCheckBoxes);
        }

        _pnlSubCategories.ResumeLayout();
        _pnlJobFilters.ResumeLayout();
    }

    private void AddFilterCategoryToPanel(FlowLayoutPanel panel, FilterCategory filterCategory, Dictionary<string, CheckBox> checkBoxDict)
    {
        var lblCategory = new Label
        {
            Text = $"[{filterCategory.Name}]",
            AutoSize = true,
            ForeColor = _colors.TextMuted,
            Margin = new Padding(5, 4, 3, 0),
            Font = new Font("Malgun Gothic", 8.5f)
        };
        panel.Controls.Add(lblCategory);

        var separator = new Label
        {
            Text = "|",
            AutoSize = true,
            ForeColor = _colors.Border,
            Margin = new Padding(0, 4, 5, 0),
            Font = new Font("Malgun Gothic", 8.5f)
        };
        panel.Controls.Add(separator);

        foreach (var option in filterCategory.Options)
        {
            if (string.IsNullOrEmpty(option.Pattern)) continue;

            var key = $"{filterCategory.Name}:{option.DisplayName}";
            var chk = new CheckBox
            {
                Text = option.DisplayName,
                Tag = option,
                AutoSize = true,
                Checked = false,
                ForeColor = _colors.Text,
                Margin = new Padding(0, 2, 5, 0),
                Font = new Font("Malgun Gothic", 8.5f)
            };
            panel.Controls.Add(chk);
            checkBoxDict[key] = chk;
        }
    }

    private bool HasActiveSubFilter()
    {
        return _subCategoryCheckBoxes.Values.Any(chk => chk.Checked) ||
               _jobFilterCheckBoxes.Values.Any(chk => chk.Checked);
    }

    #endregion

    #region Search Logic

    /// <summary>
    /// Execute search for first page
    /// </summary>
    public async Task SearchAsync()
    {
        _itemCurrentPage = 0;
        await LoadItemPageAsync();
    }

    private async Task LoadItemPageAsync()
    {
        var searchText = _txtItemSearch.Text.Trim();
        var searchDescription = _chkSearchDescription.Checked;

        _btnItemSearch.Enabled = false;
        _btnItemPrev.Enabled = false;
        _btnItemNext.Enabled = false;
        _lblItemStatus.Text = "검색 중...";

        try
        {
            List<KafraItem> items;
            var skip = _itemCurrentPage * ItemPageSize;
            var hasSubFilter = !_selectedItemTypes.Contains(999) && HasActiveSubFilter();
            var singleType = _selectedItemTypes.Count == 1 ? _selectedItemTypes.First() : 999;

            if (_itemIndexService.IsLoaded)
            {
                if (hasSubFilter)
                {
                    var allItems = _itemIndexService.SearchItems(searchText, _selectedItemTypes, 0, int.MaxValue, searchDescription);
                    var filteredItems = ApplySubFilters(allItems);
                    _itemTotalCount = filteredItems.Count;
                    items = filteredItems.Skip(skip).Take(ItemPageSize).ToList();
                }
                else
                {
                    _itemTotalCount = _itemIndexService.CountItems(searchText, _selectedItemTypes, searchDescription);
                    items = _itemIndexService.SearchItems(searchText, _selectedItemTypes, skip, ItemPageSize, searchDescription);
                }
            }
            else
            {
                items = await _kafraClient.SearchItemsAsync(searchText, singleType, ItemPageSize);
                if (hasSubFilter)
                {
                    items = ApplySubFilters(items);
                }
                _itemTotalCount = items.Count;
            }

            _itemResults.Clear();
            _itemResults.AddRange(items);
            _itemBindingSource.ResetBindings(false);

            UpdateItemPagination();

            var source = _itemIndexService.IsLoaded ? "(인덱스)" : "(API)";
            var filterInfo = hasSubFilter ? " (필터 적용)" : "";
            var pageInfo = _itemTotalCount > ItemPageSize ? $" (페이지 {_itemCurrentPage + 1})" : "";
            _lblItemStatus.Text = $"검색 완료 {source}: {_itemTotalCount}개 중 {items.Count}개 표시{pageInfo}{filterInfo}";

            if (items.Count == 0 && string.IsNullOrEmpty(searchText))
            {
                _lblItemStatus.Text = "아이템 타입을 선택하면 해당 타입의 아이템 목록이 표시됩니다.";
            }
        }
        catch (Exception ex)
        {
            _lblItemStatus.Text = "오류: " + ex.Message;
            Debug.WriteLine($"[ItemTabController] Search error: {ex}");
        }
        finally
        {
            _btnItemSearch.Enabled = true;
            UpdateItemPaginationButtons();
        }
    }

    private void UpdateItemPagination()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        _lblItemPage.Text = totalPages > 1 ? $"{_itemCurrentPage + 1} / {totalPages}" : "";
    }

    private void UpdateItemPaginationButtons()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        _btnItemPrev.Enabled = _itemCurrentPage > 0;
        _btnItemNext.Enabled = _itemCurrentPage < totalPages - 1;
    }

    private async Task PreviousPageAsync()
    {
        if (_itemCurrentPage > 0)
        {
            _itemCurrentPage--;
            await LoadItemPageAsync();
        }
    }

    private async Task NextPageAsync()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        if (_itemCurrentPage < totalPages - 1)
        {
            _itemCurrentPage++;
            await LoadItemPageAsync();
        }
    }

    #endregion

    #region Filter Logic

    private List<KafraItem> ApplySubFilters(List<KafraItem> items)
    {
        var allCheckBoxes = _subCategoryCheckBoxes
            .Concat(_jobFilterCheckBoxes)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var checkedFilters = allCheckBoxes
            .Where(kvp => kvp.Value.Checked && kvp.Value.Tag is FilterOption)
            .Select(kvp => (Key: kvp.Key, Option: (FilterOption)kvp.Value.Tag!))
            .GroupBy(x => x.Key.Split(':')[0])
            .ToList();

        if (!checkedFilters.Any())
            return items;

        var result = items;

        var filterCategories = new Dictionary<string, FilterTarget>();
        foreach (var typeId in _selectedItemTypes)
        {
            foreach (var filter in ItemFilters.GetFiltersForType(typeId))
            {
                if (!filterCategories.ContainsKey(filter.Name))
                    filterCategories[filter.Name] = filter.Target;
            }
        }

        foreach (var categoryGroup in checkedFilters)
        {
            var categoryName = categoryGroup.Key;
            if (!filterCategories.TryGetValue(categoryName, out var target))
                continue;

            var patterns = categoryGroup.Select(x => x.Option.Pattern).Where(p => !string.IsNullOrEmpty(p));
            var combinedPattern = string.Join("|", patterns);
            if (string.IsNullOrEmpty(combinedPattern))
                continue;

            var regex = new Regex(combinedPattern, RegexOptions.IgnoreCase);
            result = result.Where(item => MatchesFilter(item, target, regex)).ToList();
        }

        return result;
    }

    private static bool MatchesFilter(KafraItem item, FilterTarget target, Regex regex)
    {
        var text = target switch
        {
            FilterTarget.ScreenName => item.ScreenName ?? "",
            FilterTarget.ItemText => item.ItemText ?? "",
            FilterTarget.EquipJobsText => item.EquipJobsText ?? "",
            _ => ""
        };
        return regex.IsMatch(text);
    }

    #endregion

    #region Grid Events

    private void DgvItems_SelectionChanged(object? sender, EventArgs e)
    {
        var currentRow = _dgvItems.CurrentRow;
        if (currentRow == null) return;

        var selectedIndex = currentRow.Index;
        if (selectedIndex < 0 || selectedIndex >= _itemResults.Count) return;

        var item = _itemResults[selectedIndex];

        _lblItemName.Text = item.ScreenName ?? item.Name ?? $"Item {item.ItemConst}";

        var basicInfo = new List<string>
        {
            $"ID: {item.ItemConst}  |  타입: {item.GetTypeDisplayName()}",
            $"무게: {item.GetFormattedWeight()}  |  슬롯: {item.Slots}",
            $"NPC 구매가: {item.GetFormattedNpcBuyPrice()}",
            $"NPC 판매가: {item.GetFormattedNpcSellPrice()}"
        };
        _lblItemBasicInfo.Text = string.Join(Environment.NewLine, basicInfo);

        _rtbItemDetail.Clear();
        var itemText = item.ItemText ?? "(설명 없음)";
        itemText = Regex.Replace(itemText, @"\^[0-9a-fA-F]{6}_?", "");
        itemText = itemText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        _rtbItemDetail.Text = itemText;

        if (!string.IsNullOrEmpty(item.EquipJobsText))
        {
            _rtbItemDetail.AppendText(Environment.NewLine + Environment.NewLine);
            _rtbItemDetail.SelectionColor = _colors.LinkColor;
            _rtbItemDetail.SelectionFont = new Font(_rtbItemDetail.Font, FontStyle.Bold);
            _rtbItemDetail.AppendText("[장착 가능 직업]" + Environment.NewLine);
            _rtbItemDetail.SelectionColor = _colors.Text;
            _rtbItemDetail.SelectionFont = _rtbItemDetail.Font;

            var equipJobs = item.EquipJobsText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            _rtbItemDetail.AppendText(equipJobs);
        }

        _ = LoadItemImageAsync(item.ItemConst);
    }

    private void DgvItems_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _itemResults.Count) return;

        var item = _itemResults[e.RowIndex];

        var existingForm = _openItemInfoForms
            .OfType<ItemInfoForm>()
            .FirstOrDefault(f => !f.IsDisposed && f.ItemConst == item.ItemConst);

        if (existingForm != null)
        {
            existingForm.BringToFront();
            existingForm.Focus();
            if (existingForm.WindowState == FormWindowState.Minimized)
            {
                existingForm.WindowState = FormWindowState.Normal;
            }
            return;
        }

        // Cast to concrete type for ItemInfoForm compatibility
        var indexService = _itemIndexService as ItemIndexService;
        var infoForm = new ItemInfoForm(item, indexService, _currentTheme, _baseFontSize);
        infoForm.FormClosed += (s, args) => _openItemInfoForms.Remove(infoForm);
        _openItemInfoForms.Add(infoForm);
        infoForm.Show(_tabPage.FindForm());
    }

    #endregion

    #region Image Loading

    private async Task LoadItemImageAsync(int itemId)
    {
        try
        {
            _picItemImage.Image?.Dispose();
            _picItemImage.Image = null;

            byte[]? imageBytes = null;
            var cacheFilePath = Path.Combine(_imageCacheDir, $"{itemId}_col.png");

            if (File.Exists(cacheFilePath))
            {
                imageBytes = await File.ReadAllBytesAsync(cacheFilePath);
            }

            if (imageBytes == null)
            {
                string? itemName = null;
                if (_itemIndexService.IsLoaded)
                {
                    var cachedItem = _itemIndexService.GetItemById(itemId);
                    itemName = cachedItem?.Name;
                }

                if (!string.IsNullOrEmpty(itemName))
                {
                    try
                    {
                        var encodedName = Uri.EscapeDataString(itemName);
                        var kafraUrl = $"http://static.kafra.kr/kro/data/texture/%EC%9C%A0%EC%A0%80%EC%9D%B8%ED%84%B0%ED%8E%98%EC%9D%B4%EC%8A%A4/collection/png/{encodedName}.png";
                        imageBytes = await _imageHttpClient.GetByteArrayAsync(kafraUrl);

                        _ = Task.Run(async () =>
                        {
                            try { await File.WriteAllBytesAsync(cacheFilePath, imageBytes); }
                            catch { }
                        });
                    }
                    catch { }
                }
            }

            if (imageBytes == null)
            {
                var oldCacheFilePath = Path.Combine(_imageCacheDir, $"{itemId}.png");
                if (File.Exists(oldCacheFilePath))
                {
                    imageBytes = await File.ReadAllBytesAsync(oldCacheFilePath);
                }
            }

            if (imageBytes == null) return;

            using var ms = new MemoryStream(imageBytes);
            var image = Image.FromStream(ms);

            var currentRow = _dgvItems.CurrentRow;
            if (!_tabPage.IsDisposed && currentRow != null)
            {
                var selectedIndex = currentRow.Index;
                if (selectedIndex >= 0 && selectedIndex < _itemResults.Count)
                {
                    var selectedItem = _itemResults[selectedIndex];
                    if (selectedItem.ItemConst == itemId)
                    {
                        _picItemImage.Image = image;
                        return;
                    }
                }
            }

            image.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemTabController] Failed to load image for item {itemId}: {ex.Message}");
        }
    }

    #endregion

    #region Index Operations

    /// <summary>
    /// Rebuild the item index
    /// </summary>
    public async Task<bool> RebuildIndexAsync(Form owner)
    {
        if (_itemIndexService.IsLoading)
            return false;

        // Cast to concrete type for IndexProgressDialog compatibility
        var indexService = _itemIndexService as ItemIndexService;
        if (indexService == null)
            return false;

        using var progressDialog = new IndexProgressDialog(_currentTheme, _baseFontSize);
        var success = await progressDialog.ShowAndRunAsync(owner, indexService);

        if (success)
        {
            UpdateIndexStatus();
            AutoCompleteRefreshNeeded?.Invoke(this, EventArgs.Empty);
            MessageBox.Show(
                $"인덱스 생성 완료!\n\n총 {progressDialog.TotalCount:N0}개 아이템이 저장되었습니다.",
                "완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else if (progressDialog.WasCancelled)
        {
            _lblItemStatus.Text = "인덱스 생성이 취소되었습니다.";
        }

        return success;
    }

    /// <summary>
    /// Update status text based on index state
    /// </summary>
    public void UpdateIndexStatus()
    {
        if (_itemIndexService.IsLoaded)
        {
            var lastUpdated = _itemIndexService.LastUpdated;
            if (lastUpdated.HasValue)
            {
                _lblItemStatus.Text = $"인덱스 로드됨 ({_itemIndexService.TotalCount:N0}개, 마지막 업데이트: {lastUpdated.Value:g})";
            }
            else
            {
                _lblItemStatus.Text = $"인덱스 로드됨 ({_itemIndexService.TotalCount:N0}개)";
            }
        }

        IndexStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Theme & Font

    /// <inheritdoc/>
    public override void ApplyTheme(ThemeColors colors)
    {
        base.ApplyTheme(colors);

        // Update status label
        if (_lblItemStatus != null)
        {
            _lblItemStatus.ForeColor = colors.Text;
        }

        // Update DataGridView
        if (_dgvItems != null)
        {
            ApplyDataGridViewStyle(_dgvItems);
        }

        // Update pagination buttons
        if (_btnItemPrev != null) ApplyRoundedButtonStyle(_btnItemPrev, false);
        if (_btnItemNext != null) ApplyRoundedButtonStyle(_btnItemNext, false);
        if (_lblItemPage != null) _lblItemPage.ForeColor = colors.Text;

        // Update ToolStrip
        if (_toolStrip != null)
        {
            _toolStrip.BackColor = colors.Panel;
        }

        // Update search controls
        if (_txtItemSearch != null)
        {
            _txtItemSearch.BackColor = colors.Grid;
            _txtItemSearch.ForeColor = colors.Text;
        }
        if (_cboItemType != null)
        {
            _cboItemType.BackColor = colors.Grid;
            _cboItemType.ForeColor = colors.Text;
        }
        if (_chkSearchDescription != null)
        {
            _chkSearchDescription.ForeColor = colors.Text;
        }

        // Update detail panel
        if (_lblItemName != null) _lblItemName.ForeColor = colors.LinkColor;
        if (_lblItemBasicInfo != null) _lblItemBasicInfo.ForeColor = colors.TextMuted;
        if (_rtbItemDetail != null)
        {
            _rtbItemDetail.BackColor = colors.Grid;
            _rtbItemDetail.ForeColor = colors.Text;
        }

        // Update filter panels
        UpdateFilterPanelTheme(_pnlSubCategories, colors);
        UpdateFilterPanelTheme(_pnlJobFilters, colors);
    }

    private void UpdateFilterPanelTheme(FlowLayoutPanel? panel, ThemeColors colors)
    {
        if (panel == null) return;
        panel.BackColor = colors.Panel;

        foreach (Control ctrl in panel.Controls)
        {
            if (ctrl is CheckBox chk)
            {
                chk.ForeColor = colors.Text;
            }
            else if (ctrl is Label lbl)
            {
                lbl.ForeColor = lbl.Text.StartsWith("[") ? colors.TextMuted : colors.Border;
            }
        }
    }

    /// <inheritdoc/>
    public override void UpdateFontSize(float baseFontSize)
    {
        // Base class handles recursive font application to all controls
        base.UpdateFontSize(baseFontSize);

        // Additional specific adjustments for grid headers
        if (_dgvItems != null)
        {
            _dgvItems.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
        }

        // Ensure item name label uses highlighted style
        if (_lblItemName != null)
        {
            _lblItemName.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
        }
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Close all open item info popups
    /// </summary>
    public void CloseAllItemInfoForms()
    {
        var formsToClose = _openItemInfoForms.ToList();
        _openItemInfoForms.Clear();

        foreach (var form in formsToClose)
        {
            try
            {
                if (!form.IsDisposed)
                {
                    form.Close();
                    form.Dispose();
                }
            }
            catch { }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            CloseAllItemInfoForms();
            _imageHttpClient?.Dispose();
            _itemBindingSource?.Dispose();
            _dgvItems?.Dispose();
            _picItemImage?.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
