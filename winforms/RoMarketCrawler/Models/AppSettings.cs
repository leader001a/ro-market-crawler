namespace RoMarketCrawler.Models;

/// <summary>
/// Application settings model for JSON serialization
/// </summary>
public class AppSettings
{
    public float FontSize { get; set; } = 12f;
    public ThemeType Theme { get; set; } = ThemeType.Dark;
    public bool IsSoundMuted { get; set; } = false;
    public AlarmSoundType AlarmSound { get; set; } = AlarmSoundType.SystemSound;
    public int AlarmIntervalSeconds { get; set; } = 5;
    public List<string> DealSearchHistory { get; set; } = new();
    public List<CostumeSearchEntry> CostumeSearchHistory { get; set; } = new();
    public bool HideDealSearchGuide { get; set; } = false;
    public bool HideUsageNotice { get; set; } = false;
    public DateTime? ApiLockoutUntil { get; set; }
}
