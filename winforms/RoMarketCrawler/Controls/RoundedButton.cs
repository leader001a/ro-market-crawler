using System.Drawing.Drawing2D;

namespace RoMarketCrawler.Controls;

/// <summary>
/// Modern button with rounded corners, gradient background, and smooth hover effects
/// </summary>
public class RoundedButton : Control
{
    #region Fields

    private bool _isHovered = false;
    private bool _isPressed = false;
    private int _cornerRadius = 8;
    private bool _isPrimary = true;

    // Colors
    private Color _normalBackground = Color.FromArgb(70, 130, 200);
    private Color _hoverBackground = Color.FromArgb(90, 150, 220);
    private Color _pressedBackground = Color.FromArgb(50, 110, 180);
    private Color _disabledBackground = Color.FromArgb(80, 80, 90);
    private Color _normalForeColor = Color.White;
    private Color _disabledForeColor = Color.FromArgb(140, 140, 150);
    private Color _borderColor = Color.Transparent;

    // Gradient
    private bool _useGradient = true;
    private Color _gradientEndColor = Color.Empty;

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
    /// Whether this is a primary (accent) button or secondary button
    /// </summary>
    public bool IsPrimary
    {
        get => _isPrimary;
        set { _isPrimary = value; Invalidate(); }
    }

    /// <summary>
    /// Background color in normal state
    /// </summary>
    public Color NormalBackground
    {
        get => _normalBackground;
        set { _normalBackground = value; Invalidate(); }
    }

    /// <summary>
    /// Background color when hovered
    /// </summary>
    public Color HoverBackground
    {
        get => _hoverBackground;
        set { _hoverBackground = value; Invalidate(); }
    }

    /// <summary>
    /// Background color when pressed
    /// </summary>
    public Color PressedBackground
    {
        get => _pressedBackground;
        set { _pressedBackground = value; Invalidate(); }
    }

    /// <summary>
    /// Background color when disabled
    /// </summary>
    public Color DisabledBackground
    {
        get => _disabledBackground;
        set { _disabledBackground = value; Invalidate(); }
    }

    /// <summary>
    /// Text color in normal state
    /// </summary>
    public Color NormalForeColor
    {
        get => _normalForeColor;
        set { _normalForeColor = value; Invalidate(); }
    }

    /// <summary>
    /// Text color when disabled
    /// </summary>
    public Color DisabledForeColor
    {
        get => _disabledForeColor;
        set { _disabledForeColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color (set to Transparent for no border)
    /// </summary>
    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Whether to use gradient background
    /// </summary>
    public bool UseGradient
    {
        get => _useGradient;
        set { _useGradient = value; Invalidate(); }
    }

    /// <summary>
    /// End color for gradient (leave empty for auto-calculated)
    /// </summary>
    public Color GradientEndColor
    {
        get => _gradientEndColor;
        set { _gradientEndColor = value; Invalidate(); }
    }

    #endregion

    #region Constructor

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Malgun Gothic", 9, FontStyle.Bold);
        Size = new Size(100, 32);
        Cursor = Cursors.Hand;
    }

    #endregion

    #region Paint

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // Determine current background color
        Color bgColor;
        if (!Enabled)
            bgColor = _disabledBackground;
        else if (_isPressed)
            bgColor = _pressedBackground;
        else if (_isHovered)
            bgColor = _hoverBackground;
        else
            bgColor = _normalBackground;

        // Create rounded path
        using var path = CreateRoundedRectanglePath(rect, _cornerRadius);

        // Fill background
        if (_useGradient && Enabled)
        {
            var gradientEnd = _gradientEndColor.IsEmpty
                ? ControlPaint.Light(bgColor, 0.15f)
                : _gradientEndColor;

            using var brush = new LinearGradientBrush(rect, bgColor, gradientEnd, LinearGradientMode.Vertical);
            g.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(bgColor);
            g.FillPath(brush, path);
        }

        // Draw border if specified
        if (_borderColor != Color.Transparent && _borderColor != Color.Empty)
        {
            using var pen = new Pen(_borderColor, 1);
            g.DrawPath(pen, path);
        }

        // Draw text
        var textColor = Enabled ? _normalForeColor : _disabledForeColor;
        TextRenderer.DrawText(g, Text, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    #endregion

    #region Theme Methods

    /// <summary>
    /// Apply primary button style (accent color)
    /// </summary>
    public void ApplyPrimaryStyle(Color accent, Color accentHover, Color accentPressed, Color accentText)
    {
        _isPrimary = true;
        _normalBackground = accent;
        _hoverBackground = accentHover;
        _pressedBackground = accentPressed;
        _normalForeColor = accentText;
        _borderColor = Color.Transparent;
        Invalidate();
    }

    /// <summary>
    /// Apply secondary button style (muted color)
    /// </summary>
    public void ApplySecondaryStyle(Color background, Color hover, Color pressed, Color text, Color border)
    {
        _isPrimary = false;
        _normalBackground = background;
        _hoverBackground = hover;
        _pressedBackground = pressed;
        _normalForeColor = text;
        _borderColor = border;
        Invalidate();
    }

    #endregion
}
