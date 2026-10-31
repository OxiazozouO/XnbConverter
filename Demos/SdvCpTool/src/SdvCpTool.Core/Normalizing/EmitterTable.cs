namespace SdvCpTool.Core.Normalizing;

/// <summary>
/// 一个 <c>(载体, 维度)</c> 格子的归一化结论。
/// <para>
/// <b><see cref="Unknown"/> 必须独立存在</b>：把「还没查」算进「有」会虚报覆盖率。
/// 实测这一档约占 10%，混入会虚报约 5 个百分点。
/// </para>
/// </summary>
public enum EmitKind
{
    /// <summary>已确证原版有原生最简形式，且能够容纳该维度的<b>全体</b>分支。</summary>
    Native,

    /// <summary>已确证原版<b>没有</b>通用机制承载这个格子 —— 这是结论，不是失败。</summary>
    None,

    /// <summary>
    /// 原版<b>有机制但装不下</b>这一格的全部情形（表达能力的精确边界）。**不得**当成 <see cref="None"/>，
    /// 也不得当成 <see cref="Native"/>。
    /// <para>
    /// 两种边界都归这一档（都有实证）：
    /// <list type="number">
    /// <item><b>分支数超容量</b>：<c>$query</c> 只有两个分支，而季节有四个；</item>
    /// <item><b>机制只在部分消费点 / 部分字段上生效</b>：
    ///   <c>Strings/StringsFromMaps</c> 的 <c>$query</c> 只对走 <c>new Dialogue(...)</c> 的那几个键有效
    ///   （走 <c>DialogueBox(string)</c> 的键是死文本），而键走哪条由**别处**（地图 <c>Action</c>）决定；
    ///   <c>Data/Locations</c> 的条件位只在 <c>Fish[]/Forage[]/…</c> 这些**数组元素**上，
    ///   条目的顶层字段没有。</item>
    /// </list>
    /// </para>
    /// </summary>
    CapacityExceeded,

    /// <summary>发射表未覆盖 —— <b>待查</b>，既不是「有」也不是「无」。</summary>
    Unknown,
}

/// <summary>一个格子的发射结论。</summary>
/// <param name="Kind">四态结论。</param>
/// <param name="NativeForm">最简形式。</param>
/// <param name="Evidence">源码依据（文件:行号）。</param>
/// <param name="MergesPatches">
/// <b>本格的最简形式是否把多条补丁<b>合并</b>成更少的产物</b>（真·压缩）。
/// <para>
/// 绝大多数格子是<b>逐条改写</b>（<c>false</c>）：N 条补丁 → N 个原生形式，条数不变
/// —— 例如 113 个 NPC 各自的 <c>&lt;NPC&gt;_spouse</c>，一个也没少。
/// 把它们算成「收敛」是<b>过报</b>。只有真的把 N 条并成 1 个（如四季合一的 <c>Seasonal</c>）
/// 才算 <c>true</c>。
/// </para>
/// </param>
/// <param name="Precondition">
/// <b>本格成立的前提</b>；空串表示无条件成立。
/// <para>
/// 需要它是因为 <c>(载体, 维度)</c> 这一粒度<b>装不下</b>「仅当条件只含本维度时才成立」。
/// 实例：<c>Characters/schedules</c> 的季节键确实原生支持，但<b>季节与另一维度同时出现时就不行</b>
/// （键链是优先列表，两个维度不可组合）。<b>没有这个字段，表就会给出过强的结论。</b>
/// </para>
/// </param>
public readonly record struct EmitPlan(
    EmitKind Kind,
    string NativeForm,
    string Evidence,
    string Precondition = "",
    bool MergesPatches = false)
{
    public static EmitPlan Unknown => new(EmitKind.Unknown, "", "");
}

/// <summary>
/// ★ <b>发射表</b> —— 本设计的核心事实来源：<c>(载体, 维度) → 最简形式</c>。
/// <para>
/// <b>这是编译器里的「指令选择」。</b>新增一个原版机制只改这张表，不改归一化器。
/// </para>
/// <para>
/// <b>录入纪律（沿用 <see cref="Assets.KeyEncodedVariants"/> 的教训）：</b>
/// 每一项都必须有<b>逐条核对过的源码依据</b>（文件:行号）。
/// 没查过的格子<b>留空 → 落到 <see cref="EmitKind.Unknown"/></b>，
/// <b>绝不</b>因为「看起来应该有」而填 <see cref="EmitKind.Native"/>。
/// </para>
/// </summary>
public static class EmitterTable
{

    /// <summary>
    /// ★ <b>唯一的表</b>：<c>(载体, 维度, 资产类型) → 最简形式</c>。
    /// <para>
    /// <b>为什么必须有资产类型这一轴（有实证）</b>：同一个 <c>(载体, 维度)</c> 在不同资产上**最简形式不同**。
    /// 同是「婚姻 × 条目」——<c>Data/Festivals</c> 用 <c>&lt;NPC&gt;_spouse</c>、
    /// <c>Data/Events</c> 用事件键的 <c>O &lt;NPC&gt;</c>、<c>Data/Characters</c> 用 <c>Appearance[].Condition</c>。
    /// </para>
    /// <para>
    /// <b>有意不设「通用格兜底」</b>：实测过一条过宽的通用格（<c>(条目, 婚姻) → _spouse</c>）
    /// 会让没查过的资产（如 <c>Data/mail</c>）被误判成「有原生形式」。
    /// 宁可在报告里多出几格 <see cref="EmitKind.Unknown"/> —— 那是<b>待查清单</b>，正是它的用处。
    /// </para>
    /// </summary>
    /// <summary>
    /// 图片资产按季节换图的结论 —— <b>与资产名的前缀无关，按「消费方」判</b>。
    /// <c>Mods/*</c> / <c>LooseSprites/*</c> / <c>Tilesheets/*</c> 三格共用它：
    /// 发射器只按 Target 分组，从没限制过资产前缀。拆成三格是为了「不设通用兜底」那条纪律，
    /// 但**表里漏写**会把「已查清」误报成 <see cref="EmitKind.Unknown"/>（实测 12 条）。
    /// </summary>
    private static readonly EmitPlan SeasonalImagePlan = new(
        EmitKind.Native,
        "按**消费方**二选一：① 若它是 BigCraftable 的贴图 —— 四季图放 4 个连续索引，"
        + "并把 Data/BigCraftables 的 Name 加上 Seasonal；"
        + "② 若它是被地图引用的图集 —— 命名成 spring_X/summer_X/…，地图 ImageSource 指向 spring_X",
        "① Object.cs:1940-1943（另有配套的 :1428、:2918）；② GameLocation.cs:16439-16447",
        "两条都**装不下「仅室外」**：① 的 GetSeasonIndex() 不看室内外，② 的前缀只在室外生效"
        + "（:16417）—— 实测 SVE 那 212 条全带 IsOutdoors: true，故归一只标「近似」。"
        + "① 另需「消费方恰好一个 BigCraftable 条目且 SpriteIndex=0」+「ToArea 是 (0,0,16,32)」，"
        + "不满足则整组保持原判");

    /// <summary>
    /// 「整片都是 <c>$query</c> 宿主」的资产的通用格：二值维度（看过的剧情 / 好感 / 婚姻 / 年份）
    /// 都装得下。**季节不收** —— <c>$query</c> 只有两个分支而季节有四个（表里那几格也一样）。
    /// </summary>
    private static EmitPlan DialogueQueryRow(string asset, string evidence) => new(
        EmitKind.Native,
        $"{asset} 的值内联 `$query <GSQ>#<真分支>|<假分支>`",
        $"Dialogue.cs:646-652（$query 解析）；宿主：{evidence}",
        "假分支必须是个真值（`$query` 表达不了「条件不成立时不显示」）",
        MergesPatches: false);

    private static readonly Dictionary<(VariantCarrier, VariantDimension, string), EmitPlan> Typed = new()
    {
        // ── Data/Events：事件键自带**前置条件段**，是原版的通用条件机制 ──────────
        // 键形如 `<事件ID>/<前置条件>/…`，段间 AND、`!` 可否定。
        // 令牌 102 个（54 规范名 + 48 别名），全在 StardewValley/Preconditions.cs，
        // 反射注册于 Event.cs:4143-4166；解析入口 Event.cs:11720 → GameLocation.cs:16454 → Event.cs:4187。
        // ⚠ 通用 GSQ 入口是 `G`（规范名 GameStateQuery，Preconditions.cs:744-753），
        //   **不是** `x` —— `x` 是 SendMail 伪事件（:666-683），恒返回 false。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Events")] = new(
            EmitKind.Native,
            "事件键的前置条件段写 `e <事件ID>`（要否定写 `!e <事件ID>`）",
            "Preconditions.cs（e）；Event.cs:11720, :4187"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Data/Events")] = new(
            EmitKind.Native,
            "事件键写 `O <NPC>`（配偶或订婚）；要严格「已婚」用 `G PLAYER_NPC_RELATIONSHIP … Married`",
            "Preconditions.cs（O）；⚠ O **含订婚**，与 CP 的 Spouse 有语义差"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/Events")] = new(
            EmitKind.Native,
            "事件键写 `f <NPC> <点数>`；⚠ 好感度是**点数**，1 心 = 250 点，须换算",
            "Preconditions.cs（f）；⚠ Hearts 心数须 ×250 换成 f 的点数"),

        // ⚠ **本格原先是错的**，两处都错（2026-10-06 逐行核对后改正）：
        //   ① `y <n>` **不是相等** —— `Preconditions.cs:551-566` 里 `n == 1` 走 `year == 1`，
        //      其余走 `year >= n`（**下界，上无界**）。原文写「y 只表相等」是错的；
        //   ② `G IsOddYear` **不存在** —— GSQ 的 130 个关键字里**没有任何年代奇偶**的
        //      （`GameStateQuery.cs` 全表已列，`YEAR` 只吃 `min max` 闭区间）。
        //      `IsOddYear` 其实是 **Ridgeside 内容包自建的 DynamicToken**
        //      （`{{Query: {{Year}} % 2 = 1}}`），不是原版机制。
        //   ⇒ 年份**离散集合（含奇偶）在原版任何机制里都表达不了**；连续区间才行。
        [(VariantCarrier.Entry, VariantDimension.Year, "Data/Events")] = new(
            EmitKind.Native,
            "连续区间：GSQ `G YEAR <min> <max>`；单点：`y <n>`（**仅当 n=1 才恰为第 1 年**，"
            + "n≠1 是 `>= n`，**上无界**，故一般**不要**用它表达区间）",
            "Preconditions.cs:551-566（y，读出一致性）；GameStateQuery.cs:411-422（YEAR，闭区间）",
            "**年份必须是连续区间**（`YEAR a b` 或单点 `y 1`）。离散集合 —— 尤其**奇偶年** —— "
            + "原版没有任何机制能表达：`y` 是下界、`YEAR` 是区间、事件键的段之间是 AND 表不了 OR。"
            + "构造器对 >2 个离散值直接拒绝（`TokenTranslator.Interval`）"),

        // ⚠ 这一格原先**漏了**（季节与另外四维同样是事件键的通用条件位）。
        //   漏条目会把「已查清」误报成 `Unknown`（待查）—— 与 `LooseSprites/*` 那两格同一个毛病。
        [(VariantCarrier.Entry, VariantDimension.Season, "Data/Events")] = new(
            EmitKind.Native,
            "事件键写 `s <季节>`（Preconditions.cs 的 s），或通用 `G SEASON <季节>`",
            "Preconditions.cs（s, G）"),

        // ── `Strings/StringsFromCSFiles`：**值是 Dialogue 宿主**，走 `$query` 内联 ──────
        // 依据：`Event.cs:1527` 的那条值最终走 `new Dialogue(...)`，与
        // `Characters/Dialogue` / `Strings/schedules` / `Data/Festivals` 同属
        // `ConditionLanding.ClassifyDialogueQueryHost` 确证过的四个宿主之一。
        // `$query <GSQ>#真|假` **只有两个分支**（`Dialogue.cs:651` 的 `Split('|')` 不带 limit），
        // 所以四季那一格装不下 —— 与 `(Entry, Season, Data/Festivals)` 同一档。
        [(VariantCarrier.Entry, VariantDimension.Season, "Strings/StringsFromCSFiles")] = new(
            EmitKind.CapacityExceeded,
            "$query 只有两个分支，四季装不下",
            "Dialogue.cs:646-652（$query 解析）；条件位在**值**里，不在键里"),

        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Strings/StringsFromCSFiles")] = new(
            EmitKind.Native,
            "值内联 `$query <GSQ>#新文案|原文案`",
            "Dialogue.cs:646-652；宿主经 ConditionLanding.ClassifyDialogueQueryHost 确证"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Strings/StringsFromCSFiles")] = new(
            EmitKind.Native, "同上：值内联 `$query`",
            "Dialogue.cs:646-652"),

        [(VariantCarrier.Entry, VariantDimension.Year, "Strings/StringsFromCSFiles")] = new(
            EmitKind.Native, "同上：值内联 `$query`",
            "Dialogue.cs:646-652"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Strings/StringsFromCSFiles")] = new(
            EmitKind.Native, "同上：值内联 `$query`",
            "Dialogue.cs:646-652"),

        // ── 地图 × 其余维度：与「看过的剧情」同一结论（只有 ConditionalDoor / BuildConditions，
        //    两者都**改不了图块内容**）。原先只写了 Year/Season/SeenEvent/Marriage 四格，
        //    其余维度落进 `Unknown` —— 同样是表漏条目。
        [(VariantCarrier.Map, VariantDimension.Hearts, "Maps")] = new(
            EmitKind.CapacityExceeded,
            "只有 ConditionalDoor（开门/挡路）与 BuildConditions（可否建造），改不了图块内容",
            "GameLocation.cs:9546, :3907, :2469"),

        // 地图上的**贴图**（tilesheet）按剧情换：`GetSeasonalTilesheetName`
        // （GameLocation.cs:16439-16447）**只认 spring_/summer_/fall_/winter_ 四个前缀**，
        // 字符串里没有任何别的维度 —— 这是读出来的结论，不是「没查」。
        [(VariantCarrier.Sprite, VariantDimension.SeenEvent, "Maps")] = new(
            EmitKind.None, "",
            "图集名的季节前缀替换（GetSeasonalTilesheetName）只认四季，没有剧情维度"),

        // ⚠ **保持 Unknown（待查），不硬判**：实测 SVE 有 4 条
        //   `Load Maps/Town-Halloween` 按 `Year: "1,5,9,…"`（≡1 mod 4）与
        //   `Year: "3,7,11,…"`（≡3 mod 4）分两组 —— 万圣节节日地图按年份换外观。
        //   机制上**也许**能挂到 `Data/Festivals/fall27` 的 `_yN` 姐妹键（`Event.cs:4417-4448`），
        //   但那要①跨资产改写（从 Maps/* 的 Load 改到 Data/Festivals 的键）②按 `_yN`
        //   的**模数由兄弟键数出来**那条规则补齐 `_y2`/`_y4`。**没确证，不做**。

        // ── 日程：两种 schedules 的能力完全不同，必须分开（混为一类会判错落点）──────
        // `Characters/schedules` 的**键名原生支持季节**：spring / spring_13 / spring_Sun 都认。
        // ⚠ 早先判成 None 是**错的**，且引用也引错了：Game1.cs:8296 是**邮件**键、
        //   GameLocation.cs:16442 是**地图 tilesheet 前缀**，都不是日程判定点。
        [(VariantCarrier.Entry, VariantDimension.Season, "Characters/schedules")] = new(
            EmitKind.Native,
            "把季节写进键名：spring / spring_13 / spring_Sun",
            "NPC.cs:5822, :5861, :5878（另 GOTO season :5539）",
            "条件只含季节（或再加日/星期）。季节与另一维度同时出现时键链不可组合，落回 None"),

        // `Strings/schedules` 走 `new Dialogue(...)` 解析，是 `$query` 宿主 ——
        // 但 `$query` 只有两个分支，而季节有四个（Dialogue.cs:651 的 Split('|') 不带 limit，
        // 第 3、4 分支被静默丢弃）。实测 225 条里 222 条装不下。
        [(VariantCarrier.Entry, VariantDimension.Season, "Strings/schedules")] = new(
            EmitKind.CapacityExceeded,
            "$query 只有两个分支，装不下四季（Dialogue.cs:651 的 Split('|') 不带 limit）",
            "Dialogue.cs:646-652"),

        // ── Data/Festivals：键名自带配偶/年份变体（见通用格与 Event.cs）─────────
        [(VariantCarrier.Entry, VariantDimension.Marriage, "Data/Festivals")] = new(
            EmitKind.Native,
            "Data/Festivals 的条目键用 <NPC>_spouse（室友用 <NPC>_roommate）",
            "Event.cs:12609-12665"),

        [(VariantCarrier.Entry, VariantDimension.Year, "Data/Festivals")] = new(
            EmitKind.Native,
            "Data/Festivals 的条目键用 <key>_yN（N 为年份模数）",
            "Event.cs:4417-4448"),

        // ── 其余资产的条件图片落点 / $query 宿主 ─────────────────────────────────
        // `Appearance[]` 是原生的**条件图片**落点：Precedence 越低越优先，
        // 同 precedence 按权重随机（种子 CreateDaySaveRandom(hash(NPC 名))）。NPC.cs:918-1011。
        [(VariantCarrier.Entry, VariantDimension.Season, "Data/Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[] 里写 Season 字段",
            "NPC.cs:918-1011"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Data/Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_NPC_RELATIONSHIP <NPC> Married",
            "NPC.cs:918-1011"),

        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_HAS_SEEN_EVENT <玩家> <事件ID>",
            "NPC.cs:918-1011"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_HEARTS …",
            "NPC.cs:918-1011"),

        // `$query` 宿主：值内联 GSQ，形如 `$query <GSQ>#真|假`。
        // ⚠ 只有一层、两个分支 —— `Split('|')` 不带 limit，嵌套会把尾段吞掉（Dialogue.cs:651）。
        // **二值**维度（看过的剧情/好感度）装得下；**四季装不下**（见上）。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Strings/schedules")] = new(
            EmitKind.Native,
            "$query PLAYER_HAS_SEEN_EVENT …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Festivals")] = new(
            EmitKind.Native,
            "$query PLAYER_HAS_SEEN_EVENT …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Strings/schedules")] = new(
            EmitKind.Native,
            "$query PLAYER_HEARTS …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        // 年份同样是 `$query` 的二值：`$query YEAR <min> <max>#真|假`。
        // ⚠ 前提与 `(Entry, Year, Data/Events)` **完全一样** —— GSQ 的 `YEAR` 只吃闭区间，
        //   离散集合（尤其奇偶年）二值化之后依然表达不了。
        //   实测语料 7 条（「请记住我的生日」写 `Strings/schedules/<NPC>`）。
        [(VariantCarrier.Entry, VariantDimension.Year, "Strings/schedules")] = new(
            EmitKind.Native,
            "$query YEAR <min> <max>#<真分支>|<假分支>",
            "Dialogue.cs:646-652（$query 宿主）；GameStateQuery.cs:411-422（YEAR）",
            "**年份必须是连续区间**；离散集合（奇偶年）表达不了 —— 与 "
            + "`(Entry, Year, Data/Events)` 同一条限制"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/Festivals")] = new(
            EmitKind.Native,
            "$query PLAYER_HEARTS …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        // ── BigCraftable：名字里带 'Seasonal' 时索引被钳到「当季那一组」─────────────
        // Object.cs:1940-1944（共 5 处同名判断）。
        // ⚠ 打在图集资产上的 208 条（SVE 灌木）**未确证**适用 —— 该机制认的是
        //   BigCraftable 的 **Name 字段**，而 `Mods/*` 图集不是 BigCraftable 数据条目。
        //   故 `(Sprite, 季节, "Mods/*")` **有意留空** → 落 Unknown（待查），不算「有」。
        [(VariantCarrier.Entry, VariantDimension.Season, "Data/BigCraftables")] = new(
            EmitKind.Native,
            "Name 加 Seasonal，SpriteIndex 与 4 对齐（同组 4 项 = 四季）",
            "Object.cs:1940-1944",
            "", MergesPatches: true),

        [(VariantCarrier.Sprite, VariantDimension.Season, "Data/BigCraftables")] = new(
            EmitKind.Native,
            "Name 加 Seasonal，SpriteIndex 与 4 对齐（同组 4 项 = 四季）",
            "Object.cs:1940-1944",
            "", MergesPatches: true),

        // ── 自建图集（`Mods/*`）：按季节换图的**原生形式是图集名加季节前缀** ──────────
        // `GetSeasonalTilesheetName`（GameLocation.cs:16439-16447）把文件名开头的
        // spring_/summer_/fall_/winter_ **替换**成当季；`updateSeasonalTileSheets`（:16411）
        // 对地图的每个 TileSheets[].ImageSource 调它。
        // 所以：4 张变体命名为 spring_X / summer_X / fall_X / winter_X，
        //       地图 tilesheet 的 ImageSource 指向 spring_X —— 游戏自己按季节换。零条件。
        // ✅ 与前缀机制的前提**精确吻合**：`:16417` 只在「非 Summit 且 IsOutdoors 且非 Desert」生效，
        //    而 SVE 那 208 条季节灌木**全部**带 `IsOutdoors: true`。
        // ✅ ① **已实现**：`SeasonalSpritePlanner` + `SeasonalSpriteEmitter`（Task 29）。
        // ✅ ② **已实现**：`SeasonalTilesheetEmitter.TryEmit` 写四季图集后，
        //    通过 `MapCache.Set` 把引用地图的 `TileSheet.Image` 改指 `spring_X`，
        //    由 `MapCache.Flush` 统一落盘（测试见 `SeasonalTilesheetTests`）。
        // ⚠ `MergesPatches` 仍留 false：它被 `VariantNormalizer` 消费，而那份报告**只看判定、不看发射结局**，
        //    本格还要「消费方恰好唯一」等前提才成立 —— 算进去会虚报。**实际发射量**看
        //    `AnalysisReport.SeasonalEmission`（按结局统计）。
        [(VariantCarrier.Sprite, VariantDimension.Season, "Mods/*")] = SeasonalImagePlan,

        // ⚠ 这两格与 `Mods/*` 是**同一个结论**：机制按**消费方**判，与资产名的前缀无关 ——
        //   发射器只按 Target 分组，从没限制过 `Mods/*`。原先表里只写了 `Mods/*`，
        //   于是 `LooseSprites/*` / `Tilesheets/*` 的 12 条落进 `Unknown`（"待查"），
        //   而其实机制早就实现了 —— 表漏条目会把「已查清」误报成「没查」。
        [(VariantCarrier.Sprite, VariantDimension.Season, "LooseSprites/*")] = SeasonalImagePlan,
        [(VariantCarrier.Sprite, VariantDimension.Season, "Tilesheets/*")] = SeasonalImagePlan,
        // ⚠ `Maps` 与上面三格**也是同一个结论** —— 载体是 `Sprite`（改的是**图集图片**），
        //   不是 `Map`（那是 EditMap 改图块，另一格、另一个结论）。
        //   实测 SVE 就把图集放在 `Maps/` 下（`Maps/Spring_z_extras`、`Maps/HighlandsBackground`），
        //   被地图的 `TileSheet.Image` 引用 —— 与 `LooseSprites/*` 那批没有区别。
        [(VariantCarrier.Sprite, VariantDimension.Season, "Maps")] = SeasonalImagePlan,

        // ⚠ 自定义资产（`Mods/*`）的**数据条目**：条目由注册该资产的 mod 的 C# 消费，
        //   原版不认识这个资产名 —— 没有原生条件位，也不需要（纯 XNB 场景里那段代码本就不在）。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Mods/*")] = new(
            EmitKind.None, "",
            "自定义资产的条目由注册它的 mod 的 C# 消费，原版不认识该资产名 —— 无原生条件位"),
        [(VariantCarrier.Entry, VariantDimension.Marriage, "Mods/*")] = new(
            EmitKind.None, "", "同上：自定义资产的数据条目无原生条件位"),

        // ── `Data/PassiveFestivals`：条件位是**条目自己的字段** `/<id>/Condition`（任意 GSQ）──
        // 台账确证（docs/research/2026-10-06-condition-consumers.md §2，逐点附源码行号）。
        // ⚠ **只逐格补、不批量**：同台账里的 `Data/Locations` 条件位在 `Forage[]/Condition`
        //   （**数组元素**，我们的写入器穿不进数组），`Data/Characters` 的字段名又不叫 `Condition`
        //   （`UnlockConditions`/`CanSocialize`…）—— 那些判 Native 就是虚报。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/PassiveFestivals")] = new(
            EmitKind.Native, "条目级 `Condition` 字段（任意 GSQ）",
            "docs/research/2026-10-06-condition-consumers.md §2（Data/PassiveFestivals /<id>/Condition）"),

        // ── `Data/Shops`：条件位在**数组元素**上（`<id>/Owners[]/Condition`、`<id>/Items[]/Condition`）──
        // 台账确证（docs/research/2026-10-06-condition-consumers.md §1.2，附源码行号）。
        // ⚠ 按 `Native` 的定义（「**原版**有原生最简形式」）它是 Native；
        //   但**本工具的写入器够不到数组元素**，所以 `Precondition` 如实写明这是
        //   「原版能力」而不是「本工具已实现的收敛」。报告的抬头本来就把两者分开了
        //   （「以上『按发射表推』是**原版有没有机制**的结论」）。
        //   我一度想为了「不虚报」把它判成非 Native —— 那会**把游戏能力说小**，
        //   与抬头的口径打架；正确做法是把限制写进 Precondition。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Shops")] = new(
            EmitKind.Native,
            "条目里写 `<id>/Owners[]/Condition` 或 `<id>/Items[]/Condition`（任意 GSQ）",
            "docs/research/2026-10-06-condition-consumers.md §1.2（ShopBuilder.cs:117/:122, :211）",
            "条件位在**数组元素**上；本工具的写入器目前穿不进数组元素"),

        // ── `Data/ExtraDialogue` / `Data/EngagementDialogue`：**整片**都是 `$query` 宿主 ──
        // 这两格的宿主身份是 2026-10-06 新查出来的：把 `new Dialogue(` / `Dialogue.TryGetDialogue(`
        // 的实参字面量抓出来统计，与 `drawObjectDialogue(`（走 `DialogueBox(string)`，死路）**分开**数 ——
        // `Data/ExtraDialogue` 11 处**全在活路**，`Data/EngagementDialogue` 4 处**也全在活路**，
        // 死路那侧一处都没有 ⇒ 可以当**整片**宿主。
        // ⚠ 同一轮扫描里 `Strings/Characters`（活 18 / 死 24）、`Strings/Locations`（死 121）、
        //   `Strings/UI`（死 21）都是**两条路都有**，所以**没收**它们 —— 同一个资产里有的键走活路、
        //   有的走死路，静态分不出某个键走哪条。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/ExtraDialogue")] =
            DialogueQueryRow("Data/ExtraDialogue", "GameLocation.cs:8483/8487/8491/10000；Game1.cs:8929"),
        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/ExtraDialogue")] =
            DialogueQueryRow("Data/ExtraDialogue", "GameLocation.cs:8483/8487/8491/10000；Game1.cs:8929"),
        [(VariantCarrier.Entry, VariantDimension.Marriage, "Data/ExtraDialogue")] =
            DialogueQueryRow("Data/ExtraDialogue", "GameLocation.cs:8483/8487/8491/10000；Game1.cs:8929"),
        [(VariantCarrier.Entry, VariantDimension.Year, "Data/ExtraDialogue")] =
            DialogueQueryRow("Data/ExtraDialogue", "GameLocation.cs:8483/8487/8491/10000；Game1.cs:8929"),

        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/EngagementDialogue")] =
            DialogueQueryRow("Data/EngagementDialogue", "NPC.cs:1660/1681/3848/3852"),
        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/EngagementDialogue")] =
            DialogueQueryRow("Data/EngagementDialogue", "NPC.cs:1660/1681/3848/3852"),
        [(VariantCarrier.Entry, VariantDimension.Marriage, "Data/EngagementDialogue")] =
            DialogueQueryRow("Data/EngagementDialogue", "NPC.cs:1660/1681/3848/3852"),
        [(VariantCarrier.Entry, VariantDimension.Year, "Data/EngagementDialogue")] =
            DialogueQueryRow("Data/EngagementDialogue", "NPC.cs:1660/1681/3848/3852"),

        // ── `Strings/StringsFromMaps`：`$query` 只在**部分消费点**生效 ────────────────
        // 死路：`Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromMaps:…"))`
        //   → `new DialogueBox(string)`（Game1.cs:9648-9654）—— `$query` 是死文本。
        //   （事件 `message` Event.cs:12479、`GameLocation.cs:9616/9760`、`IslandSouthEastCave.cs` 共 5 处字面量。）
        // 活路：`nPC2.setNewDialogue("Strings\\StringsFromMaps:" + key, add: true)`
        //   （GameLocation.cs:9650）→ `NPC.setNewDialogue(translationKey)` → `new Dialogue(this, key)`
        //   （NPC.cs:4696-4698）—— `$query` **会**执行。
        // ⇒ 同一个资产的两个键**可能各走一条路**，而**光看资产数据分不出某个键走哪条**
        //   （键名由地图里的 `Action` 决定，不在这个资产里）。所以不是 Native。
        [(VariantCarrier.Entry, VariantDimension.Hearts, "Strings/StringsFromMaps")] = new(
            EmitKind.CapacityExceeded,
            "`$query` 只在该资产的**部分**消费点生效",
            "活：NPC.cs:4696-4698（setNewDialogue→new Dialogue）；死：Game1.cs:9648-9654（DialogueBox(string)）",
            "两种消费点（`new Dialogue(...)` 与 `DialogueBox(string)`）在读**同一个键的不同代码**里出现，"
            + "键走哪条由地图 `Action` 决定、光看本资产分不出 ⇒ 装不下全部情形"),
        [(VariantCarrier.Entry, VariantDimension.Marriage, "Strings/StringsFromMaps")] = new(
            EmitKind.CapacityExceeded,
            "同上：`$query` 只在该资产的部分消费点生效",
            "活：NPC.cs:4696-4698；死：Game1.cs:9648-9654",
            "同上：静态分不出某个键是否经 `new Dialogue(...)` 读"),

        // ── `Data/Locations` 的**条目级**条件：落点存在但只在部分字段上 ──────────────
        // 台账确证：`/<id>/Fish[]/Condition`、`/<id>/Forage[]/Condition`、
        // `/<id>/ArtifactSpots[]/Condition`、`/<id>/Music[]/Condition`（各附源码行号）。
        // ⚠ 但 `MusicDefault` 这类**顶层字符串字段没有**条件位 —— 实测 SVE 那条
        //   （`TargetField: ["JojaMart"]` 改 `MusicDefault`）就属于没有的那一类。
        //   所以判 `Native` 必须带上「**只在数组元素上**」这个前提，且写明本工具够不到数组元素。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Locations")] = new(
            EmitKind.Native,
            "条目里的数组元素上写 `Condition`：`<id>/Fish[]/`、`<id>/Forage[]/`、"
            + "`<id>/ArtifactSpots[]/`、`<id>/Music[]/`（任意 GSQ）",
            "docs/research/2026-10-06-condition-consumers.md §1.2（GameLocation.cs:13977, :15142, :14248, :3417）",
            "**只对数组元素成立**：条目的**顶层字段**（如 `MusicDefault`）没有条件位；"
            + "且本工具的写入器够不到数组元素"),

        // ── 载体是 `AssetName`（`Load` = 整文件替换）的几格 ──────────────────────────
        // ★ **结论按消费方走，不按载体走** —— 这是表自己早就定下的规矩
        //   （`SeasonalImagePlan` 那三格就是这么合的）。所以：
        // · 图像资产按季节换 → 与 `(Sprite, 季节)` **同一个机制**，原样复用那条结论；
        // · 对话资产按条件换 → 值层面确有 `$query`，但**整文件替换**这件事本身
        //   要求逐键取到「合并前的旧值」，而 `Load` 是整文件替换、新键在基础资产里根本没有
        //   ⇒ 装不下全部键。
        [(VariantCarrier.AssetName, VariantDimension.Season, "Tilesheets/*")] = SeasonalImagePlan,
        [(VariantCarrier.AssetName, VariantDimension.Season, "Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[] 写 Season",
            "NPC.cs:957-1005",
            "同 `(Sprite, 季节, Characters)`：须把改后贴图另存为独立资产并在 "
            + "`Appearance[].Sprite` 引用（**已实现**，Task 32）；且该 NPC 要在 `Data/Characters` 里有条目"),

        [(VariantCarrier.AssetName, VariantDimension.SeenEvent, "Characters/Dialogue")] = new(
            EmitKind.CapacityExceeded,
            "值层面可用 `$query`，但**整文件替换**表达不了",
            "Dialogue.cs:646-652（$query）；CP 的 Load 是整资产替换",
            "`$query` 的假分支必须是「该键合并前的原有值」，而 `Load` 是**整文件替换**、"
            + "新键在基础资产里根本不存在 ⇒ 装不下全部键"),
        [(VariantCarrier.AssetName, VariantDimension.SeenEvent, "Strings/schedules")] = new(
            EmitKind.CapacityExceeded,
            "同上：值层面可用 `$query`，整文件替换表达不了",
            "Dialogue.cs:646-652；NPC.cs:4356-4381（该资产是 $query 宿主）",
            "同 `(AssetName, 看过的剧情, Characters/Dialogue)`"),

        // ── 值不是 Dialogue 宿主、条目也没有对象：无落点（None 是**结论**）────────────
        // `Data/NPCGiftTastes` 是 `Dictionary<string,string>`（DataLoader.cs:399-404），
        // 值是**空格分隔的物品 ID**（`ArgUtility.SplitBySpace`，NPC.cs:1413-1414）——
        // 既没有条目对象可挂字段，也不是 `new Dialogue(...)` 宿主（`$query` 在这里是死文本）。
        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/NPCGiftTastes")] = new(
            EmitKind.None, "",
            "`Dictionary<string,string>`：值是按空格切的物品 ID 表（NPC.cs:1413-1414），"
            + "无条目对象、非 Dialogue 宿主"),
        // 「整片没有条件位」与维度无关，但表是**精确匹配**的 —— 少一格就会把已查清的
        // 误报成「待查」（实测语料里 SeenEvent 这一格有 1 条）。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/NPCGiftTastes")] = new(
            EmitKind.None, "", "同上：`Dictionary<string,string>` 的物品 ID 表，无 GSQ 落点"),

        // `Strings/animationDescriptions` 是 1.6 的**遗留资产**：全 `sdv_src` 只有
        // `Data/animationDescriptions` 的加载点（DataLoader.cs:75-80），
        // 没有任何 `Strings\\animationDescriptions:` 取用；原版数据里也无引用。
        // ⚠ 这是**否定性证据**（穷举 grep 反编译全源），不是读到一个消费点。
        //   注意别拿 `StardewValley.Tests/SyntaxAbstractor.cs` 那张表当依据 ——
        //   那张表管的是**翻译语法**（值要不要按对话语法抽语法骨架），
        //   与「`$query` 会不会被**执行**」是两回事：它同样把
        //   `Strings/StringsFromMaps` 列为对话语法，而该资产的主要消费点是
        //   `Game1.drawObjectDialogue` → `new DialogueBox(string)`，`$query` 在那里是死文本。
        [(VariantCarrier.Entry, VariantDimension.Season, "Strings/animationDescriptions")] = new(
            EmitKind.None, "",
            "1.6 里没有任何运行时读取点（全 sdv_src 只加载 `Data/animationDescriptions`）"),

        // ── 「值是 `/` 分隔的数据串、键也没有维度」的资产：None 是**结论** ────────────
        // 共同点：`Dictionary<string,string>`，值被按 `/` 切字段读 —— 既没有条目对象可挂
        // GSQ 字段，也不是 `new Dialogue(...)` 宿主（`$query` 塞进去只会当普通文本画出来）。
        // ⚠ `Data/CraftingRecipes`/`Data/CookingRecipes` 要多说一句：它们的**第 5 / 第 4 段**
        //   确实叫「解锁条件」，但语法是**技能要求** `s <技能> <等级>`（或 `default`），
        //   **不是 GSQ** —— `CraftingRecipe.cs:34`（crafting 索引 4）与 `:220-224`
        //   （`TryParseLevelRequirement` 只认 `s`）。

        // `Data/Fish`：`Load<Dictionary<string,string>>`（DataLoader.cs:222-228），值是
        // `<名>/<难度>/<行为>/…` 的鱼数据串（实测原版 `128` = `Pufferfish/80/floater/…`）。
        [(VariantCarrier.Entry, VariantDimension.Season, "Data/Fish")] = new(
            EmitKind.None, "", "`Dictionary<string,string>`；值是 `/` 分隔的鱼数据串，无条目对象、非对话宿主"),
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Fish")] = new(
            EmitKind.None, "", "同上：`Dictionary<string,string>` 的鱼数据串，无 GSQ 落点"),

        // `Data/Monsters`：`Load<Dictionary<string,string>>`（DataLoader.cs:364-367），
        // 值是 `<HP>/<伤害>/…`（实测原版 `Green Slime` = `24/5/0/0/false/…`）。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Monsters")] = new(
            EmitKind.None, "", "`Dictionary<string,string>`；值是 `/` 分隔的怪物数据串，无 GSQ 落点"),

        // `Data/CraftingRecipes`：第 5 段是「解锁条件」但**只认技能要求**，不是 GSQ。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/CraftingRecipes")] = new(
            EmitKind.None, "",
            "值是 `/` 分隔的配方串；第 5 段（`CraftingRecipe.cs:34`）只认技能要求 `s <技能> <等级>`，不是 GSQ"),

        // `Data/mail`：`Load<Dictionary<string,string>>`（DataLoader.cs:334-339），值 = 信正文 + `%item`/`%action`。
        // 「条件化投递」原版走的是 `Data/TriggerActions` 的 `TriggerActionData.Condition` + `Action: AddMail`，
        // **不是**信件条目自己的字段 —— 全库 `CheckConditions` 调用点里没有一处读 `Data/mail`。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/mail")] = new(
            EmitKind.None, "",
            "`Dictionary<string,string>`；条件化投递走 `Data/TriggerActions` 的 Condition，信件条目自身没有 GSQ 位"),

        // `Data/ConcessionTastes`：`List<ConcessionTaste>`，模型只有
        // `Name` / `LovedTags` / `LikedTags` / `DislikedTags`（`StardewValley.GameData.Movies/ConcessionTaste.cs`）
        // —— **一个条件字段都没有**。逐字段读过。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/ConcessionTastes")] = new(
            EmitKind.None, "",
            "`ConcessionTaste` 只有 Name/LovedTags/LikedTags/DislikedTags —— 模型里没有条件字段"),

        // `Data/BigCraftables` 的**好感度**维：与它那一格 `SeenEvent` 同一个理由（模型无条件字段）。
        [(VariantCarrier.Entry, VariantDimension.Hearts, "Data/BigCraftables")] = new(
            EmitKind.None, "", "`BigCraftableData` 模型里没有任何条件字段（与 SeenEvent 那格同一依据）"),

        // `Tilesheets/*` 的好感度维：与它那一格 `SeenEvent` 同一个理由（图集资产的条件位写死在 C# 里）。
        [(VariantCarrier.Sprite, VariantDimension.Hearts, "Tilesheets/*")] = new(
            EmitKind.None, "", "同上：图集资产的消费点写死在 C# 里，没有条件位（季节前缀也只支持季节维）"),

        // ── 三个「模型里根本没有 Condition 字段」的资产：None 是**结论**，不是「没查」 ──
        // 依据是模型类本身（`D:\sdv_gd`），逐个字段读过：
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/BigCraftables")] = new(
            EmitKind.None, "",
            "`BigCraftableData` 模型里没有任何条件字段（全字段：Name/DisplayName/Description/"
            + "Price/Fragility/CanBePlacedOutdoors/CanBePlacedIndoors/Texture/SpriteIndex 等）"),
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/MoviesReactions")] = new(
            EmitKind.None, "",
            "`MovieReaction` 只有 Tag/Response/Whitelist/SpecialResponses/Id —— 没有条件字段；"
            + "且该资产顶层是**数组**，条目也没处挂"),
        [(VariantCarrier.Entry, VariantDimension.Marriage, "Data/animationDescriptions")] = new(
            EmitKind.None, "",
            "`Data/animationDescriptions` 是 `字典<id, 字符串>` —— 值是 `/` 分隔的数据串，"
            + "既没有条目级条件字段，键名也没有任何维度"),

        // ── 地图：只有 ConditionalDoor 与 MapProperties 的 BuildConditions 能装 GSQ ──
        // ConditionalDoor 的效果只有**一个 bit**：Action 版只对「室内 + Doors 属性登记的门」开门，
        // TouchAction 版把玩家顶回去 —— **都改不了图块内容**。全原版仅 AdventureGuild.tbin 2 处。
        [(VariantCarrier.Map, VariantDimension.SeenEvent, "Maps")] = new(
            EmitKind.CapacityExceeded,
            "ConditionalDoor <GSQ> —— 能装任意 GSQ，但效果只是开关门；全原版仅 AdventureGuild.tbin 2 处",
            "GameLocation.cs:9546, :3907, :8221-8248"),

        [(VariantCarrier.Map, VariantDimension.Marriage, "Maps")] = new(
            EmitKind.CapacityExceeded,
            "同上：只有 ConditionalDoor，改不了图块内容",
            "GameLocation.cs:9546, :3907"),

        // ⚠ **降级为「装不下」**：实测语料里 169 个「季节 × 地图」补丁**全是** `SetProperties`（改图块属性），
        //   而 tilesheet 前缀只对**图集指向**生效 —— 命中≈0。前缀本身也仅室外生效
        //   （GameLocation.cs:16417，非 Summit 且 IsOutdoors 且非 Desert；岛图被 IslandLocation 覆写为无）。
        [(VariantCarrier.Map, VariantDimension.Season, "Maps")] = new(
            EmitKind.CapacityExceeded,
            "季节前缀只改图集指向，改不了图块内容 —— 而语料要的正是改图块",
            "GameLocation.cs:16417（前缀仅室外）"),

        // 地图上**没有任何**按年份分叉的原生机制 —— 这是结论，不是「没查」。
        [(VariantCarrier.Map, VariantDimension.Year, "Maps")] = new(
            EmitKind.None,
            "",
            "地图解析入口无年份分支（已普查 GameLocation 的地图加载与属性读取）"),
        // ⚠ `Characters/schedules` 是**纯键链**（`season_day` / `day` / `GOTO`），
        //   键里没有可承载 GSQ 的位置，值也不是 Dialogue 宿主 —— 四维都落 `None`。
        //   这是**结论**，不是「没查」。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Characters/schedules")] = new(
            EmitKind.None, "", "Characters/schedules 是纯键链，键里没有可承载 GSQ 的位置"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Characters/schedules")] = new(
            EmitKind.None, "", "同上：纯键链，无 GSQ 位"),

        [(VariantCarrier.Entry, VariantDimension.Year, "Characters/schedules")] = new(
            EmitKind.None, "", "同上：纯键链，无 GSQ 位"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Characters/schedules")] = new(
            EmitKind.None, "", "同上：纯键链，无 GSQ 位"),
        // ── 立绘/行走图（`Portraits/*` 与 `Characters/*`）：原生落点是 Appearance[] ──────
        // `NPC.ChooseAppearance`（NPC.cs:957-1005）逐条筛 Appearance：
        //   Season（可空，按季）· **Outdoors/Indoors 两个独立开关** · IsIslandAttire
        //   · Condition（**任意 GSQ**）· Precedence（越小越优先，同优先级按 Weight 加权随机，
        //   种子 CreateDaySaveRandom(hash(NPC 名))）。
        // ✅ 它**区分室内外** —— 不像图集季节前缀那格。
        // ✅ 图像层面的重写已实现：`AppearancePlanner` 把改后的贴图另存为独立资产，
        //   并在 Appearance[].Portrait/Sprite 里引用（Task 32）。
        [(VariantCarrier.Sprite, VariantDimension.Season, "Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[] 写 Season（另有 Outdoors/Indoors 开关）",
            "NPC.cs:957-1005",
            "需把改后贴图另存为独立资产并在 Appearance[].Sprite 引用 —— "
            + "**已实现**（Task 32：AppearancePlanner + AppearanceEmitter）。"
            + "⚠ 前提：该 NPC 必须在 `Data/Characters` 里有条目；且 `IsOutdoors` 由 Outdoors/Indoors "
            + "两个**字段**承载（不是条件），新增条目的 Precedence 必须**大于**原版的 −100/−1000"),

        [(VariantCarrier.Sprite, VariantDimension.Season, "Portraits")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[] 写 Season",
            "NPC.cs:957-1005",
            "需把改后贴图另存为独立资产并在 Appearance[].Portrait 引用 —— 同上，**已实现**（Task 32）"),

        [(VariantCarrier.Sprite, VariantDimension.Marriage, "Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_NPC_RELATIONSHIP …",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Sprite, VariantDimension.Marriage, "Portraits")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_NPC_RELATIONSHIP …",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Sprite, VariantDimension.SeenEvent, "Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_HAS_SEEN_EVENT …",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Sprite, VariantDimension.SeenEvent, "Portraits")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_HAS_SEEN_EVENT …",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Entry, VariantDimension.Season, "Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[] 写 Season",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Entry, VariantDimension.Season, "Portraits")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[] 写 Season",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Characters")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_NPC_RELATIONSHIP …",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Portraits")] = new(
            EmitKind.Native,
            "<NPC>.Appearance[].Condition 写 PLAYER_NPC_RELATIONSHIP …",
            "NPC.cs:957-1005",
            "同上：贴图须另存为独立资产"),

        // ── 对话资产（`Characters/Dialogue/*`）：**$query 宿主**，二值维度都装得下 ──
        // 早先它被 `characters/` 那条前缀吞进「立绘」那一格，落点判错。
        // `$query <GSQ>#真|假` 只有两个分支（Dialogue.cs:646-652），四季装不下 —— 故 Season 不收。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Characters/Dialogue")] = new(
            EmitKind.Native,
            "$query PLAYER_HAS_SEEN_EVENT …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        [(VariantCarrier.Entry, VariantDimension.Hearts, "Characters/Dialogue")] = new(
            EmitKind.Native,
            "$query PLAYER_HEARTS …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        [(VariantCarrier.Entry, VariantDimension.Marriage, "Characters/Dialogue")] = new(
            EmitKind.Native,
            "$query PLAYER_NPC_RELATIONSHIP …#<真分支>|<假分支>",
            "Dialogue.cs:646-652"),

        // 对话键的**季节前缀**：`NPC.cs:3881` 先拿 `Game1.currentSeason + "_"` 去试键。
        // 实测原版 `Characters/Dialogue/*.json` 确实有 `<season>_<日>` / `<season>_<星期>` 键
        // （Abigail 150 键里 54 个带季节前缀）。
        // ⚠ **前提：春季可能不加前缀** —— `NPC.cs:3937` 是
        //   `Game1.season != Season.Spring ? Game1.currentSeason : ""`，
        //   而实测四季里 `spring_` 键也明显最少（4 份文件里 2 个 vs 其余 19~22 个）。
        [(VariantCarrier.Entry, VariantDimension.Season, "Characters/Dialogue")] = new(
            EmitKind.Native,
            "把季节写进键名：<season>_<日> / <season>_<星期>",
            "NPC.cs:3881（另 :3937）",
            "春季可能走**无前缀**的键（NPC.cs:3937），四季变体未必都能表达"),

        // ── 已确证**无**机制的几个格子（是结论，不是「没查」）────────────────────
        // `Data/Objects` **只在 `GeodeDrops[]` 与 `GeodeDrops[].StackModifiers[]` 下有**
        // `Condition`（实测原版全库 11 处）；**条目级没有**。
        // 实测踩过：我们曾把条目级 `Condition` 写上 25 个物件，游戏不读、静默失效。
        [(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Objects")] = new(
            EmitKind.None,
            "",
            "原版只在 GeodeDrops[] 下有 Condition；条目级无（实测全库 11 处，无一是条目级）"),

        // 条件化**整图替换**：`GetSeasonKey()` 全库只有 3 处用处，唯一消费点是图集名替换；
        // 原版没有任何「整张地图按条件换」的机制。详见计划 Task 27（SVE 348 条已结案）。
        [(VariantCarrier.AssetName, VariantDimension.SeenEvent, "Maps")] = new(
            EmitKind.None,
            "",
            "原版没有条件化整图替换的机制（GetSeasonKey 全库仅 3 处用处，只用于图集名）"),

        // 图像资产（`LooseSprites/*`、`Tilesheets/*`）没有承载条件的落点 ——
        // 它们的消费点是写死在 C# 里的。
        [(VariantCarrier.Sprite, VariantDimension.SeenEvent, "LooseSprites/*")] = new(
            EmitKind.None, "", "图像资产的消费点写死在 C# 里，没有条件位"),

        [(VariantCarrier.Sprite, VariantDimension.SeenEvent, "Tilesheets/*")] = new(
            EmitKind.None, "", "同上：图集资产没有条件位（季节前缀只支持季节维度）"),
    };

    /// <summary>
    /// 查表。<b>精确匹配</b> <c>(载体, 维度, 资产类型)</c>；
    /// 没收录的格子返回 <see cref="EmitKind.Unknown"/> —— <b>不得当成「无」</b>。
    /// </summary>
    public static EmitPlan Lookup(VariantCarrier carrier, VariantDimension dimension, string? assetType) =>
        assetType != null && Typed.TryGetValue((carrier, dimension, assetType), out EmitPlan plan)
            ? plan
            : EmitPlan.Unknown;

    /// <summary>发射表已收录的格子数（供测试与报告核对，防止表被悄悄改小）。</summary>
    public static int Count => Typed.Count;
}
