namespace RoMarketCrawler.Models;

internal class AlarmSoundItem
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
