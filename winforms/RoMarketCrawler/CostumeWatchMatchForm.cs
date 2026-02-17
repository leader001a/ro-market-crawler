using System.Diagnostics;
using System.Runtime.InteropServices;
using RoMarketCrawler.Models;

namespace RoMarketCrawler;

/// <summary>
/// Modal popup showing matched items for a specific costume watch condition
/// </summary>
public class CostumeWatchMatchForm : Form
{
    // Windows Dark Mode API
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly CostumeWatchItem _watchItem;
    private readonly List<DealItem> _matches;
    private readonly ThemeType _theme;
    private readonly float _baseFontSize;
    private readonly ThemeColors _colors;

    private DataGridView _dgvMatches = null!;
    private Label _lblStatus = null!;

    /// <summary>
    /// Raised when an item detail form needs to be shown
    /// </summary>
    public event EventHandler<DealItem>? ShowItemDetail;

    public CostumeWatchMatchForm(
        CostumeWatchItem watchItem,
        List<DealItem> matches,
        ThemeType theme,
        float baseFontSize)
    {
        _watchItem = watchItem;
        _matches = matches;
        _theme = theme;
        _baseFontSize = baseFontSize;
        _colors = ThemeColors.ForTheme(theme);

        InitializeUI();

        Load += (s, e) =>
        {
            if (_theme == ThemeType.Dark)
                ApplyDarkModeToForm();
        };
    }

    private void ApplyDarkModeToForm()
    {
        if (IsHandleCreated)
        {
            int darkMode = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }
    }

    private void InitializeUI()
    {
        // Form properties
        var stonePart = string.IsNullOrWhiteSpace(_watchItem.StoneName) ? "" : _watchItem.StoneName;
        var itemPart = string.IsNullOrWhiteSpace(_watchItem.ItemName) ? "" : _watchItem.ItemName;
        Text = $"감시 매칭: {stonePart} / {itemPart} ≤ {_watchItem.WatchPrice:N0}z";

        Size = new Size(900, 450);
        MinimumSize = new Size(700, 350);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = _colors.Background;

        LoadTitleBarIcon();

        // Header label
        var headerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_watchItem.StoneName))
            headerParts.Add($"스톤: {_watchItem.StoneName}");
        if (!string.IsNullOrWhiteSpace(_watchItem.ItemName))
            headerParts.Add($"의상: {_watchItem.ItemName}");
        headerParts.Add($"감시가: {_watchItem.WatchPrice:N0}z");

        var lblHeader = new Label
        {
            Text = $"감시 조건: {string.Join(" | ", headerParts)}",
            Dock = DockStyle.Top,
            Height = (int)(30 * _baseFontSize / 12f),
            ForeColor = _colors.Text,
            BackColor = _colors.Panel,
            Font = new Font("Malgun Gothic", _baseFontSize),
            Padding = new Padding(8, 4, 8, 4),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // DataGridView
        _dgvMatches = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
        };
        ApplyDataGridViewStyle(_dgvMatches);
        SetupGridColumns(_dgvMatches);

        _dgvMatches.CellFormatting += DgvMatches_CellFormatting;
        _dgvMatches.CellDoubleClick += DgvMatches_CellDoubleClick;

        _dgvMatches.DataSource = new BindingSource { DataSource = _matches };

        // Status label
        _lblStatus = new Label
        {
            Text = $"상태: {_matches.Count}건 표시",
            Dock = DockStyle.Bottom,
            Height = (int)(24 * _baseFontSize / 12f),
            ForeColor = _colors.TextMuted,
            BackColor = _colors.Panel,
            Font = new Font("Malgun Gothic", _baseFontSize - 1),
            Padding = new Padding(8, 2, 8, 2),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Add controls (Dock order: last added docks first)
        Controls.Add(_dgvMatches);
        Controls.Add(_lblStatus);
        Controls.Add(lblHeader);
    }

    private void SetupGridColumns(DataGridView dgv)
    {
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn
            {
                Name = "DealTypeDisplay",
                HeaderText = "유형",
                DataPropertyName = "DealTypeDisplay",
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 7,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "DisplayName",
                HeaderText = "아이템",
                DataPropertyName = "DisplayName",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 30
            },
            new DataGridViewTextBoxColumn
            {
                Name = "SlotAndOptionsDisplay",
                HeaderText = "스톤",
                DataPropertyName = "SlotAndOptionsDisplay",
                MinimumWidth = 100,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 25,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "수량",
                DataPropertyName = "Quantity",
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 7,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "PriceFormatted",
                HeaderText = "가격",
                DataPropertyName = "PriceFormatted",
                MinimumWidth = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 12,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
            },
            new DataGridViewTextBoxColumn
            {
                Name = "ShopName",
                HeaderText = "상점명",
                DataPropertyName = "ShopName",
                MinimumWidth = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 12
            },
            new DataGridViewTextBoxColumn
            {
                Name = "CrawledPage",
                HeaderText = "P",
                DataPropertyName = "CrawledPage",
                MinimumWidth = 30,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 5,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter },
                ToolTipText = "수집 페이지 번호"
            }
        });
    }

    private void ApplyDataGridViewStyle(DataGridView dgv)
    {
        dgv.BackgroundColor = _colors.Grid;
        dgv.GridColor = _colors.Border;
        dgv.DefaultCellStyle.BackColor = _colors.Grid;
        dgv.DefaultCellStyle.ForeColor = _colors.Text;
        dgv.DefaultCellStyle.SelectionBackColor = _colors.Accent;
        dgv.DefaultCellStyle.SelectionForeColor = _colors.AccentText;
        dgv.DefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize);
        dgv.AlternatingRowsDefaultCellStyle.BackColor = _colors.GridAlt;
        dgv.ColumnHeadersDefaultCellStyle.BackColor = _colors.Panel;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = _colors.Text;
        dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 1, FontStyle.Bold);
        dgv.EnableHeadersVisualStyles = false;

        if (_theme == ThemeType.Dark)
        {
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        }

        dgv.BorderStyle = BorderStyle.None;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    }

    private void DgvMatches_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle == null || e.RowIndex < 0) return;

        var columnName = _dgvMatches.Columns[e.ColumnIndex].Name;

        if (columnName == "DealTypeDisplay" && e.Value != null)
        {
            var value = e.Value.ToString();
            if (value == "판매")
                e.CellStyle.ForeColor = _colors.SaleColor;
            else if (value == "구매")
                e.CellStyle.ForeColor = _colors.BuyColor;
        }
    }

    private void DgvMatches_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _matches.Count) return;

        var selectedItem = _matches[e.RowIndex];
        ShowItemDetail?.Invoke(this, selectedItem);
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
            Debug.WriteLine($"[CostumeWatchMatchForm] Failed to load icon: {ex.Message}");
        }
    }
}
