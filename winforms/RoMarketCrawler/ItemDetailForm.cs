using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Item detail popup form - shows image, price stats, enchants/cards
/// Supports both Dark and Classic themes
/// </summary>
public class ItemDetailForm : Form
{
    // Windows Dark Mode API
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly DealItem _item;
    private readonly ItemIndexService? _itemIndexService;
    private readonly HttpClient _imageClient;
    private readonly string _imageCacheDir;
    private readonly ThemeType _theme;

    // Theme colors (set dynamically based on theme)
    private Color ThemeBackground;
    private Color ThemePanel;
    private Color ThemeCard;
    private Color ThemeAccent;
    private Color ThemeText;
    private Color ThemeTextMuted;
    private Color ThemeTextHighlight;
    private Color ThemeBorder;
    private Color ThemePositive;
    private Color ThemeNegative;
    private Color ThemeWarning;

    // UI Controls
    private PictureBox _picItem = null!;
    private Label _lblItemName = null!;
    private Label _lblBasicInfo = null!;
    private RichTextBox _rtbItemDesc = null!;
    private RichTextBox _rtbSlotInfo = null!;
    private RichTextBox _rtbRandomOptions = null!;

    public ItemDetailForm(DealItem item, ItemIndexService? itemIndexService = null, ThemeType theme = ThemeType.Dark)
    {
        _item = item;
        _itemIndexService = itemIndexService;
        _theme = theme;
        _imageClient = new HttpClient();
        _imageClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        _imageClient.DefaultRequestHeaders.Referrer = new Uri("https://ro.gnjoy.com/");

        // Setup image cache directory
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoMarketCrawler");
        _imageCacheDir = Path.Combine(dataDir, "ItemImages");
        Directory.CreateDirectory(_imageCacheDir);

        // TEST: Write startup log
        try { File.WriteAllText(Path.Combine(_imageCacheDir, "startup_test.log"), $"ItemDetailForm created at {DateTime.Now}"); } catch { }

        ApplyThemeColors();
        InitializeUI();

        // Load data after form is shown (handle must be created for Invoke to work)
        Load += async (s, e) =>
        {
            // Apply dark mode to title bar and scrollbars
            if (_theme == ThemeType.Dark)
            {
                ApplyDarkModeToForm();
            }
            await LoadDataAsync();
        };
    }

    private void ApplyDarkModeToForm()
    {
        // Apply dark mode to title bar
        if (IsHandleCreated)
        {
            int darkMode = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        // Apply dark scrollbar theme to all RichTextBox controls
        ApplyDarkScrollBarTheme(_rtbItemDesc);
        ApplyDarkScrollBarTheme(_rtbSlotInfo);
        ApplyDarkScrollBarTheme(_rtbRandomOptions);
    }

    private void ApplyDarkScrollBarTheme(Control control)
    {
        if (control.IsHandleCreated)
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        else
        {
            control.HandleCreated += (s, e) =>
            {
                if (s is Control c)
                {
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                }
            };
        }
    }

    private void ApplyThemeColors()
    {
        if (_theme == ThemeType.Dark)
        {
            // Dark Theme
            ThemeBackground = Color.FromArgb(30, 30, 35);
            ThemePanel = Color.FromArgb(45, 45, 55);
            ThemeCard = Color.FromArgb(38, 38, 46);
            ThemeAccent = Color.FromArgb(70, 130, 200);
            ThemeText = Color.FromArgb(230, 230, 235);
            ThemeTextMuted = Color.FromArgb(160, 160, 170);
            ThemeTextHighlight = Color.FromArgb(100, 180, 255);
            ThemeBorder = Color.FromArgb(70, 75, 90);
            ThemePositive = Color.FromArgb(100, 200, 120);
            ThemeNegative = Color.FromArgb(255, 100, 100);
            ThemeWarning = Color.FromArgb(255, 180, 80);
        }
        else
        {
            // Classic Theme - use system colors
            ThemeBackground = SystemColors.Control;
            ThemePanel = SystemColors.Control;
            ThemeCard = SystemColors.Window;
            ThemeAccent = SystemColors.Highlight;
            ThemeText = SystemColors.WindowText;
            ThemeTextMuted = SystemColors.GrayText;
            ThemeTextHighlight = Color.FromArgb(0, 102, 204);
            ThemeBorder = SystemColors.ActiveBorder;
            ThemePositive = Color.FromArgb(0, 128, 0);
            ThemeNegative = Color.FromArgb(200, 0, 0);
            ThemeWarning = Color.FromArgb(180, 100, 0);
        }
    }

    private void InitializeUI()
    {
        Text = "아이템 상세정보";
        Size = new Size(1050, 650);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(900, 550);
        BackColor = ThemeBackground;
        ForeColor = ThemeText;
        ShowIcon = true;
        LoadTitleBarIcon();

        // Main layout: Header | Content | Random Options
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(15),
            BackColor = ThemeBackground,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));  // Top: Header
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Middle: Content (+50 from removed bottom)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 175)); // Random Options (+50)

        // === TOP: Header card (image + name + basic info + price) ===
        var headerCard = CreateCard(new Size(0, 130));
        headerCard.Dock = DockStyle.Fill;

        // Use TableLayoutPanel for header layout
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));  // Image column
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // Name/info column

        // Item image (column 0)
        _picItem = new PictureBox
        {
            Size = new Size(100, 100),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeCard,
            Margin = new Padding(5)
        };
        headerLayout.Controls.Add(_picItem, 0, 0);

        // Name and basic info panel (column 1)
        var infoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(10, 5, 10, 5)
        };

        _lblItemName = new Label
        {
            Text = _item.DisplayName ?? _item.ItemName,
            Font = new Font("Malgun Gothic", 14, FontStyle.Bold),
            ForeColor = ThemeTextHighlight,
            AutoSize = false,
            Location = new Point(0, 0),
            Size = new Size(600, 28),
            TextAlign = ContentAlignment.MiddleLeft
        };
        infoPanel.Controls.Add(_lblItemName);

        _lblBasicInfo = new Label
        {
            Text = BuildBasicInfoText(),
            Font = new Font("Malgun Gothic", 9),
            ForeColor = ThemeTextMuted,
            AutoSize = false,
            Location = new Point(0, 32),
            Size = new Size(600, 70)
        };
        infoPanel.Controls.Add(_lblBasicInfo);

        headerLayout.Controls.Add(infoPanel, 1, 0);

        headerCard.Controls.Add(headerLayout);
        mainPanel.Controls.Add(headerCard, 0, 0);

        // === MIDDLE: Content area (left: desc, right: enchants) ===
        var contentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = ThemeBackground,
            Margin = new Padding(0, 10, 0, 0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));  // Left: Item desc
        contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));  // Right: Enchants

        // Left: Item description card
        var descCard = CreateCard(new Size(0, 0));
        descCard.Dock = DockStyle.Fill;
        descCard.Margin = new Padding(0, 0, 8, 0);

        var descTitle = new Label
        {
            Text = "아이템 설명",
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            ForeColor = ThemeText,
            Location = new Point(15, 12),
            AutoSize = true
        };
        descCard.Controls.Add(descTitle);

        _rtbItemDesc = CreateRichTextBox();
        _rtbItemDesc.Location = new Point(15, 40);
        _rtbItemDesc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _rtbItemDesc.Size = new Size(descCard.Width - 30, descCard.Height - 55);
        _rtbItemDesc.Text = "로딩중...";
        descCard.Controls.Add(_rtbItemDesc);

        contentPanel.Controls.Add(descCard, 0, 0);

        // Right: Enchants only (random options moved to bottom)
        var slotCard = CreateCard(new Size(0, 0));
        slotCard.Dock = DockStyle.Fill;
        slotCard.Margin = new Padding(8, 0, 0, 0);

        var slotTitle = new Label
        {
            Text = "인챈트/카드 효과",
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            ForeColor = ThemeText,
            Location = new Point(15, 12),
            AutoSize = true
        };
        slotCard.Controls.Add(slotTitle);

        _rtbSlotInfo = CreateRichTextBox();
        _rtbSlotInfo.Location = new Point(15, 40);
        _rtbSlotInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _rtbSlotInfo.Size = new Size(slotCard.Width - 30, slotCard.Height - 55);
        _rtbSlotInfo.Text = "로딩중...";
        slotCard.Controls.Add(_rtbSlotInfo);

        contentPanel.Controls.Add(slotCard, 1, 0);

        mainPanel.Controls.Add(contentPanel, 0, 1);

        // === ROW 2: Random options card (separate bottom section) ===
        var optionsCard = CreateCard(new Size(0, 0));
        optionsCard.Dock = DockStyle.Fill;
        optionsCard.Margin = new Padding(0, 10, 0, 0);

        var optionsTitle = new Label
        {
            Text = "랜덤 옵션",
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            ForeColor = ThemeText,
            Location = new Point(15, 12),
            AutoSize = true
        };
        optionsCard.Controls.Add(optionsTitle);

        _rtbRandomOptions = CreateRichTextBox();
        _rtbRandomOptions.Location = new Point(15, 38);
        _rtbRandomOptions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _rtbRandomOptions.Size = new Size(optionsCard.Width - 30, optionsCard.Height - 50);
        _rtbRandomOptions.Text = "로딩중...";
        optionsCard.Controls.Add(_rtbRandomOptions);

        mainPanel.Controls.Add(optionsCard, 0, 2);

        Controls.Add(mainPanel);
    }

    private Panel CreateCard(Size size)
    {
        var card = new Panel
        {
            BackColor = ThemeCard,
            Size = size,
            Padding = new Padding(15),
            BorderStyle = BorderStyle.None
        };
        return card;
    }

    private RichTextBox CreateRichTextBox()
    {
        return new RichTextBox
        {
            BackColor = ThemeCard,
            ForeColor = ThemeText,
            Font = new Font("Malgun Gothic", 9.5f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
    }

    private void AppendColoredText(RichTextBox rtb, string text, Color color, bool bold = false, bool newLine = true)
    {
        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionLength = 0;
        rtb.SelectionColor = color;
        rtb.SelectionFont = new Font(rtb.Font, bold ? FontStyle.Bold : FontStyle.Regular);
        rtb.AppendText(newLine ? text + Environment.NewLine : text);
        rtb.SelectionColor = rtb.ForeColor;
    }

    private string BuildBasicInfoText()
    {
        var lines = new List<string>
        {
            $"서버: {_item.ServerName}  |  유형: {_item.DealTypeDisplay}  |  수량: {_item.Quantity}",
            $"상점: {_item.ShopName}" + (!string.IsNullOrEmpty(_item.MapName) ? $"  |  위치: {_item.MapName}" : ""),
            $"현재가: {_item.PriceFormatted} z"
        };
        return string.Join(Environment.NewLine, lines);
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var imageTask = LoadImageAsync();
            LoadDetailInfo();
            var itemDescTask = LoadItemDescriptionAsync();
            await Task.WhenAll(imageTask, itemDescTask);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] LoadDataAsync error: {ex.Message}");
        }
    }

    private Task LoadItemDescriptionAsync()
    {
        try
        {
            var baseItemName = _item.GetBaseItemName();
            var originalName = _item.ItemName;
            var effectiveItemId = _item.GetEffectiveItemId();

            KafraItem? kafraItem = null;

            // Check if ItemIndexService is available
            if (_itemIndexService == null || !_itemIndexService.IsLoaded)
            {
                if (!IsDisposed)
                {
                    Invoke(() =>
                    {
                        _rtbItemDesc.Clear();
                        AppendColoredText(_rtbItemDesc, "아이템 캐시가 로드되지 않았습니다.", ThemeTextMuted);
                        AppendColoredText(_rtbItemDesc, "(아이템 정보 수집 메뉴에서 데이터를 먼저 수집해주세요)", ThemeTextMuted);
                        _rtbItemDesc.SelectionStart = 0;
                        _rtbItemDesc.ScrollToCaret();
                    });
                }
                return Task.CompletedTask;
            }

            // Try by ID first (most accurate)
            if (effectiveItemId.HasValue)
            {
                var cachedItem = _itemIndexService.GetItemById(effectiveItemId.Value);
                if (cachedItem != null)
                {
                    kafraItem = cachedItem.ToKafraItem();
                }
            }

            // Try by name if not found by ID
            if (kafraItem == null)
            {
                var cachedItem = _itemIndexService.GetItemByName(baseItemName);
                if (cachedItem == null && baseItemName != originalName)
                {
                    cachedItem = _itemIndexService.GetItemByName(originalName);
                }
                if (cachedItem != null)
                {
                    kafraItem = cachedItem.ToKafraItem();
                }
            }

            // Display result
            if (kafraItem != null && !IsDisposed)
            {
                Invoke(() =>
                {
                    _rtbItemDesc.Clear();

                    // Item metadata section
                    AppendColoredText(_rtbItemDesc, "기본 정보", ThemeTextHighlight, true);

                    var infoText = $"분류: {kafraItem.GetTypeDisplayName()}";
                    if (kafraItem.Slots > 0) infoText += $"  |  슬롯: {kafraItem.Slots}";
                    if (kafraItem.Weight > 0) infoText += $"  |  무게: {kafraItem.GetFormattedWeight()}";
                    AppendColoredText(_rtbItemDesc, infoText, ThemeTextMuted);

                    var npcBuy = kafraItem.GetFormattedNpcBuyPrice();
                    var npcSell = kafraItem.GetFormattedNpcSellPrice();
                    if (npcBuy != "-" || npcSell != "-")
                    {
                        AppendColoredText(_rtbItemDesc, $"NPC: 구매 {npcBuy} / 판매 {npcSell}", ThemeTextMuted);
                    }

                    if (!string.IsNullOrEmpty(kafraItem.EquipJobsText))
                    {
                        AppendColoredText(_rtbItemDesc, $"착용: {kafraItem.EquipJobsText}", ThemeTextMuted);
                    }

                    AppendColoredText(_rtbItemDesc, "", ThemeText); // Spacer

                    // Item effect section
                    AppendColoredText(_rtbItemDesc, "아이템 효과", ThemeTextHighlight, true);
                    if (!string.IsNullOrEmpty(kafraItem.ItemText))
                    {
                        AppendColoredText(_rtbItemDesc, kafraItem.ItemText, ThemeText);
                    }
                    else
                    {
                        AppendColoredText(_rtbItemDesc, "(효과 설명 없음)", ThemeTextMuted);
                    }

                    // Scroll to top
                    _rtbItemDesc.SelectionStart = 0;
                    _rtbItemDesc.ScrollToCaret();
                });
            }
            else if (!IsDisposed)
            {
                Invoke(() =>
                {
                    _rtbItemDesc.Clear();
                    AppendColoredText(_rtbItemDesc, "아이템 정보를 찾을 수 없습니다.", ThemeTextMuted);
                    AppendColoredText(_rtbItemDesc, "(아이템 정보 수집을 먼저 실행해주세요)", ThemeTextMuted);
                    _rtbItemDesc.SelectionStart = 0;
                    _rtbItemDesc.ScrollToCaret();
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] Item description load error: {ex.Message}");
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    _rtbItemDesc.Clear();
                    AppendColoredText(_rtbItemDesc, "정보 로딩 실패: " + ex.Message, ThemeNegative);
                    _rtbItemDesc.SelectionStart = 0;
                    _rtbItemDesc.ScrollToCaret();
                });
            }
        }

        return Task.CompletedTask;
    }

    private async Task LoadImageAsync()
    {
        try
        {
            byte[]? imageBytes = null;
            var effectiveItemId = _item.GetEffectiveItemId();
            string? itemInternalName = null;

            // Get item name from cache for kafra.kr image URL
            if (_itemIndexService?.IsLoaded == true && effectiveItemId.HasValue)
            {
                var cachedItem = _itemIndexService.GetItemById(effectiveItemId.Value);
                itemInternalName = cachedItem?.Name;
            }

            // Check local cache first
            if (effectiveItemId.HasValue)
            {
                var cacheFilePath = Path.Combine(_imageCacheDir, $"{effectiveItemId.Value}_col.png");
                if (File.Exists(cacheFilePath))
                {
                    imageBytes = await File.ReadAllBytesAsync(cacheFilePath);
                }
            }

            // Try kafra.kr collection image (75x100 high quality)
            if (imageBytes == null && !string.IsNullOrEmpty(itemInternalName))
            {
                try
                {
                    var encodedName = Uri.EscapeDataString(itemInternalName);
                    var kafraUrl = $"http://static.kafra.kr/kro/data/texture/%EC%9C%A0%EC%A0%80%EC%9D%B8%ED%84%B0%ED%8E%98%EC%9D%B4%EC%8A%A4/collection/png/{encodedName}.png";
                    imageBytes = await _imageClient.GetByteArrayAsync(kafraUrl);

                    // Save to cache
                    if (effectiveItemId.HasValue)
                    {
                        var cacheFilePath = Path.Combine(_imageCacheDir, $"{effectiveItemId.Value}_col.png");
                        _ = Task.Run(async () =>
                        {
                            try { await File.WriteAllBytesAsync(cacheFilePath, imageBytes); }
                            catch { }
                        });
                    }
                }
                catch { }
            }

            // Fallback: Check old cache format
            if (imageBytes == null && effectiveItemId.HasValue)
            {
                var oldCacheFilePath = Path.Combine(_imageCacheDir, $"{effectiveItemId.Value}.png");
                if (File.Exists(oldCacheFilePath))
                {
                    imageBytes = await File.ReadAllBytesAsync(oldCacheFilePath);
                }
            }

            // Fallback: GNJOY URL
            if (imageBytes == null && !string.IsNullOrEmpty(_item.ItemImageUrl))
            {
                try
                {
                    var imageUrl = _item.ItemImageUrl;
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = "https://ro.gnjoy.com" + imageUrl;
                    }
                    imageBytes = await _imageClient.GetByteArrayAsync(imageUrl);
                }
                catch { }
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

    private void LoadDetailInfo()
    {
        if (IsDisposed) return;

        _rtbSlotInfo.Clear();
        _rtbRandomOptions.Clear();

        // Display slot info (enchants/cards)
        if (_item.SlotInfo.Count > 0)
        {
            var slotCount = 0;
            foreach (var slot in _item.SlotInfo)
            {
                slotCount++;

                // Get effect from ItemIndexService cache
                string? effect = null;
                if (_itemIndexService?.IsLoaded == true)
                {
                    // Try exact name first
                    var cachedItem = _itemIndexService.GetItemByName(slot);

                    // If not found and it's a card, try with "카드" suffix
                    if (cachedItem == null && !slot.Contains("카드"))
                    {
                        cachedItem = _itemIndexService.GetItemByName(slot + " 카드");
                    }

                    // If not found, try without "카드" suffix
                    if (cachedItem == null && slot.EndsWith(" 카드"))
                    {
                        var baseName = slot.Replace(" 카드", "");
                        cachedItem = _itemIndexService.GetItemByName(baseName);
                    }

                    if (cachedItem != null)
                    {
                        effect = cachedItem.ItemText;
                    }
                }

                AppendColoredText(_rtbSlotInfo, $"[{slotCount}] {slot}", ThemeTextHighlight, true);

                if (!string.IsNullOrEmpty(effect))
                {
                    AppendColoredText(_rtbSlotInfo, effect, ThemeText);
                }
                else
                {
                    AppendColoredText(_rtbSlotInfo, "(효과 정보 없음)", ThemeTextMuted);
                }
                AppendColoredText(_rtbSlotInfo, "", ThemeText); // Spacer
            }
        }
        else
        {
            AppendColoredText(_rtbSlotInfo, "인챈트/카드 없음", ThemeTextMuted);
        }

        // Scroll slot info to top
        _rtbSlotInfo.SelectionStart = 0;
        _rtbSlotInfo.ScrollToCaret();

        // Display random options
        if (_item.RandomOptions.Count > 0)
        {
            foreach (var option in _item.RandomOptions)
            {
                AppendColoredText(_rtbRandomOptions, option, ThemePositive);
            }
        }
        else
        {
            AppendColoredText(_rtbRandomOptions, "없음", ThemeTextMuted);
        }

        // Scroll to top
        _rtbRandomOptions.SelectionStart = 0;
        _rtbRandomOptions.ScrollToCaret();
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
            Debug.WriteLine($"[ItemDetailForm] Failed to load icon: {ex.Message}");
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _imageClient.Dispose();
        base.OnFormClosed(e);
    }
}
