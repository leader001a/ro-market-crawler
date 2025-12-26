namespace RoMarketCrawler.Models;

/// <summary>
/// Theme color palette for the application
/// </summary>
public class ThemeColors
{
    public Color Background { get; init; }
    public Color Panel { get; init; }
    public Color Grid { get; init; }
    public Color GridAlt { get; init; }
    public Color Accent { get; init; }
    public Color AccentHover { get; init; }
    public Color AccentText { get; init; }
    public Color Text { get; init; }
    public Color TextMuted { get; init; }
    public Color LinkColor { get; init; }
    public Color Border { get; init; }
    public Color SaleColor { get; init; }
    public Color BuyColor { get; init; }

    /// <summary>
    /// Dark theme color palette
    /// </summary>
    public static ThemeColors Dark => new()
    {
        Background = Color.FromArgb(30, 30, 35),
        Panel = Color.FromArgb(45, 45, 55),
        Grid = Color.FromArgb(35, 35, 42),
        GridAlt = Color.FromArgb(45, 45, 55),
        Accent = Color.FromArgb(70, 130, 200),
        AccentHover = Color.FromArgb(90, 150, 220),
        AccentText = Color.White,
        Text = Color.FromArgb(230, 230, 235),
        TextMuted = Color.FromArgb(160, 160, 170),
        LinkColor = Color.FromArgb(100, 180, 255),
        Border = Color.FromArgb(70, 75, 90),
        SaleColor = Color.FromArgb(100, 200, 120),
        BuyColor = Color.FromArgb(255, 180, 80)
    };

    /// <summary>
    /// Classic (light) theme color palette
    /// </summary>
    public static ThemeColors Classic => new()
    {
        Background = SystemColors.Control,
        Panel = SystemColors.Control,
        Grid = SystemColors.Window,
        GridAlt = Color.FromArgb(240, 240, 245),
        Accent = SystemColors.Highlight,
        AccentHover = SystemColors.HotTrack,
        AccentText = SystemColors.HighlightText,
        Text = SystemColors.WindowText,
        TextMuted = SystemColors.GrayText,
        LinkColor = Color.FromArgb(0, 102, 204),
        Border = SystemColors.ActiveBorder,
        SaleColor = Color.FromArgb(0, 128, 0),
        BuyColor = Color.FromArgb(180, 100, 0)
    };

    /// <summary>
    /// Get colors for the specified theme type
    /// </summary>
    public static ThemeColors ForTheme(ThemeType theme) =>
        theme == ThemeType.Dark ? Dark : Classic;
}
