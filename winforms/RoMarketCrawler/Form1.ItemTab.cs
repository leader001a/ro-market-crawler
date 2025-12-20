using System.Diagnostics;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Form1 partial class - Item Search Tab (kafra.kr)
/// </summary>
public partial class Form1
{
    #region Tab 2: Item Search (kafra.kr)

    private void SetupItemTab(TabPage tab)
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // Row 0: Dropdown + Search
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Row 1: Sub-filters (auto-wrap)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Row 2: Job filters (auto-wrap)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Row 3: Content (left-right)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // Row 4: Status bar
        ApplyTableLayoutPanelStyle(mainPanel);

        // Row 0: Category dropdown + Search controls + Index button
        var row0Panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        ApplyFlowLayoutPanelStyle(row0Panel);

        // Category dropdown (single selection)
        _cboItemType = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Margin = new Padding(0, 2, 10, 0)
        };

        // Add category items
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
            _cboItemType.Items.Add(new CategoryItem(id, name));
        }
        _cboItemType.SelectedIndex = 0; // Default: 전체
        _cboItemType.SelectedIndexChanged += CboItemType_SelectedIndexChanged;

        // Search textbox
        _txtItemSearch = new TextBox
        {
            Width = 200,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Margin = new Padding(0, 2, 5, 0)
        };
        _txtItemSearch.KeyDown += (s, e) =>
        {
            // Enter key triggers search if:
            // 1. Dropdown is not showing, OR
            // 2. Dropdown is showing but no item is selected
            if (e.KeyCode == Keys.Enter && !_autoCompleteDropdown.HasSelection)
            {
                e.Handled = true; // Prevent other handlers from processing
                e.SuppressKeyPress = true;
                _autoCompleteDropdown.Hide(); // Hide dropdown and stop debounce timer
                BtnItemSearch_Click(s, e);
            }
        };

        // Search button
        var btnSearch = new Button
        {
            Text = "검색",
            AutoSize = true,
            Margin = new Padding(0, 0, 5, 0)
        };
        ApplyButtonStyle(btnSearch, true);
        btnSearch.Click += BtnItemSearch_Click;
        _btnItemSearch = btnSearch;
        _btnItemSearchToolStrip = new ToolStripButton(); // Dummy for compatibility

        // Description search checkbox
        _chkSearchDescription = new CheckBox
        {
            Text = "설명 포함 검색",
            AutoSize = true,
            ForeColor = ThemeText,
            Margin = new Padding(5, 4, 15, 0)
        };

        // Progress bar (hidden by default, shown during index rebuild)
        _progressIndex = new ProgressBar
        {
            Width = 120,
            Height = 20,
            Visible = false,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(10, 3, 0, 0)
        };

        // Dummy references for compatibility with existing code
        _btnIndexRebuild = new Button { Visible = false };
        _btnIndexRebuildToolStrip = new ToolStripButton();

        row0Panel.Controls.Add(_cboItemType);
        row0Panel.Controls.Add(_txtItemSearch);
        row0Panel.Controls.Add(btnSearch);
        row0Panel.Controls.Add(_chkSearchDescription);
        row0Panel.Controls.Add(_progressIndex);

        // Row 1: Sub-category checkboxes panel (2-column layout: label | checkboxes)
        _pnlSubCategories = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            MinimumSize = new Size(0, 0),
            Margin = new Padding(0)
        };
        ApplyFlowLayoutPanelStyle(_pnlSubCategories);

        // Row 2: Job class filters panel (2-column layout: label | checkboxes)
        _pnlJobFilters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            MinimumSize = new Size(0, 0),
            Margin = new Padding(0)
        };
        ApplyFlowLayoutPanelStyle(_pnlJobFilters);

        // Content area: Left-Right layout
        var contentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); // Left: Item list
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); // Right: Detail
        ApplyTableLayoutPanelStyle(contentPanel);

        // Left wrapper: Grid + Pagination (vertical layout)
        var leftWrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 5, 0)
        };
        leftWrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Grid
        leftWrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // Pagination
        ApplyTableLayoutPanelStyle(leftWrapper);

        // Left panel: Item list
        var leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemePanel,
            Padding = new Padding(3),
            Margin = new Padding(0)
        };

        _dgvItems = new DataGridView
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
        ApplyDataGridViewStyle(_dgvItems);

        // Force header center alignment by custom painting
        _dgvItems.CellPainting += (s, e) =>
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.Graphics != null) // Header row
            {
                e.PaintBackground(e.ClipBounds, true);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.FormattedValue?.ToString() ?? "",
                    e.CellStyle?.Font ?? _dgvItems.Font,
                    e.CellBounds,
                    e.CellStyle?.ForeColor ?? ThemeText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
                e.Handled = true;
            }
        };

        SetupItemGridColumns();
        _dgvItems.DataSource = _itemBindingSource;
        _dgvItems.SelectionChanged += DgvItems_SelectionChanged;
        _dgvItems.CellDoubleClick += DgvItems_CellDoubleClick;

        leftPanel.Controls.Add(_dgvItems);

        // Right panel: Header (Image + Name/BasicInfo) -> Description (matching ItemInfoForm layout)
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = ThemePanel,
            Padding = new Padding(5),
            Margin = new Padding(5, 0, 0, 0)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 135)); // Header (image + info)
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Description

        // Header card (Image on left, Info on right)
        var headerCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid,
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
        ApplyTableLayoutPanelStyle(headerLayout);

        _picItemImage = new PictureBox
        {
            Size = new Size(75, 100),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeGrid,
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
            ForeColor = ThemeLinkColor,
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
            ForeColor = ThemeTextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft
        };
        infoPanel.Controls.Add(_lblItemBasicInfo);
        _lblItemBasicInfo.BringToFront();

        headerLayout.Controls.Add(infoPanel, 1, 0);
        headerCard.Controls.Add(headerLayout);
        rightPanel.Controls.Add(headerCard, 0, 0);

        // Description card
        var descCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };

        var descTitle = new Label
        {
            Text = "아이템 설명",
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            ForeColor = ThemeText,
            Dock = DockStyle.Top,
            Height = 22
        };
        descCard.Controls.Add(descTitle);

        _rtbItemDetail = new RichTextBox
        {
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Font = new Font("Malgun Gothic", 9.5f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        descCard.Controls.Add(_rtbItemDetail);
        _rtbItemDetail.BringToFront();

        rightPanel.Controls.Add(descCard, 0, 1);

        // Pagination panel (centered below left grid)
        var paginationPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.None
        };
        ApplyFlowLayoutPanelStyle(paginationPanel);

        // Use a wrapper TableLayoutPanel for center alignment
        var paginationWrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1
        };
        ApplyTableLayoutPanelStyle(paginationWrapper);

        _btnItemPrev = new RoMarketCrawler.Controls.RoundedButton
        {
            Text = "< 이전",
            Size = new Size(80, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(0, 0, 5, 0)
        };
        ApplyRoundedButtonStyle(_btnItemPrev, false);
        _btnItemPrev.Click += BtnItemPrev_Click;

        _lblItemPage = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "",
            Margin = new Padding(10, 5, 10, 0),
            ForeColor = ThemeText,
            Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Regular)
        };

        _btnItemNext = new RoMarketCrawler.Controls.RoundedButton
        {
            Text = "다음 >",
            Size = new Size(80, 28),
            CornerRadius = 6,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0)
        };
        ApplyRoundedButtonStyle(_btnItemNext, false);
        _btnItemNext.Click += BtnItemNext_Click;

        paginationPanel.Controls.AddRange(new Control[] { _btnItemPrev, _lblItemPage, _btnItemNext });
        paginationPanel.AutoSize = true;
        paginationWrapper.Controls.Add(paginationPanel, 0, 0);
        paginationPanel.Anchor = AnchorStyles.None; // Center in cell

        // Add grid and pagination to left wrapper
        leftWrapper.Controls.Add(leftPanel, 0, 0);
        leftWrapper.Controls.Add(paginationWrapper, 0, 1);

        contentPanel.Controls.Add(leftWrapper, 0, 0);
        contentPanel.Controls.Add(rightPanel, 1, 0);

        // Status bar (just status label)
        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        ApplyFlowLayoutPanelStyle(statusPanel);

        _lblItemStatus = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "kafra.kr 아이템 데이터베이스에서 검색합니다.",
            Margin = new Padding(0, 5, 0, 0)
        };
        ApplyStatusLabelStyle(_lblItemStatus);

        statusPanel.Controls.Add(_lblItemStatus);

        mainPanel.Controls.Add(row0Panel, 0, 0);
        mainPanel.Controls.Add(_pnlSubCategories, 0, 1);
        mainPanel.Controls.Add(_pnlJobFilters, 0, 2);
        mainPanel.Controls.Add(contentPanel, 0, 3);
        mainPanel.Controls.Add(statusPanel, 0, 4);

        tab.Controls.Add(mainPanel);
    }

    /// <summary>
    /// Category item for ComboBox
    /// </summary>
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

    /// <summary>
    /// Handles category dropdown selection change
    /// </summary>
    private void CboItemType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboItemType.SelectedItem is not CategoryItem selected)
            return;

        _selectedItemTypes.Clear();
        _selectedItemTypes.Add(selected.Id);

        // Update sub-category checkboxes
        UpdateSubCategoryCheckboxes();
    }

    /// <summary>
    /// Updates sub-category checkboxes based on selected main category
    /// </summary>
    private void UpdateSubCategoryCheckboxes()
    {
        _pnlSubCategories.SuspendLayout();
        _pnlJobFilters.SuspendLayout();
        _pnlSubCategories.Controls.Clear();
        _pnlJobFilters.Controls.Clear();
        _subCategoryCheckBoxes.Clear();
        _jobFilterCheckBoxes.Clear();

        // Collect all applicable filters from selected categories
        var allFilters = new List<(FilterCategory category, int sourceType)>();

        if (_selectedItemTypes.Contains(999))
        {
            // "전체" selected - no sub-filters
            _pnlSubCategories.ResumeLayout();
            _pnlJobFilters.ResumeLayout();
            return;
        }

        foreach (var typeId in _selectedItemTypes)
        {
            var filters = ItemFilters.GetFiltersForType(typeId);
            foreach (var filter in filters)
            {
                allFilters.Add((filter, typeId));
            }
        }

        // Group filters by category name to avoid duplicates
        var uniqueFilters = allFilters
            .GroupBy(f => f.category.Name)
            .Select(g => g.First())
            .ToList();

        // Separate job filters from other filters
        var jobFilter = uniqueFilters.FirstOrDefault(f => f.category.Name == "직업군");
        var otherFilters = uniqueFilters.Where(f => f.category.Name != "직업군").ToList();

        // Create sub-category checkboxes for non-job filters (Row 1)
        foreach (var (filterCategory, sourceType) in otherFilters)
        {
            // Add category label with separator
            var lblCategory = new Label
            {
                Text = $"[{filterCategory.Name}]",
                AutoSize = true,
                ForeColor = ThemeTextMuted,
                Margin = new Padding(5, 4, 3, 0),
                Font = new Font("Malgun Gothic", 8.5f)
            };
            _pnlSubCategories.Controls.Add(lblCategory);

            // Add separator
            var separator = new Label
            {
                Text = "|",
                AutoSize = true,
                ForeColor = ThemeBorder,
                Margin = new Padding(0, 4, 5, 0),
                Font = new Font("Malgun Gothic", 8.5f)
            };
            _pnlSubCategories.Controls.Add(separator);

            // Add checkboxes for each option (except "전체")
            foreach (var option in filterCategory.Options)
            {
                if (string.IsNullOrEmpty(option.Pattern)) continue; // Skip "전체" option

                var key = $"{filterCategory.Name}:{option.DisplayName}";
                var chk = new CheckBox
                {
                    Text = option.DisplayName,
                    Tag = option,
                    AutoSize = true,
                    Checked = false,
                    ForeColor = ThemeText,
                    Margin = new Padding(0, 2, 5, 0),
                    Font = new Font("Malgun Gothic", 8.5f)
                };
                _pnlSubCategories.Controls.Add(chk);
                _subCategoryCheckBoxes[key] = chk;
            }
        }

        // Create job filter checkboxes (Row 2)
        if (jobFilter.category != null)
        {
            // Add category label with separator
            var lblJobCategory = new Label
            {
                Text = "[직업군]",
                AutoSize = true,
                ForeColor = ThemeTextMuted,
                Margin = new Padding(5, 4, 3, 0),
                Font = new Font("Malgun Gothic", 8.5f)
            };
            _pnlJobFilters.Controls.Add(lblJobCategory);

            // Add separator
            var jobSeparator = new Label
            {
                Text = "|",
                AutoSize = true,
                ForeColor = ThemeBorder,
                Margin = new Padding(0, 4, 5, 0),
                Font = new Font("Malgun Gothic", 8.5f)
            };
            _pnlJobFilters.Controls.Add(jobSeparator);

            // Add checkboxes for each job option (except "전체")
            foreach (var option in jobFilter.category.Options)
            {
                if (string.IsNullOrEmpty(option.Pattern)) continue; // Skip "전체" option

                var key = $"직업군:{option.DisplayName}";
                var chk = new CheckBox
                {
                    Text = option.DisplayName,
                    Tag = option,
                    AutoSize = true,
                    Checked = false,
                    ForeColor = ThemeText,
                    Margin = new Padding(0, 2, 5, 0),
                    Font = new Font("Malgun Gothic", 8.5f)
                };
                _pnlJobFilters.Controls.Add(chk);
                _jobFilterCheckBoxes[key] = chk;
            }
        }

        _pnlSubCategories.ResumeLayout();
        _pnlJobFilters.ResumeLayout();
    }

    private void SetupItemGridColumns()
    {
        _dgvItems.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ItemConst", HeaderText = "ID", DataPropertyName = "ItemConst", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "ScreenName", HeaderText = "아이템명", DataPropertyName = "ScreenName", FillWeight = 200 },
            new DataGridViewTextBoxColumn { Name = "Slots", HeaderText = "슬롯", DataPropertyName = "Slots", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "WeightDisplay", HeaderText = "무게", DataPropertyName = "WeightDisplay", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "NpcBuyPrice", HeaderText = "NPC구매가", DataPropertyName = "NpcBuyPrice", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "EquipJobsText", HeaderText = "장착 가능", DataPropertyName = "EquipJobsText", FillWeight = 150 }
        });
    }

    private async void BtnItemSearch_Click(object? sender, EventArgs e)
    {
        // Reset to first page on new search
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

            // Check if any sub-filter checkbox is checked
            var hasSubFilter = !_selectedItemTypes.Contains(999) && HasActiveSubFilter();
            var singleType = _selectedItemTypes.Count == 1 ? _selectedItemTypes.First() : 999;

            // Use index if loaded, otherwise fallback to API
            if (_itemIndexService.IsLoaded)
            {
                if (hasSubFilter)
                {
                    // Get all items matching types, then apply sub-filters client-side
                    var allItems = _itemIndexService.SearchItems(searchText, _selectedItemTypes, 0, int.MaxValue, searchDescription);
                    var filteredItems = ApplySubFilters(allItems);
                    _itemTotalCount = filteredItems.Count;
                    items = filteredItems.Skip(skip).Take(ItemPageSize).ToList();
                }
                else
                {
                    // No sub-filter, use server-side pagination
                    _itemTotalCount = _itemIndexService.CountItems(searchText, _selectedItemTypes, searchDescription);
                    items = _itemIndexService.SearchItems(searchText, _selectedItemTypes, skip, ItemPageSize, searchDescription);
                }
            }
            else
            {
                // API doesn't support multi-type or pagination, use single type
                // Note: API search doesn't support description search
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

            // Update pagination UI
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
            Debug.WriteLine($"[Form1] Item search error: {ex}");
        }
        finally
        {
            _btnItemSearch.Enabled = true;
            UpdateItemPaginationButtons();
        }
    }

    /// <summary>
    /// Checks if any sub-filter checkbox is checked
    /// </summary>
    private bool HasActiveSubFilter()
    {
        return _subCategoryCheckBoxes.Values.Any(chk => chk.Checked) ||
               _jobFilterCheckBoxes.Values.Any(chk => chk.Checked);
    }

    private void UpdateItemPagination()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        if (totalPages > 1)
        {
            _lblItemPage.Text = $"{_itemCurrentPage + 1} / {totalPages}";
        }
        else
        {
            _lblItemPage.Text = "";
        }
    }

    private void UpdateItemPaginationButtons()
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        _btnItemPrev.Enabled = _itemCurrentPage > 0;
        _btnItemNext.Enabled = _itemCurrentPage < totalPages - 1;
    }

    private async void BtnItemPrev_Click(object? sender, EventArgs e)
    {
        if (_itemCurrentPage > 0)
        {
            _itemCurrentPage--;
            await LoadItemPageAsync();
        }
    }

    private async void BtnItemNext_Click(object? sender, EventArgs e)
    {
        var totalPages = (_itemTotalCount + ItemPageSize - 1) / ItemPageSize;
        if (_itemCurrentPage < totalPages - 1)
        {
            _itemCurrentPage++;
            await LoadItemPageAsync();
        }
    }

    private void DgvItems_SelectionChanged(object? sender, EventArgs e)
    {
        var currentRow = _dgvItems.CurrentRow;
        if (currentRow == null) return;

        var selectedIndex = currentRow.Index;
        if (selectedIndex < 0 || selectedIndex >= _itemResults.Count) return;

        var item = _itemResults[selectedIndex];

        // Set item name label (matching ItemInfoForm)
        _lblItemName.Text = item.ScreenName ?? item.Name ?? $"Item {item.ItemConst}";

        // Set basic info label (matching ItemInfoForm format)
        var basicInfo = new List<string>
        {
            $"ID: {item.ItemConst}  |  타입: {item.GetTypeDisplayName()}",
            $"무게: {item.GetFormattedWeight()}  |  슬롯: {item.Slots}",
            $"NPC 구매가: {item.GetFormattedNpcBuyPrice()}",
            $"NPC 판매가: {item.GetFormattedNpcSellPrice()}"
        };
        _lblItemBasicInfo.Text = string.Join(Environment.NewLine, basicInfo);

        // Load item description into RichTextBox (matching ItemInfoForm)
        _rtbItemDetail.Clear();
        var itemText = item.ItemText ?? "(설명 없음)";
        // Remove color codes
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"\^[0-9a-fA-F]{6}_?", "");
        itemText = itemText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        _rtbItemDetail.Text = itemText;

        // Add equip jobs section if available
        if (!string.IsNullOrEmpty(item.EquipJobsText))
        {
            _rtbItemDetail.AppendText(Environment.NewLine + Environment.NewLine);
            _rtbItemDetail.SelectionColor = ThemeLinkColor;
            _rtbItemDetail.SelectionFont = new Font(_rtbItemDetail.Font, FontStyle.Bold);
            _rtbItemDetail.AppendText("[장착 가능 직업]" + Environment.NewLine);
            _rtbItemDetail.SelectionColor = ThemeText;
            _rtbItemDetail.SelectionFont = _rtbItemDetail.Font;

            var equipJobs = item.EquipJobsText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            _rtbItemDetail.AppendText(equipJobs);
        }

        // Load item image asynchronously
        _ = LoadItemImageAsync(item.ItemConst);
    }

    private void DgvItems_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _itemResults.Count) return;

        var item = _itemResults[e.RowIndex];

        // Check if popup for this item already exists
        var existingForm = _openItemInfoForms
            .OfType<ItemInfoForm>()
            .FirstOrDefault(f => !f.IsDisposed && f.ItemConst == item.ItemConst);

        if (existingForm != null)
        {
            // Bring existing popup to front
            existingForm.BringToFront();
            existingForm.Focus();
            if (existingForm.WindowState == FormWindowState.Minimized)
            {
                existingForm.WindowState = FormWindowState.Normal;
            }
            return;
        }

        // Open new ItemInfoForm (non-modal, allows multiple popups)
        var infoForm = new ItemInfoForm(item, _itemIndexService, _currentTheme);
        infoForm.FormClosed += (s, args) => _openItemInfoForms.Remove(infoForm);
        _openItemInfoForms.Add(infoForm);
        infoForm.Show();
    }

    private void CloseAllItemInfoForms()
    {
        // Close all open ItemInfoForm popups
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

    private async Task LoadItemImageAsync(int itemId)
    {
        try
        {
            // Clear previous image
            _picItemImage.Image?.Dispose();
            _picItemImage.Image = null;

            byte[]? imageBytes = null;
            var cacheFilePath = Path.Combine(_imageCacheDir, $"{itemId}_col.png");

            // Check local cache first
            if (File.Exists(cacheFilePath))
            {
                imageBytes = await File.ReadAllBytesAsync(cacheFilePath);
            }

            // Try kafra.kr collection image
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

                        // Save to cache
                        _ = Task.Run(async () =>
                        {
                            try { await File.WriteAllBytesAsync(cacheFilePath, imageBytes); }
                            catch { }
                        });
                    }
                    catch { }
                }
            }

            // Fallback: Check old cache format
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

            // Check if form is still alive and same item is still selected
            var currentRow = _dgvItems.CurrentRow;
            if (!IsDisposed && currentRow != null)
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

            // If not displayed, dispose
            image.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Form1] Failed to load image for item {itemId}: {ex.Message}");
            // Keep image blank on error
        }
    }

    private async void BtnIndexRebuild_Click(object? sender, EventArgs e)
    {
        if (_itemIndexService.IsLoading)
        {
            return;
        }

        // Use modal dialog for progress - visible from any tab
        using var progressDialog = new IndexProgressDialog(_currentTheme, _baseFontSize);
        var success = await progressDialog.ShowAndRunAsync(this, _itemIndexService);

        if (success)
        {
            UpdateIndexStatus();
            RefreshAutoCompleteSource(); // Update autocomplete with new item names
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
    }

    private void UpdateProgress(IndexProgress p)
    {
        if (p.IsComplete)
        {
            _progressIndex.Value = 100;
            _lblItemStatus.Text = $"완료: {p.ItemsCollected:N0}개 아이템";
        }
        else if (p.IsCancelled)
        {
            _lblItemStatus.Text = "취소됨";
        }
        else if (p.HasError)
        {
            _lblItemStatus.Text = p.Phase;
        }
        else
        {
            _progressIndex.Value = (int)Math.Min(p.ProgressPercent, 100);
            _lblItemStatus.Text = $"{p.Phase} ({p.CategoryIndex}/{p.TotalCategories}) - {p.ItemsCollected:N0}개 수집됨";
        }
    }

    /// <summary>
    /// Applies client-side filtering based on selected sub-filter checkboxes
    /// </summary>
    private List<KafraItem> ApplySubFilters(List<KafraItem> items)
    {
        // Get checked filters grouped by category (both sub-category and job filters)
        var allCheckBoxes = _subCategoryCheckBoxes
            .Concat(_jobFilterCheckBoxes)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var checkedFilters = allCheckBoxes
            .Where(kvp => kvp.Value.Checked && kvp.Value.Tag is FilterOption)
            .Select(kvp => (Key: kvp.Key, Option: (FilterOption)kvp.Value.Tag!))
            .GroupBy(x => x.Key.Split(':')[0]) // Group by category name
            .ToList();

        if (!checkedFilters.Any())
            return items;

        var result = items;

        // Build filter categories from all selected types
        var filterCategories = new Dictionary<string, FilterTarget>();
        foreach (var typeId in _selectedItemTypes)
        {
            foreach (var filter in ItemFilters.GetFiltersForType(typeId))
            {
                if (!filterCategories.ContainsKey(filter.Name))
                    filterCategories[filter.Name] = filter.Target;
            }
        }

        // Apply filters - items must match at least one option within each category (OR within category)
        // But must match all categories (AND between categories)
        foreach (var categoryGroup in checkedFilters)
        {
            var categoryName = categoryGroup.Key;
            if (!filterCategories.TryGetValue(categoryName, out var target))
                continue;

            // Build combined regex pattern for this category (OR logic)
            var patterns = categoryGroup.Select(x => x.Option.Pattern).Where(p => !string.IsNullOrEmpty(p));
            var combinedPattern = string.Join("|", patterns);
            if (string.IsNullOrEmpty(combinedPattern))
                continue;

            var regex = new System.Text.RegularExpressions.Regex(
                combinedPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            result = result.Where(item => MatchesFilter(item, target, regex)).ToList();
        }

        return result;
    }

    /// <summary>
    /// Checks if an item matches a filter pattern against the specified target field
    /// </summary>
    private static bool MatchesFilter(KafraItem item, FilterTarget target, System.Text.RegularExpressions.Regex regex)
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
}
