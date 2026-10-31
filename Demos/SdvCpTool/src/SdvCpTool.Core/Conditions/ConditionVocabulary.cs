using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Conditions;

/// <summary>CP 条件键名的分类表。</summary>
public static class ConditionVocabulary
{
    // 有 GSQ 等价关键字，可机械翻译（依据 docs/research/2026-10-05-cp-to-gsq-token-mapping.md）
    private static readonly HashSet<string> Translatable = new(StringComparer.OrdinalIgnoreCase)
    {
        // 日期与天气
        "Day", "DayOfWeek", "DaysPlayed", "Season", "Time", "Weather", "Year",
        // 玩家
        "FarmhouseUpgrade", "HasCaughtFish", "HasConversationTopic", "HasCookingRecipe",
        "HasCraftingRecipe", "HasDialogueAnswer", "HasFlag", "HasProfession", "HasReadLetter",
        "HasSeenEvent", "HasVisitedLocation", "LocationContext", "LocationName",
        "LocationUniqueName", "PlayerGender", "PreferredPet", "SkillLevel",
        // 钱包物品：**值**要查表（`Validating/WalletItems.cs`），表里有才算译得出。
        // 原先它被判成 `NoGsqEquivalent`（「无通用钱包关键字」）—— 那是错的，见那张表的说明。
        "HasWalletItem",
        // 关系
        "Hearts", "Relationship", "Roommate", "Spouse",
        // 世界
        "FarmCave", "FarmName", "FarmType", "IsCommunityCenterComplete", "IsJojaMartComplete"
    };

    // 能生成 GSQ 但语义有偏差
    private static readonly HashSet<string> Approximate = new(StringComparer.OrdinalIgnoreCase)
    {
        "DayEvent",     // GSQ 只有 IS_FESTIVAL_DAY 布尔，匹配不了节日名
        "IsMainPlayer", // GSQ IS_HOST 是"本机是否主机"，无玩家参数
        "IsOutdoors"    // GSQ LOCATION_IS_OUTDOORS 按地点不按玩家
    };

    // GSQ 原理上表达不了 —— 需要返回集合或遍历实体
    private static readonly HashSet<string> NoEquivalent = new(StringComparer.OrdinalIgnoreCase)
    {
        "DailyLuck",       // 小数日运，GSQ 无比较关键字
        "HasActiveQuest",  // GSQ 完全没有任务关键字
        "PlayerName",      // GSQ 无玩家名关键字
        "ChildNames",      // GSQ 只有 PLAYER_HAS_CHILDREN 计数
        "ChildGenders",
        "HavingChild",     // 需遍历所有玩家读 NextBirthingDate
        "Pregnant",
        // ⚠ `HasWalletItem` **已从这里移出**（2026-10-07）：原判「无通用钱包关键字」是错的。
        //   钱包物品就是原版 power，而 `Data/Powers.json` 里每个 power **自带**
        //   `UnlockedCondition` —— 那本身就是一句 GSQ。逐条核过，完全可译。
        //   见 `Validating/WalletItems.cs`（由 `tools/gen-wallet-items.py` 从那份数据生成）。
    };

    // 可烘焙：读 Mods/*/manifest.json 与包内文件即可离线求值（通道 C）
    // 依据：CP 把它们注册成 ImmutableValueProvider（TokenManager.cs:268,286），
    // 对给定的 mod 集与内容包，值在一次会话内不可变 —— 所以固化在语义上正确。
    private static readonly HashSet<string> Bakeable = new(StringComparer.OrdinalIgnoreCase)
    {
        "HasMod", "ModId", "HasFile", "FirstValidFile"
    };

    // 语言：写进对应语言后缀的资产即可（通道 B1）
    private static readonly HashSet<string> Locale = new(StringComparer.OrdinalIgnoreCase)
    {
        "I18n"
    };

    // 纯计算 / 需要求值才能得到结果，没有 GSQ 的一对一等价物
    private static readonly HashSet<string> Computation = new(StringComparer.OrdinalIgnoreCase)
    {
        // Query 是对 CP 令牌的表达式求值（如 `Query: {{Day}} < 15`），参数里常嵌别的令牌，
        // 甚至嵌自定义 mod 令牌（真实数据：`Query: '{{Rafseazz.RidgesideVillage/SpouseGender}}' = 'female'`）。
        // 它没有 GSQ 等价物，绝不能归入 Translatable —— 那会产出一个错误的"可转换"背书。
        "Query",
        "Range", "Round", "Count", "Lowercase", "Uppercase", "Merge", "PathPart", "Render",
        "HasValue", "FromFile", "Target", "TargetPathOnly", "TargetWithoutPath",
        "AbsoluteFilePath", "FormatAssetName", "InternalAssetKey",
        // research §五 把这四个列为"分类表遗漏"（待定）。它们是 CP 的取值类令牌、没有 GSQ 一对一
        // 等价物，归入 Computation（该类的语义正是"取值…没有 GSQ 的一对一等价物"）。
        // 覆盖度测试要求 CP 2.9.1 的每个真实令牌都不落入 Unknown，故在此补齐。
        "Random", "Language", "LocationOwnerId", "FarmMapAsset"
    };

    /// <param name="packConfig">
    /// 本内容包 <c>content.json</c> 的 <c>ConfigSchema</c>（配置令牌）。判定与写入共用同一个实例：
    /// 判定说「能求值」的依据，正是写入选用的那份取数结果。为 <c>null</c> 时不认配置令牌（旧行为）。
    /// </param>
    public static TokenKind Classify(string key, ConfigSchemaTokens? packConfig = null,
        IReadOnlyCollection<string>? packTokens = null)
    {
        string name = key.Trim();
        if (name.Length == 0) return TokenKind.Unknown;

        // 自定义 mod 令牌形如 `作者.Mod名/令牌名`
        if (name.Contains('/') && name.Contains('.')) return TokenKind.CustomModToken;

        if (NoEquivalent.Contains(name)) return TokenKind.NoGsqEquivalent;
        if (Approximate.Contains(name)) return TokenKind.GsqApproximate;
        if (Translatable.Contains(name)) return TokenKind.GsqTranslatable;
        if (Bakeable.Contains(name)) return TokenKind.Bakeable;
        if (Locale.Contains(name)) return TokenKind.Locale;
        if (Computation.Contains(name)) return TokenKind.PureComputation;

        // 内容包的配置令牌：`content.json` 的 `ConfigSchema` 字段名就是令牌名，值取自包内
        // `config.json`（缺则 `ConfigSchema.Default`）—— 和 HasMod/HasFile 一样是「读包内文件即可离线求值」，
        // 因此归入 Bakeable（通道 C）。
        //
        // 必须放在**内建令牌之后**：CP 会丢弃与内建条件同名的 ConfigSchema 字段
        //（ConfigFileHandler.cs:84-88），所以内建名永远优先。
        if (packConfig != null && packConfig.Contains(name)) return TokenKind.Bakeable;

        // ★ 包内 `DynamicTokens`（包自己在 `content.json` 里定义的令牌，如 CapeStardew 的
        //   `DLXCabin` / `DLXForest`）：值由包自己给、`When` 也由包自己写 ⇒ **读包内文件即可离线求值**，
        //   与配置字段同一条道理，同样归 Bakeable。
        //   实测代价（2026-10-12）：不认这一档时，凡是拿包内令牌当条件的补丁一律"求值失败"，
        //   连带把**地图改动整条丢掉** —— 直接表现是 9 张原版地图的 warp 比原版少
        //   （`Summit` 8→2、`AdventureGuild` 10→5、`JoshHouse` 15→9 …，玩家看到的就是
        //   "地图之间的传送不见了"）。
        if (packTokens != null && packTokens.Contains(name, StringComparer.OrdinalIgnoreCase))
            return TokenKind.Bakeable;

        return TokenKind.Unknown;
    }
}
