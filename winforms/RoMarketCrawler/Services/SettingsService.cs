using System.Diagnostics;
using System.Text.Json;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Settings service for managing application settings with persistence.
/// Thread-safe implementation with event notifications.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly object _lock = new();
    private AppSettings _settings = new();
    private bool _isDirty = false;

    /// <inheritdoc/>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public SettingsService(string? dataDirectory = null)
    {
        var dataDir = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RoMarketCrawler");
        Directory.CreateDirectory(dataDir);
        _settingsFilePath = Path.Combine(dataDir, "settings.json");
    }

    #region Properties

    /// <inheritdoc/>
    public float FontSize
    {
        get { lock (_lock) return _settings.FontSize; }
        set => SetFontSize(value);
    }

    /// <inheritdoc/>
    public ThemeType Theme
    {
        get { lock (_lock) return _settings.Theme; }
        set => SetTheme(value);
    }

    /// <inheritdoc/>
    public bool IsSoundMuted
    {
        get { lock (_lock) return _settings.IsSoundMuted; }
        set => SetIsSoundMuted(value);
    }

    /// <inheritdoc/>
    public AlarmSoundType AlarmSound
    {
        get { lock (_lock) return _settings.AlarmSound; }
        set => SetAlarmSound(value);
    }

    /// <inheritdoc/>
    public int AlarmIntervalSeconds
    {
        get { lock (_lock) return _settings.AlarmIntervalSeconds; }
        set => SetAlarmIntervalSeconds(value);
    }

    private void SetFontSize(float value)
    {
        float oldValue;
        lock (_lock)
        {
            if (_settings.FontSize == value) return;
            oldValue = _settings.FontSize;
            _settings.FontSize = value;
            _isDirty = true;
        }
        RaiseSettingChanged(nameof(FontSize), oldValue, value);
    }

    private void SetTheme(ThemeType value)
    {
        ThemeType oldValue;
        lock (_lock)
        {
            if (_settings.Theme == value) return;
            oldValue = _settings.Theme;
            _settings.Theme = value;
            _isDirty = true;
        }
        RaiseSettingChanged(nameof(Theme), oldValue, value);
    }

    private void SetIsSoundMuted(bool value)
    {
        bool oldValue;
        lock (_lock)
        {
            if (_settings.IsSoundMuted == value) return;
            oldValue = _settings.IsSoundMuted;
            _settings.IsSoundMuted = value;
            _isDirty = true;
        }
        RaiseSettingChanged(nameof(IsSoundMuted), oldValue, value);
    }

    private void SetAlarmSound(AlarmSoundType value)
    {
        AlarmSoundType oldValue;
        lock (_lock)
        {
            if (_settings.AlarmSound == value) return;
            oldValue = _settings.AlarmSound;
            _settings.AlarmSound = value;
            _isDirty = true;
        }
        RaiseSettingChanged(nameof(AlarmSound), oldValue, value);
    }

    private void SetAlarmIntervalSeconds(int value)
    {
        int oldValue;
        lock (_lock)
        {
            if (_settings.AlarmIntervalSeconds == value) return;
            oldValue = _settings.AlarmIntervalSeconds;
            _settings.AlarmIntervalSeconds = value;
            _isDirty = true;
        }
        RaiseSettingChanged(nameof(AlarmIntervalSeconds), oldValue, value);
    }

    /// <inheritdoc/>
    public List<string> DealSearchHistory
    {
        get
        {
            lock (_lock)
            {
                return new List<string>(_settings.DealSearchHistory);
            }
        }
    }

    /// <inheritdoc/>
    public bool HideDealSearchGuide
    {
        get { lock (_lock) return _settings.HideDealSearchGuide; }
        set => SetHideDealSearchGuide(value);
    }

    private void SetHideDealSearchGuide(bool value)
    {
        bool oldValue;
        lock (_lock)
        {
            if (_settings.HideDealSearchGuide == value) return;
            oldValue = _settings.HideDealSearchGuide;
            _settings.HideDealSearchGuide = value;
            _isDirty = true;
        }
        RaiseSettingChanged(nameof(HideDealSearchGuide), oldValue, value);
    }

    #endregion

    #region Methods

    /// <inheritdoc/>
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                Debug.WriteLine($"[SettingsService] Settings file not found at {_settingsFilePath}");
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings != null)
            {
                lock (_lock)
                {
                    _settings = settings;
                    _isDirty = false;
                }
                Debug.WriteLine($"[SettingsService] Loaded settings: FontSize={settings.FontSize}, Theme={settings.Theme}, " +
                    $"SearchHistory={settings.DealSearchHistory?.Count ?? 0} items");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Failed to load settings: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync()
    {
        try
        {
            AppSettings settingsCopy;
            lock (_lock)
            {
                if (!_isDirty)
                {
                    Debug.WriteLine("[SettingsService] No changes to save");
                    return;
                }
                settingsCopy = new AppSettings
                {
                    FontSize = _settings.FontSize,
                    Theme = _settings.Theme,
                    IsSoundMuted = _settings.IsSoundMuted,
                    AlarmSound = _settings.AlarmSound,
                    AlarmIntervalSeconds = _settings.AlarmIntervalSeconds,
                    DealSearchHistory = new List<string>(_settings.DealSearchHistory),
                    HideDealSearchGuide = _settings.HideDealSearchGuide
                };
                _isDirty = false;
            }

            var json = JsonSerializer.Serialize(settingsCopy, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsFilePath, json);
            Debug.WriteLine($"[SettingsService] Saved settings to {_settingsFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsService] Failed to save settings: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void AddToSearchHistory(string searchTerm, int maxItems = 10)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return;

        searchTerm = searchTerm.Trim();

        lock (_lock)
        {
            // Remove if already exists (to move to front)
            _settings.DealSearchHistory.Remove(searchTerm);

            // Add to front
            _settings.DealSearchHistory.Insert(0, searchTerm);

            // Trim to max items
            if (_settings.DealSearchHistory.Count > maxItems)
            {
                _settings.DealSearchHistory.RemoveRange(maxItems, _settings.DealSearchHistory.Count - maxItems);
            }

            _isDirty = true;
        }

        RaiseSettingChanged(nameof(DealSearchHistory), null, searchTerm);
        Debug.WriteLine($"[SettingsService] Added '{searchTerm}' to search history");
    }

    /// <inheritdoc/>
    public void ClearSearchHistory()
    {
        lock (_lock)
        {
            _settings.DealSearchHistory.Clear();
            _isDirty = true;
        }

        RaiseSettingChanged(nameof(DealSearchHistory), null, null);
        Debug.WriteLine("[SettingsService] Cleared search history");
    }

    #endregion

    #region Private Helpers

    private void RaiseSettingChanged(string settingName, object? oldValue, object? newValue)
    {
        SettingChanged?.Invoke(this, new SettingChangedEventArgs
        {
            SettingName = settingName,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    #endregion
}
