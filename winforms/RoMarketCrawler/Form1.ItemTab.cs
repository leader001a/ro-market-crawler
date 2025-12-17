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
            RowCount = 3,
            Padding = new Padding(10)
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // Toolbar
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content (left-right)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Status bar
        ApplyTableLayoutPanelStyle(mainPanel);

        // ToolStrip-based toolbar
        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = ThemePanel,
            Renderer = new DarkToolStripRenderer()
        };

        // Item type combo
        var cboType = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            ToolTipText = "아이템 타입"
        };
        cboType.Items.AddRange(new object[] {
            new ItemTypeItem(999, "전체"),
            new ItemTypeItem(4, "무기"),
            new ItemTypeItem(5, "방어구"),
            new ItemTypeItem(6, "카드"),
            new ItemTypeItem(7, "펫"),
            new ItemTypeItem(10, "화살/탄약/투사체"),
            new ItemTypeItem(19, "쉐도우"),
            new ItemTypeItem(20, "의상"),
            new ItemTypeItem(998, "기타")
        });
        cboType.SelectedIndex = 0;
        _cboItemType = cboType.ComboBox;
        cboType.SelectedIndexChanged += CboItemType_SelectedIndexChanged;

        // Sub-filter combo 1 (weapon type, armor position, card position, shadow position, costume position)
        _cboSubFilter1 = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Visible = false,
            ToolTipText = "세부 필터 1"
        };

        // Sub-filter combo 2 (job class - for weapons and armor only)
        _cboSubFilter2 = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            Visible = false,
            ToolTipText = "직업군 필터"
        };

        // Search text
        var txtSearch = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 250,
            BackColor = ThemeGrid,
            ForeColor = ThemeText,
            ToolTipText = "검색할 아이템명 입력"
        };
        txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BtnItemSearch_Click(s, e); } };
        _txtItemSearch = txtSearch.TextBox;

        // Search button
        var btnSearch = new ToolStripButton
        {
            Text = "검색",
            BackColor = ThemeAccent,
            ForeColor = ThemeAccentText,
            ToolTipText = "검색 실행"
        };
        btnSearch.Click += BtnItemSearch_Click;
        _btnItemSearch = new Button(); // Dummy
        _btnItemSearchToolStrip = btnSearch;

        // Index rebuild button (right-aligned)
        var btnIndexRebuild = new ToolStripButton
        {
            Text = "아이템정보 수집",
            Alignment = ToolStripItemAlignment.Right,
            ToolTipText = "kafra.kr에서 아이템 정보 수집"
        };
        btnIndexRebuild.Click += BtnIndexRebuild_Click;
        _btnIndexRebuild = new Button(); // Dummy
        _btnIndexRebuildToolStrip = btnIndexRebuild;

        // Progress bar (right-aligned)
        _progressIndex = new ProgressBar
        {
            Width = 150,
            Visible = false,
            Style = ProgressBarStyle.Continuous
        };
        var progressHost = new ToolStripControlHost(_progressIndex)
        {
            Alignment = ToolStripItemAlignment.Right,
            Visible = false
        };
        _progressIndexHost = progressHost;

        // Add items to toolbar
        toolStrip.Items.Add(cboType);
        toolStrip.Items.Add(_cboSubFilter1);
        toolStrip.Items.Add(_cboSubFilter2);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(txtSearch);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnSearch);
        toolStrip.Items.Add(progressHost);
        toolStrip.Items.Add(btnIndexRebuild);

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
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
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

        leftPanel.Controls.Add(_dgvItems);

        // Right panel: Name -> Image -> Detail text (vertical layout)
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = ThemePanel,
            Padding = new Padding(0),
            Margin = new Padding(5, 0, 0, 0)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));  // Name area
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 115)); // Image area
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Detail text area

        // Item name container with border
        var nameContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeBorder,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 3)
        };
        var nameInner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid
        };
        _lblItemName = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            Font = new Font("Segoe UI", _baseFontSize - 1, FontStyle.Bold),
            ForeColor = ThemeLinkColor,
            BackColor = ThemeGrid,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(5, 0, 0, 0)
        };
        nameInner.Controls.Add(_lblItemName);
        nameContainer.Controls.Add(nameInner);

        // Item image container with border
        var imageContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeBorder,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 3)
        };
        var imageInner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeGrid
        };
        _picItemImage = new PictureBox
        {
            Width = 100,
            Height = 100,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeGrid,
            Location = new Point(5, 5)
        };
        imageInner.Controls.Add(_picItemImage);
        imageContainer.Controls.Add(imageInner);

        // Detail text container with border
        var detailContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeBorder,
            Padding = new Padding(1),
            Margin = new Padding(0)
        };
        _txtItemDetail = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None
        };
        ApplyDetailTextBoxStyle(_txtItemDetail);
        detailContainer.Controls.Add(_txtItemDetail);

        rightPanel.Controls.Add(nameContainer, 0, 0);
        rightPanel.Controls.Add(imageContainer, 0, 1);
        rightPanel.Controls.Add(detailContainer, 0, 2);

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

        _btnItemPrev = new Button
        {
            Text = "< 이전",
            Width = 70,
            Enabled = false,
            Margin = new Padding(0, 0, 5, 0)
        };
        _btnItemPrev.Click += BtnItemPrev_Click;
        ApplyButtonStyle(_btnItemPrev, false);

        _lblItemPage = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "",
            Margin = new Padding(10, 5, 10, 0),
            ForeColor = ThemeText,
            Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Regular)
        };

        _btnItemNext = new Button
        {
            Text = "다음 >",
            Width = 70,
            Enabled = false,
            Margin = new Padding(5, 0, 0, 0)
        };
        _btnItemNext.Click += BtnItemNext_Click;
        ApplyButtonStyle(_btnItemNext, false);

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

        mainPanel.Controls.Add(toolStrip, 0, 0);
        mainPanel.Controls.Add(contentPanel, 0, 1);
        mainPanel.Controls.Add(statusPanel, 0, 2);

        tab.Controls.Add(mainPanel);
    }

    private void SetupItemGridColumns()
    {
        _dgvItems.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ItemConst", HeaderText = "ID", DataPropertyName = "ItemConst", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "ScreenName", HeaderText = "아이템명", DataPropertyName = "ScreenName", FillWeight = 200 },
            new DataGridViewTextBoxColumn { Name = "TypeDisplay", HeaderText = "타입", DataPropertyName = "TypeDisplay", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Slots", HeaderText = "슬롯", DataPropertyName = "Slots", Width = 50 },
            new DataGridViewTextBoxColumn { Name = "WeightDisplay", HeaderText = "무게", DataPropertyName = "WeightDisplay", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "NpcBuyPrice", HeaderText = "NPC구매가", DataPropertyName = "NpcBuyPrice", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "EquipJobsText", HeaderText = "장착 가능", DataPropertyName = "EquipJobsText", FillWeight = 150 }
        });
    }

    private void TxtItemSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            BtnItemSearch_Click(sender, e);
        }
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
        var selectedType = _cboItemType.SelectedItem as ItemTypeItem;
        var itemType = selectedType?.Id ?? 999;

        _btnItemSearch.Enabled = false;
        _btnItemPrev.Enabled = false;
        _btnItemNext.Enabled = false;
        _lblItemStatus.Text = "검색 중...";

        try
        {
            List<KafraItem> items;
            var skip = _itemCurrentPage * ItemPageSize;

            // Check if any sub-filter is active
            var hasSubFilter = HasActiveSubFilter();

            // Use index if loaded, otherwise fallback to API
            if (_itemIndexService.IsLoaded)
            {
                if (hasSubFilter)
                {
                    // Get all items matching type, then apply sub-filters client-side
                    var allItems = _itemIndexService.SearchItems(searchText, itemType, 0, int.MaxValue);
                    var filteredItems = ApplySubFilters(allItems, itemType);
                    _itemTotalCount = filteredItems.Count;
                    items = filteredItems.Skip(skip).Take(ItemPageSize).ToList();
                    Debug.WriteLine($"[Form1] Index search with sub-filter: '{searchText}' type={itemType} page={_itemCurrentPage} -> {items.Count}/{_itemTotalCount} results (filtered from {allItems.Count})");
                }
                else
                {
                    // No sub-filter, use server-side pagination
                    _itemTotalCount = _itemIndexService.CountItems(searchText, itemType);
                    items = _itemIndexService.SearchItems(searchText, itemType, skip, ItemPageSize);
                    Debug.WriteLine($"[Form1] Index search: '{searchText}' type={itemType} page={_itemCurrentPage} -> {items.Count}/{_itemTotalCount} results");
                }
            }
            else
            {
                // API doesn't support pagination, just get first page
                items = await _kafraClient.SearchItemsAsync(searchText, itemType, ItemPageSize);
                if (hasSubFilter)
                {
                    items = ApplySubFilters(items, itemType);
                }
                _itemTotalCount = items.Count;
                Debug.WriteLine($"[Form1] API search: '{searchText}' type={itemType} -> {items.Count} results");
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
    /// Checks if any sub-filter is active (not set to "전체")
    /// </summary>
    private bool HasActiveSubFilter()
    {
        if (_cboSubFilter1.Visible && _cboSubFilter1.SelectedItem is FilterOption f1 && !string.IsNullOrEmpty(f1.Pattern))
            return true;
        if (_cboSubFilter2.Visible && _cboSubFilter2.SelectedItem is FilterOption f2 && !string.IsNullOrEmpty(f2.Pattern))
            return true;
        return false;
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
        if (_dgvItems.SelectedRows.Count == 0) return;

        var selectedIndex = _dgvItems.SelectedRows[0].Index;
        if (selectedIndex < 0 || selectedIndex >= _itemResults.Count) return;

        var item = _itemResults[selectedIndex];

        // Set item name label
        _lblItemName.Text = $"{item.ScreenName} (ID: {item.ItemConst})";

        // Build detail text (without name, since it's shown in label)
        var details = new System.Text.StringBuilder();
        details.AppendLine($"타입: {item.GetTypeDisplayName()}");
        details.AppendLine($"무게: {item.GetFormattedWeight()}");
        details.AppendLine($"슬롯: {item.Slots}");
        details.AppendLine($"NPC 구매가: {item.GetFormattedNpcBuyPrice()}");
        details.AppendLine($"NPC 판매가: {item.GetFormattedNpcSellPrice()}");
        details.AppendLine();
        details.AppendLine("[효과]");
        var itemText = item.ItemText ?? "-";
        // Remove color codes and normalize line breaks for Windows TextBox
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"\^[0-9a-fA-F]{6}_?", "");
        itemText = itemText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        details.AppendLine(itemText);
        if (!string.IsNullOrEmpty(item.EquipJobsText))
        {
            details.AppendLine();
            details.AppendLine($"[장착 가능 직업]");
            var equipJobs = item.EquipJobsText;
            equipJobs = equipJobs.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            details.AppendLine(equipJobs);
        }

        _txtItemDetail.Text = details.ToString();

        // Load item image asynchronously
        _ = LoadItemImageAsync(item.ItemConst);
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
            if (!IsDisposed && _dgvItems.SelectedRows.Count > 0)
            {
                var selectedIndex = _dgvItems.SelectedRows[0].Index;
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
            // Cancel current operation
            _indexCts?.Cancel();
            return;
        }

        _indexCts = new CancellationTokenSource();
        _btnIndexRebuildToolStrip.Text = "취소";
        _btnItemSearchToolStrip.Enabled = false;
        _progressIndexHost.Visible = true;
        _progressIndex.Visible = true;
        _progressIndex.Value = 0;

        var progress = new Progress<IndexProgress>(p =>
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateProgress(p)));
            }
            else
            {
                UpdateProgress(p);
            }
        });

        try
        {
            var success = await _itemIndexService.RebuildIndexAsync(progress, _indexCts.Token);

            if (success)
            {
                UpdateIndexStatus();
                MessageBox.Show(
                    $"인덱스 생성 완료!\n\n총 {_itemIndexService.TotalCount:N0}개 아이템이 저장되었습니다.",
                    "완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            _lblItemStatus.Text = "인덱스 생성이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"인덱스 생성 중 오류가 발생했습니다:\n{ex.Message}",
                "오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Debug.WriteLine($"[Form1] Index rebuild error: {ex}");
        }
        finally
        {
            _btnIndexRebuildToolStrip.Text = "아이템정보 수집";
            _btnItemSearchToolStrip.Enabled = true;
            _progressIndexHost.Visible = false;
            _progressIndex.Visible = false;
            _indexCts = null;
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
    /// Updates sub-filter combo boxes based on selected item type
    /// </summary>
    private void CboItemType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selectedType = _cboItemType.SelectedItem as ItemTypeItem;
        var itemType = selectedType?.Id ?? 999;

        // Get filters for this item type
        var filters = ItemFilters.GetFiltersForType(itemType);

        // Reset sub-filter combos
        _cboSubFilter1.Items.Clear();
        _cboSubFilter2.Items.Clear();
        _cboSubFilter1.Visible = false;
        _cboSubFilter2.Visible = false;

        if (filters.Length > 0)
        {
            // First filter (position/type)
            var filter1 = filters[0];
            _cboSubFilter1.ToolTipText = filter1.Name;
            foreach (var option in filter1.Options)
            {
                _cboSubFilter1.Items.Add(option);
            }
            _cboSubFilter1.SelectedIndex = 0;
            _cboSubFilter1.Visible = true;

            // Second filter (job class) if available
            if (filters.Length > 1)
            {
                var filter2 = filters[1];
                _cboSubFilter2.ToolTipText = filter2.Name;
                foreach (var option in filter2.Options)
                {
                    _cboSubFilter2.Items.Add(option);
                }
                _cboSubFilter2.SelectedIndex = 0;
                _cboSubFilter2.Visible = true;
            }
        }
    }

    /// <summary>
    /// Applies client-side filtering based on selected sub-filters
    /// </summary>
    private List<KafraItem> ApplySubFilters(List<KafraItem> items, int itemType)
    {
        var filters = ItemFilters.GetFiltersForType(itemType);
        if (filters.Length == 0)
            return items;

        var result = items;

        // Apply first filter
        if (_cboSubFilter1.Visible && _cboSubFilter1.SelectedItem is FilterOption filter1)
        {
            if (!string.IsNullOrEmpty(filter1.Pattern))
            {
                var target = filters[0].Target;
                var regex = new System.Text.RegularExpressions.Regex(filter1.Pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = result.Where(item => MatchesFilter(item, target, regex)).ToList();
            }
        }

        // Apply second filter
        if (_cboSubFilter2.Visible && _cboSubFilter2.SelectedItem is FilterOption filter2)
        {
            if (!string.IsNullOrEmpty(filter2.Pattern) && filters.Length > 1)
            {
                var target = filters[1].Target;
                var regex = new System.Text.RegularExpressions.Regex(filter2.Pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = result.Where(item => MatchesFilter(item, target, regex)).ToList();
            }
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
