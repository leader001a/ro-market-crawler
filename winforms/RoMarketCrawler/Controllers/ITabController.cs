using RoMarketCrawler.Models;

namespace RoMarketCrawler.Controllers;

/// <summary>
/// Interface for tab controllers that manage UI tabs
/// </summary>
public interface ITabController : IDisposable
{
    /// <summary>
    /// The TabPage control managed by this controller
    /// </summary>
    TabPage TabPage { get; }

    /// <summary>
    /// Tab display name
    /// </summary>
    string TabName { get; }

    /// <summary>
    /// Initialize the tab UI and components
    /// </summary>
    void Initialize();

    /// <summary>
    /// Apply theme colors to the tab
    /// </summary>
    void ApplyTheme(ThemeColors colors);

    /// <summary>
    /// Update font size across the tab
    /// </summary>
    void UpdateFontSize(float baseFontSize);

    /// <summary>
    /// Called when the tab becomes active (visible)
    /// </summary>
    void OnActivated();

    /// <summary>
    /// Called when the tab becomes inactive (hidden)
    /// </summary>
    void OnDeactivated();

    /// <summary>
    /// Save any pending state before closing
    /// </summary>
    Task SaveStateAsync();
}
