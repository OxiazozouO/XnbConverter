using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

public class GsqTranslatorTests
{
    private static GsqTranslation T(string whenJson) =>
        GsqTranslator.Translate(WhenParser.Parse(JObject.Parse(whenJson)));

    [Fact]
    public void 无条件返回空串()
    {
        var r = GsqTranslator.Translate(null);
        Assert.True(r.Success);
        Assert.Equal("", r.Gsq);
    }

    [Fact]
    public void 单键()
    {
        Assert.Equal("SEASON spring", T("""{"Season": "spring"}""").Gsq);
    }

    [Fact]
    public void 同对象多键是与()
    {
        var r = T("""{"Season": "spring", "Year": "1"}""");
        Assert.Equal("SEASON spring, YEAR 1 1", r.Gsq);
    }

    [Fact]
    public void AND数组也是与()
    {
        var r = T("""{"AND": [{"Season": "spring"}, {"Year": "1"}]}""");
        Assert.Equal("SEASON spring, YEAR 1 1", r.Gsq);
    }

    [Fact]
    public void OR数组用ANY()
    {
        var r = T("""{"OR": [{"Season": "spring"}, {"Season": "fall"}]}""");
        Assert.True(r.Success);
        Assert.Equal("""ANY "SEASON spring" "SEASON fall" """.Trim(), r.Gsq);
    }

    [Fact]
    public void NOT加感叹号()
    {
        var r = T("""{"NOT": {"Season": "winter"}}""");
        Assert.Equal("!SEASON winter", r.Gsq);
    }

    [Fact]
    public void 值的逗号是或()
    {
        // 关键规则：不能写成 `SEASON spring, SEASON summer`（那是"与"，恒假）
        Assert.Equal("SEASON spring summer", T("""{"Season": "spring, summer"}""").Gsq);
    }

    [Fact]
    public void 嵌套令牌的值翻译不了()
    {
        var r = T("""{"Season": "{{Day}}"}""");
        Assert.False(r.Success);
        Assert.Contains("令牌", r.FailureReason);
    }

    [Fact]
    public void 认不出的键翻译不了()
    {
        var r = T("""{"NoSuchToken": "x"}""");
        Assert.False(r.Success);
    }

    [Fact]
    public void OR里出现多键对象时翻译不了()
    {
        // ANY 只能包单个 query，包不住"与"
        var r = T("""{"OR": [{"Season": "spring", "Year": "1"}, {"Season": "fall"}]}""");
        Assert.False(r.Success);
    }

    // 规则 2：PLAYER_* 必须显式带玩家键
    [Theory]
    [InlineData("""{"HasSeenEvent": "123"}""", "PLAYER_HAS_SEEN_EVENT Current 123")]
    [InlineData("""{"HasSeenEvent: hostPlayer": "123"}""", "PLAYER_HAS_SEEN_EVENT Host 123")]
    [InlineData("""{"HasSeenEvent: anyPlayer": "1, 2"}""", "PLAYER_HAS_SEEN_EVENT Any 1 2")]
    [InlineData("""{"HasFlag": "flagA"}""", "PLAYER_HAS_MAIL Current flagA Any")]
    [InlineData("""{"HasReadLetter": "x"}""", "PLAYER_HAS_MAIL Current x Received")]
    [InlineData("""{"HasVisitedLocation": "Town"}""", "PLAYER_VISITED_LOCATION Current Town")]
    [InlineData("""{"PlayerGender": "Female"}""", "PLAYER_GENDER Current Female")]
    [InlineData("""{"PreferredPet": "Cat"}""", "PLAYER_PREFERRED_PET Current Cat")]
    [InlineData("""{"LocationName": "Town"}""", "PLAYER_LOCATION_NAME Current Town")]
    [InlineData("""{"LocationContext": "Island"}""", "PLAYER_LOCATION_CONTEXT Current Island")]
    public void 玩家类令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    /// <summary>
    /// `Spouse`/`Roommate` 是**值令牌**：NPC 名在**值侧**（CP 源码 `TokenManager.cs:200-201`）。
    /// 三种写法（直给值 / `|contains=` 真 / `|contains=` 假）都要出正确 GSQ。
    /// </summary>
    [Theory]
    [InlineData("""{"Spouse": "Haley"}""", "PLAYER_NPC_RELATIONSHIP Current Haley Married")]
    [InlineData("""{"Roommate": "Krobus"}""", "PLAYER_NPC_RELATIONSHIP Current Krobus Roommate")]
    [InlineData("""{"Spouse |contains=Haley": true}""", "PLAYER_NPC_RELATIONSHIP Current Haley Married")]
    [InlineData("""{"Spouse |contains=Haley": false}""", "!PLAYER_NPC_RELATIONSHIP Current Haley Married")]
    [InlineData("""{"Roommate |contains=Krobus": false}""", "!PLAYER_NPC_RELATIONSHIP Current Krobus Roommate")]
    public void 配偶与室友是值令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    /// <summary>
    /// 多 NPC **必须**用 ANY 分组：`PLAYER_NPC_RELATIONSHIP` 只取**一个** NPC
    /// （`GameStateQuery.cs:1527` 的格式串、`:1531` 的单参数位）。
    /// 写成 `... A B Married` 会解析成 npc=A、类型=B/Married，语义全错。
    /// </summary>
    [Theory]
    [InlineData("""{"Spouse": "A, B"}""",
        "ANY \"PLAYER_NPC_RELATIONSHIP Current A Married\" \"PLAYER_NPC_RELATIONSHIP Current B Married\"")]
    [InlineData("""{"Spouse |contains=A, B": false}""",
        "!ANY \"PLAYER_NPC_RELATIONSHIP Current A Married\" \"PLAYER_NPC_RELATIONSHIP Current B Married\"")]
    public void 多个配偶候选用ANY分组(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    /// <summary>
    /// `|contains=` 里**竖线后的空格可有可无** —— 真实语料两种都出现。
    /// 原先只认字面 `|contains=`，带空格的一类全漏（实测 SVE 单类 19 条）。
    /// </summary>
    [Theory]
    [InlineData("""{"HasSeenEvent: anyPlayer| contains=5978939": true}""", "PLAYER_HAS_SEEN_EVENT Any 5978939")]
    [InlineData("""{"HasSeenEvent: anyPlayer|contains=5978939": true}""", "PLAYER_HAS_SEEN_EVENT Any 5978939")]
    [InlineData("""{"HasSeenEvent: anyPlayer | contains=5978939": true}""", "PLAYER_HAS_SEEN_EVENT Any 5978939")]
    [InlineData("""{"HasSeenEvent: anyPlayer| contains=5978939": false}""", "!PLAYER_HAS_SEEN_EVENT Any 5978939")]
    public void 竖线后的空格可有可无(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    /// <summary>回归：`Relationship` 的 NPC 在**键侧**，行为不得被这次改动带偏。</summary>
    [Fact]
    public void 关系令牌的NPC仍在键侧()
    {
        Assert.Equal("PLAYER_NPC_RELATIONSHIP Current June Married Roommate",
            T("""{"Relationship:June": "married"}""").Gsq);
    }

    // 规则 3：数值型是闭区间，单值须补第二个参数
    [Theory]
    [InlineData("""{"Year": "1"}""", "YEAR 1 1")]
    [InlineData("""{"DaysPlayed": "28"}""", "DAYS_PLAYED 28 28")]
    [InlineData("""{"FarmhouseUpgrade": "2"}""", "PLAYER_FARMHOUSE_UPGRADE Current 2 2")]
    [InlineData("""{"Time": "600 1200"}""", "TIME 600 1200")]
    public void 数值型令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    // ── 钱包物品 ────────────────────────────────────────────────────────────
    // ★ 这一格原先判的是「GSQ 里没有等价关键字」，**那是错的**。挖下去发现：
    //   钱包物品就是原版 power，`Data/Powers.json` 里每个 power **自带** `UnlockedCondition`，
    //   本身就是一句 GSQ。下面这些期望值**逐条抄自那份游戏数据**，不是我编的。
    //   `Host`/`Current` 的差别也是真的（`Farmer.cs` 里 RustyKey/SkullKey 读 `Game1.MasterPlayer`）。

    [Theory]
    [InlineData("RustyKey", "PLAYER_HAS_MAIL Host HasRustyKey")]
    [InlineData("SkullKey", "PLAYER_HAS_MAIL Host HasSkullKey")]
    [InlineData("DwarvishTranslationGuide", "PLAYER_HAS_MAIL Host HasDwarvishTranslationGuide")]
    [InlineData("ClubCard", "PLAYER_HAS_MAIL Current HasClubCard")]
    [InlineData("SpecialCharm", "PLAYER_HAS_MAIL Current HasSpecialCharm")]
    [InlineData("MagicInk", "PLAYER_HAS_MAIL Current HasMagicInk")]
    [InlineData("DarkTalisman", "PLAYER_HAS_MAIL Current HasDarkTalisman")]
    [InlineData("MagnifyingGlass", "PLAYER_HAS_MAIL Current HasMagnifyingGlass")]
    [InlineData("KeyToTheTown", "PLAYER_HAS_MAIL Current HasTownKey")]
    // 这两个不是邮件标记而是**看过某事件**（见 `Data/Powers.json`）。
    [InlineData("BearsKnowledge", "PLAYER_HAS_SEEN_EVENT Current 2120303")]
    [InlineData("SpringOnionMastery", "PLAYER_HAS_SEEN_EVENT Current 3910979")]
    public void 钱包物品译成原版Powers数据里的条件(string item, string expected)
    {
        Assert.Equal(expected, T($$"""{"HasWalletItem |contains={{item}}": true}""").Gsq);
    }

    /// <summary>`|contains=`: false 是「没有这个物品」—— 整条取反，与其它 `|contains=` 一致。</summary>
    [Fact]
    public void 钱包物品取反()
    {
        Assert.Equal("!PLAYER_HAS_MAIL Host HasRustyKey",
            T("""{"HasWalletItem |contains=RustyKey": false}""").Gsq);
    }

    /// <summary>
    /// ⚠ <b>表外的值必须拒绝</b>：CP 的取值是固定词表，放行一个它不认的名字，
    /// 等于把一个 CP 本会拒绝的条件翻译出去。
    /// </summary>
    [Fact]
    public void 钱包物品表外的值拒绝()
    {
        var r = T("""{"HasWalletItem |contains=NotARealWalletItem": true}""");

        Assert.False(r.Success);
        Assert.Contains("不在已核对过的钱包物品表里", r.FailureReason);
    }

    /// <summary>
    /// 多值 `|contains=A, B` 是「任一命中」，翻成带引号的 `ANY "…" "…"`
    /// （语法已用游戏自己的 `GameStateQuery.Parse` 验证）。
    /// </summary>
    [Fact]
    public void 钱包物品多值翻成ANY()
    {
        var r = T("""{"HasWalletItem |contains=RustyKey, SkullKey": true}""");

        Assert.True(r.Success, r.FailureReason);
        Assert.Equal(
            "ANY \"PLAYER_HAS_MAIL Host HasRustyKey\" \"PLAYER_HAS_MAIL Host HasSkullKey\"",
            r.Gsq);
    }

    /// <summary>
    /// ★ 年份**只有连续区间**能进 GSQ —— 这是与事件键 `y` 那条语义差配套的边界。
    /// <para>
    /// GSQ 的 <c>YEAR</c> 只吃 <c>min max</c> 闭区间（<c>GameStateQuery.cs:411-422</c>），
    /// 而事件键的 <c>y &lt;n&gt;</c>（n≠1）是 <c>&gt;= n</c> 的下界、<b>上无界</b>
    /// （<c>Preconditions.cs:551-566</c>），**没有**任何关键字表达年代奇偶。
    /// ⇒ 离散集合（尤其奇偶年）必须**拒绝**，不能硬塞成区间 ——
    /// 硬塞会让 `Year: "1,3,5"` 变成 `YEAR 1 5`，把第 2、4 年也**误命中**。
    /// </para>
    /// </summary>
    [Fact]
    public void 年份的三个以上离散值必须拒绝而不是压成区间()
    {
        var r = T("""{"Year": "1, 3, 5"}""");
        Assert.False(r.Success);
        Assert.Contains("区间", r.FailureReason);
    }

    [Fact]
    public void 年份的两个值是区间()
    {
        Assert.Equal("YEAR 2 20", T("""{"Year": "2 20"}""").Gsq);
    }

    // 无玩家键的关键字
    [Theory]
    [InlineData("""{"Season": "spring"}""", "SEASON spring")]
    [InlineData("""{"Day": "1, 2"}""", "DAY_OF_MONTH 1 2")]
    [InlineData("""{"DayOfWeek": "Friday, Saturday"}""", "DAY_OF_WEEK Friday Saturday")]
    [InlineData("""{"FarmCave": "Bats"}""", "FARM_CAVE Bats")]
    [InlineData("""{"FarmType": "Beach"}""", "FARM_TYPE Beach")]
    [InlineData("""{"IsCommunityCenterComplete": "true"}""", "IS_COMMUNITY_CENTER_COMPLETE")]
    [InlineData("""{"IsJojaMartComplete": "true"}""", "IS_JOJA_MART_COMPLETE")]
    public void 无玩家键的令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    // 带地点参数的
    [Fact]
    public void 天气要插地点参数()
    {
        Assert.Equal("WEATHER Here Rain Storm", T("""{"Weather": "Rain, Storm"}""").Gsq);
    }

    // 单值整串
    [Fact]
    public void 农场名是单个整串()
    {
        Assert.Equal("""FARM_NAME "My Farm" """.Trim(), T("""{"FarmName": "My Farm"}""").Gsq);
    }

    // NPC 名在键侧
    [Fact]
    public void 好感度()
    {
        Assert.Equal("PLAYER_HEARTS Current Abigail 10 10",
            T("""{"Hearts:Abigail": "10"}""").Gsq);
    }

    [Fact]
    public void 关系()
    {
        Assert.Equal("PLAYER_NPC_RELATIONSHIP Current June Married Roommate",
            T("""{"Relationship:June": "Married"}""").Gsq);
    }

    [Fact]
    public void 技能等级决定关键字()
    {
        Assert.Equal("PLAYER_COMBAT_LEVEL Current 5 5",
            T("""{"SkillLevel:Combat": "5"}""").Gsq);
    }

    // 无等价物的令牌
    [Theory]
    [InlineData("""{"DailyLuck": "0.02"}""")]
    [InlineData("""{"PlayerName": "Alex"}""")]
    [InlineData("""{"ChildNames": "x"}""")]
    public void 无等价物的令牌翻译失败(string when)
    {
        Assert.False(T(when).Success);
    }

    [Theory]
    [InlineData("""{"HasSeenEvent: |contains=75160120": "true"}""",
                "PLAYER_HAS_SEEN_EVENT Current 75160120")]
    [InlineData("""{"HasDialogueAnswer|contains=751600641": "true"}""",
                "PLAYER_HAS_DIALOGUE_ANSWER Current 751600641")]
    [InlineData("""{"HasFlag: hostPlayer |contains=GH1letter": "true"}""",
                "PLAYER_HAS_MAIL Host GH1letter Any")]
    [InlineData("""{"HasSeenEvent: anyPlayer |contains=1, 2": "true"}""",
                "PLAYER_HAS_SEEN_EVENT Any 1 2")]
    [InlineData("""{"Relationship:June |contains=Married": "true"}""",
                "PLAYER_NPC_RELATIONSHIP Current June Married Roommate")]
    // |contains= 的声明值 false 是"没有交集" → 整条 query 取反（CP 文档：`// NOT year 1`）
    [InlineData("""{"HasSeenEvent: |contains=75160120": "false"}""",
                "!PLAYER_HAS_SEEN_EVENT Current 75160120")]
    [InlineData("""{"Relationship:June |contains=Married": false}""",
                "!PLAYER_NPC_RELATIONSHIP Current June Married Roommate")]
    [InlineData("""{"HasFlag: hostPlayer |contains=GH1letter": false}""",
                "!PLAYER_HAS_MAIL Host GH1letter Any")]
    public void 具名参数contains(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    [Fact]
    public void contains的值来自contains参数()
    {
        // 真正要匹配的是 |contains= 里的内容，JSON 值只当取反开关用
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 123",
            T("""{"HasSeenEvent: |contains=123": "true"}""").Gsq);
    }

    [Fact]
    public void contains的声明值必须是布尔()
    {
        // CP 会因为非 true/false 的声明值直接拒绝内容包（Token.cs TryValidateValues），别替它猜
        var r = T("""{"HasSeenEvent: |contains=123": "随便什么占位"}""");
        Assert.False(r.Success);
        Assert.Contains("true/false", r.FailureReason);
    }

    [Fact]
    public void 无contains时仍从JSON值取值()
    {
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 123",
            T("""{"HasSeenEvent": "123"}""").Gsq);
    }

    // ------------------------------------------------------ 常量折叠的 Literal 节点

    [Fact]
    public void 字面量真翻译成无条件()
    {
        var r = GsqTranslator.Translate(WhenNode.Literal(true));

        Assert.True(r.Success);
        Assert.Equal("", r.Gsq);
        Assert.False(r.NeverApplies);
    }

    [Fact]
    public void 字面量假表示该补丁不适用()
    {
        var r = GsqTranslator.Translate(WhenNode.Literal(false));

        Assert.True(r.Success);           // 不是翻译失败
        Assert.True(r.NeverApplies);      // 而是"该补丁本就不适用"
        Assert.Null(r.Gsq);
    }
}
