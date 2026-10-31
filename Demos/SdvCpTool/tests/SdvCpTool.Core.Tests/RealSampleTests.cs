using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 来自真实内容包的翻译样本。所有 <c>When</c> 原文都逐字抄自
/// Ridgeside Village 2.5.17 的 <c>[CP] Ridgeside Village</c>，来源见每条用例上方的注释。
/// 覆盖度：全部真实成功形态 + 每类"该失败"形态 + 两处实现缺陷修复后的回归护栏。
/// </summary>
public class RealSampleTests
{
    private static GsqTranslation T(string whenJson) =>
        GsqTranslator.Translate(WhenParser.Parse(JObject.Parse(whenJson)));

    private static string Translate(string whenJson)
    {
        GsqTranslation r = T(whenJson);
        Assert.True(r.Success, $"本该翻译成功，却失败了：{r.FailureReason}");
        return r.Gsq!;
    }

    [Theory]
    // Ridgeside Village · Assets/Schedules/June.json
    [InlineData("""{"Season":"fall"}""", "SEASON fall")]
    // Ridgeside Village · Data/Festivals/FestivalData.json（同对象多键 = 与）
    [InlineData("""{"Season":"winter","Day":28}""", "SEASON winter, DAY_OF_MONTH 28")]
    // Ridgeside Village · Assets/Schedules/Kiarra.json
    [InlineData("""{"DayOfWeek":"Friday"}""", "DAY_OF_WEEK Friday")]
    // Ridgeside Village · Data/DependencyData/CustomCompanions.json（值侧逗号是"或"）
    [InlineData("""{"Weather":"Sun, Wind"}""", "WEATHER Here Sun Wind")]
    // Ridgeside Village · Data/Events/Events.json
    // 回归护栏：|contains= 的声明值 false 是"没有交集"，不是占位符 —— 整条 query 必须取反。
    // 旧实现忽略这个布尔，把真实数据里 126 处 false 条件全静默翻成了正向语义。
    [InlineData("""{"HasSeenEvent: |contains=75160249":false}""",
                "!PLAYER_HAS_SEEN_EVENT Current 75160249")]
    // Ridgeside Village · Data/DynamicChanges/MapChanges.json
    [InlineData("""{"HasFlag: hostPlayer":"RSV.FixedMinecart"}""",
                "PLAYER_HAS_MAIL Host RSV.FixedMinecart Any")]
    // Ridgeside Village · Data/Events/Events.json（声明值 true = 正向）
    [InlineData("""{"HasDialogueAnswer: |contains=7516439":true}""",
                "PLAYER_HAS_DIALOGUE_ANSWER Current 7516439")]
    // Ridgeside Village · Data/DynamicChanges/MapChanges.json
    [InlineData("""{"HasConversationTopic: hostPlayer":"RSV.OreCT"}""",
                "PLAYER_HAS_CONVERSATION_TOPIC Host RSV.OreCT")]
    // Ridgeside Village · Data/DependencyData/FarmTypeManager.json
    [InlineData("""{"LocationName":"Custom_Ridgeside_RidgeForest, Custom_Ridgeside_RSVSpiritRealm"}""",
                "PLAYER_LOCATION_NAME Current Custom_Ridgeside_RidgeForest Custom_Ridgeside_RSVSpiritRealm")]
    // Ridgeside Village · Data/Events/Events.json
    // 回归护栏：CP 的 Relationship 直接吐 FriendshipStatus 枚举名，Friendly 是其一；
    // 旧实现只认 Married/Dating/Engaged/Divorced/Roommate，把这条真实样本判成了"没有对应值"。
    [InlineData("""{"Relationship:Alissa":"Friendly"}""",
                "PLAYER_NPC_RELATIONSHIP Current Alissa Friendly")]
    // Ridgeside Village · Data/Events/Events.json
    // CP 的 Married 含室友婚、GSQ 的 Married 不含 —— 必须补 Roommate（research §二 陷阱②）
    [InlineData("""{"Relationship:Flor":"Dating, Engaged, Married"}""",
                "PLAYER_NPC_RELATIONSHIP Current Flor Dating Engaged Married Roommate")]
    // Ridgeside Village · Data/Events/Events.json
    // 回归护栏：CP 用 Enum.TryParse(ignoreCase: true)，真实数据写的是 FarmHouseUpgrade（大写 H），
    // 旧实现按大小写敏感匹配 FarmhouseUpgrade，4 条真实补丁全部误判为"没有翻译规则"。
    [InlineData("""{"FarmHouseUpgrade |contains=1":true}""",
                "PLAYER_FARMHOUSE_UPGRADE Current 1 1")]
    public void 真实样本翻译成功(string whenJson, string expectedGsq)
    {
        Assert.Equal(expectedGsq, Translate(whenJson));
    }

    [Theory]
    // Ridgeside Village · Data/DynamicChanges/ExclusionChanges.json
    // CP 支持别的 mod 注册的自定义令牌；GSQ 在游戏运行时没有对应关键字，静态也求不了值。
    [InlineData("""{"Rafseazz.RidgesideVillage/EasyIntroduction":true}""", "自定义 mod 令牌")]
    // Ridgeside Village · Data/Festivals/FestivalLocations/Gathering.json
    [InlineData("""{"HasMod |contains=FlashShifter.StardewValleyExpandedCP":false}""",
                "应走离线烘焙通道")]
    // Ridgeside Village · Data/DependencyData/CustomCompanions.json
    // 值里嵌了 CP 令牌，要等运行时才知道是什么 —— 静态翻译必须拒绝，不能猜。
    [InlineData("""{"Time":"{{Range: 2000, 2600}}"}""", "值里嵌套了令牌")]
    // Ridgeside Village · Data/NPCData/ScheduleStrings.json
    // IsOddYear 是 Ridgeside 在 content.json 的 DynamicTokens 里自建的，不是 CP 内置令牌。
    [InlineData("""{"IsOddYear":true}""", "不在 CP 内置令牌表里")]
    // Ridgeside Village · Data/DependencyData/CustomCompanions.json
    [InlineData("""{"Query: '{{Rafseazz.RidgesideVillage/FoxbloomSpawned}}' = 'true'":true}""",
                "取值/计算类令牌")]
    // Ridgeside Village · Data/Events/Events.json
    // 注意这条的 DayEvent 排在 Query 前面，报告只会说第一个失败的原因。
    [InlineData("""{"DayEvent":"wedding","HasConversationTopic":"RSV.ReservedReception","Query: '{{SpouseGender}}' = 'female'":true}""",
                "只能近似映射")]
    // Ridgeside Village · Data/DynamicChanges/MapChanges.json
    // ⚠ 这条原先断的是「GSQ 没有等价关键字」—— **那个判断是错的**（2026-10-07 订正）：
    //   钱包物品就是原版 power，`Data/Powers.json` 里每个 power 自带 `UnlockedCondition`，
    //   那本身就是 GSQ。现在它**译得出来**，所以从「该失败」表里移走，
    //   正确行为见 GsqTranslatorTests 的 `钱包物品译成原版 Powers.json 里的条件`。
    // Ridgeside Village · Data/Events/Events.json
    // CP 的 Weather 参数是 LocationContext（island/valley），GSQ 的 WEATHER 第一参只认地点名。
    [InlineData("""{"Weather: island":"Sun"}""", "带了地点上下文")]
    public void 真实样本该失败(string whenJson, string expectedReasonPart)
    {
        GsqTranslation r = T(whenJson);
        Assert.False(r.Success, "本该失败，却翻译成功了");
        Assert.Contains(expectedReasonPart, r.FailureReason);
    }
}
