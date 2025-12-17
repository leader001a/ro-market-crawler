namespace RoMarketCrawler.Models;

/// <summary>
/// Item sub-filter definitions based on kafra.kr categories
/// </summary>
public static class ItemFilters
{
    /// <summary>
    /// Weapon types (무기 종류) - matches "계열 : [type]" format in item_text
    /// Note: One-handed swords may appear as "계열 : 검" or "계열 : 한손검"
    /// </summary>
    public static readonly FilterOption[] WeaponTypes = new[]
    {
        new FilterOption("전체", ""),
        new FilterOption("한손검", "계열 : 검(?!사)|계열 : 한손검"),
        new FilterOption("양손검", "계열 : 양손검"),
        new FilterOption("단검", "계열 : 단검"),
        new FilterOption("한손도끼", "계열 : 한손도끼|계열 : 도끼(?!\\s*공)"),
        new FilterOption("양손도끼", "계열 : 양손도끼"),
        new FilterOption("카타르", "계열 : 카타르"),
        new FilterOption("한손지팡이", "계열 : 한손지팡이|계열 : 로드|계열 : 완드"),
        new FilterOption("양손지팡이", "계열 : 양손지팡이|계열 : 스태프"),
        new FilterOption("둔기", "계열 : 둔기|계열 : 메이스"),
        new FilterOption("활", "계열 : 활|계열 : 보우"),
        new FilterOption("한손창", "계열 : 한손창|계열 : 스피어"),
        new FilterOption("양손창", "계열 : 양손창|계열 : 창(?!\\s*공)|계열 : 랜스"),
        new FilterOption("손톱", "계열 : 손톱|계열 : 클로우|계열 : 너클"),
        new FilterOption("책", "계열 : 책|계열 : 북"),
        new FilterOption("채찍", "계열 : 채찍|계열 : 휩"),
        new FilterOption("악기", "계열 : 악기|계열 : 기타(?!\\s)|계열 : 바이올린"),
        new FilterOption("수리검", "계열 : 수리검|계열 : 표창"),
        new FilterOption("그레네이드런처", "계열 : 그레네이드|계열 : 런처"),
        new FilterOption("샷건", "계열 : 샷건"),
        new FilterOption("리볼버", "계열 : 리볼버|계열 : 권총"),
        new FilterOption("게틀링건", "계열 : 게틀링|계열 : 기관총"),
        new FilterOption("라이플", "계열 : 라이플|계열 : 소총")
    };

    /// <summary>
    /// Armor positions (방어구 위치) - matches "계열 : [type]" and "위치 : [position]" format in item_text
    /// Headgear uses both 계열 : 투구 and 위치 : 상단/중단/하단
    /// </summary>
    public static readonly FilterOption[] ArmorPositions = new[]
    {
        new FilterOption("전체", ""),
        new FilterOption("투구상단", "계열 : 투구.*위치 : 상단(?![.,])"),
        new FilterOption("투구중단", "계열 : 투구.*위치 : 중단(?![.,])"),
        new FilterOption("투구하단", "계열 : 투구.*위치 : 하단(?![.,])"),
        new FilterOption("투구상중단", "계열 : 투구.*위치 : 상[.,]중단"),
        new FilterOption("투구상하단", "계열 : 투구.*위치 : 상[.,]하단"),
        new FilterOption("투구중하단", "계열 : 투구.*위치 : 중[.,]하단"),
        new FilterOption("투구상중하단", "계열 : 투구.*위치 : 상[.,]중[.,]하단"),
        new FilterOption("갑옷", "계열 : 갑옷"),
        new FilterOption("방패", "계열 : 방패"),
        new FilterOption("겉칠것", "계열 : 겉칠것|계열 : 가먼트|계열 : 망토"),
        new FilterOption("신발", "계열 : 신발"),
        new FilterOption("악세사리", "계열 : 악세사리(?!\\[)"),
        new FilterOption("악세사리(L)", "계열 : 악세사리\\[왼쪽"),
        new FilterOption("악세사리(R)", "계열 : 악세사리\\[오른쪽")
    };

    /// <summary>
    /// Card positions (카드 위치) - matches "장착 : [position]" format in item_text
    /// </summary>
    public static readonly FilterOption[] CardPositions = new[]
    {
        new FilterOption("전체", ""),
        new FilterOption("무기", "장착 : 무기"),
        new FilterOption("투구", "장착 : 투구"),
        new FilterOption("겉칠것", "장착 : 걸칠것"),
        new FilterOption("갑옷", "장착 : 갑옷"),
        new FilterOption("방패", "장착 : 방패"),
        new FilterOption("신발", "장착 : 신발"),
        new FilterOption("악세사리", "장착 : 액세서리")
    };

    /// <summary>
    /// Shadow positions (쉐도우 위치) - matches against screen_name
    /// Some items use "악세R/L 쉐도우" instead of "이어링/펜던트 쉐도우"
    /// </summary>
    public static readonly FilterOption[] ShadowPositions = new[]
    {
        new FilterOption("전체", ""),
        new FilterOption("아머", "아머 쉐도우|아머쉐도우"),
        new FilterOption("웨폰", "웨폰 쉐도우|웨폰쉐도우"),
        new FilterOption("쉴드", "쉴드 쉐도우|쉴드쉐도우"),
        new FilterOption("슈즈", "슈즈 쉐도우|슈즈쉐도우"),
        new FilterOption("펜던트", "펜던트 쉐도우|펜던트쉐도우|악세L 쉐도우"),
        new FilterOption("이어링", "이어링 쉐도우|이어링쉐도우|악세R 쉐도우")
    };

    /// <summary>
    /// Costume positions (의상 위치) - matches "위치 : [position]" format in item_text
    /// Combined positions use period or comma: "위치 : 상.중단" or "위치 : 상단,중단"
    /// </summary>
    public static readonly FilterOption[] CostumePositions = new[]
    {
        new FilterOption("전체", ""),
        new FilterOption("상단", "위치 : 상단(?![.,])|위치 : 상단\\s"),
        new FilterOption("중단", "위치 : 중단(?![.,])|위치 : 중단\\s"),
        new FilterOption("하단", "위치 : 하단(?![.,])|위치 : 하단\\s"),
        new FilterOption("상중단", "위치 : 상[.,]중단|위치 : 상단[.,]중단"),
        new FilterOption("상하단", "위치 : 상[.,]하단|위치 : 상단[.,]하단"),
        new FilterOption("중하단", "위치 : 중[.,]하단|위치 : 중단[.,]하단"),
        new FilterOption("상중하단", "위치 : 상[.,]중[.,]하단|위치 : 상단[.,]중단[.,]하단"),
        new FilterOption("겉칠것", "계열 : 겉칠것|계열 : 가먼트")
    };

    /// <summary>
    /// Job classes (직업군)
    /// </summary>
    public static readonly FilterOption[] JobClasses = new[]
    {
        new FilterOption("전체", ""),
        new FilterOption("노비스계열", "노비스|초보자"),
        new FilterOption("검사계열", "검사|소드맨"),
        new FilterOption("나이트", "나이트|룬나이트|로얄가드"),
        new FilterOption("크루세이더", "크루세이더|팔라딘"),
        new FilterOption("법사계열", "마법사|매지션"),
        new FilterOption("위자드", "위자드|워록"),
        new FilterOption("세이지", "세이지|소서러"),
        new FilterOption("궁수계열", "궁수|아처"),
        new FilterOption("헌터", "헌터|레인저|스나이퍼"),
        new FilterOption("바드/댄서", "바드|댄서|클라운|집시|음유시인|원더러|민스트럴"),
        new FilterOption("복사계열", "복사|아콜라이트"),
        new FilterOption("프리스트", "프리스트|하이프리스트|아크비숍"),
        new FilterOption("몽크", "몽크|챔피언|슈라"),
        new FilterOption("상인계열", "상인|머천트"),
        new FilterOption("블랙스미스", "블랙스미스|화이트스미스|메카닉"),
        new FilterOption("알케미스트", "알케미스트|크리에이터|제네릭"),
        new FilterOption("도둑계열", "도둑|시프"),
        new FilterOption("어쌔신", "어쌔신|길로틴크로스"),
        new FilterOption("로그", "로그|쉐도우체이서|스톨커"),
        new FilterOption("태권소년/소녀계열", "태권|권성|소울링커|소울리퍼"),
        new FilterOption("건슬링거/리벨리온계열", "건슬링거|리벨리온"),
        new FilterOption("닌자/카게로우/오보로우계열", "닌자|카게로우|오보로우"),
        new FilterOption("도람", "도람|수라")
    };

    /// <summary>
    /// Get filter options for a given item type
    /// </summary>
    public static FilterCategory[] GetFiltersForType(int itemType)
    {
        return itemType switch
        {
            4 => new[] // 무기
            {
                new FilterCategory("무기 종류", WeaponTypes, FilterTarget.ItemText),
                new FilterCategory("직업군", JobClasses, FilterTarget.EquipJobsText)
            },
            5 => new[] // 방어구
            {
                new FilterCategory("방어구 위치", ArmorPositions, FilterTarget.ItemText),
                new FilterCategory("직업군", JobClasses, FilterTarget.EquipJobsText)
            },
            6 => new[] // 카드
            {
                new FilterCategory("카드 위치", CardPositions, FilterTarget.ItemText)
            },
            19 => new[] // 쉐도우
            {
                new FilterCategory("쉐도우 위치", ShadowPositions, FilterTarget.ScreenName)
            },
            20 => new[] // 의상
            {
                new FilterCategory("의상 위치", CostumePositions, FilterTarget.ItemText)
            },
            _ => Array.Empty<FilterCategory>()
        };
    }
}

/// <summary>
/// Single filter option with display name and regex pattern
/// </summary>
public class FilterOption
{
    public string DisplayName { get; }
    public string Pattern { get; }

    public FilterOption(string displayName, string pattern)
    {
        DisplayName = displayName;
        Pattern = pattern;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Category of filters (e.g., "무기 종류", "직업군")
/// </summary>
public class FilterCategory
{
    public string Name { get; }
    public FilterOption[] Options { get; }
    public FilterTarget Target { get; }

    public FilterCategory(string name, FilterOption[] options, FilterTarget target)
    {
        Name = name;
        Options = options;
        Target = target;
    }
}

/// <summary>
/// Which field to apply filter against
/// </summary>
public enum FilterTarget
{
    ScreenName,
    ItemText,
    EquipJobsText
}
