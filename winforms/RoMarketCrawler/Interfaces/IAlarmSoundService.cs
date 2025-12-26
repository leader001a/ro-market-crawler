using RoMarketCrawler.Models;

namespace RoMarketCrawler.Interfaces;

/// <summary>
/// Interface for alarm sound service
/// </summary>
public interface IAlarmSoundService
{
    /// <summary>
    /// Play the specified alarm sound type
    /// </summary>
    void PlaySound(AlarmSoundType soundType);
}
