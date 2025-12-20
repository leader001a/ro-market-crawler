using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace RoMarketCrawler.Controls;

/// <summary>
/// ComboBox with rounded corners and modern styling
/// Uses owner-draw for custom appearance while maintaining full functionality
/// </summary>
public class RoundedComboBox : ComboBox
{
    #region Fields

    private int _cornerRadius = 8;
    private bool _isHovered = false;

    // Colors
    private Color _backgroundColor = Color.FromArgb(50, 50, 60);
    private Color _borderColor = Color.FromArgb(70, 75, 90);
    private Color _hoverBorderColor = Color.FromArgb(90, 95, 110);
    private Color _focusBorderColor = Color.FromArgb(70, 130, 200);
    private Color _dropdownArrowColor = Color.FromArgb(180, 180, 190);
    private Color _textColor = Color.FromArgb(230, 230, 235);

    // Dropdown colors
    private Color _dropdownBackground = Color.FromArgb(45, 45, 55);
    private Color _dropdownItemHoverBackground = Color.FromArgb(70, 130, 200);
    private Color _dropdownItemHoverForeground = Color.White;

    #endregion

    #region Properties

    /// <summary>
    /// Corner radius in pixels
    /// </summary>
    public int CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = Math.Max(0, value); Invalidate(); }
    }

    /// <summary>
    /// Background color
    /// </summary>
    public Color BackgroundColor
    {
        get => _backgroundColor;
        set { _backgroundColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color in normal state
    /// </summary>
    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color when hovered
    /// </summary>
    public Color HoverBorderColor
    {
        get => _hoverBorderColor;
        set { _hoverBorderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color when focused
    /// </summary>
    public Color FocusBorderColor
    {
        get => _focusBorderColor;
        set { _focusBorderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Color of the dropdown arrow
    /// </summary>
    public Color DropdownArrowColor
    {
        get => _dropdownArrowColor;
        set { _dropdownArrowColor = value; Invalidate(); }
    }

    /// <summary>
    /// Text color
    /// </summary>
    public Color TextColor
    {
        get => _textColor;
        set { _textColor = value; Invalidate(); }
    }

    /// <summary>
    /// Dropdown background color
    /// </summary>
    public Color DropdownBackground
    {
        get => _dropdownBackground;
        set { _dropdownBackground = value; }
    }

    /// <summary>
    /// Dropdown item hover background
    /// </summary>
    public Color DropdownItemHoverBackground
    {
        get => _dropdownItemHoverBackground;
        set { _dropdownItemHoverBackground = value; }
    }

    /// <summary>
    /// Dropdown item hover foreground
    /// </summary>
    public Color DropdownItemHoverForeground
    {
        get => _dropdownItemHoverForeground;
        set { _dropdownItemHoverForeground = value; }
    }

    #endregion

    #region Constructor

    public RoundedComboBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);

        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        Font = new Font("Malgun Gothic", 9);
        ItemHeight = 24;
    }

    #endregion

    #region Paint

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // Create rounded path
        using var path = CreateRoundedRectanglePath(rect, _cornerRadius);

        // Fill background
        using (var brush = new SolidBrush(_backgroundColor))
        {
            g.FillPath(brush, path);
        }

        // Draw border
        var borderColor = Focused ? _focusBorderColor : (_isHovered ? _hoverBorderColor : _borderColor);
        using (var pen = new Pen(borderColor, 1))
        {
            g.DrawPath(pen, path);
        }

        // Draw selected text
        var textRect = new Rectangle(8, 0, Width - 28, Height);
        var displayText = SelectedItem?.ToString() ?? string.Empty;

        // Handle DisplayMember
        if (!string.IsNullOrEmpty(DisplayMember) && SelectedItem != null)
        {
            var prop = SelectedItem.GetType().GetProperty(DisplayMember);
            if (prop != null)
            {
                displayText = prop.GetValue(SelectedItem)?.ToString() ?? string.Empty;
            }
        }

        TextRenderer.DrawText(g, displayText, Font, textRect,
            Enabled ? _textColor : Color.FromArgb(120, 120, 130),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // Draw dropdown arrow
        DrawDropdownArrow(g, Width - 20, Height / 2);
    }

    private void DrawDropdownArrow(Graphics g, int x, int y)
    {
        var arrowColor = Enabled ? _dropdownArrowColor : Color.FromArgb(100, 100, 110);
        using var pen = new Pen(arrowColor, 2);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        // Draw V-shaped arrow
        var points = new Point[]
        {
            new Point(x - 4, y - 2),
            new Point(x, y + 2),
            new Point(x + 4, y - 2)
        };
        g.DrawLines(pen, points);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = e.Bounds;
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        // Draw background
        var bgColor = isSelected ? _dropdownItemHoverBackground : _dropdownBackground;
        using (var brush = new SolidBrush(bgColor))
        {
            g.FillRectangle(brush, bounds);
        }

        // Get display text
        var item = Items[e.Index];
        var displayText = item?.ToString() ?? string.Empty;

        if (!string.IsNullOrEmpty(DisplayMember) && item != null)
        {
            var prop = item.GetType().GetProperty(DisplayMember);
            if (prop != null)
            {
                displayText = prop.GetValue(item)?.ToString() ?? string.Empty;
            }
        }

        // Draw text
        var textColor = isSelected ? _dropdownItemHoverForeground : _textColor;
        var textRect = new Rectangle(bounds.X + 8, bounds.Y, bounds.Width - 16, bounds.Height);
        TextRenderer.DrawText(g, displayText, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int diameter = radius * 2;
        var arcRect = new Rectangle(rect.X, rect.Y, diameter, diameter);

        // Top-left arc
        path.AddArc(arcRect, 180, 90);

        // Top-right arc
        arcRect.X = rect.Right - diameter;
        path.AddArc(arcRect, 270, 90);

        // Bottom-right arc
        arcRect.Y = rect.Bottom - diameter;
        path.AddArc(arcRect, 0, 90);

        // Bottom-left arc
        arcRect.X = rect.Left;
        path.AddArc(arcRect, 90, 90);

        path.CloseFigure();
        return path;
    }

    #endregion

    #region Mouse Events

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    #endregion

    #region Theme Methods

    /// <summary>
    /// Apply dark theme colors
    /// </summary>
    public void ApplyDarkTheme(Color background, Color border, Color focusBorder, Color text, Color accent)
    {
        _backgroundColor = background;
        _borderColor = border;
        _hoverBorderColor = ControlPaint.Light(border, 0.2f);
        _focusBorderColor = focusBorder;
        _textColor = text;
        _dropdownArrowColor = Color.FromArgb(180, 180, 190);

        _dropdownBackground = ControlPaint.Light(background, 0.1f);
        _dropdownItemHoverBackground = accent;
        _dropdownItemHoverForeground = Color.White;

        Invalidate();
    }

    /// <summary>
    /// Apply light theme colors
    /// </summary>
    public void ApplyLightTheme()
    {
        _backgroundColor = SystemColors.Window;
        _borderColor = SystemColors.ActiveBorder;
        _hoverBorderColor = SystemColors.ControlDark;
        _focusBorderColor = SystemColors.Highlight;
        _textColor = SystemColors.WindowText;
        _dropdownArrowColor = SystemColors.ControlDark;

        _dropdownBackground = SystemColors.Window;
        _dropdownItemHoverBackground = SystemColors.Highlight;
        _dropdownItemHoverForeground = SystemColors.HighlightText;

        Invalidate();
    }

    #endregion
}
