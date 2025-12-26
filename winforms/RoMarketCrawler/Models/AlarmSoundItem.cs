namespace RoMarketCrawler.Models;

/// <summary>
/// Model for alarm sound combo box items
/// </summary>
public class AlarmSoundItem
{
    public AlarmSoundType SoundType { get; }
    public string Name { get; }

    public AlarmSoundItem(AlarmSoundType soundType, string name)
    {
        SoundType = soundType;
        Name = name;
    }

    public override string ToString() => Name;
}
