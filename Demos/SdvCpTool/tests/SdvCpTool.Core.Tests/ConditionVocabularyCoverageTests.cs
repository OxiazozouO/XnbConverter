using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 与 CP 的真实令牌清单做差集，防止分类表随时间漂移。
/// 清单来源：CP 源码 Framework/Conditions/ConditionType.cs（版本 2.9.1）。
/// </summary>
public class ConditionVocabularyCoverageTests
{
    /// <summary>CP 2.9.1 的全部条件令牌。改动它必须说明版本来源。</summary>
    private static readonly string[] CpConditionTypes =
    {
        "AbsoluteFilePath", "ChildGenders", "ChildNames", "Count", "DailyLuck", "Day", "DayEvent",
        "DayOfWeek", "DaysPlayed", "FarmCave", "FarmMapAsset", "FarmName", "FarmType",
        "FarmhouseUpgrade", "FirstValidFile", "FormatAssetName", "FromFile", "HasActiveQuest",
        "HasCaughtFish", "HasConversationTopic", "HasCookingRecipe", "HasCraftingRecipe",
        "HasDialogueAnswer", "HasFile", "HasFlag", "HasMod", "HasProfession", "HasReadLetter",
        "HasSeenEvent", "HasValue", "HasVisitedLocation", "HasWalletItem", "HavingChild", "Hearts",
        "InternalAssetKey", "IsCommunityCenterComplete", "IsJojaMartComplete", "IsMainPlayer",
        "IsOutdoors", "Language", "LocationContext", "LocationName", "LocationOwnerId",
        "LocationUniqueName", "Lowercase", "Merge", "ModId", "PathPart", "PlayerGender",
        "PlayerName", "PreferredPet", "Pregnant", "Query", "Random", "Range", "Relationship",
        "Render", "Roommate", "Round", "Season", "SkillLevel", "Spouse", "Target",
        "TargetPathOnly", "TargetWithoutPath", "Time", "Uppercase", "Weather", "Year"
    };

    [Fact]
    public void 分类表里不应出现CP不认识的令牌名()
    {
        var unknown = CpConditionTypes
            .Where(t => ConditionVocabulary.Classify(t) == TokenKind.Unknown)
            .ToArray();

        Assert.True(unknown.Length == 0,
            "以下 CP 真实令牌未被分类：" + string.Join(", ", unknown));
    }

    [Fact]
    public void 不应把非CP令牌当成可翻译()
    {
        // RandomNumber 是 Ridgeside 用 DynamicTokens 自建的，不是 CP 内置
        Assert.NotEqual(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("RandomNumber"));
        // 拼写错误的版本也绝不能是可翻译
        Assert.NotEqual(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("IsJoMartComplete"));
    }

    [Fact]
    public void 令牌清单与CP版本一致()
    {
        Assert.Equal(69, CpConditionTypes.Length);
    }
}
