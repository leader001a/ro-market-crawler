using System.Text.Json.Serialization;

namespace RoMarketCrawler.Models;

/// <summary>
/// Monster information from kafra.kr API
/// </summary>
public class MonsterInfo
{
    [JsonPropertyName("idx")]
    public int Idx { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("mob_const")]
    public int MobConst { get; set; }

    [JsonPropertyName("mob_code")]
    public string? MobCode { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("name_ko")]
    public string? NameKo { get; set; }

    [JsonPropertyName("name_jp")]
    public string? NameJp { get; set; }

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("mob_lv")]
    public int Level { get; set; }

    [JsonPropertyName("mob_hp")]
    public int Hp { get; set; }

    [JsonPropertyName("mob_exp")]
    public int BaseExp { get; set; }

    [JsonPropertyName("mob_jexp")]
    public int JobExp { get; set; }

    [JsonPropertyName("mob_atk1")]
    public int AtkMin { get; set; }

    [JsonPropertyName("mob_atk2")]
    public int AtkMax { get; set; }

    [JsonPropertyName("mob_def")]
    public int Def { get; set; }

    [JsonPropertyName("mob_mdef")]
    public int Mdef { get; set; }

    [JsonPropertyName("mob_str")]
    public int Str { get; set; }

    [JsonPropertyName("mob_agi")]
    public int Agi { get; set; }

    [JsonPropertyName("mob_vit")]
    public int Vit { get; set; }

    [JsonPropertyName("mob_int")]
    public int Int { get; set; }

    [JsonPropertyName("mob_dex")]
    public int Dex { get; set; }

    [JsonPropertyName("mob_luk")]
    public int Luk { get; set; }

    [JsonPropertyName("need_hit")]
    public int? NeedHit { get; set; }

    [JsonPropertyName("need_flee")]
    public int? NeedFlee { get; set; }

    [JsonPropertyName("element")]
    public int Element { get; set; }

    [JsonPropertyName("element_lv")]
    public int ElementLevel { get; set; }

    [JsonPropertyName("race")]
    public int Race { get; set; }

    [JsonPropertyName("scale")]
    public int Scale { get; set; }

    [JsonPropertyName("boss_gb")]
    public int BossType { get; set; }

    [JsonPropertyName("gifurl")]
    public string? GifUrl { get; set; }

    [JsonPropertyName("mob_skill")]
    public string? MobSkill { get; set; }

    [JsonPropertyName("mvp_exp")]
    public int? MvpExp { get; set; }

    // Display properties
    public string DisplayName => NameKo ?? Name ?? $"Mob {MobConst}";

    public string ElementDisplay => Element switch
    {
        0 => "무",
        1 => "수",
        2 => "지",
        3 => "화",
        4 => "풍",
        5 => "독",
        6 => "성",
        7 => "암",
        8 => "염",
        9 => "불사",
        _ => $"속성{Element}"
    };

    public string ElementFullDisplay => $"{ElementDisplay}{ElementLevel}";

    public string RaceDisplay => Race switch
    {
        0 => "무형",
        1 => "불사",
        2 => "동물",
        3 => "식물",
        4 => "곤충",
        5 => "어패류",
        6 => "악마",
        7 => "인간",
        8 => "천사",
        9 => "용족",
        _ => $"종족{Race}"
    };

    public string ScaleDisplay => Scale switch
    {
        0 => "소형",
        1 => "중형",
        2 => "대형",
        _ => $"크기{Scale}"
    };

    public string BossTypeDisplay => BossType switch
    {
        0 => "일반",
        1 => "보스",
        2 => "MVP",
        _ => "-"
    };

    public string AtkDisplay => AtkMin == AtkMax ? AtkMin.ToString() : $"{AtkMin}~{AtkMax}";

    public string StatsDisplay => $"STR:{Str} AGI:{Agi} VIT:{Vit} INT:{Int} DEX:{Dex} LUK:{Luk}";

    public string ExpDisplay => $"Base: {BaseExp:N0} / Job: {JobExp:N0}";

    public string HitFleeDisplay =>
        NeedHit.HasValue && NeedFlee.HasValue
            ? $"Hit:{NeedHit} / Flee:{NeedFlee}"
            : "-";
}
