using RoMarketCrawler.Models;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for theme management
/// </summary>
public interface IThemeManager
{
    /// <summary>
    /// Event fired when theme changes
    /// </summary>
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>
    /// Current theme type
    /// </summary>
    ThemeType CurrentTheme { get; }

    /// <summary>
    /// Current theme colors
    /// </summary>
    ThemeColors Colors { get; }

    /// <summary>
    /// Set the application theme
    /// </summary>
    void SetTheme(ThemeType theme);
}

/// <summary>
/// Event arguments for theme changes
/// </summary>
public class ThemeChangedEventArgs : EventArgs
{
    public ThemeType OldTheme { get; init; }
    public ThemeType NewTheme { get; init; }
    public ThemeColors Colors { get; init; } = ThemeColors.Dark;
}
