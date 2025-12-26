namespace RoMarketCrawler.Controls;

/// <summary>
/// Dark theme ToolStrip renderer for consistent styling across the application
/// </summary>
public class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkToolStripColorTable()) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var color = e.Item.Selected ? Color.FromArgb(60, 60, 65) : Color.Transparent;
        if (e.Item.Pressed) color = Color.FromArgb(70, 70, 75);

        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var color = e.Item.Selected ? Color.FromArgb(60, 60, 65) : Color.Transparent;
        if (e.Item.Pressed) color = Color.FromArgb(70, 70, 75);

        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.FromArgb(220, 220, 220);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var center = rect.Width / 2;
        using var pen = new Pen(Color.FromArgb(70, 70, 75));
        e.Graphics.DrawLine(pen, center, 4, center, rect.Height - 4);
    }
}

/// <summary>
/// Color table for dark theme ToolStrip styling
/// </summary>
public class DarkToolStripColorTable : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Color.FromArgb(45, 45, 55);
    public override Color ToolStripGradientMiddle => Color.FromArgb(45, 45, 55);
    public override Color ToolStripGradientEnd => Color.FromArgb(45, 45, 55);
    public override Color ToolStripBorder => Color.FromArgb(60, 60, 70);

    public override Color ButtonSelectedHighlight => Color.FromArgb(60, 60, 75);
    public override Color ButtonSelectedGradientBegin => Color.FromArgb(55, 55, 70);
    public override Color ButtonSelectedGradientMiddle => Color.FromArgb(55, 55, 70);
    public override Color ButtonSelectedGradientEnd => Color.FromArgb(55, 55, 70);
    public override Color ButtonSelectedBorder => Color.FromArgb(70, 130, 200);

    public override Color ButtonPressedGradientBegin => Color.FromArgb(50, 50, 65);
    public override Color ButtonPressedGradientMiddle => Color.FromArgb(50, 50, 65);
    public override Color ButtonPressedGradientEnd => Color.FromArgb(50, 50, 65);
    public override Color ButtonPressedBorder => Color.FromArgb(70, 130, 200);

    public override Color ButtonCheckedGradientBegin => Color.FromArgb(60, 60, 75);
    public override Color ButtonCheckedGradientMiddle => Color.FromArgb(60, 60, 75);
    public override Color ButtonCheckedGradientEnd => Color.FromArgb(60, 60, 75);

    public override Color MenuItemSelected => Color.FromArgb(60, 60, 70);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 70);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 70);
    public override Color MenuItemBorder => Color.FromArgb(70, 130, 200);
    public override Color MenuBorder => Color.FromArgb(55, 55, 65);

    public override Color ToolStripDropDownBackground => Color.FromArgb(40, 40, 48);
    public override Color ImageMarginGradientBegin => Color.FromArgb(40, 40, 48);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(40, 40, 48);
    public override Color ImageMarginGradientEnd => Color.FromArgb(40, 40, 48);

    public override Color SeparatorDark => Color.FromArgb(55, 55, 65);
    public override Color SeparatorLight => Color.FromArgb(70, 70, 80);

    public override Color GripDark => Color.FromArgb(55, 55, 65);
    public override Color GripLight => Color.FromArgb(75, 75, 85);
}
