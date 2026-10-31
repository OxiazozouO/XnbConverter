using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Conditions;

/// <summary>单个条件令牌 → GSQ 片段。</summary>
/// <remarks>
/// 三条必须遵守的规则（见 research 文档 §一）：
/// ① CP 值侧逗号是"或" → GSQ 同关键字多实参；绝不写成顶层逗号（那是"与"）
/// ② PLAYER_* 的玩家键是必填位置参数
/// ③ 数值型关键字是闭区间，单值须补第二个 max
/// </remarks>
internal static class TokenTranslator
{
    public static GsqTranslation Translate(WhenNode key)
    {
        string name = key.KeyName ?? "";
        string? args = key.Arguments;
        string? rawValue = key.Value;

        // CP 的具名参数 `|contains=x, y`：问"令牌值集合与 x/y 有没有交集"，返回 true/false，
        // JSON 里的值就是拿这个 true/false 去比对。实测出现 589 次，不处理则绝大多数真实补丁翻译不了。
        // args 形如 `hostPlayer |contains=GH1letter`，`|contains=` 之前是键槽（玩家键或名字），之后才是值列表。
        bool negate = false;
        if (TrySplitContains(args, out string head, out string tail))
        {
            if (tail.Length == 0)
                return GsqTranslation.Fail($"条件 `{name}` 的 |contains= 后面没有值");

            // ⚠ JSON 值是**取反开关**，不是占位符：false 表示"没有交集"。
            // 依据：CP 源码 Token.cs:209 `ApplyContains` 返回 true/false，Condition.cs:110 拿声明值去比；
            // 官方文档 tokens.md §contains 的例子写着 `"Year |contains=1": "false"`，注释是 `// NOT year 1`。
            // 真实数据里 126 处声明值是 false —— 当成占位符会静默翻出**取反**的语义。
            if (!bool.TryParse(rawValue, out bool expected))
            {
                return GsqTranslation.Fail(
                    $"条件 `{name}` 用了 |contains=，值必须是 true/false（CP 会拒绝其它值），实际是 `{rawValue}`");
            }
            negate = !expected;

            args = head.Length == 0 ? null : head;   // 键槽：玩家键 或 名字
            rawValue = tail;                         // 值来自 |contains=，JSON 值只管取反
        }

        if (string.IsNullOrWhiteSpace(rawValue))
            return GsqTranslation.Fail($"条件 `{name}` 没有值");

        // CP 用 `Enum.TryParse<ConditionType>(name, ignoreCase: true)` 解析条件名
        // （CP 源码 ConfigFileHandler.cs:108、ScreenManager.cs:280,336），所以令牌名大小写不敏感：
        // 真实数据里 `FarmHouseUpgrade`（4 处）与研究的 `FarmhouseUpgrade` 是同一个令牌。
        // 先确认名字认得，否则下面的玩家键解析会盖住真正的原因（真实数据 23 条 Query 被误报成玩家键问题）。
        if (ConditionVocabulary.Classify(name) != TokenKind.GsqTranslatable)
            return GsqTranslation.Fail(NoTranslationReason(name));

        if (rawValue.Contains("{{"))
            return GsqTranslation.Fail($"条件 `{name}` 的值里嵌套了令牌，无法静态翻译");

        string[] values = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
            return GsqTranslation.Fail($"条件 `{name}` 的值为空");

        // 玩家键：CP 用 currentPlayer/hostPlayer/anyPlayer，GSQ 用 Current/Host/Any（规则 2）。
        // 只有 args 槽**确实是玩家键**的令牌才解析它 —— Hearts/SkillLevel 的 args 是 NPC 名/技能名，
        // Weather 的是地点上下文（research §二：前两者的玩家恒为 Current）。
        string player = "Current";
        if (TakesPlayerKey(name))
        {
            player = MapPlayer(args, out bool playerOk);
            if (!playerOk)
                return GsqTranslation.Fail($"条件 `{name}` 的玩家参数 `{args}` 无法映射到 GSQ");
        }

        GsqTranslation translated = name.ToLowerInvariant() switch
        {
            // —— 无玩家键，纯或列表 ——
            "season" => Ok("SEASON", values),
            "day" => Ok("DAY_OF_MONTH", values),
            "dayofweek" => Ok("DAY_OF_WEEK", values),
            "farmcave" => Ok("FARM_CAVE", values),
            "farmtype" => Ok("FARM_TYPE", values),

            // —— 带地点上下文 ——
            "weather" => Weather(args, values),

            // —— 布尔（无参）——
            "iscommunitycentercomplete" => Ok("IS_COMMUNITY_CENTER_COMPLETE"),
            "isjojamartcomplete" => Ok("IS_JOJA_MART_COMPLETE"),

            // —— 数值区间（规则 3）——
            "year" => Interval("YEAR", values),
            "daysplayed" => Interval("DAYS_PLAYED", values),
            "time" => Interval("TIME", values),
            "farmhouseupgrade" => Interval("PLAYER_FARMHOUSE_UPGRADE", values, player),

            // —— 玩家 + 或列表 ——
            "hasseenevent" => Ok("PLAYER_HAS_SEEN_EVENT", Prepend(player, values)),
            "hascaughtfish" => Ok("PLAYER_HAS_CAUGHT_FISH", Prepend(player, values)),
            "hasconversationtopic" => Ok("PLAYER_HAS_CONVERSATION_TOPIC", Prepend(player, values)),
            "hasdialogueanswer" => Ok("PLAYER_HAS_DIALOGUE_ANSWER", Prepend(player, values)),
            "hasvisitedlocation" => Ok("PLAYER_VISITED_LOCATION", Prepend(player, values)),
            "locationcontext" => Ok("PLAYER_LOCATION_CONTEXT", Prepend(player, values)),
            "locationname" => Ok("PLAYER_LOCATION_NAME", Prepend(player, values)),
            "locationuniquename" => Ok("PLAYER_LOCATION_UNIQUE_NAME", Prepend(player, values)),
            "playergender" => Ok("PLAYER_GENDER", Prepend(player, values)),
            "preferredpet" => Ok("PLAYER_PREFERRED_PET", Prepend(player, values)),

            // —— 玩家 + 或列表 + 固定后缀 ——
            "hasflag" => Ok("PLAYER_HAS_MAIL", Prepend(player, values).Append("Any").ToArray()),
            "hasreadletter" => Ok("PLAYER_HAS_MAIL", Prepend(player, values).Append("Received").ToArray()),

            // —— 玩家 + 单个整串（多值须 ANY）——
            "hascookingrecipe" => SingleString("PLAYER_HAS_COOKING_RECIPE", player, values),
            "hascraftingrecipe" => SingleString("PLAYER_HAS_CRAFTING_RECIPE", player, values),
            "farmname" => SingleStringNoPlayer("FARM_NAME", values),

            // —— 特殊 ——
            "hearts" => Hearts(args, values, player),
            "relationship" => Relationship(args, values, player),
            "spouse" => SpouseOrRoommate("Spouse", "Married", values, player),
            "roommate" => SpouseOrRoommate("Roommate", "Roommate", values, player),
            "skilllevel" => SkillLevel(args, values, player),
            "hasprofession" => HasProfession(player, values),
            "haswalletitem" => WalletItem(values),

            // Classify 已放行，正常到不了这里；留着当兜底。
            _ => GsqTranslation.Fail($"条件 `{name}` 没有已实现的 GSQ 翻译规则"),
        };

        // `|contains=x`: false 是"没有交集"，整条 query 取反（GSQ 的 `!` 前缀，GameStateQuery.cs:2166）。
        return negate && translated.Success && !string.IsNullOrEmpty(translated.Gsq)
            ? GsqTranslation.Ok("!" + translated.Gsq)
            : translated;
    }

    // ----- 辅助方法 -----

    /// <summary>
    /// 拆 CP 的具名参数 <c>|contains=…</c>。返回 <c>false</c> 表示没有这个参数。
    /// <para>
    /// <b>竖线后面的空格可有可无</b> —— 真实语料两种都出现：
    /// <c>hostPlayer |contains=GH1letter</c> 与 <c>anyPlayer| contains=5978939</c>。
    /// CP 的词法器把 <c>|</c> 当分隔符，空格只是排版；<b>原先只认字面 <c>|contains=</c></b>，
    /// 带空格的那一类一律漏掉（实测 SVE 单独一类就有 19 条因此翻不出来）。
    /// </para>
    /// </summary>
    /// <param name="head">竖线之前的键槽（玩家键或名字），已 Trim。</param>
    /// <param name="tail">等号之后的值列表，已 Trim。</param>
    internal static bool TrySplitContains(string? args, out string head, out string tail)
    {
        head = "";
        tail = "";
        if (args == null) return false;

        int bar = args.IndexOf('|');
        if (bar < 0) return false;

        int i = bar + 1;
        while (i < args.Length && char.IsWhiteSpace(args[i])) i++;

        const string Keyword = "contains=";
        if (i + Keyword.Length > args.Length) return false;
        if (!args.AsSpan(i, Keyword.Length).Equals(Keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        head = args[..bar].Trim();
        tail = args[(i + Keyword.Length)..].Trim();
        return true;
    }

    private const string SkillCombat = "PLAYER_COMBAT_LEVEL";
    private const string SkillFarming = "PLAYER_FARMING_LEVEL";
    private const string SkillFishing = "PLAYER_FISHING_LEVEL";
    private const string SkillForaging = "PLAYER_FORAGING_LEVEL";
    private const string SkillMining = "PLAYER_MINING_LEVEL";
    private const string SkillLuck = "PLAYER_LUCK_LEVEL";

    /// <summary>CP 技能名 → GSQ 关键字。CP 取的是含 buff 的等级，所以用**非 BASE** 版。</summary>
    private static readonly Dictionary<string, string> SkillKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Combat"] = SkillCombat,
        ["Farming"] = SkillFarming,
        ["Fishing"] = SkillFishing,
        ["Foraging"] = SkillForaging,
        ["Mining"] = SkillMining,
        ["Luck"] = SkillLuck,
    };

    /// <summary>
    /// args 槽**不是**玩家键的令牌（research §二）。其余已实现令牌的 args 都按玩家键解析。
    /// </summary>
    private static readonly HashSet<string> NonPlayerArgTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // 无参
        "IsCommunityCenterComplete", "IsJojaMartComplete",

        // 世界时间/世界类型，不按玩家算
        "Season", "Day", "DayOfWeek", "Year", "DaysPlayed", "Time", "FarmCave", "FarmType", "FarmName",

        // args 是地点上下文（island/valley），不是玩家键
        "Weather",

        // args 是 NPC 名 / 技能名，玩家恒为 Current
        "Hearts", "Relationship", "SkillLevel",
        // 注：`Spouse`/`Roommate` **不在此表** —— 它们是值令牌，NPC 名在**值侧**；
        //     args 槽（若有）是玩家键，须走正常解析。见 SpouseOrRoommate。
    };

    private static bool TakesPlayerKey(string name) => !NonPlayerArgTokens.Contains(name);

    /// <summary>
    /// CP 的 Weather 可带一个 LocationContext 参数（`island`/`valley`）。
    /// GSQ 的 `WEATHER` 第一参只认 `Here`/`Target`/真实地点名，表达不了上下文（research §四.2），
    /// 所以带参数的 Weather 不翻译；不带的按当前地点 `Here` 翻。
    /// </summary>
    private static GsqTranslation Weather(string? context, string[] values)
    {
        if (!string.IsNullOrWhiteSpace(context))
        {
            return GsqTranslation.Fail(
                $"`Weather` 带了地点上下文 `{context}`：GSQ 的 WEATHER 第一参只认 Here/Target/" +
                "真实地点名，表达不了 CP 的 island/valley 上下文");
        }
        return Ok("WEATHER", Prepend("Here", values));
    }

    /// <summary>
    /// 说清某个令牌为什么翻不了。借 <see cref="ConditionVocabulary"/> 的分类让报告能按类聚合 ——
    /// 否则"无等价物""环境依赖""自定义令牌""pack 自建 DynamicTokens"会全挤在一个桶里。
    /// </summary>
    private static string NoTranslationReason(string name) =>
        ConditionVocabulary.Classify(name) switch
        {
            TokenKind.NoGsqEquivalent => $"条件 `{name}` 在 GSQ 里没有等价关键字",
            TokenKind.Bakeable => $"条件 `{name}` 依赖已装模组或包内文件，应走离线烘焙通道（通道 C），GSQ 翻译器不处理",
            TokenKind.Locale => $"条件 `{name}` 依赖游戏语言，GSQ 没有对应关键字",
            TokenKind.CustomModToken => $"自定义 mod 令牌 `{name}`，GSQ 无法静态求值",
            TokenKind.GsqApproximate => $"条件 `{name}` 只能近似映射（语义有偏差），本工具不自动翻译",
            TokenKind.PureComputation => $"条件 `{name}` 是取值/计算类令牌，没有 GSQ 一对一等价物",
            _ => $"条件 `{name}` 不在 CP 内置令牌表里（内容包 DynamicTokens 自建或拼写有误），没有 GSQ 翻译规则",
        };

    /// <summary>
    /// CP 玩家键 → GSQ 玩家实参。规则 2：PLAYER_* 的玩家键是**必填位置参数**。
    /// CP 用 currentPlayer/hostPlayer/anyPlayer，GSQ 用 Current/Host/Any。
    /// </summary>
    private static string MapPlayer(string? args, out bool ok)
    {
        ok = true;
        if (string.IsNullOrWhiteSpace(args)) return "Current";

        string trimmed = args.Trim();
        switch (trimmed.ToLowerInvariant())
        {
            case "currentplayer": return "Current";
            case "hostplayer": return "Host";
            case "anyplayer": return "Any";
        }

        if (int.TryParse(trimmed, out int id)) return id.ToString();

        ok = false;
        return "";
    }

    private static GsqTranslation Ok(string keyword) => GsqTranslation.Ok(keyword);

    private static GsqTranslation Ok(string keyword, params string[] args) =>
        GsqTranslation.Ok(keyword + " " + string.Join(' ', args));

    /// <summary>
    /// CP 的 <c>HasWalletItem</c> → 等价 GSQ。<b>值要查表</b>（<see cref="WalletItems.Map"/>）。
    /// <para>
    /// <b>为什么以前判成「GSQ 没有等价关键字」是错的</b>：钱包物品就是原版 **power**，
    /// 而 <c>Data/Powers.json</c> 里每个 power **自带</b> <c>UnlockedCondition</c> ——
    /// 那本身就是一句 GSQ。表由 <c>tools/gen-wallet-items.py</c> 从那份**游戏数据**生成，
    /// 并与 <c>Farmer.cs</c> 的属性实现交叉核对过（`Host`/`Current` 的差别是真实的，
    /// 见表内注释）。**不自己发明条件**。
    /// </para>
    /// <para>
    /// ⚠ <b>表外的值一律拒绝</b>：CP 的 <c>HasWalletItem</c> 取值是**固定词表**，
    /// 放行一个它不认的名字，等于把一个 CP 本会拒绝的条件翻译出去。
    /// </para>
    /// <para>
    /// <b>多值</b>（`|contains=A, B`）的 CP 语义是「任一命中」：翻成
    /// <c>ANY "q1" "q2"</c>。语法已用游戏自己的 `GameStateQuery.Parse` 实跑验证。
    /// </para>
    /// </summary>
    private static GsqTranslation WalletItem(string[] values)
    {
        if (values.Length == 0)
            return GsqTranslation.Fail("`HasWalletItem` 的 |contains= 后面没有值");

        var queries = new List<string>(values.Length);
        foreach (string v in values)
        {
            if (!WalletItems.Map.TryGetValue(v, out string? gsq))
                return GsqTranslation.Fail(
                    $"`HasWalletItem` 的值 `{v}` 不在已核对过的钱包物品表里"
                    + $"（共 {WalletItems.Map.Count} 个，取自 `Data/Powers.json`）—— 表外的值不猜");
            queries.Add(gsq);
        }

        if (queries.Count == 1) return GsqTranslation.Ok(queries[0]);
        return GsqTranslation.Ok("ANY " + string.Join(" ",
            queries.Select(q => "\"" + q.Replace("\"", "\\\"") + "\"")));
    }

    private static string[] Prepend(string head, string[] rest) =>
        new[] { head }.Concat(rest).ToArray();

    /// <summary>规则 3：数值型关键字是**闭区间**，单值必须补第二个 max。</summary>
    private static GsqTranslation Interval(string keyword, string[] values) =>
        Interval(keyword, values, null);

    private static GsqTranslation Interval(string keyword, string[] values, string? player)
    {
        // CP 的区间用空格分隔（如 `Time: "600 1200"`），逗号切出来的单值要再按空白拆开
        string[] nums = Flatten(values);
        if (nums.Length > 2)
        {
            return GsqTranslation.Fail(
                $"`{keyword}` 是区间关键字，表达不了 {nums.Length} 个离散值");
        }

        string[] args = nums.Length == 1
            ? new[] { nums[0], nums[0] }   // 单值 → min max 相同
            : nums;

        return player == null
            ? Ok(keyword, args)
            : Ok(keyword, Prepend(player, args));
    }

    /// <summary>把逗号切出来的每个值再按空白拆开（CP 区间写法是空格分隔）。</summary>
    private static string[] Flatten(string[] values) =>
        values
            .SelectMany(v => v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

    /// <summary>单值整串关键字（可含空格，须加引号）。多值表达不了 → 失败。</summary>
    private static GsqTranslation SingleString(string keyword, string player, string[] values)
    {
        if (values.Length > 1)
        {
            return GsqTranslation.Fail(
                $"`{keyword}` 只接受单个整串，{values.Length} 个值须改用 ANY 包装，请人工处理");
        }
        return Ok(keyword, player, $"\"{values[0]}\"");
    }

    /// <summary>无玩家键的单值整串关键字。</summary>
    private static GsqTranslation SingleStringNoPlayer(string keyword, string[] values)
    {
        if (values.Length > 1)
        {
            return GsqTranslation.Fail(
                $"`{keyword}` 只接受单个整串，{values.Length} 个值须改用 ANY 包装，请人工处理");
        }
        return Ok(keyword, $"\"{values[0]}\"");
    }

    private static GsqTranslation Hearts(string? npc, string[] values, string player)
    {
        if (string.IsNullOrWhiteSpace(npc))
            return GsqTranslation.Fail("`Hearts` 需要 NPC 名，须写在键侧（如 `\"Hearts:Abigail\"`）");

        // NPC 名是位置参数，不是键侧 —— 重排后玩家键在最前
        string[] args = Flatten(values);
        if (args.Length > 2)
            return GsqTranslation.Fail("`Hearts` 是区间关键字，表达不了多个离散值");

        if (args.Length == 1) args = new[] { args[0], args[0] };

        return Ok("PLAYER_HEARTS", Prepend(player, Prepend(npc.Trim(), args)));
    }

    private static GsqTranslation Relationship(string? npc, string[] values, string player)
    {
        if (string.IsNullOrWhiteSpace(npc))
            return GsqTranslation.Fail("关系类条件需要 NPC 名，须写在键侧（如 `\"Relationship:June\"`）");

        var types = new List<string>();
        foreach (string v in values)
        {
            switch (v.Trim().ToLowerInvariant())
            {
                case "married":
                    // research §二 陷阱②：CP 的 Married **包含**室友婚，GSQ 的 Married **排除**，
                    // 所以两个类型都要列上（GSQ 同关键字多实参 = 或）
                    types.Add("Married");
                    types.Add("Roommate");
                    break;
                case "friendly":
                    // CP 的 Relationship 令牌直接吐 friendship.Status 的枚举名
                    // （CP 源码 VillagerRelationshipValueProvider.cs:44），SDV 的 FriendshipStatus
                    // 第一个值就是 Friendly；GSQ 的 PLAYER_NPC_RELATIONSHIP 也认它
                    // （GameStateQuery.cs:1527）。真实数据：{"Relationship:Alissa": "Friendly"}
                    types.Add("Friendly");
                    break;
                case "dating":
                    types.Add("Dating");
                    break;
                case "engaged":
                    types.Add("Engaged");
                    break;
                case "divorced":
                    types.Add("Divorced");
                    break;
                case "roommate":
                    types.Add("Roommate");
                    break;
                case "unmet":
                    return GsqTranslation.Fail(
                        "`Unmet` 在 GSQ 里没有对应类型，须改写为 `!PLAYER_HAS_MET`，请人工处理");
                default:
                    return GsqTranslation.Fail($"关系类型 `{v}` 没有 GSQ 对应值");
            }
        }

        return Ok("PLAYER_NPC_RELATIONSHIP", Prepend(player, Prepend(npc.Trim(), types.ToArray())));
    }

    /// <summary>
    /// CP 的 <c>Spouse</c> / <c>Roommate</c> 是**值令牌**：令牌的值就是玩家配偶（室友）的名字，
    /// <b>NPC 名在值侧，不在键侧</b> —— 这与 <see cref="Relationship"/>（NPC 在键侧）相反。
    /// </summary>
    /// <remarks>
    /// 依据（CP 源码反编译 <c>TokenManager.cs:200-201</c>）：
    /// <code>
    /// PerPlayerValueProvider(ConditionType.Roommate, player =>  player.hasRoommate() ? player.spouse : null)
    /// PerPlayerValueProvider(ConditionType.Spouse,   player => !player.hasRoommate() ? player.spouse : null)
    /// </code>
    /// 所以 <c>{"Spouse": "Haley"}</c> 与 <c>{"Spouse |contains=Haley": true}</c> 都是「配偶是 Haley」。
    /// </remarks>
    private static GsqTranslation SpouseOrRoommate(string token, string relationshipType, string[] values, string player)
    {
        string[] npcs = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToArray();
        if (npcs.Length == 0)
            return GsqTranslation.Fail($"`{token}` 的值里没有 NPC 名");

        // ⚠ `PLAYER_NPC_RELATIONSHIP` 的签名是 `<player> <npc> <type>+` —— **一个 NPC、多个类型**
        //（GameStateQuery.cs:1527 的格式串；:1531 的 npcName 取自**单个**参数位）。
        // 多个 NPC 必须用 ANY 分组（GameStateQuery.cs:251-253，实参带引号），
        // **不能**写成 `... A B Married`：那会解析成 npc=A、类型=B/Married，语义全错。
        string[] clauses = npcs
            .Select(npc => $"PLAYER_NPC_RELATIONSHIP {player} {npc} {relationshipType}")
            .ToArray();

        return clauses.Length == 1
            ? GsqTranslation.Ok(clauses[0])
            : GsqTranslation.Ok("ANY " + string.Join(' ', clauses.Select(c => $"\"{c}\"")));
    }

    private static GsqTranslation SkillLevel(string? skill, string[] values, string player)
    {
        if (string.IsNullOrWhiteSpace(skill))
            return GsqTranslation.Fail("`SkillLevel` 需要技能名，须写在键侧（如 `\"SkillLevel:Combat\"`）");

        string skillName = skill.Trim();
        if (!SkillKeywords.TryGetValue(skillName, out string? keyword))
            return GsqTranslation.Fail($"技能 `{skillName}` 没有对应的 GSQ 关键字");

        string[] args = Flatten(values);
        if (args.Length > 2)
            return GsqTranslation.Fail("`SkillLevel` 是区间关键字，表达不了多个离散值");

        if (args.Length == 1) args = new[] { args[0], args[0] };

        return Ok(keyword, Prepend(player, args));
    }

    /// <summary>
    /// CP 职业名要转成游戏整数 ID 才能喂给 GSQ。
    /// research 文档只给出了 3 处改名陷阱与一个例子（Blacksmith → 20），**没有完整映射表**。
    /// 因此这里**一律失败**，宁可不可翻译也不要写错 ID。
    /// </summary>
    private static GsqTranslation HasProfession(string? player, string[] values) =>
        GsqTranslation.Fail(
            "`HasProfession` 需要 CP 职业名到游戏整数 ID 的完整映射表；调研只给出部分，" +
            "为避免写错 ID，本翻译器暂不翻译它");
}
