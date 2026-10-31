using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Cp;

/// <summary>一次 FTM 翻译的结果：CP 形状的 <c>content.json</c> + 必须让人看见的说明。</summary>
public sealed record FtmImportResult(JObject Content, IReadOnlyList<string> Notes);

/// <summary>
/// **把 Farm Type Manager（FTM）内容包翻成 CP 形状的 <c>content.json</c>。**
/// <para>
/// FTM 的 <c>content.json</c> 是另一套格式（顶层 10 个键、四大段 `*_Spawn_Settings`，每个 area 带
/// 地图名 / 矩形坐标 / 逐季物品表）。它**不是 CP 补丁**，所以光靠 <see cref="ContentPack"/> 读不了。
/// 但它要的效果里，**觅食与埋藏物**这两类原版有现成落点：
/// <c>Data/Locations.&lt;地图&gt;.Forage</c>（地表可采集物）与 <c>.ArtifactSpots</c>（挖掘点）——
/// 于是这里把那些 area **翻译成普通的 <c>EditData</c> 补丁**，后面**整条流水线原样复用**
/// （判定、写入、报告都不必知道 FTM 的存在）。
/// </para>
/// <para>
/// <b>换不到的、以及换得有损的，一律如实记在 <see cref="FtmImportResult.Notes"/> 里</b>
/// （见 <c>docs/research/2026-10-11-ftm-can-land-forage-buried.md</c> 的实测数字）：
/// <list type="bullet">
/// <item><b>刷怪 / 刷矿 / 大件</b>三段原版**没有任何落点**（唯一室外刷怪入口是 <c>Farm.cs</c> 里
/// 写死的、只作用于农场的那段），⇒ 整段跳过并报出 area 数；</item>
/// <item><b>坐标</b>表达不了 —— 原版的 <c>Forage</c> / <c>ArtifactSpots</c> 是**地点级**的，
/// 没有"只在 (12,6) 那一格刷"这种槽；</item>
/// <item><b>宝箱</b>（<c>Category: "Chest"</c>）没有落点，跳过并计数；</item>
/// <item>area 上**代不掉的额外条件**（年份 / 星期 / 天气 / 其它 mod 的 CP 条件 /
/// 限次刷新）⇒ **整条 area 不写**（宁可少写不可写错：丢了条件的觅食会变成"天天都有"）。</item>
/// </list>
/// </para>
/// </summary>
public static class FtmPackTranslator
{
    /// <summary>FTM 自己的框架 UniqueID（`ContentPackFor.UniqueID`）。</summary>
    public const string FtmUniqueId = "Esca.FarmTypeManager";

    private static readonly string[] Seasons = ["Spring", "Summer", "Fall", "Winter"];

    /// <summary>
    /// 是 FTM 包就返回一份 CP 形状的 <c>content.json</c>，否则返回 <c>null</c>（调用方走原来那条路）。
    /// </summary>
    public static FtmImportResult? TryImport(string packDirectory, Func<string, bool?>? hasMod = null)
    {
        if (!IsFtmPack(packDirectory)) return null;

        string contentJson = Path.Combine(packDirectory, "content.json");
        if (!File.Exists(contentJson))
            throw new FileNotFoundException("FTM 内容包根目录缺少 content.json", contentJson);

        var notes = new List<string>();
        var changes = new JArray();

        JObject root = JsoncReader.ParseFile(contentJson);

        // 文件级条件：整份文件只在 `File_Conditions` 成立时才生效。**先说它** ——
        // 它表达不了就整包不转（下面的段一个都别写，否则写的就是"FTM 根本不会刷的东西"）。
        JObject? fileWhen = FileWhen(root, notes);
        if (fileWhen != null)
        {
            ImportForage(root, changes, notes, fileWhen, hasMod);
            ImportUnlandable(root, notes);
        }

        return new FtmImportResult(new JObject
        {
            ["Format"] = "1.28.0",
            ["Changes"] = changes,
        }, notes);
    }

    /// <summary>
    /// FTM 的 <c>File_Conditions</c> → CP 形状的 <c>When</c>（要 AND 进这份文件产出的**每一条**补丁）。
    /// <para>
    /// 字段名取自它自带模板：<c>FarmTypes</c> / <c>FarmerNames</c> / <c>SaveFileNames</c> / <c>OtherMods</c>。
    /// <c>FarmTypes</c> 能对到 CP 的 <c>FarmType</c>；<c>OtherMods</c> 对到 <c>HasMod</c>
    /// （**可离线烘焙**，交给 CLI 的 Baker 去折）；<c>FarmerNames</c> / <c>SaveFileNames</c>
    /// 是**运行时**才知道的（玩家名、存档名），静态表达不了 ⇒ 返回 <c>null</c>，
    /// 调用方**整份文件都不转**（宁可少写，不可写错）。
    /// </para>
    /// </summary>
    private static JObject? FileWhen(JObject root, List<string> notes)
    {
        if (root["File_Conditions"] is not JObject fc) return new JObject();
        string? farmTypes = null;

        // ⚠ **形状不认识就整份不转**，别静默当"没有" —— 那会把"只在某个玩家/存档/农场类型下才生效"
        //   的整份文件写成无条件（over-apply），正是这套代码最怕的错。
        //   （形状依据：FTM 自己的模板 `data/default.json`：三个是数组、`OtherMods` 是对象。）
        foreach ((string key, string label) in new[]
                 {
                     ("FarmerNames", "玩家名"), ("SaveFileNames", "存档名"), ("FarmTypes", "农场类型"),
                 })
        {
            if (fc[key] is not JArray arr) continue;      // 缺席 = 不限；**别的形状**下面统一拦
            if (arr.Count == 0) continue;
            if (key == "FarmTypes")
            {
                farmTypes = string.Join(", ", arr.Values<string>());
                continue;
            }
            notes.Add($"FTM `File_Conditions.{key}` 非空（{arr.Count} 项，{label}）—— 那是**运行时**才知道的，"
                      + "静态表达不了 ⇒ **整份文件都没写**（宁可少写，不可写错）");
            return null;
        }
        if (fc["OtherMods"] is not (null or JObject))
        {
            notes.Add("FTM `File_Conditions.OtherMods` 不是对象（形状与 FTM 模板不符）—— 判不了它要什么条件，"
                      + "⇒ **整份文件都没写**（宁可少写，不可写错）");
            return null;
        }
        foreach ((string key, string label) in new[]
                 {
                     ("FarmerNames", "玩家名"), ("SaveFileNames", "存档名"), ("FarmTypes", "农场类型"),
                 })
        {
            if (fc[key] != null && fc[key] is not JArray)
            {
                notes.Add($"FTM `File_Conditions.{key}` 不是数组（形状与 FTM 模板不符，{label}）—— "
                          + "不知道它要什么条件 ⇒ **整份文件都没写**（宁可少写，不可写错）");
                return null;
            }
        }

        var when = new JObject();
        if (farmTypes is string ft) when["FarmType"] = ft;
        foreach (JProperty p in (fc["OtherMods"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            when[$"HasMod |contains={p.Name}"] = p.Value;

        return when;
    }

    /// <summary>
    /// FTM 的 <c>&lt;段&gt;SpawnEnabled</c> 开关：**显式 <c>false</c>** 表示这一段它自己就没开。
    /// 缺省或 <c>true</c> 都当开（与它自带模板的口径一致：设置段存在即生效）。
    /// 关掉的段**不该报成"没写"** —— 那不是我们的损失。
    /// </summary>
    private static bool Enabled(JObject root, string flag, string label, List<string> notes)
    {
        if (root[flag]?.Type != JTokenType.Boolean || root[flag]!.Value<bool>()) return true;
        notes.Add($"FTM `{flag}` 是 `false` ⇒ {label}这一段**该包自己就没开**（FTM 也不会刷），没写");
        return false;
    }

    /// <summary>把文件级条件 AND 进一条补丁的 <c>When</c>（没有就新建）。</summary>
    private static void MergeWhen(JObject patch, JObject? fileWhen)
    {
        if (fileWhen == null || !fileWhen.HasValues) return;
        if (patch["When"] is not JObject existing)
        {
            patch["When"] = fileWhen.DeepClone();
            return;
        }
        foreach (JProperty p in fileWhen.Properties())
        {
            if (existing.Property(p.Name, StringComparison.OrdinalIgnoreCase) == null)
                existing[p.Name] = p.Value.DeepClone();
        }
    }

    private static bool IsFtmPack(string packDirectory)
    {
        string manifest = Path.Combine(packDirectory, "manifest.json");
        if (!File.Exists(manifest)) return false;

        try
        {
            JObject m = JsoncReader.ParseFile(manifest);
            return string.Equals(m["ContentPackFor"]?["UniqueID"]?.Value<string>(), FtmUniqueId,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;   // manifest 读不了就当不是 FTM 包（后面自然报"缺少 content.json"）
        }
    }

    /// <summary>觅食 + 埋藏物 → 对 <c>Data/Locations</c> 的 EditData 补丁。</summary>
    private static void ImportForage(JObject root, JArray changes, List<string> notes, JObject? fileWhen,
        Func<string, bool?>? hasMod)
    {
        if (!Enabled(root, "ForageSpawnEnabled", "觅食", notes)) return;
        if (root["Forage_Spawn_Settings"]?["Areas"] is not JArray areas || areas.Count == 0) return;

        // (地图, 字段) → 该字段这一批新条目
        var forage = new Dictionary<string, JObject>(StringComparer.Ordinal);
        var buried = new Dictionary<string, JObject>(StringComparer.Ordinal);
        int skippedConditions = 0, skippedChests = 0, skippedItems = 0, withCoords = 0;
        int globalFallback = 0;
        int oneShotMail = 0, oneShotSeq = 0, oneShotEmpty = 0, oneShotMails = 0;

        // 原版 `Forage`/`ArtifactSpots` 是**地点级**的槽：FTM 这些"放在哪、放多少"的旋钮它没有。
        // 不读它们不是"少写"（物品与季节是对的），但那几层的**意图确实丢了** —— 如实报出来。
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        void Drop(string key)
        {
            dropped.TryGetValue(key, out int n);
            dropped[key] = n + 1;
        }

        foreach (JToken token in areas)
        {
            if (token is not JObject area) continue;
            string? map = area["MapName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(map)) continue;
            map = map.Replace('\\', '/').Trim('/');

            // area 的额外条件尽量翻成 GSQ（复用 CP 的 When → GSQ 那条既有通路）；
            // 翻不了的**整条不写** —— 丢了条件的觅食会变成"天天都有"，那是写错不是少写。
            string? why = Unexpressible(area["ExtraConditions"] as JObject, hasMod, out string? gsq);
            if (why != null)
            {
                skippedConditions++;
                notes.Add(why == "条件恒为假"
                    ? $"area `{area["UniqueAreaID"]?.Value<string>()}`（{map}）条件恒为假（该 area 本就不适用）"
                    : $"area `{area["UniqueAreaID"]?.Value<string>()}`（{map}）整条没写：额外条件 `{why}` 表达不了");
                continue;
            }

            // 「限次刷新」：限 **1 次**（22/24 个 area）原版有一条路 —— `Data/TriggerActions` 的
            // **一次性动作**（原版 31 条全是 `MarkActionApplied: true`）。于是把「在地图上捡到一次」
            // 换成「条件满足**且到过那张地图**时邮寄一次」：物品、数量、条件都不变，**换的是投递方式**。
            // 限次 > 1 的没有对应机制（`MarkActionApplied` 只有"一次"），继续整条跳过。
            if (LimitOf(area) is int limit)
            {
                if (limit != 1)
                {
                    skippedConditions++;
                    notes.Add($"area `{area["UniqueAreaID"]?.Value<string>()}`（{map}）整条没写：" +
                              $"`限次刷新 {limit}` 表达不了（原版的一次性动作只有「只做一次」这一档）");
                    continue;
                }
                oneShotMail++;
                int emitted = EmitOneShot(area, map, gsq, ++oneShotSeq, changes, fileWhen);
                oneShotMails += emitted;
                if (emitted == 0)
                {
                    // "改成了邮寄"却**一封都没寄出去**（例如整条 area 里只有认不出的写法）——
                    // 这也要说，否则报告会**多报**（说改了 22 个，实际寄出 20 个）。
                    oneShotEmpty++;
                    notes.Add($"area `{area["UniqueAreaID"]?.Value<string>()}`（{map}）标着「一次性」，" +
                              "但里面没有能寄的东西，**一封都没寄**");
                }
                continue;
            }
            if ((area["IncludeCoordinates"] as JArray)?.Count > 0) withCoords++;
            if ((area["ExcludeCoordinates"] as JArray)?.Count > 0) Drop("ExcludeCoordinates");
            if ((area["MinimumSpawnsPerDay"]?.Value<int?>() ?? 0) > 0) Drop("MinimumSpawnsPerDay");
            foreach (string knob in new[]
                     {
                         "IncludeTerrainTypes", "ExcludeTerrainTypes", "StrictTileChecking",
                         "SpawnTiming", "DaysUntilSpawnsExpire",
                     })
            {
                if (area[knob] != null) Drop(knob);
            }

            // area 自己声明的 GSQ 直接接在后面（AND 就是顶层逗号）
            if (JoinGsq(area["ExtraConditions"]?["GameStateQueries"] as JArray) is string raw)
                gsq = gsq == null ? raw : gsq + ", " + raw;

            // 刷新量摊到该 area 的每一条上：原版每个地点每天会按 `Chance` 逐条掷骰，
            // 一圈下来的**期望条数** = 条目数 × 概率 ⇒ 想每天出 m 条就给每条 m/pool。
            // 取 `MaximumSpawnsPerDay`（FTM 里 1 最常见），不取 Minimum：宁可多刷一两点，
            // 也别把"地图上什么都没有"造出来。
            double spawned = MaxSpawns(area);

            foreach (string season in Seasons)
            {
                // ★ area 的季节表为 **null** 时，FTM 回退到段级（全局）表
                //   （反编译 `ModEntry.cs:7967-7982`）。实测 Sunflower Acres：
                //   两个 area 四季全 null，全局是 4/3/4/5 条 —— 只读 area 会得到 0 条。
                JArray? own = area[season + "ItemIndex"] as JArray;
                JArray? list = own ?? root["Forage_Spawn_Settings"]?[season + "ItemIndex"] as JArray;
                if (list == null || list.Count == 0) continue;
                if (own == null) globalFallback++;

                // FTM 运行时只取**当季**表，所以刷新量的分母也必须只算当季；
                // 用四季总数会把当季期望量压到约 1/4（实测 Forest 1：5 条总数 ⇒ 春季每条 0.2，
                // 正确应为 2 条 ⇒ 0.5）。
                int pool = list.Count;

                foreach (JToken item in list)
                {
                    if (DecodeItem(item) is not (List<string> itemIds, string kind, bool isBuried)) { skippedItems++; continue; }
                    if (kind == "Container") { skippedChests++; continue; }

                  foreach (string itemId in itemIds)
                  {
                    string id = $"SdvCpTool_FTM_{map}_{season}_{(isBuried ? "buried" : "forage")}_{list.IndexOf(item)}_{itemIds.IndexOf(itemId)}";
                    var entry = new JObject
                    {
                        ["Id"] = id,
                        ["ItemId"] = itemId,
                        ["Chance"] = isBuried ? 1.0 : Math.Clamp(spawned / Math.Max(1, pool), 0.05, 1.0),
                    };
                    // `Forage` 有 `Season` 槽；`ArtifactSpots` **没有** —— 它的季节只能写进 Condition，
                    // 否则"春季才有的埋藏物"会变成四季都有。
                    if (!isBuried) entry["Season"] = season;
                    string? condition = isBuried
                        ? (gsq == null ? $"SEASON {season}" : $"SEASON {season}, {gsq}")
                        : gsq;
                    if (condition != null) entry["Condition"] = condition;

                    (isBuried ? buried : forage).TryAdd(map, new JObject());
                    var bucket = isBuried ? buried : forage;
                    ((JObject)bucket[map]!)[id] = entry;
                  }
                }
            }
        }

        if (globalFallback > 0)
            notes.Add($"FTM 有 area 的季节物品表为空 ⇒ 按 FTM 语义回退到全局 `Forage_Spawn_Settings` 表");

        foreach ((Dictionary<string, JObject> bucket, string field) in
                 new[] { (forage, "Forage"), (buried, "ArtifactSpots") })
        {
            foreach ((string map, JObject entries) in bucket.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var patch = new JObject
                {
                    ["Action"] = "EditData",
                    ["Target"] = "Data/Locations",
                    ["TargetField"] = new JArray(map, field),
                    ["Entries"] = entries,
                };
                MergeWhen(patch, fileWhen);      // 文件级条件
                changes.Add(patch);
            }
        }

        int landed = forage.Values.Sum(v => v.Count) + buried.Values.Sum(v => v.Count);
        if (landed > 0)
        {
            // 地图数取**两边的并集**（两张表常常落在同一张图上，相加会虚报）
            var maps = new HashSet<string>(forage.Keys, StringComparer.Ordinal);
            maps.UnionWith(buried.Keys);
            notes.Add($"FTM 觅食/埋藏物：**落 {landed} 条**（{forage.Values.Sum(v => v.Count)} 条 As `Forage` + "
                      + $"{buried.Values.Sum(v => v.Count)} 条 As `ArtifactSpots`），覆盖 {maps.Count} 张地图；"
                      + $"其中 {withCoords} 个 area 原本是**按坐标**刷的 —— 原版的 `Forage`/`ArtifactSpots` 是地点级，"
                      + "**坐标这一层换了就丢了**（会在整张地图上刷）");
        }
        foreach (string knob in new[] { "CustomTileIndex", "PercentExtraSpawnsPerForagingLevel" })
        {
            if (root["Forage_Spawn_Settings"]?[knob] != null) Drop("段级 " + knob);
        }
        if (dropped.Count > 0)
        {
            string list = string.Join("、", dropped.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}（{kv.Value} 个 area）"));
            notes.Add($"FTM 觅食/埋藏物有 {dropped.Count} 个**放不下的旋钮**：{list} —— "
                      + "原版 `Forage`/`ArtifactSpots` 是**地点级**的槽（只有物品、季节、概率、条件），"
                      + "「刷在哪些图块上 / 至少几个 / 会不会过期 / 什么时段」这层**没有对应字段** ⇒ "
                      + "**物品与季节对得上，位置与数量这层丢了**");
        }
        if (skippedChests > 0)
            notes.Add($"FTM 容器（宝箱 / 木桶）{skippedChests} 条**没写**：原版没有「按坐标放一个容器」的机制" +
                      "（一次性 area 里的容器例外 —— 那里改寄箱子里的东西，见下）");
        if (skippedItems > 0) notes.Add($"FTM 有 {skippedItems} 条物品写法认不出来（既不是原版下标 / 限定 id，也不是 Buried），**没写**");
        if (skippedConditions > 0) notes.Add($"FTM 有 {skippedConditions} 个 area 因为**条件代不掉**整条没写");
        if (oneShotMail > 0)
            notes.Add($"FTM 有 {oneShotMail} 个**一次性** area（限次刷新 = 1）改成了**邮寄**，"
                      + $"实际寄出 **{oneShotMails} 封**："
                      + "原版没有「地图上一次性生成」的机制，于是用 `Data/TriggerActions` 的一次性动作"
                      + "（原版 31 条全是 MarkActionApplied）+ `Data/Mail` 顶上 —— 条件里额外加了 "
                      + "`PLAYER_VISITED_LOCATION`，**到过那张地图之后**才会寄到，"
                      + "物品与数量与原 area 一致。⚠ **投递方式变了**（不是在地图上捡到的）："
                      + "不接受就把 `Data/TriggerActions.json` 与 `Data/Mail.json` 里 "
                      + "Id 以 `SdvCpTool_FTM_` 开头的那几条删掉");
    }

    /// <summary>三大段没有原版落点 —— 只报数，不写。</summary>
    private static void ImportUnlandable(JObject root, List<string> notes)
    {
        foreach ((string section, string flag, string label) in new[]
                 {
                     ("Monster_Spawn_Settings", "MonsterSpawnEnabled", "刷怪"),
                     ("Ore_Spawn_Settings", "OreSpawnEnabled", "刷矿"),
                     ("Large_Object_Spawn_Settings", "LargeObjectSpawnEnabled", "大件"),
                 })
        {
            // 这一段它自己就关掉了 ⇒ 不是我们的损失，连"没写"都不该报
            if (!Enabled(root, flag, label, notes)) continue;
            if (root[section]?["Areas"] is not JArray areas || areas.Count == 0) continue;
            var maps = areas.OfType<JObject>()
                .Select(a => a["MapName"]?.Value<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            notes.Add($"FTM {label} {areas.Count} 个 area（{maps.Count} 张地图）**没写**："
                      + "原版没有数据驱动的入口（唯一室外刷怪入口写死在 `Farm.cs` 里、只作用于农场）");
        }
    }

    /// <summary>
    /// 认得出就返回 <c>(物品 id, 写法类别, 是否埋藏)</c>；认不出返回 <c>null</c>。
    /// <list type="bullet">
    /// <item><c>16</c>（数字）→ <c>(O)16</c>（原版物品下标）</item>
    /// <item><c>"FlashShifter.…CP_Pear"</c> / <c>"Big Conch"</c>（字符串）→ 原样（限定 id 或物品名，
    /// 1.6 的 <c>ItemRegistry</c> 两种都认）</item>
    /// <item><c>{"Category":"Buried","contents":[…]}</c> → 埋藏物，进 <c>ArtifactSpots</c></item>
    /// <item><c>{"Category":"None","category":"weapon","name":"Lava Katana"}</c> → <c>(W)Lava Katana</c></item>
    /// <item><c>{"Category":"Chest",…}</c> → 类别记 <c>Chest</c>，调用方跳过</item>
    /// </list>
    /// </summary>
    private static (List<string> Ids, string Kind, bool IsBuried)? DecodeItem(JToken item)
    {
        if (item.Type == JTokenType.Integer) return ([( "(O)" + item.Value<long>() )], "Index", false);
        if (item.Type == JTokenType.String)
        {
            string text = item.Value<string>() ?? "";
            return text.Length == 0 ? null : ([text], "Name", false);
        }
        if (item is not JObject obj) return null;

        // ⚠ FTM 里同一个意思有两套大小写（`Category` / `category`），两种都得认。
        string category = (obj["Category"] ?? obj["category"])?.Value<string>()?.ToLowerInvariant() ?? "";

        if (category == "buried")
        {
            // ⚠ **不止一样**：实测 40 条 Buried 带 **2 个** contents（如 `[390, 390]`）。
            //   取第一个就静默丢一半 —— 所以这里返回**全部（去重后）**。
            var ids = new List<string>();
            foreach (JToken inner in (obj["contents"] ?? obj["Contents"]) as JArray ?? new JArray())
                if (DecodeItem(inner) is (List<string> got, _, _))
                    foreach (string id in got)
                        if (!ids.Contains(id)) ids.Add(id);
            return ids.Count == 0 ? null : (ids, "Buried", true);
        }

        // 容器（宝箱 / 木桶）：`{category, contents:[{category,name,Stack,PercentChanceToSpawn}…]}`
        // —— 原版没有"按坐标放一个容器"的机制，**整类跳过**（调用方按 `Container` 计数报出）。
        if ((obj["contents"] ?? obj["Contents"]) is JArray)
            return (new List<string>(), "Container", false);

        string? name = (obj["Name"] ?? obj["name"])?.Value<string>();
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (category == "weapon") return (["(W)" + name], "Weapon", false);
        return ([name], "Name", false);       // 其余按物品名，交给 ItemRegistry 解析
    }

    /// <summary>
    /// 把 area 的 `ExtraConditions` 翻成 GSQ。**复用 CP 的 `When` → GSQ 那条既有通路**
    /// （`WhenParser` + `GsqTranslator`，当年为补丁的 `When` 写的）—— FTM 这几个字段
    /// 与 CP 的 `When` 是同义的，只是换了名字：
    /// <list type="bullet">
    /// <item><c>Days</c> → CP <c>DayOfWeek</c>；<c>Years</c> → CP <c>Year</c>；
    /// <c>WeatherToday</c> → CP <c>Weather</c>；<c>CPConditions</c> 本来就是 CP 的 <c>When</c> 对象 ✓</item>
    /// <item><c>Seasons</c> 不进 GSQ：它由 `Forage.Season` / `ArtifactSpots` 的 `SEASON` 条件各管各的</item>
    /// </list>
    /// 返回非 null = **代不掉**的原因（调用方整条 area 不写）。
    /// </summary>
    private static string? Unexpressible(JObject? extra, Func<string, bool?>? hasMod, out string? gsq)
    {
        gsq = null;
        if (extra == null) return null;

        if (IsSet(extra["WeatherYesterday"])) return "昨天天气";           // GSQ 的 WEATHER 只问"今天"
        if (IsSet(extra["WeatherTomorrow"])) return "明天天气";

        // ⚠ 值都**拼成逗号串**再交给翻译器：直接塞 JArray 会被 `ToString()` 成一段
        //   带换行与引号的废条件（实测踩过）。
        // ⚠ `Days` 是**日期（几号）**不是星期几 —— 实测取值全是 `1..28` 与 `13-15` 这类区间，
        //   所以对应 CP 的 `Day`（`DAY_OF_MONTH`），不是 `DayOfWeek`（一开始就错标成"星期"了）。
        var when = new JObject();
        // ★ FTM 的 `Days` **可以写区间**（`"1-4"`）—— 原样拼出去会得到 `DAY_OF_MONTH 1-4`，
        //   而 GSQ 的 `DAY_OF_MONTH` 只认单个日子（`Helpers.AnyArgMatches`：多参数=任一命中，
    //   **没有区间语法**）⇒ 游戏报
    //   `Failed parsing condition 'DAY_OF_MONTH 1-4': '1-4' isn't a valid day of month`
    //   ⇒ 那条觅食条目的条件整条失效（实机日志 121 行、产出树 **48 条**）。
        //   展开成离散天数即可，语义不变（「这几号里任一天」）。
        if (extra["Days"] is JArray days && days.Count > 0)
            when["Day"] = string.Join(", ", ExpandDayRanges(days));
        // ★ FTM 的 `Years` 可以写 `"2+"`（「2 年及以后」）—— 这个缺口**真出现了**（2026-10-15 实机
        //   72 行 `Failed parsing condition 'YEAR 2+ 2+'`）：走 CP 的 `Year` 那条路会把它拼成
        //   min=max=`2+`（非法 GSQ）。
        //   **游戏源码**：`YEAR <min> [max]` 的 **max 默认 `int.MaxValue`**（`GameStateQuery.cs:411-423`）
        //   ⇒ `"2+"` 正好是 `YEAR 2`（上无界）。⚠ 它与 CP 的 `Year: 2`（**恰为** 2 年 ⇒ `YEAR 2 2`）
        //   **不同义**，所以这一档**不走 CP 那条路**，直接拼成 GSQ 子句，最后与其余条件 AND 起来。
        string? yearGsq = null;
        if (extra["Years"] is JArray years && years.Count > 0)
        {
            var openEnded = new List<int>();
            var exact = new List<int>();
            foreach (JToken y in years)
            {
                string text = (y.ToString() ?? "").Trim();
                if (text.EndsWith("+", StringComparison.Ordinal) && int.TryParse(text[..^1], out int from))
                    openEnded.Add(from);
                else if (int.TryParse(text, out int one))
                    exact.Add(one);
                else
                    return $"`Years` 的取值认不出（{text}）";   // 不猜
            }
            if (openEnded.Count == 0)
            {
                when["Year"] = string.Join(", ", exact);
            }
            else
            {
                var clauses = new List<string>();
                foreach (int n in openEnded) clauses.Add($"YEAR {n}");        // max 缺省 = 上无界
                foreach (int n in exact) clauses.Add($"YEAR {n} {n}");       // 恰为 n 年
                yearGsq = clauses.Count == 1
                    ? clauses[0]
                    : "ANY " + string.Join(" ", clauses.Select(c => $"\"{c}\""));
            }
        }
        if (extra["WeatherToday"] is JArray weather && weather.Count > 0)
            when["Weather"] = string.Join(", ", weather.Select(w => w.ToString()));
        if (extra["CPConditions"] is JObject cp)
            foreach (JProperty prop in cp.Properties()) when[prop.Name] = prop.Value.DeepClone();

        // ★ `CPConditions.HasMod` 是**离线可判**的（我们有已装模组索引）：判真就从条件里去掉，
        //   判假则整个 area 本就不生效。交给 GSQ 翻译器只会得到“表达不了”并白丢内容
        //   （实测 Secret Wood Weeds 的 `mistyspring.ItemExtensions`，4 条季节物品）。
        if (hasMod != null)
        {
            foreach (JProperty prop in when.Properties().ToList())
            {
                string bare = prop.Name.TrimStart('!').Trim();
                int cut = bare.IndexOfAny(new[] { ':', '|' });
                string name = (cut >= 0 ? bare[..cut] : bare).Trim();
                if (!name.Equals("HasMod", StringComparison.OrdinalIgnoreCase)) continue;

                string tail = cut >= 0 ? bare[cut..] : "";
                int containsAt = tail.IndexOf("contains=", StringComparison.OrdinalIgnoreCase);
                string ids = containsAt >= 0
                    ? tail[(containsAt + "contains=".Length)..].Trim()
                    : prop.Value.Type == JTokenType.String ? (prop.Value.Value<string>() ?? "").Trim() : "";
                if (ids.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase))
                    ids = ids["|contains=".Length..].Trim();
                if (ids.Length == 0) continue;   // 形状认不出：留给 GSQ 翻译器报

                string[] list = ids.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var known = new List<bool>(list.Length);
                bool unknown = false;
                foreach (string id in list)
                {
                    bool? installed = hasMod(id);
                    if (installed == null) { unknown = true; break; }
                    known.Add(installed.Value);
                }
                if (unknown) continue;

                bool contains = known.Any(v => v);
                if (prop.Name.StartsWith('!')) contains = !contains;
                bool expected = prop.Value.Type == JTokenType.Boolean
                    ? prop.Value.Value<bool>()
                    : prop.Value.Type == JTokenType.String
                      && bool.TryParse(prop.Value.Value<string>(), out bool parsed) ? parsed : true;
                if (contains != expected) return "条件恒为假";
                prop.Remove();
            }
        }

        if (when.Count == 0 && yearGsq == null) return null;

        string? rest = null;
        if (when.Count > 0)
        {
            GsqTranslation translated = GsqTranslator.Translate(WhenParser.Parse(when));
            if (!translated.Success) return $"条件翻不成 GSQ（{translated.FailureReason}）";
            if (translated.NeverApplies) return "条件恒为假";
            rest = string.IsNullOrEmpty(translated.Gsq) ? null : translated.Gsq;
        }
        // GSQ 里**逗号 = AND**（`GameStateQuery.cs` 的组合子那一带）⇒ 两段直接接起来。
        gsq = rest == null ? yearGsq : yearGsq == null ? rest : $"{rest}, {yearGsq}";
        return null;
    }

    /// <summary>
    /// FTM `Days` 的值 → 离散天数：`"1-4"` ⇒ `1,2,3,4`；单个 `"13"` 原样。
    /// 区间只在 **`1..28`**（FTM 的日子范围）内展开，越界或形状不认识的原样留着
    /// —— 宁可让游戏报错，也不替作者猜。
    /// </summary>
    private static IEnumerable<string> ExpandDayRanges(JArray values)
    {
        foreach (JToken token in values)
        {
            string text = token.ToString().Trim();

            int dash = text.IndexOf('-');
            if (dash > 0
                && int.TryParse(text[..dash].Trim(), out int low)
                && int.TryParse(text[(dash + 1)..].Trim(), out int high)
                && low >= 1 && high <= 28 && low <= high)
            {
                for (int day = low; day <= high; day++) yield return day.ToString();
                continue;
            }

            yield return text;   // 单个日子（`"13"`）或认不出的形状：原样留着，不替作者猜
        }
    }

    /// <summary>area 声明的刷新次数上限（`LimitedNumberOfSpawns`）；没写返回 <c>null</c>。</summary>
    private static int? LimitOf(JObject area)
    {
        JToken? v = (area["ExtraConditions"] as JObject)?["LimitedNumberOfSpawns"];
        if (v == null || v.Type == JTokenType.Null) return null;
        long? n = v.Type == JTokenType.Integer ? v.Value<long>() : null;
        return n is > 0 ? (int)Math.Min(n.Value, int.MaxValue) : null;
    }

    /// <summary>
    /// 一次性 area（限次刷新 = 1）→ `Data/Mail` + `Data/TriggerActions` 各一条/季。
    /// <para>
    /// 条件 = area 自己的条件 + <c>SEASON &lt;季节&gt;</c> + <c>PLAYER_VISITED_LOCATION Current &lt;地图&gt;</c>；
    /// 动作 = <c>AddMail Current &lt;邮件 id&gt;</c>、<c>MarkActionApplied: true</c>（一次；原版 31 条都这样）。
    /// </para>
    /// </summary>
    /// <summary>返回**真的寄出去几封**（0 = 这条 area 里没有能寄的东西）。</summary>
    private static int EmitOneShot(JObject area, string map, string? gsq, int seq, JArray changes,
        JObject? fileWhen)
    {
        string areaName = area["UniqueAreaID"]?.Value<string>() ?? map;
        int emitted = 0;

        foreach (string season in Seasons)
        {
            if (area[season + "ItemIndex"] is not JArray list) continue;

            // ⚠ 附件语法是 `%item id <id> <数量> … %%`，而**一个 `%item … %%` 段只给一样东西**：
            //   段里写 `A 1 B 1` 时游戏会**随机挑一对**（`LetterViewerMenu.cs:322-330`），
            //   也就是"给 N 样里的随机一样"。FTM 那边的意图是这些都会刷出来 —— 所以**每样一段**。
            // ⚠ 一次性 area 里常常**只有一个宝箱**（如「Diamond Wand Chest」）—— 箱子本身搬不进原版，
            //   但**箱子里的东西**正是玩家该拿到的那一样：所以把 contents 摊平寄过去。
            var segments = new List<string>();
            foreach (JToken item in list)
            {
                if (DecodeItem(item) is not (List<string> ids, string kind, _)) continue;

                // ⚠ 这里**必须写大括号**：写成 `if (A) foreach (…) if (…) …; else foreach …;`
                //   时，`else` 会按"悬挂 else"规则绑到**最内层那个 if** 上 ——
                //   于是"非容器"这一支整个不执行（实测：22 个一次性 area 里只有 4 个宝箱的
                //   寄出来了，另外 18 个一条邮件都没有，而且**一声不响**）。
                if (kind == "Container" && item is JObject box)
                {
                    foreach (JToken inner in (box["contents"] ?? box["Contents"]) as JArray ?? new JArray())
                    {
                        if (DecodeItem(inner) is not (List<string> innerIds, _, _)) continue;
                        foreach (string boxed in innerIds)
                            if (!segments.Contains($"%item id {boxed} 1 %%"))
                                segments.Add($"%item id {boxed} 1 %%");
                    }
                }
                else
                {
                    foreach (string id in ids)
                        if (!segments.Contains($"%item id {id} 1 %%"))
                            segments.Add($"%item id {id} 1 %%");
                }
            }
            if (segments.Count == 0) continue;

            // ⚠ id 里**必须带 area 序号**：同一张地图同一季常有**多个**一次性 area
            //（实测 `Custom_ForestWest` 有 7 个）—— 只用 `地图_季节` 的话后一个会把前一个
            // 的邮件**覆盖掉**，前一个的物品静默消失。
            string mailId = $"SdvCpTool_FTM_{map}_{season}_{seq}";
            string triggerId = $"SdvCpTool_FTM_mail_{map}_{season}_{seq}";
            string condition = string.Join(", ", new[]
            {
                $"SEASON {season}",
                $"PLAYER_VISITED_LOCATION Current {map}",
                gsq,
            }.Where(x => !string.IsNullOrEmpty(x)));

            var trigger = new JObject
            {
                ["Action"] = "EditData",
                ["Target"] = "Data/TriggerActions",
                ["Entries"] = new JObject
                {
                    [triggerId] = new JObject
                    {
                        ["Id"] = triggerId,
                        ["Trigger"] = "DayEnding",         // 与官方那 24 条邮件一致：夜里入箱、次日早晨看到
                        ["Condition"] = condition,
                        ["Action"] = $"AddMail Current {mailId}",
                        ["MarkActionApplied"] = true,
                    },
                },
            };
            // 文件级条件挂在**补丁**上（CP 形状 ⇒ 走既有通路；`HasMod` 那类由 Baker 烘焙），
            // 不往 TriggerActions 的 `Condition` 里塞 —— 那个字段是**原版 GSQ**，不认 `HasMod`。
            MergeWhen(trigger, fileWhen);
            changes.Add(trigger);
            emitted++;
            changes.Add(new JObject
            {
                ["Action"] = "EditData",
                ["Target"] = "Data/Mail",
                ["Entries"] = new JObject
                {
                    [mailId] = $"{areaName} 的东西已经送到。{string.Join("", segments)}[#]{areaName}",
                },
            });
        }

        return emitted;
    }

    private static bool IsSet(JToken? v) => v switch
    {
        JArray arr => arr.Count > 0,
        JObject o => o.Properties().Any(),
        JValue { Type: JTokenType.Boolean } b => b.Value<bool>(),
        JValue { Type: JTokenType.String } s => !string.IsNullOrWhiteSpace(s.Value<string>()),
        JValue { Type: JTokenType.Integer } i => i.Value<long>() != 0,
        { Type: JTokenType.Null } => false,
        null => false,
        _ => true,
    };

    private static string? JoinGsq(JArray? queries)
    {
        List<string> parts = queries?.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>area 声明的每日刷新上限（`MaximumSpawnsPerDay`），认不出就当 1。</summary>
    private static double MaxSpawns(JObject area)
    {
        double? v = area["MaximumSpawnsPerDay"]?.Value<double?>();
        return v is > 0 ? Math.Min(v.Value, 50) : 1.0;
    }

}
