using System.Text.RegularExpressions;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Parser for extracting structured data from item_text field
/// </summary>
public static class ItemTextParser
{
    #region Regex Patterns

    // Basic equipment info
    private static readonly Regex SubTypePattern = new(@"계열\s*:\s*([^\s\n]+)", RegexOptions.Compiled);
    private static readonly Regex PositionPattern = new(@"위치\s*:\s*([^\s\n]+)", RegexOptions.Compiled);
    private static readonly Regex ElementPattern = new(@"속성\s*:\s*(\S+)", RegexOptions.Compiled);
    private static readonly Regex RequiredLevelPattern = new(@"요구\s*레벨\s*:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex WeaponLevelPattern = new(@"무기\s*레벨\s*:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex ArmorLevelPattern = new(@"방어구\s*레벨\s*:\s*(\d+)", RegexOptions.Compiled);

    // Combat stats
    private static readonly Regex AttackPattern = new(@"공격\s*:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex DefensePattern = new(@"방어\s*:\s*(-?\d+)", RegexOptions.Compiled);

    // Stat bonuses (STR, AGI, VIT, INT, DEX, LUK, POW, STA, WIS, SPL, CON, RES, P.ATK, S.MATK)
    private static readonly Regex StatBonusPattern = new(
        @"(STR|AGI|VIT|INT|DEX|LUK|POW|STA|WIS|SPL|CON|RES|CRI|HIT|FLEE|P\.ATK|S\.MATK|MHP|MSP|MDEF|ATK|MATK)\s*([+\-])\s*(\d+)(?!\s*%)",
        RegexOptions.Compiled);

    // HP/SP max patterns (Korean style)
    private static readonly Regex HpMaxPattern = new(@"(HP|MHP)\s*최대치?\s*(\d+)\s*(증가)?", RegexOptions.Compiled);
    private static readonly Regex SpMaxPattern = new(@"(SP|MSP)\s*최대치?\s*(\d+)\s*(증가)?", RegexOptions.Compiled);

    // Percentage bonuses
    private static readonly Regex PercentBonusPattern = new(
        @"(ATK|MATK|물리\s*데미지|마법\s*데미지|크리티컬\s*데미지|힐량)\s*([+]?\s*)?(\d+(?:\.\d+)?)\s*%\s*(증가)?",
        RegexOptions.Compiled);

    // Refine effects
    private static readonly Regex RefineAtPattern = new(@"(\d+)\s*제련\s*시[,\s]*(.+?)(?=\n|\d+제련|$)", RegexOptions.Compiled);
    private static readonly Regex RefinePerPattern = new(@"(\d+)\s*제련\s*당[,\s]*(.+?)(?=\n|\d+제련|$)", RegexOptions.Compiled);

    // Grade effects
    private static readonly Regex GradePattern = new(@"\[([DCBA])등급[^\]]*\]\s*(.+?)(?=\n|\[|$)", RegexOptions.Compiled);

    // Set bonuses
    private static readonly Regex SetBonusPattern = new(@"(.+?)와\s*함께\s*장착\s*시[,\s]*(.+?)(?=\n|$)", RegexOptions.Compiled);
    private static readonly Regex SetRefinePattern = new(@"(.+?)의\s*제련도가\s*(\d+)\s*이상인\s*경우[,\s]*(.+?)(?=\n|$)", RegexOptions.Compiled);

    // Skill effects
    private static readonly Regex SkillDamagePattern = new(@"(\S+)\s*데미지\s*(\d+)\s*%\s*(증가|추가\s*증가)", RegexOptions.Compiled);
    private static readonly Regex SkillCooldownPattern = new(@"(\S+)\s*(?:스킬\s*)?쿨타임\s*(\d+(?:\.\d+)?)\s*초?\s*감소", RegexOptions.Compiled);

    // Special flags
    private static readonly Regex IndestructiblePattern = new(@"파괴\s*불가", RegexOptions.Compiled);
    private static readonly Regex UnrefinablePattern = new(@"제련\s*불가", RegexOptions.Compiled);
    private static readonly Regex DurationPattern = new(@"(\d+)\s*일간\s*사용", RegexOptions.Compiled);

    // Card slot
    private static readonly Regex CardSlotPattern = new(@"장착\s*:\s*(무기|갑옷|투구|신발|방패|겉?칠?것|악세사리)", RegexOptions.Compiled);

    // Pet equipment
    private static readonly Regex PetPattern = new(@"장착\s*:\s*(.+?)(?:\n|$)", RegexOptions.Compiled);
    private static readonly Regex PetTypePattern = new(@"종류\s*:\s*(큐펫장비|몬스터\s*알)", RegexOptions.Compiled);

    #endregion

    /// <summary>
    /// Parse item_text and extract structured details based on item type
    /// </summary>
    public static ParsedItemDetails? Parse(string? itemText, int itemType)
    {
        if (string.IsNullOrWhiteSpace(itemText))
            return null;

        var details = new ParsedItemDetails();

        // Parse based on item type
        switch (itemType)
        {
            case 4: // Weapon
                ParseWeapon(itemText, details);
                break;
            case 5: // Armor
                ParseArmor(itemText, details);
                break;
            case 6: // Card
                ParseCard(itemText, details);
                break;
            case 7: // Pet Egg
                ParsePetEgg(itemText, details);
                break;
            case 8: // Pet Equipment
                ParsePetEquipment(itemText, details);
                break;
            case 19: // Shadow
                ParseShadow(itemText, details);
                break;
            case 20: // Costume
                ParseCostume(itemText, details);
                break;
            default: // Consumables and others
                ParseConsumable(itemText, details);
                break;
        }

        // Parse common effects for equipment types
        if (itemType is 4 or 5 or 6 or 19 or 20)
        {
            ParseCommonEffects(itemText, details);
        }

        return details.HasData() ? details : null;
    }

    #region Type-Specific Parsers

    private static void ParseWeapon(string text, ParsedItemDetails details)
    {
        // Sub-type (계열)
        var subTypeMatch = SubTypePattern.Match(text);
        if (subTypeMatch.Success)
            details.SubType = subTypeMatch.Groups[1].Value.Trim();

        // Attack
        var atkMatch = AttackPattern.Match(text);
        if (atkMatch.Success && int.TryParse(atkMatch.Groups[1].Value, out var atk))
            details.Attack = atk;

        // Weapon level
        var wlMatch = WeaponLevelPattern.Match(text);
        if (wlMatch.Success && int.TryParse(wlMatch.Groups[1].Value, out var wl))
            details.EquipLevel = wl;

        // Element
        var elemMatch = ElementPattern.Match(text);
        if (elemMatch.Success)
            details.Element = elemMatch.Groups[1].Value.Trim();

        // Required level
        var reqMatch = RequiredLevelPattern.Match(text);
        if (reqMatch.Success && int.TryParse(reqMatch.Groups[1].Value, out var req))
            details.RequiredLevel = req;

        // Special flags
        details.IsIndestructible = IndestructiblePattern.IsMatch(text);
        details.IsUnrefinable = UnrefinablePattern.IsMatch(text);
    }

    private static void ParseArmor(string text, ParsedItemDetails details)
    {
        // Sub-type (계열)
        var subTypeMatch = SubTypePattern.Match(text);
        if (subTypeMatch.Success)
            details.SubType = subTypeMatch.Groups[1].Value.Trim();

        // Position (위치)
        var posMatch = PositionPattern.Match(text);
        if (posMatch.Success)
            details.Position = posMatch.Groups[1].Value.Trim();

        // Defense
        var defMatch = DefensePattern.Match(text);
        if (defMatch.Success && int.TryParse(defMatch.Groups[1].Value, out var def))
            details.Defense = def;

        // Armor level
        var alMatch = ArmorLevelPattern.Match(text);
        if (alMatch.Success && int.TryParse(alMatch.Groups[1].Value, out var al))
            details.EquipLevel = al;

        // Required level
        var reqMatch = RequiredLevelPattern.Match(text);
        if (reqMatch.Success && int.TryParse(reqMatch.Groups[1].Value, out var req))
            details.RequiredLevel = req;

        // Special flags
        details.IsIndestructible = IndestructiblePattern.IsMatch(text);
        details.IsUnrefinable = UnrefinablePattern.IsMatch(text);
    }

    private static void ParseCard(string text, ParsedItemDetails details)
    {
        details.SubType = "카드";

        // Card slot
        var slotMatch = CardSlotPattern.Match(text);
        if (slotMatch.Success)
            details.CardSlot = slotMatch.Groups[1].Value.Trim();
    }

    private static void ParsePetEgg(string text, ParsedItemDetails details)
    {
        var typeMatch = PetTypePattern.Match(text);
        if (typeMatch.Success && typeMatch.Groups[1].Value.Contains("몬스터"))
            details.SubType = "몬스터 알";
    }

    private static void ParsePetEquipment(string text, ParsedItemDetails details)
    {
        details.SubType = "큐펫장비";

        // Pet name
        var petMatch = PetPattern.Match(text);
        if (petMatch.Success)
        {
            var petName = petMatch.Groups[1].Value.Trim();
            // Exclude common non-pet values
            if (!petName.Contains("전직업") && !petName.Contains("장착가능"))
                details.PetName = petName;
        }
    }

    private static void ParseShadow(string text, ParsedItemDetails details)
    {
        details.SubType = "쉐도우 장비";

        // Position
        var posMatch = PositionPattern.Match(text);
        if (posMatch.Success)
            details.Position = posMatch.Groups[1].Value.Trim();

        // Required level
        var reqMatch = RequiredLevelPattern.Match(text);
        if (reqMatch.Success && int.TryParse(reqMatch.Groups[1].Value, out var req))
            details.RequiredLevel = req;
    }

    private static void ParseCostume(string text, ParsedItemDetails details)
    {
        details.SubType = "의상장비";

        // Position
        var posMatch = PositionPattern.Match(text);
        if (posMatch.Success)
            details.Position = posMatch.Groups[1].Value.Trim();

        // Defense (usually 0 for costumes)
        var defMatch = DefensePattern.Match(text);
        if (defMatch.Success && int.TryParse(defMatch.Groups[1].Value, out var def))
            details.Defense = def;

        // Required level
        var reqMatch = RequiredLevelPattern.Match(text);
        if (reqMatch.Success && int.TryParse(reqMatch.Groups[1].Value, out var req))
            details.RequiredLevel = req;
    }

    private static void ParseConsumable(string text, ParsedItemDetails details)
    {
        // Duration for temporary items
        var durMatch = DurationPattern.Match(text);
        if (durMatch.Success && int.TryParse(durMatch.Groups[1].Value, out var dur))
            details.DurationDays = dur;
    }

    #endregion

    #region Common Effects Parser

    private static void ParseCommonEffects(string text, ParsedItemDetails details)
    {
        // Stat bonuses
        ParseStatBonuses(text, details);

        // Percentage bonuses
        ParsePercentBonuses(text, details);

        // Refine effects
        ParseRefineEffects(text, details);

        // Grade effects
        ParseGradeEffects(text, details);

        // Set bonuses
        ParseSetBonuses(text, details);

        // Skill effects
        ParseSkillEffects(text, details);
    }

    private static void ParseStatBonuses(string text, ParsedItemDetails details)
    {
        var stats = new Dictionary<string, int>();

        // Standard stat pattern
        foreach (Match match in StatBonusPattern.Matches(text))
        {
            var stat = match.Groups[1].Value;
            var sign = match.Groups[2].Value == "-" ? -1 : 1;
            if (int.TryParse(match.Groups[3].Value, out var value))
            {
                var key = stat.Replace(".", ""); // P.ATK -> PATK
                stats[key] = stats.GetValueOrDefault(key) + (value * sign);
            }
        }

        // HP max pattern (Korean)
        foreach (Match match in HpMaxPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[2].Value, out var value))
                stats["MHP"] = stats.GetValueOrDefault("MHP") + value;
        }

        // SP max pattern (Korean)
        foreach (Match match in SpMaxPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[2].Value, out var value))
                stats["MSP"] = stats.GetValueOrDefault("MSP") + value;
        }

        if (stats.Count > 0)
            details.Stats = stats;
    }

    private static void ParsePercentBonuses(string text, ParsedItemDetails details)
    {
        var percents = new Dictionary<string, double>();

        foreach (Match match in PercentBonusPattern.Matches(text))
        {
            var stat = match.Groups[1].Value.Replace(" ", "");
            if (double.TryParse(match.Groups[3].Value, out var value))
            {
                var key = stat + "%";
                percents[key] = percents.GetValueOrDefault(key) + value;
            }
        }

        if (percents.Count > 0)
            details.PercentBonuses = percents;
    }

    private static void ParseRefineEffects(string text, ParsedItemDetails details)
    {
        var effects = new List<RefineEffect>();

        // "N제련 시" pattern
        foreach (Match match in RefineAtPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var level))
            {
                effects.Add(new RefineEffect
                {
                    RefineLevel = level,
                    Type = "at",
                    Effect = match.Groups[2].Value.Trim().TrimEnd('.')
                });
            }
        }

        // "N제련 당" pattern
        foreach (Match match in RefinePerPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var level))
            {
                effects.Add(new RefineEffect
                {
                    RefineLevel = level,
                    Type = "per",
                    Effect = match.Groups[2].Value.Trim().TrimEnd('.')
                });
            }
        }

        if (effects.Count > 0)
            details.RefineEffects = effects.OrderBy(e => e.RefineLevel).ToList();
    }

    private static void ParseGradeEffects(string text, ParsedItemDetails details)
    {
        var grades = new Dictionary<string, string>();

        foreach (Match match in GradePattern.Matches(text))
        {
            var grade = match.Groups[1].Value;
            var effect = match.Groups[2].Value.Trim().TrimEnd('.');
            grades[grade] = effect;
        }

        if (grades.Count > 0)
            details.GradeEffects = grades;
    }

    private static void ParseSetBonuses(string text, ParsedItemDetails details)
    {
        var sets = new List<SetBonus>();

        // "XXX와 함께 장착 시" pattern
        foreach (Match match in SetBonusPattern.Matches(text))
        {
            sets.Add(new SetBonus
            {
                SetItem = match.Groups[1].Value.Trim(),
                Effect = match.Groups[2].Value.Trim().TrimEnd('.')
            });
        }

        // "XXX의 제련도가 N 이상인 경우" pattern
        foreach (Match match in SetRefinePattern.Matches(text))
        {
            if (int.TryParse(match.Groups[2].Value, out var refine))
            {
                sets.Add(new SetBonus
                {
                    SetItem = match.Groups[1].Value.Trim(),
                    Effect = match.Groups[3].Value.Trim().TrimEnd('.'),
                    RequiredRefine = refine
                });
            }
        }

        if (sets.Count > 0)
            details.SetBonuses = sets;
    }

    private static void ParseSkillEffects(string text, ParsedItemDetails details)
    {
        var skills = new List<SkillEffect>();

        // Skill damage pattern
        foreach (Match match in SkillDamagePattern.Matches(text))
        {
            if (int.TryParse(match.Groups[2].Value, out var value))
            {
                skills.Add(new SkillEffect
                {
                    SkillName = match.Groups[1].Value.Trim(),
                    EffectType = "damage",
                    Value = value,
                    IsPercent = true
                });
            }
        }

        // Skill cooldown pattern
        foreach (Match match in SkillCooldownPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[2].Value, out var value))
            {
                skills.Add(new SkillEffect
                {
                    SkillName = match.Groups[1].Value.Trim(),
                    EffectType = "cooldown",
                    Value = value,
                    IsPercent = false
                });
            }
        }

        if (skills.Count > 0)
            details.SkillEffects = skills;
    }

    #endregion
}
