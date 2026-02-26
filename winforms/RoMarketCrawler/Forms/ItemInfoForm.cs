using System.Diagnostics;
using System.Runtime.InteropServices;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Simple item info popup - shows image and description only (no enchants/cards/random options)
/// Used for Item Database tab double-click
/// </summary>
public class ItemInfoForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // Static counter for staggered popup positioning
    private static int _popupOffsetCounter = 0;
    private const int OffsetStep = 30;
    private const int MaxOffsetSteps = 8;

    private readonly KafraItem _item;
    private readonly ItemIndexService? _itemIndexService;
    private readonly HttpClient _imageClient;
    private readonly string _imageCacheDir;
    private readonly ThemeType _theme;
    private readonly float _baseFontSize;

    /// <summary>
    /// Item ID for this popup (used to detect duplicate popups)
    /// </summary>
    public int ItemConst => _item.ItemConst;

    // Theme colors
    private Color ThemeBackground;
    private Color ThemeCard;
    private Color ThemeText;
    private Color ThemeTextHighlight;
    private Color ThemeTextMuted;

    // Theme accent color for link styling
    private Color ThemeAccent;

    // UI Controls
    private PictureBox _picItem = null!;
    private Label _lblItemName = null!;
    private Label _lblBasicInfo = null!;
    private RichTextBox _rtbItemDesc = null!;
    private TableLayoutPanel _mainPanel = null!;
    private Panel _relatedItemsCard = null!;
    private FlowLayoutPanel _relatedItemsFlow = null!;
    private bool _updatingRelatedHeight;

    public ItemInfoForm(KafraItem item, ItemIndexService? itemIndexService = null, ThemeType theme = ThemeType.Dark, float baseFontSize = 12f)
    {
        _item = item;
        _itemIndexService = itemIndexService;
        _theme = theme;
        _baseFontSize = baseFontSize;
        _imageClient = new HttpClient();
        _imageClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        _imageClient.DefaultRequestHeaders.Referrer = new Uri("https://ro.gnjoy.com/");

        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoMarketCrawler");
        _imageCacheDir = Path.Combine(dataDir, "ItemImages");
        Directory.CreateDirectory(_imageCacheDir);

        ApplyThemeColors();
        InitializeUI();

        Load += async (s, e) =>
        {
            if (_theme == ThemeType.Dark && IsHandleCreated)
            {
                int darkMode = 1;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }

            // Adjust form height to fit description content
            AdjustFormHeightToContent();

            // Measure actual related items content and adjust
            if (_relatedItemsCard.Visible)
            {
                UpdateRelatedItemsHeight();
                var relatedRowHeight = (int)_mainPanel.RowStyles[2].Height;
                Height += relatedRowHeight + (int)(15 * (_baseFontSize / 12f));
            }

            await LoadImageAsync();
        };

        Resize += (s, e) => UpdateRelatedItemsHeight();
    }

    private void ApplyThemeColors()
    {
        if (_theme == ThemeType.Dark)
        {
            ThemeBackground = Color.FromArgb(30, 30, 30);
            ThemeCard = Color.FromArgb(45, 45, 45);
            ThemeText = Color.FromArgb(220, 220, 220);
            ThemeTextHighlight = Color.FromArgb(100, 180, 255);
            ThemeTextMuted = Color.FromArgb(150, 150, 150);
            ThemeAccent = Color.FromArgb(70, 130, 200);
        }
        else
        {
            ThemeBackground = Color.FromArgb(240, 240, 240);
            ThemeCard = Color.White;
            ThemeText = Color.FromArgb(30, 30, 30);
            ThemeTextHighlight = Color.FromArgb(0, 100, 180);
            ThemeTextMuted = Color.FromArgb(100, 100, 100);
            ThemeAccent = SystemColors.Highlight;
        }
    }

    private void InitializeUI()
    {
        Text = $"{_item.ScreenName ?? _item.Name} - 아이템 정보";
        // Initial size - will be adjusted after load
        var baseWidth = 390;
        Size = new Size(baseWidth, 400); // Initial height, will be adjusted
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(330, 300);
        BackColor = ThemeBackground;
        ForeColor = ThemeText;
        ShowIcon = true;
        LoadTitleBarIcon();

        // Center on current monitor
        Load += (s, e) => CenterOnCurrentScreen();

        var scale = _baseFontSize / 12f;
        // Header needs: name (30) + 4 lines of basicInfo (~90) + padding (30) = ~150 base, increase for safety
        var headerHeight = (int)(180 * scale);
        var nameHeight = (int)(30 * scale);
        var imageHeight = (int)(100 * scale);

        _mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(15),
            BackColor = ThemeBackground
        };
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight)); // Header
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Description
        _mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));   // Related Items (hidden initially)

        // Header card
        var headerCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeCard,
            Padding = new Padding(10)
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(100 * scale)));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Force row to take full available height
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _picItem = new PictureBox
        {
            Size = new Size((int)(80 * scale), imageHeight),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeCard,
            Margin = new Padding(5),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        headerLayout.Controls.Add(_picItem, 0, 0);

        var infoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };

        _lblItemName = new Label
        {
            Text = _item.ScreenName ?? _item.Name ?? $"Item {_item.ItemConst}",
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold),
            ForeColor = ThemeTextHighlight,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = nameHeight
        };
        infoPanel.Controls.Add(_lblItemName);

        _lblBasicInfo = new Label
        {
            Text = BuildBasicInfoText(),
            Font = new Font("Malgun Gothic", _baseFontSize - 1),  // Slightly smaller for basic info
            ForeColor = ThemeTextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill
        };
        infoPanel.Controls.Add(_lblBasicInfo);
        _lblBasicInfo.BringToFront();

        headerLayout.Controls.Add(infoPanel, 1, 0);
        headerCard.Controls.Add(headerLayout);
        _mainPanel.Controls.Add(headerCard, 0, 0);

        // Description card
        var descCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeCard,
            Padding = new Padding(10),
            Margin = new Padding(0, 10, 0, 0)
        };

        var descTitle = new Label
        {
            Text = "아이템 설명",
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold),
            ForeColor = ThemeText,
            Dock = DockStyle.Top,
            Height = 25
        };
        descCard.Controls.Add(descTitle);

        _rtbItemDesc = new RichTextBox
        {
            BackColor = ThemeCard,
            ForeColor = ThemeText,
            Font = new Font("Malgun Gothic", _baseFontSize),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        descCard.Controls.Add(_rtbItemDesc);
        _rtbItemDesc.BringToFront();

        // Load item description
        LoadItemDescription();

        _mainPanel.Controls.Add(descCard, 0, 1);

        // === ROW 2: Related items card (hidden initially) ===
        _relatedItemsCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeCard,
            Padding = new Padding(10),
            Margin = new Padding(0, 10, 0, 0),
            Visible = false
        };

        var relatedTitle = new Label
        {
            Text = "연관 아이템",
            Font = new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold),
            ForeColor = ThemeText,
            Dock = DockStyle.Top,
            Height = 25
        };
        _relatedItemsCard.Controls.Add(relatedTitle);

        _relatedItemsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeCard,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true
        };
        _relatedItemsCard.Controls.Add(_relatedItemsFlow);
        _relatedItemsFlow.BringToFront();

        _mainPanel.Controls.Add(_relatedItemsCard, 0, 2);

        // Load related items
        LoadRelatedItems();

        Controls.Add(_mainPanel);
    }

    private string BuildBasicInfoText()
    {
        var lines = new List<string>
        {
            $"ID: {_item.ItemConst}  |  타입: {_item.GetTypeDisplayName()}",
            $"무게: {_item.GetFormattedWeight()}  |  슬롯: {_item.Slots}",
            $"NPC 구매가: {_item.GetFormattedNpcBuyPrice()}",
            $"NPC 판매가: {_item.GetFormattedNpcSellPrice()}"
        };
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Adjust form height to fit content using actual RichTextBox content measurement
    /// </summary>
    private void AdjustFormHeightToContent()
    {
        var scale = _baseFontSize / 12f;
        var minHeight = (int)(350 * scale);
        var maxHeight = (int)(900 * scale);

        try
        {
            var textLength = _rtbItemDesc.TextLength;
            int contentHeight;

            if (textLength > 0)
            {
                var lastCharPos = _rtbItemDesc.GetPositionFromCharIndex(textLength - 1);
                contentHeight = lastCharPos.Y + (int)(60 * scale);
            }
            else
            {
                contentHeight = (int)(50 * scale);
            }

            var headerHeight = (int)(180 * scale);
            var requiredHeight = 35 + (int)(15 * scale) + headerHeight + (int)(10 * scale) +
                                (int)(10 * scale) + (int)(25 * scale) + contentHeight + (int)(25 * scale);

            Height = Math.Clamp(requiredHeight, minHeight, maxHeight);
        }
        catch
        {
            Height = (int)(500 * scale);
        }
    }

    private void LoadItemDescription()
    {
        _rtbItemDesc.Clear();

        var itemText = _item.ItemText ?? "(설명 없음)";
        // Remove color codes
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"\^[0-9a-fA-F]{6}_?", "");
        // Remove RO inline tags: <NAVI>[NPC]<INFO>map,x,y,...</INFO></NAVI>
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"<INFO>[^<]*</INFO>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"</?[A-Z_]+>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        itemText = itemText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

        _rtbItemDesc.Text = itemText;

        if (!string.IsNullOrEmpty(_item.EquipJobsText))
        {
            _rtbItemDesc.AppendText(Environment.NewLine + Environment.NewLine);
            _rtbItemDesc.SelectionColor = ThemeTextHighlight;
            _rtbItemDesc.SelectionFont = new Font(_rtbItemDesc.Font, FontStyle.Bold);
            _rtbItemDesc.AppendText("[장착 가능 직업]" + Environment.NewLine);
            _rtbItemDesc.SelectionColor = ThemeText;
            _rtbItemDesc.SelectionFont = _rtbItemDesc.Font;

            var equipJobs = _item.EquipJobsText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            _rtbItemDesc.AppendText(equipJobs);
        }

        // Reset cursor to beginning and scroll to top
        _rtbItemDesc.SelectionStart = 0;
        _rtbItemDesc.SelectionLength = 0;
        _rtbItemDesc.ScrollToCaret();
    }

    private void LoadRelatedItems()
    {
        if (_itemIndexService == null || !_itemIndexService.IsLoaded) return;

        var itemName = _item.ScreenName ?? _item.Name;
        if (string.IsNullOrWhiteSpace(itemName) || itemName.Length < 2) return;

        // Search for items whose description (ItemText) mentions this item's name
        var searchResults = _itemIndexService.SearchItems(itemName, 999, 0, 50, searchDescription: true);

        var relatedItems = searchResults
            .Where(k => k.ItemText?.Contains(itemName, StringComparison.OrdinalIgnoreCase) == true
                && k.ItemConst != _item.ItemConst)
            .GroupBy(k => k.ScreenName ?? k.Name ?? k.ItemConst.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(k => k.ItemConst).First())
            .ToList();

        if (relatedItems.Count == 0) return;

        // Show the related items panel with initial height estimate
        // Actual height will be refined by UpdateRelatedItemsHeight() after layout
        var scale = _baseFontSize / 12f;
        _relatedItemsCard.Visible = true;
        _mainPanel.RowStyles[2] = new RowStyle(SizeType.Absolute, (int)(80 * scale));

        foreach (var kafraItem in relatedItems)
        {
            var displayName = kafraItem.ScreenName ?? kafraItem.Name ?? $"Item {kafraItem.ItemConst}";
            var link = new LinkLabel
            {
                Text = displayName,
                Font = new Font("Malgun Gothic", _baseFontSize - 1),
                AutoSize = true,
                LinkColor = ThemeTextHighlight,
                ActiveLinkColor = ThemeAccent,
                VisitedLinkColor = ThemeTextHighlight,
                Margin = new Padding(4, 2, 4, 2),
                Tag = kafraItem
            };
            link.LinkClicked += RelatedItemLink_Clicked;
            _relatedItemsFlow.Controls.Add(link);
        }
    }

    private void UpdateRelatedItemsHeight()
    {
        if (!_relatedItemsCard.Visible || _relatedItemsFlow.Controls.Count == 0) return;
        if (_updatingRelatedHeight) return;
        _updatingRelatedHeight = true;
        try
        {
            // Get available width for flow content
            var flowWidth = _relatedItemsFlow.ClientSize.Width;
            if (flowWidth <= 0)
                flowWidth = _mainPanel.ClientSize.Width - _mainPanel.Padding.Horizontal -
                            _relatedItemsCard.Margin.Horizontal - _relatedItemsCard.Padding.Horizontal;
            if (flowWidth <= 0) return;

            // Manually calculate wrapping height using TextRenderer for accurate font measurement
            int currentX = 0;
            int currentRowHeight = 0;
            int totalHeight = 0;

            foreach (Control ctrl in _relatedItemsFlow.Controls)
            {
                // TextRenderer.MeasureText is more accurate than GetPreferredSize for font metrics
                var textSize = TextRenderer.MeasureText(ctrl.Text, ctrl.Font);
                var itemWidth = textSize.Width + ctrl.Margin.Horizontal + 8; // +8 for link label internal padding
                var itemHeight = textSize.Height + ctrl.Margin.Vertical + 4; // +4 for font descent/leading

                if (currentX > 0 && currentX + itemWidth > flowWidth)
                {
                    totalHeight += currentRowHeight;
                    currentX = 0;
                    currentRowHeight = 0;
                }

                currentX += itemWidth;
                currentRowHeight = Math.Max(currentRowHeight, itemHeight);
            }
            totalHeight += currentRowHeight; // Last row

            if (totalHeight <= 0) return;

            var scale = _baseFontSize / 12f;
            // Card overhead: margin-top(10) + padding(10+10) + title(25) + AutoScroll reserve + rounding buffer
            var cardOverhead = (int)(70 * scale);
            var maxFlowHeight = (int)(200 * scale);
            var minFlowHeight = (int)(30 * scale); // Minimum one comfortable line
            var flowHeight = Math.Max(Math.Min(totalHeight, maxFlowHeight), minFlowHeight);

            var newRowHeight = cardOverhead + flowHeight;
            _mainPanel.RowStyles[2] = new RowStyle(SizeType.Absolute, newRowHeight);
        }
        finally
        {
            _updatingRelatedHeight = false;
        }
    }

    private void RelatedItemLink_Clicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (sender is not LinkLabel link || link.Tag is not KafraItem kafraItem) return;

        var infoForm = new ItemInfoForm(kafraItem, _itemIndexService, _theme, _baseFontSize);
        infoForm.Show(this);
    }

    private async Task LoadImageAsync()
    {
        try
        {
            byte[]? imageBytes = null;
            var cacheFilePath = Path.Combine(_imageCacheDir, $"{_item.ItemConst}_col.png");

            // 1. Check local cache first
            if (File.Exists(cacheFilePath))
            {
                imageBytes = await File.ReadAllBytesAsync(cacheFilePath);
            }

            // 2. Try GNJOY image URL (most reliable source)
            if (imageBytes == null)
            {
                try
                {
                    var gnjoyUrl = $"https://imgc1.gnjoy.com/games/ro1/object/201306/{_item.ItemConst}.png";
                    imageBytes = await _imageClient.GetByteArrayAsync(gnjoyUrl);

                    // Save to cache
                    _ = Task.Run(async () =>
                    {
                        try { await File.WriteAllBytesAsync(cacheFilePath, imageBytes); }
                        catch { }
                    });
                }
                catch { }
            }

            // 3. Fallback: Try kafra.kr with internal item name
            if (imageBytes == null)
            {
                string? itemInternalName = _item.Name;
                if (string.IsNullOrEmpty(itemInternalName) && _itemIndexService?.IsLoaded == true)
                {
                    var cachedItem = _itemIndexService.GetItemById(_item.ItemConst);
                    itemInternalName = cachedItem?.Name;
                }

                if (!string.IsNullOrEmpty(itemInternalName))
                {
                    try
                    {
                        var encodedName = Uri.EscapeDataString(itemInternalName);
                        var kafraUrl = $"http://static.kafra.kr/kro/data/texture/%EC%9C%A0%EC%A0%80%EC%9D%B8%ED%84%B0%ED%8E%98%EC%9D%B4%EC%8A%A4/collection/png/{encodedName}.png";
                        imageBytes = await _imageClient.GetByteArrayAsync(kafraUrl);

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

            // 4. Fallback: check old cache format
            if (imageBytes == null)
            {
                var oldCachePath = Path.Combine(_imageCacheDir, $"{_item.ItemConst}.png");
                if (File.Exists(oldCachePath))
                {
                    imageBytes = await File.ReadAllBytesAsync(oldCachePath);
                }
            }

            if (imageBytes != null && !IsDisposed)
            {
                using var ms = new MemoryStream(imageBytes);
                var image = Image.FromStream(ms);
                Invoke(() => _picItem.Image = image);
            }
        }
        catch { }
    }

    private void LoadTitleBarIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("RoMarketCrawler.app.ico");
            if (stream != null)
            {
                Icon = new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemInfoForm] Failed to load icon: {ex.Message}");
        }
    }

    private void CenterOnCurrentScreen()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var workingArea = screen.WorkingArea;

        // Apply staggered offset for multiple popups
        var offset = (_popupOffsetCounter % MaxOffsetSteps) * OffsetStep;
        _popupOffsetCounter++;

        var x = workingArea.Left + (workingArea.Width - Width) / 2 + offset;
        var y = workingArea.Top + (workingArea.Height - Height) / 2 + offset;

        // Ensure popup stays within screen bounds
        x = Math.Min(x, workingArea.Right - Width);
        y = Math.Min(y, workingArea.Bottom - Height);

        Location = new Point(x, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imageClient?.Dispose();
        }
        base.Dispose(disposing);
    }
}
