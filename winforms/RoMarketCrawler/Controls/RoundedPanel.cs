using System.Drawing.Drawing2D;

namespace RoMarketCrawler.Controls;

/// <summary>
/// Panel with rounded corners, gradient background, and optional shadow
/// </summary>
public class RoundedPanel : Panel
{
    #region Fields

    private int _cornerRadius = 10;
    private int _shadowDepth = 0;
    private Color _shadowColor = Color.FromArgb(30, 0, 0, 0);

    // Colors
    private Color _panelColor = Color.FromArgb(45, 45, 55);
    private Color _borderColor = Color.FromArgb(60, 65, 75);
    private int _borderWidth = 0;

    // Gradient
    private bool _useGradient = false;
    private Color _gradientStartColor = Color.FromArgb(50, 50, 62);
    private Color _gradientEndColor = Color.FromArgb(40, 40, 50);
    private LinearGradientMode _gradientMode = LinearGradientMode.Vertical;

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
    /// Shadow depth (0 for no shadow)
    /// </summary>
    public int ShadowDepth
    {
        get => _shadowDepth;
        set { _shadowDepth = Math.Max(0, value); Invalidate(); }
    }

    /// <summary>
    /// Shadow color
    /// </summary>
    public Color ShadowColor
    {
        get => _shadowColor;
        set { _shadowColor = value; Invalidate(); }
    }

    /// <summary>
    /// Panel background color
    /// </summary>
    public Color PanelColor
    {
        get => _panelColor;
        set { _panelColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color
    /// </summary>
    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border width (0 for no border)
    /// </summary>
    public int BorderWidth
    {
        get => _borderWidth;
        set { _borderWidth = Math.Max(0, value); Invalidate(); }
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
    /// Gradient start color
    /// </summary>
    public Color GradientStartColor
    {
        get => _gradientStartColor;
        set { _gradientStartColor = value; Invalidate(); }
    }

    /// <summary>
    /// Gradient end color
    /// </summary>
    public Color GradientEndColor
    {
        get => _gradientEndColor;
        set { _gradientEndColor = value; Invalidate(); }
    }

    /// <summary>
    /// Gradient direction
    /// </summary>
    public LinearGradientMode GradientMode
    {
        get => _gradientMode;
        set { _gradientMode = value; Invalidate(); }
    }

    #endregion

    #region Constructor

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
    }

    #endregion

    #region Paint

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Calculate panel rect (account for shadow)
        var panelRect = new Rectangle(
            _shadowDepth > 0 ? 0 : 0,
            0,
            Width - 1 - _shadowDepth,
            Height - 1 - _shadowDepth);

        // Draw shadow if enabled
        if (_shadowDepth > 0)
        {
            DrawShadow(g, panelRect);
        }

        // Create rounded path for panel
        using var path = CreateRoundedRectanglePath(panelRect, _cornerRadius);

        // Fill background
        if (_useGradient && panelRect.Width > 0 && panelRect.Height > 0)
        {
            using var brush = new LinearGradientBrush(panelRect, _gradientStartColor, _gradientEndColor, _gradientMode);
            g.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(_panelColor);
            g.FillPath(brush, path);
        }

        // Draw border if specified
        if (_borderWidth > 0)
        {
            using var pen = new Pen(_borderColor, _borderWidth);
            g.DrawPath(pen, path);
        }

        base.OnPaint(e);
    }

    private void DrawShadow(Graphics g, Rectangle panelRect)
    {
        for (int i = _shadowDepth; i > 0; i--)
        {
            var shadowRect = new Rectangle(
                panelRect.X + i,
                panelRect.Y + i,
                panelRect.Width,
                panelRect.Height);

            // Calculate alpha based on depth (fading out)
            var alpha = (int)((float)(_shadowDepth - i + 1) / _shadowDepth * _shadowColor.A * 0.5f);
            var color = Color.FromArgb(alpha, _shadowColor.R, _shadowColor.G, _shadowColor.B);

            using var path = CreateRoundedRectanglePath(shadowRect, _cornerRadius);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
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

    #region Theme Methods

    /// <summary>
    /// Apply dark theme styling
    /// </summary>
    public void ApplyDarkTheme(Color panelColor, Color borderColor, bool withGradient = false)
    {
        _panelColor = panelColor;
        _borderColor = borderColor;
        _useGradient = withGradient;

        if (withGradient)
        {
            _gradientStartColor = ControlPaint.Light(panelColor, 0.1f);
            _gradientEndColor = ControlPaint.Dark(panelColor, 0.1f);
        }

        Invalidate();
    }

    /// <summary>
    /// Apply card-style (elevated panel with shadow)
    /// </summary>
    public void ApplyCardStyle(Color backgroundColor, int shadowDepth = 4)
    {
        _panelColor = backgroundColor;
        _shadowDepth = shadowDepth;
        _borderWidth = 0;
        _cornerRadius = 12;
        Invalidate();
    }

    #endregion
}
