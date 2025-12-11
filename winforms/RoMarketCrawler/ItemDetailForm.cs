using System.Diagnostics;
using System.Net.Http;
using System.Text;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler;

/// <summary>
/// Item detail popup form - shows image, price stats, enchants/cards
/// </summary>
public class ItemDetailForm : Form
{
    private readonly DealItem _item;
    private readonly GnjoyClient _gnjoyClient;
    private readonly HttpClient _imageClient;

    // UI Controls
    private PictureBox _picItem = null!;
    private Label _lblItemName = null!;
    private Label _lblBasicInfo = null!;
    private GroupBox _grpPriceStats = null!;
    private Label _lblCurrentPrice = null!;
    private Label _lblYesterdayAvg = null!;
    private Label _lblWeek7Avg = null!;
    private Label _lblPriceCompare = null!;
    private GroupBox _grpItemDesc = null!;
    private TextBox _txtItemDesc = null!;
    private GroupBox _grpSlotInfo = null!;
    private TextBox _txtSlotInfo = null!;
    private GroupBox _grpRandomOptions = null!;
    private ListBox _lstRandomOptions = null!;
    private Label _lblStatus = null!;
    private Button _btnClose = null!;

    public ItemDetailForm(DealItem item, GnjoyClient gnjoyClient)
    {
        _item = item;
        _gnjoyClient = gnjoyClient;
        _imageClient = new HttpClient();
        _imageClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        _imageClient.DefaultRequestHeaders.Referrer = new Uri("https://ro.gnjoy.com/");

        // Initialize KafraClient for enchant lookup
        EnchantDatabase.Instance.InitializeKafraClient();

        InitializeUI();
        _ = LoadDataAsync();
    }

    private void InitializeUI()
    {
        Text = "아이템 상세정보";
        Size = new Size(600, 800);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(500, 650);

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(10)
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));  // Row 0: Image + Name
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Row 1: Basic info
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); // Row 2: Price stats
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));   // Row 3: Item description
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));   // Row 4: Slot info (enchants+effects)
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));   // Row 5: Random options
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));  // Row 6: Status + Close

        // Row 0: Item image and name
        _picItem = new PictureBox
        {
            Size = new Size(80, 80),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        mainPanel.Controls.Add(_picItem, 0, 0);

        var namePanel = new Panel { Dock = DockStyle.Fill };
        _lblItemName = new Label
        {
            Text = _item.DisplayName ?? _item.ItemName,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        namePanel.Controls.Add(_lblItemName);
        mainPanel.Controls.Add(namePanel, 1, 0);

        // Row 1: Basic info
        _lblBasicInfo = new Label
        {
            Text = BuildBasicInfoText(),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft
        };
        mainPanel.Controls.Add(_lblBasicInfo, 0, 1);
        mainPanel.SetColumnSpan(_lblBasicInfo, 2);

        // Row 2: Price statistics group
        _grpPriceStats = new GroupBox
        {
            Text = "시세 정보",
            Dock = DockStyle.Fill
        };
        var pricePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(5)
        };
        pricePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        pricePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        _lblCurrentPrice = new Label { Text = "현재가: " + _item.PriceFormatted, Dock = DockStyle.Fill };
        _lblYesterdayAvg = new Label { Text = "전일평균: 로딩중...", Dock = DockStyle.Fill };
        _lblWeek7Avg = new Label { Text = "7일평균: 로딩중...", Dock = DockStyle.Fill };
        _lblPriceCompare = new Label { Text = "시세비교: 로딩중...", Dock = DockStyle.Fill };

        pricePanel.Controls.Add(_lblCurrentPrice, 0, 0);
        pricePanel.Controls.Add(_lblYesterdayAvg, 1, 0);
        pricePanel.Controls.Add(_lblWeek7Avg, 0, 1);
        pricePanel.Controls.Add(_lblPriceCompare, 1, 1);

        _grpPriceStats.Controls.Add(pricePanel);
        mainPanel.Controls.Add(_grpPriceStats, 0, 2);
        mainPanel.SetColumnSpan(_grpPriceStats, 2);

        // Row 3: Item description (base effect from kafra.kr)
        _grpItemDesc = new GroupBox
        {
            Text = "아이템 설명",
            Dock = DockStyle.Fill
        };
        _txtItemDesc = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font("Malgun Gothic", 9.5f),
            Text = "로딩중..."
        };
        _grpItemDesc.Controls.Add(_txtItemDesc);
        mainPanel.Controls.Add(_grpItemDesc, 0, 3);
        mainPanel.SetColumnSpan(_grpItemDesc, 2);

        // Row 4: Slot info (enchants/cards) - ALL effects shown at once
        _grpSlotInfo = new GroupBox
        {
            Text = "인챈트/카드 효과",
            Dock = DockStyle.Fill
        };
        _txtSlotInfo = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font("Malgun Gothic", 9.5f),
            Text = "로딩중..."
        };
        _grpSlotInfo.Controls.Add(_txtSlotInfo);
        mainPanel.Controls.Add(_grpSlotInfo, 0, 4);
        mainPanel.SetColumnSpan(_grpSlotInfo, 2);

        // Row 5: Random options
        _grpRandomOptions = new GroupBox
        {
            Text = "랜덤 옵션",
            Dock = DockStyle.Fill
        };
        _lstRandomOptions = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };
        _lstRandomOptions.Items.Add("로딩중...");
        _grpRandomOptions.Controls.Add(_lstRandomOptions);
        mainPanel.Controls.Add(_grpRandomOptions, 0, 5);
        mainPanel.SetColumnSpan(_grpRandomOptions, 2);

        // Row 6: Status and close button
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        _lblStatus = new Label
        {
            Text = "데이터 로딩중...",
            AutoSize = true,
            Margin = new Padding(0, 10, 20, 0)
        };
        _btnClose = new Button
        {
            Text = "닫기",
            Width = 80,
            Height = 30
        };
        _btnClose.Click += (s, e) => Close();
        bottomPanel.Controls.Add(_lblStatus);
        bottomPanel.Controls.Add(_btnClose);
        mainPanel.Controls.Add(bottomPanel, 0, 6);
        mainPanel.SetColumnSpan(bottomPanel, 2);

        Controls.Add(mainPanel);
    }

    private string BuildBasicInfoText()
    {
        var parts = new List<string>
        {
            $"서버: {_item.ServerName}",
            $"유형: {_item.DealTypeDisplay}",
            $"수량: {_item.Quantity}",
            $"상점: {_item.ShopName}"
        };
        if (!string.IsNullOrEmpty(_item.MapName))
        {
            parts.Add($"위치: {_item.MapName}");
        }
        return string.Join("  |  ", parts);
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Load image and stats in parallel (they don't depend on SlotInfo)
            var imageTask = LoadImageAsync();
            var statsTask = LoadPriceStatsAsync();

            // Load detail info FIRST to get SlotInfo (card names)
            // This is needed to extract base item name for kafra.kr search
            await LoadDetailInfoAsync();

            // Now load item description with base item name (card prefixes removed)
            var itemDescTask = LoadItemDescriptionAsync();

            await Task.WhenAll(imageTask, statsTask, itemDescTask);
            _lblStatus.Text = "로딩 완료";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] LoadDataAsync error: {ex.Message}");
            _lblStatus.Text = "일부 데이터 로딩 실패";
        }
    }

    private async Task LoadItemDescriptionAsync()
    {
        try
        {
            // Get base item name (card prefixes removed)
            var baseItemName = _item.GetBaseItemName();
            var originalName = _item.ItemName;

            Debug.WriteLine($"[ItemDetailForm] Original item name: '{originalName}'");
            Debug.WriteLine($"[ItemDetailForm] Base item name (card prefixes removed): '{baseItemName}'");
            Debug.WriteLine($"[ItemDetailForm] Loading item data from kafra.kr for: '{baseItemName}'");

            var kafraClient = new KafraClient();
            try
            {
                // Use GetFullItemDataAsync with base item name (card prefixes removed)
                var kafraItem = await kafraClient.GetFullItemDataAsync(baseItemName);

                // If not found with base name, try original name as fallback
                if (kafraItem == null && baseItemName != originalName)
                {
                    Debug.WriteLine($"[ItemDetailForm] Base name not found, trying original: '{originalName}'");
                    kafraItem = await kafraClient.GetFullItemDataAsync(originalName);
                }

                // If still not found, try direct ID lookup using effective ItemId
                // (either from parser or extracted from image URL)
                var effectiveItemId = _item.GetEffectiveItemId();
                if (kafraItem == null && effectiveItemId.HasValue)
                {
                    Debug.WriteLine($"[ItemDetailForm] Name search failed, trying ID lookup: {effectiveItemId}");
                    Debug.WriteLine($"[ItemDetailForm] Image URL: {_item.ItemImageUrl}");
                    kafraItem = await kafraClient.GetItemByIdAsync(effectiveItemId.Value);
                }

                if (kafraItem != null)
                {
                    if (!IsDisposed)
                    {
                        Invoke(() =>
                        {
                            // Build comprehensive item info from kafra.kr
                            var sb = new StringBuilder();

                            // Item basic info header
                            sb.AppendLine($"[kafra.kr 아이템 정보]");
                            sb.AppendLine(new string('-', 40));

                            // Item metadata
                            var infoLines = new List<string>();
                            infoLines.Add($"분류: {kafraItem.GetTypeDisplayName()}");

                            if (kafraItem.Slots > 0)
                            {
                                infoLines.Add($"슬롯: {kafraItem.Slots}");
                            }

                            if (kafraItem.Weight > 0)
                            {
                                infoLines.Add($"무게: {kafraItem.GetFormattedWeight()}");
                            }

                            var npcBuy = kafraItem.GetFormattedNpcBuyPrice();
                            var npcSell = kafraItem.GetFormattedNpcSellPrice();
                            if (npcBuy != "-" || npcSell != "-")
                            {
                                infoLines.Add($"NPC가격: 구매 {npcBuy} / 판매 {npcSell}");
                            }

                            sb.AppendLine(string.Join("  |  ", infoLines));

                            // Equip jobs if available
                            if (!string.IsNullOrEmpty(kafraItem.EquipJobsText))
                            {
                                sb.AppendLine($"착용직업: {kafraItem.EquipJobsText}");
                            }

                            sb.AppendLine(new string('-', 40));
                            sb.AppendLine();

                            // Item description/effect
                            if (!string.IsNullOrEmpty(kafraItem.ItemText))
                            {
                                sb.AppendLine("[아이템 효과]");
                                sb.AppendLine(kafraItem.ItemText);
                            }
                            else
                            {
                                sb.AppendLine("(아이템 효과 설명 없음)");
                            }

                            _txtItemDesc.Text = sb.ToString().TrimEnd();
                            _grpItemDesc.Text = $"아이템 설명 (kafra.kr)";
                        });
                    }
                }
                else
                {
                    if (!IsDisposed)
                    {
                        Invoke(() =>
                        {
                            _txtItemDesc.Text = "(kafra.kr에서 아이템 정보를 찾을 수 없음)";
                        });
                    }
                }
            }
            finally
            {
                kafraClient.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] Item description load error: {ex.Message}");
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    _txtItemDesc.Text = "kafra.kr 정보 로딩 실패: " + ex.Message;
                });
            }
        }
    }

    private async Task LoadImageAsync()
    {
        if (string.IsNullOrEmpty(_item.ItemImageUrl))
        {
            return;
        }

        try
        {
            var imageUrl = _item.ItemImageUrl;
            if (!imageUrl.StartsWith("http"))
            {
                imageUrl = "https://ro.gnjoy.com" + imageUrl;
            }

            Debug.WriteLine($"[ItemDetailForm] Loading image: {imageUrl}");
            var imageBytes = await _imageClient.GetByteArrayAsync(imageUrl);
            using var ms = new MemoryStream(imageBytes);
            var image = Image.FromStream(ms);

            if (!IsDisposed)
            {
                Invoke(() => _picItem.Image = image);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] Image load error: {ex.Message}");
        }
    }

    private async Task LoadPriceStatsAsync()
    {
        try
        {
            // Use the item name for price lookup
            var searchName = _item.ItemName;
            Debug.WriteLine($"[ItemDetailForm] Loading price stats for: {searchName}, serverId={_item.ServerId}");

            var stats = await _gnjoyClient.FetchPriceHistoryWithFallbackAsync(
                searchName,
                _item.ItemName,
                _item.ServerId);

            if (stats != null)
            {
                _item.ApplyStatistics(stats);

                if (!IsDisposed)
                {
                    Invoke(() =>
                    {
                        _lblYesterdayAvg.Text = "전일평균: " + _item.YesterdayPriceDisplay;
                        _lblWeek7Avg.Text = "7일평균: " + _item.Week7AvgPriceDisplay;
                        _lblPriceCompare.Text = "시세비교: " + _item.PriceCompareDisplay;

                        // Color code price comparison
                        var compare = _item.PriceCompareDisplay;
                        if (compare.StartsWith("+"))
                        {
                            _lblPriceCompare.ForeColor = Color.Red;
                        }
                        else if (compare.StartsWith("-"))
                        {
                            _lblPriceCompare.ForeColor = Color.Blue;
                        }
                    });
                }
            }
            else
            {
                if (!IsDisposed)
                {
                    Invoke(() =>
                    {
                        _lblYesterdayAvg.Text = "전일평균: -";
                        _lblWeek7Avg.Text = "7일평균: -";
                        _lblPriceCompare.Text = "시세비교: -";
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] Price stats error: {ex.Message}");
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    _lblYesterdayAvg.Text = "전일평균: 오류";
                    _lblWeek7Avg.Text = "7일평균: 오류";
                    _lblPriceCompare.Text = "시세비교: 오류";
                });
            }
        }
    }

    private async Task LoadDetailInfoAsync()
    {
        if (!_item.HasDetailParams)
        {
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    _txtSlotInfo.Text = "상세 정보 없음";
                    _lstRandomOptions.Items.Clear();
                    _lstRandomOptions.Items.Add("상세 정보 없음");
                });
            }
            return;
        }

        try
        {
            Debug.WriteLine($"[ItemDetailForm] Loading detail: serverId={_item.ServerId}, mapId={_item.MapId}, ssi={_item.Ssi}");

            var detail = await _gnjoyClient.FetchItemDetailAsync(
                _item.ServerId,
                _item.MapId!.Value,
                _item.Ssi!);

            if (detail != null)
            {
                _item.ApplyDetailInfo(detail);

                if (!IsDisposed)
                {
                    // Build ALL enchant effects text at once
                    var enchantDb = EnchantDatabase.Instance;
                    var sb = new StringBuilder();
                    var slotCount = 0;

                    if (_item.SlotInfo.Count > 0)
                    {
                        foreach (var slot in _item.SlotInfo)
                        {
                            slotCount++;
                            sb.AppendLine($"[{slotCount}] {slot}");
                            sb.AppendLine(new string('-', 40));

                            try
                            {
                                // Fetch full effect text from Kafra API
                                var effect = await enchantDb.GetSlotEffectAsync(slot);
                                if (!string.IsNullOrEmpty(effect))
                                {
                                    sb.AppendLine(effect);
                                }
                                else
                                {
                                    sb.AppendLine("(효과 정보 없음)");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[ItemDetailForm] Effect fetch error for '{slot}': {ex.Message}");
                                sb.AppendLine("(효과 조회 실패)");
                            }

                            sb.AppendLine();
                        }
                    }
                    else
                    {
                        sb.AppendLine("인챈트/카드 없음");
                    }

                    var finalText = sb.ToString().TrimEnd();

                    Invoke(() =>
                    {
                        // Update slot info with ALL effects
                        _txtSlotInfo.Text = finalText;
                        _grpSlotInfo.Text = $"인챈트/카드 효과 ({_item.SlotInfo.Count}개)";

                        // Update random options
                        _lstRandomOptions.Items.Clear();
                        if (_item.RandomOptions.Count > 0)
                        {
                            foreach (var option in _item.RandomOptions)
                            {
                                _lstRandomOptions.Items.Add(option);
                            }
                        }
                        else
                        {
                            _lstRandomOptions.Items.Add("없음");
                        }
                    });
                }
            }
            else
            {
                if (!IsDisposed)
                {
                    Invoke(() =>
                    {
                        _txtSlotInfo.Text = "조회 실패";
                        _lstRandomOptions.Items.Clear();
                        _lstRandomOptions.Items.Add("조회 실패");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ItemDetailForm] Detail load error: {ex.Message}");
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    _txtSlotInfo.Text = "오류: " + ex.Message;
                    _lstRandomOptions.Items.Clear();
                    _lstRandomOptions.Items.Add("오류");
                });
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _imageClient.Dispose();
        base.OnFormClosed(e);
    }
}
