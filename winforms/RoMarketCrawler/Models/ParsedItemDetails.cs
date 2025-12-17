using System.Text.Json.Serialization;

namespace RoMarketCrawler.Models;

/// <summary>
/// Parsed structured data extracted from item_text field
/// </summary>
public class ParsedItemDetails
{
    #region Basic Equipment Info

    /// <summary>
    /// Equipment sub-type (e.g., 양손검, 투구, 카드)
    /// </summary>
    [JsonPropertyName("sub_type")]
    public string? SubType { get; set; }

    /// <summary>
    /// Equipment position (e.g., 상단, 중단, 아머)
    /// </summary>
    [JsonPropertyName("position")]
    public string? Position { get; set; }

    /// <summary>
    /// Element attribute (e.g., 수, 화, 풍, 지, 성, 암)
    /// </summary>
    [JsonPropertyName("element")]
    public string? Element { get; set; }

    /// <summary>
    /// Required level to equip
    /// </summary>
    [JsonPropertyName("req_level")]
    public int? RequiredLevel { get; set; }

    /// <summary>
    /// Weapon/Armor level (1-5)
    /// </summary>
    [JsonPropertyName("equip_level")]
    public int? EquipLevel { get; set; }

    #endregion

    #region Combat Stats

    /// <summary>
    /// Base attack power
    /// </summary>
    [JsonPropertyName("atk")]
    public int? Attack { get; set; }

    /// <summary>
    /// Magic attack power
    /// </summary>
    [JsonPropertyName("matk")]
    public int? MagicAttack { get; set; }

    /// <summary>
    /// Defense value
    /// </summary>
    [JsonPropertyName("def")]
    public int? Defense { get; set; }

    /// <summary>
    /// Magic defense value
    /// </summary>
    [JsonPropertyName("mdef")]
    public int? MagicDefense { get; set; }

    #endregion

    #region Stat Bonuses

    /// <summary>
    /// Stat bonuses (e.g., {"STR": 5, "DEX": 3, "MHP": 500})
    /// </summary>
    [JsonPropertyName("stats")]
    public Dictionary<string, int>? Stats { get; set; }

    /// <summary>
    /// Percentage bonuses (e.g., {"ATK%": 10, "MATK%": 5})
    /// </summary>
    [JsonPropertyName("percent")]
    public Dictionary<string, double>? PercentBonuses { get; set; }

    #endregion

    #region Refine & Grade Effects

    /// <summary>
    /// Refine level effects
    /// </summary>
    [JsonPropertyName("refine")]
    public List<RefineEffect>? RefineEffects { get; set; }

    /// <summary>
    /// Grade-based effects (D, C, B, A grades)
    /// </summary>
    [JsonPropertyName("grade")]
    public Dictionary<string, string>? GradeEffects { get; set; }

    #endregion

    #region Set & Skill Effects

    /// <summary>
    /// Set bonus effects when equipped with other items
    /// </summary>
    [JsonPropertyName("set")]
    public List<SetBonus>? SetBonuses { get; set; }

    /// <summary>
    /// Skill-related effects
    /// </summary>
    [JsonPropertyName("skill")]
    public List<SkillEffect>? SkillEffects { get; set; }

    #endregion

    #region Special Flags

    /// <summary>
    /// Cannot be destroyed
    /// </summary>
    [JsonPropertyName("indestructible")]
    public bool IsIndestructible { get; set; }

    /// <summary>
    /// Cannot be refined
    /// </summary>
    [JsonPropertyName("unrefinable")]
    public bool IsUnrefinable { get; set; }

    /// <summary>
    /// Duration in days (for temporary items)
    /// </summary>
    [JsonPropertyName("duration")]
    public int? DurationDays { get; set; }

    #endregion

    #region Type-Specific Fields

    /// <summary>
    /// Card equip slot (무기, 갑옷, 투구, etc.)
    /// </summary>
    [JsonPropertyName("card_slot")]
    public string? CardSlot { get; set; }

    /// <summary>
    /// Pet name for pet equipment
    /// </summary>
    [JsonPropertyName("pet")]
    public string? PetName { get; set; }

    #endregion

    /// <summary>
    /// Check if this details object has any meaningful data
    /// </summary>
    public bool HasData()
    {
        return !string.IsNullOrEmpty(SubType) ||
               !string.IsNullOrEmpty(Position) ||
               !string.IsNullOrEmpty(Element) ||
               RequiredLevel.HasValue ||
               EquipLevel.HasValue ||
               Attack.HasValue ||
               MagicAttack.HasValue ||
               Defense.HasValue ||
               MagicDefense.HasValue ||
               (Stats?.Count > 0) ||
               (PercentBonuses?.Count > 0) ||
               (RefineEffects?.Count > 0) ||
               (GradeEffects?.Count > 0) ||
               (SetBonuses?.Count > 0) ||
               (SkillEffects?.Count > 0) ||
               IsIndestructible ||
               IsUnrefinable ||
               DurationDays.HasValue ||
               !string.IsNullOrEmpty(CardSlot) ||
               !string.IsNullOrEmpty(PetName);
    }
}

/// <summary>
/// Refine level effect
/// </summary>
public class RefineEffect
{
    /// <summary>
    /// Refine level (e.g., 7, 9, 11)
    /// </summary>
    [JsonPropertyName("level")]
    public int RefineLevel { get; set; }

    /// <summary>
    /// Effect type: "at" (N제련 시) or "per" (N제련 당)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "at";

    /// <summary>
    /// Effect description
    /// </summary>
    [JsonPropertyName("effect")]
    public string Effect { get; set; } = string.Empty;
}

/// <summary>
/// Set bonus when equipped with other items
/// </summary>
public class SetBonus
{
    /// <summary>
    /// Name of the set item
    /// </summary>
    [JsonPropertyName("item")]
    public string SetItem { get; set; } = string.Empty;

    /// <summary>
    /// Effect when set is complete
    /// </summary>
    [JsonPropertyName("effect")]
    public string Effect { get; set; } = string.Empty;

    /// <summary>
    /// Required refine level for set bonus (optional)
    /// </summary>
    [JsonPropertyName("refine")]
    public int? RequiredRefine { get; set; }
}

/// <summary>
/// Skill-related effect
/// </summary>
public class SkillEffect
{
    /// <summary>
    /// Skill name
    /// </summary>
    [JsonPropertyName("name")]
    public string SkillName { get; set; } = string.Empty;

    /// <summary>
    /// Effect type (damage, cooldown, casttime)
    /// </summary>
    [JsonPropertyName("type")]
    public string EffectType { get; set; } = string.Empty;

    /// <summary>
    /// Effect value
    /// </summary>
    [JsonPropertyName("value")]
    public int Value { get; set; }

    /// <summary>
    /// Whether the value is a percentage
    /// </summary>
    [JsonPropertyName("is_percent")]
    public bool IsPercent { get; set; }
}
