using System.Drawing.Drawing2D;

namespace RoMarketCrawler.Controls;

/// <summary>
/// Modern ToolStrip renderer with gradient backgrounds and smooth styling
/// </summary>
public class ModernToolStripRenderer : ToolStripProfessionalRenderer
{
    private readonly Color _backgroundStart;
    private readonly Color _backgroundEnd;
    private readonly Color _borderColor;
    private readonly Color _buttonHoverBackground;
    private readonly Color _buttonPressedBackground;
    private readonly Color _separatorColor;

    public ModernToolStripRenderer(
        Color backgroundStart,
        Color backgroundEnd,
        Color borderColor,
        Color buttonHoverBackground,
        Color buttonPressedBackground,
        Color separatorColor)
        : base(new ModernColorTable(backgroundStart, backgroundEnd, borderColor, buttonHoverBackground, buttonPressedBackground, separatorColor))
    {
        _backgroundStart = backgroundStart;
        _backgroundEnd = backgroundEnd;
        _borderColor = borderColor;
        _buttonHoverBackground = buttonHoverBackground;
        _buttonPressedBackground = buttonPressedBackground;
        _separatorColor = separatorColor;

        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var rect = e.AffectedBounds;

        if (rect.Width > 0 && rect.Height > 0)
        {
            using var brush = new LinearGradientBrush(rect, _backgroundStart, _backgroundEnd, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(2, 2, e.Item.Width - 4, e.Item.Height - 4);

        if (e.Item.Selected || e.Item.Pressed)
        {
            var bgColor = e.Item.Pressed ? _buttonPressedBackground : _buttonHoverBackground;
            using var path = CreateRoundedRectangle(bounds, 6);
            using var brush = new SolidBrush(bgColor);
            g.FillPath(brush, path);
        }
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderButtonBackground(e);
    }

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderButtonBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        var bounds = e.Item.Bounds;

        if (e.Vertical)
        {
            var x = bounds.Width / 2;
            using var pen = new Pen(_separatorColor);
            g.DrawLine(pen, x, 4, x, bounds.Height - 4);
        }
        else
        {
            var y = bounds.Height / 2;
            using var pen = new Pen(_separatorColor);
            g.DrawLine(pen, 4, y, bounds.Width - 4, y);
        }
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Draw bottom border only
        using var pen = new Pen(_borderColor);
        e.Graphics.DrawLine(pen, 0, e.AffectedBounds.Height - 1, e.AffectedBounds.Width, e.AffectedBounds.Height - 1);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = radius * 2;

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

/// <summary>
/// Color table for modern ToolStrip renderer
/// </summary>
public class ModernColorTable : ProfessionalColorTable
{
    private readonly Color _backgroundStart;
    private readonly Color _backgroundEnd;
    private readonly Color _borderColor;
    private readonly Color _buttonHoverBackground;
    private readonly Color _buttonPressedBackground;
    private readonly Color _separatorColor;

    public ModernColorTable(
        Color backgroundStart,
        Color backgroundEnd,
        Color borderColor,
        Color buttonHoverBackground,
        Color buttonPressedBackground,
        Color separatorColor)
    {
        _backgroundStart = backgroundStart;
        _backgroundEnd = backgroundEnd;
        _borderColor = borderColor;
        _buttonHoverBackground = buttonHoverBackground;
        _buttonPressedBackground = buttonPressedBackground;
        _separatorColor = separatorColor;
    }

    public override Color ToolStripGradientBegin => _backgroundStart;
    public override Color ToolStripGradientMiddle => Color.FromArgb(
        (_backgroundStart.R + _backgroundEnd.R) / 2,
        (_backgroundStart.G + _backgroundEnd.G) / 2,
        (_backgroundStart.B + _backgroundEnd.B) / 2);
    public override Color ToolStripGradientEnd => _backgroundEnd;

    public override Color ToolStripBorder => _borderColor;

    public override Color ButtonSelectedHighlight => _buttonHoverBackground;
    public override Color ButtonSelectedHighlightBorder => Color.Transparent;
    public override Color ButtonPressedHighlight => _buttonPressedBackground;
    public override Color ButtonPressedHighlightBorder => Color.Transparent;
    public override Color ButtonCheckedHighlight => _buttonHoverBackground;
    public override Color ButtonCheckedHighlightBorder => Color.Transparent;
    public override Color ButtonSelectedGradientBegin => _buttonHoverBackground;
    public override Color ButtonSelectedGradientMiddle => _buttonHoverBackground;
    public override Color ButtonSelectedGradientEnd => _buttonHoverBackground;
    public override Color ButtonSelectedBorder => Color.Transparent;
    public override Color ButtonPressedGradientBegin => _buttonPressedBackground;
    public override Color ButtonPressedGradientMiddle => _buttonPressedBackground;
    public override Color ButtonPressedGradientEnd => _buttonPressedBackground;
    public override Color ButtonPressedBorder => Color.Transparent;

    public override Color SeparatorDark => _separatorColor;
    public override Color SeparatorLight => Color.Transparent;

    public override Color GripDark => _separatorColor;
    public override Color GripLight => Color.Transparent;
}

/// <summary>
/// Factory for creating theme-specific ToolStrip renderers
/// </summary>
public static class ToolStripRendererFactory
{
    /// <summary>
    /// Create a modern dark theme ToolStrip renderer
    /// </summary>
    public static ModernToolStripRenderer CreateDarkRenderer()
    {
        return new ModernToolStripRenderer(
            backgroundStart: Color.FromArgb(52, 52, 62),
            backgroundEnd: Color.FromArgb(42, 42, 52),
            borderColor: Color.FromArgb(60, 65, 75),
            buttonHoverBackground: Color.FromArgb(70, 75, 90),
            buttonPressedBackground: Color.FromArgb(60, 65, 80),
            separatorColor: Color.FromArgb(70, 75, 85)
        );
    }

    /// <summary>
    /// Create a modern light theme ToolStrip renderer
    /// </summary>
    public static ModernToolStripRenderer CreateLightRenderer()
    {
        return new ModernToolStripRenderer(
            backgroundStart: Color.FromArgb(248, 248, 250),
            backgroundEnd: Color.FromArgb(240, 240, 245),
            borderColor: Color.FromArgb(210, 210, 215),
            buttonHoverBackground: Color.FromArgb(225, 225, 230),
            buttonPressedBackground: Color.FromArgb(210, 210, 220),
            separatorColor: Color.FromArgb(200, 200, 205)
        );
    }
}
