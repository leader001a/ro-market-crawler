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

    private readonly KafraItem _item;
    private readonly ItemIndexService? _itemIndexService;
    private readonly HttpClient _imageClient;
    private readonly string _imageCacheDir;
    private readonly ThemeType _theme;

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

    // UI Controls
    private PictureBox _picItem = null!;
    private Label _lblItemName = null!;
    private Label _lblBasicInfo = null!;
    private RichTextBox _rtbItemDesc = null!;

    public ItemInfoForm(KafraItem item, ItemIndexService? itemIndexService = null, ThemeType theme = ThemeType.Dark)
    {
        _item = item;
        _itemIndexService = itemIndexService;
        _theme = theme;
        _imageClient = new HttpClient();
        _imageClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

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

            // Adjust form height to fit content after UI is loaded
            AdjustFormHeightToContent();

            await LoadImageAsync();
        };
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
        }
        else
        {
            ThemeBackground = Color.FromArgb(240, 240, 240);
            ThemeCard = Color.White;
            ThemeText = Color.FromArgb(30, 30, 30);
            ThemeTextHighlight = Color.FromArgb(0, 100, 180);
            ThemeTextMuted = Color.FromArgb(100, 100, 100);
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
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(330, 300);
        MaximumSize = new Size(500, 750); // Limit max size
        BackColor = ThemeBackground;
        ForeColor = ThemeText;
        ShowIcon = true;
        LoadTitleBarIcon();

        // Center on current monitor
        Load += (s, e) => CenterOnCurrentScreen();

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(15),
            BackColor = ThemeBackground
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); // Header
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Description

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
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _picItem = new PictureBox
        {
            Size = new Size(80, 100),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeCard,
            Margin = new Padding(5)
        };
        headerLayout.Controls.Add(_picItem, 0, 0);

        var infoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(5)
        };

        _lblItemName = new Label
        {
            Text = _item.ScreenName ?? _item.Name ?? $"Item {_item.ItemConst}",
            Font = new Font("Malgun Gothic", 12, FontStyle.Bold),
            ForeColor = ThemeTextHighlight,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 28
        };
        infoPanel.Controls.Add(_lblItemName);

        _lblBasicInfo = new Label
        {
            Text = BuildBasicInfoText(),
            Font = new Font("Malgun Gothic", 9),
            ForeColor = ThemeTextMuted,
            AutoSize = false,
            Dock = DockStyle.Fill
        };
        infoPanel.Controls.Add(_lblBasicInfo);
        _lblBasicInfo.BringToFront();

        headerLayout.Controls.Add(infoPanel, 1, 0);
        headerCard.Controls.Add(headerLayout);
        mainPanel.Controls.Add(headerCard, 0, 0);

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
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            ForeColor = ThemeText,
            Dock = DockStyle.Top,
            Height = 25
        };
        descCard.Controls.Add(descTitle);

        _rtbItemDesc = new RichTextBox
        {
            BackColor = ThemeCard,
            ForeColor = ThemeText,
            Font = new Font("Malgun Gothic", 9.5f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        descCard.Controls.Add(_rtbItemDesc);
        _rtbItemDesc.BringToFront();

        // Load item description
        LoadItemDescription();

        mainPanel.Controls.Add(descCard, 0, 1);
        Controls.Add(mainPanel);
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
        const int minHeight = 350;
        const int maxHeight = 750;

        try
        {
            // Get actual content height from RichTextBox using GetPositionFromCharIndex
            var textLength = _rtbItemDesc.TextLength;
            int contentHeight;

            if (textLength > 0)
            {
                // Get position of last character
                var lastCharPos = _rtbItemDesc.GetPositionFromCharIndex(textLength - 1);
                // Add extra line height for safety margin
                contentHeight = lastCharPos.Y + 60;
            }
            else
            {
                contentHeight = 50; // Minimum for empty text
            }

            // Calculate total required height:
            // Title bar (~35) + Padding top (15) + Header card (130) + Gap (10) +
            // Desc card padding (10) + Desc title (25) + Content height + Padding bottom (15)
            var requiredHeight = 35 + 15 + 130 + 10 + 10 + 25 + contentHeight + 25;

            // Clamp to min/max
            var newHeight = Math.Clamp(requiredHeight, minHeight, maxHeight);

            // Apply new height
            Height = newHeight;
        }
        catch
        {
            // Fallback: use reasonable default
            Height = 450;
        }
    }

    private void LoadItemDescription()
    {
        _rtbItemDesc.Clear();

        var itemText = _item.ItemText ?? "(설명 없음)";
        // Remove color codes
        itemText = System.Text.RegularExpressions.Regex.Replace(itemText, @"\^[0-9a-fA-F]{6}_?", "");
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
    }

    private async Task LoadImageAsync()
    {
        try
        {
            byte[]? imageBytes = null;
            var cacheFilePath = Path.Combine(_imageCacheDir, $"{_item.ItemConst}_col.png");

            // Check cache
            if (File.Exists(cacheFilePath))
            {
                imageBytes = await File.ReadAllBytesAsync(cacheFilePath);
            }

            // Try kafra.kr
            if (imageBytes == null && !string.IsNullOrEmpty(_item.Name))
            {
                try
                {
                    var encodedName = Uri.EscapeDataString(_item.Name);
                    var kafraUrl = $"http://static.kafra.kr/kro/data/texture/%EC%9C%A0%EC%A0%80%EC%9D%B8%ED%84%B0%ED%8E%98%EC%9D%B4%EC%8A%A4/collection/png/{encodedName}.png";
                    imageBytes = await _imageClient.GetByteArrayAsync(kafraUrl);

                    // Cache it
                    _ = Task.Run(async () =>
                    {
                        try { await File.WriteAllBytesAsync(cacheFilePath, imageBytes); }
                        catch { }
                    });
                }
                catch { }
            }

            // Fallback: old cache
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
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Top + (workingArea.Height - Height) / 2
        );
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
