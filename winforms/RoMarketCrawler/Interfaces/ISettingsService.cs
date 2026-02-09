using RoMarketCrawler.Models;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for application settings service.
/// Provides centralized settings management with persistence.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Event fired when any setting is changed
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// Current font size
    /// </summary>
    float FontSize { get; set; }

    /// <summary>
    /// Current theme type
    /// </summary>
    ThemeType Theme { get; set; }

    /// <summary>
    /// Whether sound is muted
    /// </summary>
    bool IsSoundMuted { get; set; }

    /// <summary>
    /// Selected alarm sound type
    /// </summary>
    AlarmSoundType AlarmSound { get; set; }

    /// <summary>
    /// Alarm interval in seconds
    /// </summary>
    int AlarmIntervalSeconds { get; set; }

    /// <summary>
    /// Deal search history
    /// </summary>
    List<string> DealSearchHistory { get; }

    /// <summary>
    /// Whether to hide the deal search guide popup
    /// </summary>
    bool HideDealSearchGuide { get; set; }

    /// <summary>
    /// Load settings from file
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Save settings to file
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Add item to search history (automatically saves)
    /// </summary>
    void AddToSearchHistory(string searchTerm, int maxItems = 10);

    /// <summary>
    /// Clear search history (automatically saves)
    /// </summary>
    void ClearSearchHistory();
}

/// <summary>
/// Event arguments for setting changes
/// </summary>
public class SettingChangedEventArgs : EventArgs
{
    /// <summary>
    /// Name of the setting that changed
    /// </summary>
    public string SettingName { get; init; } = string.Empty;

    /// <summary>
    /// Old value (may be null)
    /// </summary>
    public object? OldValue { get; init; }

    /// <summary>
    /// New value
    /// </summary>
    public object? NewValue { get; init; }
}
