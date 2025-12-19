namespace RoMarketCrawler.Models;

internal class AppSettings
{
    public float FontSize { get; set; } = 12f;
    public ThemeType Theme { get; set; } = ThemeType.Dark;
    public bool IsSoundMuted { get; set; } = false;
    public AlarmSoundType AlarmSound { get; set; } = AlarmSoundType.SystemSound;
    public int AlarmIntervalSeconds { get; set; } = 5;
    public List<string> DealSearchHistory { get; set; } = new();
}
