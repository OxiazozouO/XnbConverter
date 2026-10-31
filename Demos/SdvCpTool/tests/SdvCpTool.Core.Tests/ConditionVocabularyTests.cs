using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

public class ConditionVocabularyTests
{
    [Theory]
    [InlineData("Season")]
    [InlineData("Day")]
    [InlineData("DayOfWeek")]
    [InlineData("DaysPlayed")]
    [InlineData("Time")]
    [InlineData("Weather")]
    [InlineData("Year")]
    [InlineData("HasSeenEvent")]
    [InlineData("HasFlag")]
    [InlineData("Relationship")]
    [InlineData("Hearts")]
    [InlineData("Spouse")]
    [InlineData("Roommate")]
    [InlineData("SkillLevel")]
    [InlineData("HasProfession")]
    [InlineData("IsCommunityCenterComplete")]
    [InlineData("IsJojaMartComplete")]
    [InlineData("FarmName")]
    [InlineData("FarmType")]
    [InlineData("FarmCave")]
    [InlineData("PlayerGender")]
    [InlineData("PreferredPet")]
    [InlineData("LocationName")]
    [InlineData("LocationContext")]
    [InlineData("LocationUniqueName")]
    [InlineData("FarmhouseUpgrade")]
    [InlineData("HasCaughtFish")]
    [InlineData("HasConversationTopic")]
    [InlineData("HasCookingRecipe")]
    [InlineData("HasCraftingRecipe")]
    [InlineData("HasDialogueAnswer")]
    [InlineData("HasReadLetter")]
    [InlineData("HasVisitedLocation")]
    // 钱包物品：值要查表（`Validating/WalletItems.cs`），但令牌本身是**可译**的。
    [InlineData("HasWalletItem")]
    public void 可翻译的键(string key)
    {
        Assert.Equal(TokenKind.GsqTranslatable, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("HasMod")]
    [InlineData("ModId")]
    [InlineData("HasFile")]
    [InlineData("FirstValidFile")]
    public void 可烘焙的键(string key)
    {
        Assert.Equal(TokenKind.Bakeable, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("I18n")]
    public void 语言维度的键(string key)
    {
        Assert.Equal(TokenKind.Locale, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("Range")]
    [InlineData("Round")]
    [InlineData("Count")]
    [InlineData("Lowercase")]
    [InlineData("Merge")]
    [InlineData("Query")]
    public void 纯计算的键(string key)
    {
        Assert.Equal(TokenKind.PureComputation, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals")]
    [InlineData("PeacefulEnd.CustomCompanions/SomeToken")]
    [InlineData("spacechase0.SpaceCore/Anything")]
    public void 自定义mod令牌(string key)
    {
        Assert.Equal(TokenKind.CustomModToken, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("DailyLuck")]
    [InlineData("HasActiveQuest")]
    [InlineData("PlayerName")]
    [InlineData("ChildNames")]
    [InlineData("ChildGenders")]
    [InlineData("HavingChild")]
    [InlineData("Pregnant")]
    // ⚠ `HasWalletItem` 原先在这张表里，**判断是错的**（2026-10-07 订正）：
    //   钱包物品就是原版 power，`Data/Powers.json` 里每个 power 自带 `UnlockedCondition`
    //   （本身就是一句 GSQ）。它现在是**可译**的，见 `ConditionVocabularyTests.可译的键`。
    public void 无GSQ等价物的键(string key)
    {
        Assert.Equal(TokenKind.NoGsqEquivalent, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("DayEvent")]
    [InlineData("IsMainPlayer")]
    [InlineData("IsOutdoors")]
    public void 只能近似的键(string key)
    {
        Assert.Equal(TokenKind.GsqApproximate, ConditionVocabulary.Classify(key));
    }

    [Fact]
    public void 从未见过的键是Unknown()
    {
        Assert.Equal(TokenKind.Unknown, ConditionVocabulary.Classify("NoSuchToken"));
    }

    [Fact]
    public void 键名大小写不敏感()
    {
        Assert.Equal(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("season"));
        Assert.Equal(TokenKind.Bakeable, ConditionVocabulary.Classify("hasmod"));
    }

    [Fact]
    public void 拼写错误的名字不是可翻译()
    {
        Assert.Equal(TokenKind.Unknown, ConditionVocabulary.Classify("IsJoMartComplete"));
        Assert.Equal(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("IsJojaMartComplete"));
    }
}
