using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 一个条件落点条目在<b>本次运行</b>里的条件累积状态。用来区分两种"已有条件"：
/// <list type="bullet">
/// <item><b>资产自带</b>（首次触碰时从数据里读到的值，<see cref="BaseCondition"/>）—— 补丁是在它之上追加限定，做 <b>AND</b>。</item>
/// <item><b>本次运行前序补丁写的</b>（<see cref="PatchConditions"/>）—— 多条补丁各自声明"这个条目该存在"，任一成立即可，做 <b>OR</b>。</item>
/// </list>
/// </summary>
public sealed class LandingState
{
    /// <summary>资产自带的条件（首次触碰时从数据里读到的值）。空表示条目本来无条件。</summary>
    public string? BaseCondition { get; set; }

    /// <summary>本次运行各补丁贡献的条件，按写入顺序去重。</summary>
    public List<string> PatchConditions { get; } = new();

    /// <summary>是否已被本次运行触碰过 —— 决定 <see cref="BaseCondition"/> 是否已初始化。</summary>
    public bool Touched { get; set; }

    /// <summary>
    /// 字典/数组落点下每个子条目的独立累积状态。
    /// <para>近似说明：字典落点（如 `Data/Weddings.Attendees`）多条补丁写同一子键时，
    /// 必须按**子键**各自做 OR/AND；共用一个 <see cref="LandingState"/> 会互相覆盖。</para>
    /// </summary>
    public Dictionary<string, LandingState> Children { get; } = new(StringComparer.Ordinal);

    public LandingState Child(string key)
    {
        if (!Children.TryGetValue(key, out LandingState? child))
            Children[key] = child = new LandingState();
        return child;
    }

    /// <summary>复制一份，供补丁失败时丢弃这次累积（不污染已提交的追踪状态）。</summary>
    public LandingState Clone()
    {
        var copy = new LandingState { BaseCondition = BaseCondition, Touched = Touched };
        copy.PatchConditions.AddRange(PatchConditions);
        foreach ((string key, LandingState child) in Children)
            copy.Children[key] = child.Clone();
        return copy;
    }
}

/// <summary>把 GSQ 条件写进目标资产的落点（通道 A）。</summary>
public static class ConditionWriter
{
    /// <summary>
    /// 计算一条补丁里、条件要落到哪些条目上。<paramref name="landing"/> 为
    /// <see cref="LandingKind.EntryKeyConditions"/> 时返回补丁 <c>Entries</c> 的各个 key；
    /// 为 <see cref="LandingKind.EntryConditionField"/> 时返回条目的键路径。
    /// 返回空表示无法从补丁结构确定落点（调用方应据此失败，而不是猜）。
    /// </summary>
    public static IReadOnlyList<string> EntryPaths(JObject patch, LandingKind landing)
    {
        var paths = new List<string>();

        if (landing == LandingKind.EntryKeyConditions)
        {
            if (patch["Entries"] is JObject keys)
                foreach (JProperty p in keys.Properties()) paths.Add(p.Name);
            return paths;
        }

        if (landing != LandingKind.EntryConditionField) return paths;

        // 有 TargetField：TargetField 指到的那一条就是被改写的条目（如 Data/Characters 的 ["June"]）。
        // ⚠ 分隔符不能用 `.`：Data/Shops 的条目 ID 本身带点（`MadDog.HashtagBearFam_GunShop`），
        //   按点切会把一个 ID 拆成多段，直接“定位不到”。这里用单元分隔符，`ResolvePath` 认它。
        if (patch["TargetField"] is JArray tf)
        {
            string entry = string.Join(PathSep,
                tf.Select(t => t.Value<string>() ?? "").Where(s => s.Length > 0));
            if (entry.Length > 0) paths.Add(entry);
            return paths;
        }

        // 没有 TargetField：Entries 的每个 key 是一个条目；Fields 的每个「叶子对象」是一个条目。
        if (patch["Entries"] is JObject entries)
            foreach (JProperty p in entries.Properties()) paths.Add(p.Name);

        if (patch["Fields"] is JObject fields)
            CollectFieldLeaves(fields, "", paths);

        return paths;
    }

    /// <summary>
    /// 按补丁结构算出条件要落到哪些条目，再逐个核对「那个位置游戏是否真的读 <c>Condition</c>」。
    /// <b>返回 null = 落得下去；非 null = 落不下去的原因。</b>
    /// <para>
    /// <b>判定与写入必须共用这一个函数</b> —— 否则就会出现「判定说能写、写入说不能」，
    /// 而那种不一致在报告里表现为<b>写入失败</b>（实测 SVE 64 条里有 32 条、RSV 59 条里有 3 条
    /// 就是这么来的），把「判定不准」伪装成「写入出问题」。
    /// </para>
    /// </summary>
    public static string? RefusalFor(JToken data, JObject patch, LandingKind landing,
        string? assetName = null)
    {
        if (landing != LandingKind.EntryConditionField && landing != LandingKind.EntryKeyConditions)
            return null;

        IReadOnlyList<string> paths = EntryPaths(patch, landing);

        // ⚠ 算不出落点 = 补丁**没有任何改动**（CP 下也是空操作）——
        //   写入器必须同样**不失败**（见 `PatchApplier` 里那一处），这里才与它一致。
        //   实测：语料里对「无 Entries/Fields 却有 When」的补丁，
        //   `EntryKeyConditions` 那 2 条被判 `Converted` 但**写入失败** ——
        //   根因就是写入器当时对 `paths.Count == 0` 直接 Fail，与本类的设计意图相反。
        if (paths.Count == 0) return null;

        // `EntryKeyConditions` 的条件写在**键名**里（`/G <GSQ>` 段），不去查条目里有没有字段。
        if (landing == LandingKind.EntryKeyConditions) return null;

        // ⚠ 核对必须落在**合并之后**的数据上：写入器是「先 `EditDataMerger.Apply`，再把条件写进合并结果」
        // （`PatchApplier.ApplyEditData`）。拿合并**前**的数据核对，本条补丁**自己新建**的条目
        // 会被判成「定位不到」—— 实测 RSV `Data/Weddings` 的 `Attendees.<NPC>` 就是这么被误杀 17 条的。
        IReadOnlyList<string> mergeNotes = Array.Empty<string>();
        if (patch["Entries"] != null || patch["Fields"] != null || patch["TargetField"] != null)
        {
            MergeOutcome merged = EditDataMerger.Apply(data, patch, assetName);
            // 合并本身就失败 ⇒ 这条补丁无论如何都写不出去，交给写入路径报**真实**缘由，
            // 不在这里换成一条落点说明（那会把真正的错因盖掉）。
            if (!merged.Success) return null;
            data = merged.Data!;
            mergeNotes = merged.Notes;
        }

        foreach (string path in paths)
        {
            JToken? entry = ResolvePath(data, path);

            // ★ **数组落点**：`TargetField` 指到"数组字段"（如 `Data/Minecarts` 的
            //   `["Default","Destinations"]`），补丁用 `Entries` 按键往里加/改条目 ⇒
            //   条件要落到**那几个条目自己的** `Condition` 上（游戏就是这么逐条查的：
            //   `GameLocation.cs:10246` 对每个 `MinecartDestinationData` 查 `destination.Condition`）。
            //   ⚠ 只落**本条补丁 `Entries` 里那几个 Id** 的条目 —— 铺给全数组会让原版条目也变成有条件。
            if (entry is JArray array)
            {
                if (RefuseArrayItems(array, path, patch, assetName) is string arrayRefusal)
                    return arrayRefusal;
                continue;
            }

            if (entry is not JObject obj)
            {
                // ⚠ 「定位不到」**不一定**是目标真的不在数据里 —— 更常见的是**这条补丁的合并
                //   **自己把条目丢了**（值里含代不掉的令牌 ⇒ 逐条丢弃，见 `EditDataMerger`）。
                //   不把合并的说明带上，读报告的人会去找一个不存在的落点问题。
                //   实测：SVE 的 2 个被动节日（值里有 `{{Season}}`）与聚宝盆的 1 条特殊订单
                //   （值里有 `{{GeorgeSpecialOrderRoses}}`）就是这么被误读成"定位不到"的。
                string why = mergeNotes.Count > 0
                    ? $"（这条补丁的合并把条目丢了：{string.Join("；", mergeNotes)}）"
                    : "";
                return $"定位不到条件落点条目：`{DisplayPath(path)}`{why}";
            }

            // ★ 字典落点（近似/低风险，2026-10-20）：`TargetField: ["Attendees"]` +
            //   `Entries: { "<attendeeId>": {...} }`。原版 `WeddingData.Attendees` 是
            //   `Dictionary<string, WeddingAttendeeData>`，条件应落到每个 attendee 的
            //   `Condition`，而不是字典本身。模型背书见 ArrayElementConditionSlots。
            //   ⚠ 同一 attendee 被多条补丁写条件时，这里没有 per-child LandingState，
            //     目前是“后写覆盖”；先标近似，后面改成按子键累积。
            if (HasElementConditionSlot(assetName, path) &&
                patch["Entries"] is JObject dictEntries)
            {
                foreach (JProperty p in dictEntries.Properties())
                {
                    if (obj[p.Name] is not JObject)
                        return $"`{DisplayPath(path)}.{p.Name}` 找不到字典条目（模型说它是带 Condition 的字典）";
                    // 模型已背书（ArrayElementConditionSlots），这里不再靠“同级先例”判断：
                    // 原版数据里所有 Attendee 的 Condition 都是 null 时，同级扫描会误判成没有槽。
                }
                continue;
            }

            if (!AcceptsCondition(obj, data, assetName))
                return $"`{DisplayPath(path)}` 这个位置不接受 Condition（原版在该位置不读条件字段）";
        }
        return null;
    }

    /// <summary>
    /// **数组元素自带 <c>Condition</c>** 的位置（依据 <c>D:\sdv_gd</c> 的模型类）。
    /// 键 = 资产名 + <c>\n</c> + <c>TargetField</c> 末段（大小写不敏感）。
    /// <para>
    /// <b>为什么需要它</b>：<see cref="AcceptsCondition"/> 的数据判据要求"这个位置或同级**已经有**
    /// <c>Condition</c>" —— 而这里要写的是**本条补丁刚加进数组的新元素**，它当然还没有。
    /// 于是整条补丁被判「不接受 Condition」⇒ 丢。实测（2026-10-15）：
    /// `[CP] Integrated Minecarts` 的 `Data/Minecarts` **3 条**、`[CP] Meadowlands Minecarts` **2 条**
    /// —— 矿车目的地整批没进产物（`NoLandingPoint`）。
    /// </para>
    /// <para>加一条要**先核模型类**、把出处写进来（与 <see cref="EntryElementConditionSlots"/> 一个规矩）。</para>
    /// </summary>
    private static readonly Dictionary<string, string> ArrayElementConditionSlots =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // `StardewValley.GameData.Minecarts/MinecartDestinationData.cs`：`public string Condition;`
            // 宿主：`MinecartNetworkData.Destinations`（游戏逐条查它：`GameLocation.cs:10246`）
            ["Data/Minecarts\nDestinations"] = "MinecartDestinationData.Condition",
            // `WeddingData.Attendees` -> `WeddingAttendeeData.Condition`
            ["Data/Weddings\nAttendees"] = "WeddingAttendeeData.Condition",
            // 商店：`ShopData.Items/Owners`；`ShopOwnerData.Dialogues[]` 里也有 Condition
            ["Data/Shops\nItems"] = "ShopItemData.Condition",
            ["Data/Shops\nOwners"] = "ShopOwnerData.Condition",
            ["Data/Shops\nDialogues"] = "ShopDialogueData.Condition",
            // `Data/Fish.<地点>.Fish` 与 `Data/Locations.<地点>.Fish`
            ["Data/Fish\nFish"] = "SpawnFishData.Condition",
            ["Data/Locations\nFish"] = "SpawnFishData.Condition",
            // `Data/Locations.<地点>.Forage` / `.ArtifactSpots`
            ["Data/Locations\nForage"] = "SpawnForageData.Condition",
            ["Data/Locations\nArtifactSpots"] = "SpawnForageData.Condition",
            // NPC 外观/住所
            ["Data/Characters\nAppearance"] = "CharacterAppearanceData.Condition",
            ["Data/Characters\nHome"] = "CharacterHomeData.Condition",
            // 农场动物产出、建筑皮肤
            ["Data/FarmAnimals\nProduce"] = "FarmAnimalProduce.Condition",
            ["Data/Buildings\nSkins"] = "BuildingSkin.Condition",
            // 世界地图区域
            ["Data/WorldMap\nAreas"] = "WorldMapAreaData.Condition",
            // 矿物掉落、果树果实（两者都继承 GenericSpawnItemDataWithCondition）
            ["Data/Objects\nGeodeDrops"] = "ObjectGeodeDropData.Condition",
            ["Data/FruitTrees\nFruit"] = "FruitTreeFruitData.Condition",
            // 地点上下文：天气条件、场景音乐与复活点
            ["Data/LocationContexts\nWeatherConditions"] = "WeatherCondition.Condition",
            ["Data/LocationContexts\nMusic"] = "LocationMusicData.Condition",
            ["Data/LocationContexts\nReviveLocations"] = "ReviveLocation.Condition",
            // 机器：输出触发规则与加载/工作特效
            ["Data/Machines\nTriggers"] = "MachineOutputTriggerRule.Condition",
            ["Data/Machines\nLoadEffects"] = "MachineEffects.Condition",
            ["Data/Machines\nWorkingEffects"] = "MachineEffects.Condition",
        };

    /// <summary>该（资产, 路径末段）是否有模型背书的元素级 Condition 槽。</summary>
    private static bool HasElementConditionSlot(string? assetName, string entryPath)
    {
        string key = (assetName ?? "").Replace('\\', '/').Trim('/')
                     + "\n" + PathSegments(entryPath).Last();
        return ArrayElementConditionSlots.ContainsKey(key);
    }

    /// <summary>TargetField 路径用 <see cref="PathSep"/>；Fields/旧调用仍用 `.`。</summary>
    private const char PathSep = '\u001F';

    private static string[] PathSegments(string path) =>
        path.IndexOf(PathSep) >= 0
            ? path.Split(PathSep)
            : path.Split('.');

    /// <summary>报告/错误文案里把内部路径分隔符还原成 `.`。</summary>
    private static string DisplayPath(string path) => path.Replace(PathSep, '.');

    /// <summary>本条补丁要写的条目 Id（<c>Entries</c>/<c>Fields</c> 的键）。</summary>
    private static List<string> ArrayItemIds(JObject patch)
    {
        var ids = new List<string>();
        if (patch["Entries"] is JObject entries)
            foreach (JProperty p in entries.Properties()) ids.Add(p.Name);
        if (patch["Fields"] is JObject fields)
            foreach (JProperty p in fields.Properties()) ids.Add(p.Name);
        return ids;
    }

    /// <summary>数组里按 <c>Id</c> 找条目（也认 <c>ID</c> —— 游戏大小写不敏感）。</summary>
    private static JObject? FindArrayItem(JArray array, string id) =>
        array.OfType<JObject>().FirstOrDefault(o =>
            string.Equals((string?)o["Id"] ?? (string?)o["ID"], id, StringComparison.OrdinalIgnoreCase));

    /// <summary>数组落点能不能用：模型背书 + 本补丁要写的每个 Id 都真的在数组里。</summary>
    private static string? RefuseArrayItems(JArray array, string path, JObject patch, string? assetName)
    {
        string key = (assetName ?? "").Replace('\\', '/').Trim('/') + "\n" + PathSegments(path).Last();
        if (!ArrayElementConditionSlots.TryGetValue(key, out string? slot))
        {
            return $"`{DisplayPath(path)}` 是数组，而模型没说它的元素带条件字段 ⇒ 条件**无处安放**"
                   + "（要加得先核 `D:\\sdv_gd` 的模型类，见 `ArrayElementConditionSlots`）";
        }

        List<string> ids = ArrayItemIds(patch);
        if (ids.Count == 0) return $"`{DisplayPath(path)}` 是数组，但这条补丁没给出条目 Id ⇒ 落不了（{slot}）";
        foreach (string id in ids)
        {
            if (FindArrayItem(array, id) == null)
                return $"`{DisplayPath(path)}` 里找不到本条补丁要写的条目 `{id}`（{slot}）";
        }
        return null;
    }

    /// <summary>
    /// **模型里条目元素自带 <c>Condition</c>** 的资产（依据 <c>D:\sdv_gd</c> 的模型类）。
    /// <para>
    /// <b>为什么需要它</b>：<see cref="AcceptsCondition"/> 的数据判据是「**这个位置或它的同级
    /// 已经有 <c>Condition</c>**」—— 那要求**原版数据里有人用过**。可有些资产的模型明明有
    /// <c>Condition</c> 字段、原版数据却一个先例都没用 ⇒ 判据只能保守说"不接受"，于是**条件无处安放、
    /// 整条补丁不写**（实打实的丢内容）。
    /// 实测语料里这类是 **3 条**：SVE 的 2 个被动节日（`Data/PassiveFestivals`）与聚宝盆的 1 条
    /// 特殊订单（`Data/SpecialOrders`）—— 前者在产出里**真的没有**（`Data/PassiveFestivals.json`
    /// 相对原版 0 新增）。
    /// </para>
    /// <para>
    /// 加一条要**先核模型类**、把出处写进来（与 `ConditionCapacity` 一个规矩）。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> EntryElementConditionSlots =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // `PassiveFestivalData.cs:9` `public string Condition;`
            ["Data/PassiveFestivals"] = "PassiveFestivalData.Condition",
            // `StardewValley.GameData.SpecialOrders/SpecialOrderData.cs:28` `public string Condition = "";`
            ["Data/SpecialOrders"] = "SpecialOrderData.Condition",
            // 其余根字典条目：GiantCropData / PassOutMailData / ToolUpgradeData 都带 Condition
            ["Data/GiantCrops"] = "GiantCropData.Condition",
            ["Data/PassOutMail"] = "PassOutMailData.Condition",
            ["Data/ToolUpgrades"] = "ToolUpgradeData.Condition",
        };

    /// <summary>
    /// <paramref name="entryPath"/> 是目标条目在数据里的定位：
    /// <see cref="LandingKind.EntryConditionField"/> 时是「键路径」（如 <c>Town.Forage.0</c>，用 <c>.</c> 分隔）；
    /// <see cref="LandingKind.EntryKeyConditions"/> 时就是条目 key 本身。
    /// <paramref name="state"/> 是运行内写入追踪：同一 <c>(资产名, 落点条目)</c> 多次写入时复用同一个
    /// <see cref="LandingState"/>，才能区分「资产自带条件」（AND）与「前序补丁条件」（OR）。
    /// 仅 <see cref="LandingKind.EntryConditionField"/> 需要它 —— 事件键落点每条补丁各成一个 key（见下），
    /// 不存在"一个槽被多次覆盖"的问题。
    /// </summary>
    public static MergeOutcome Apply(JToken data, string entryPath, string gsq, LandingKind landing,
        LandingState? state = null, string? assetName = null, JObject? patch = null)
    {
        if (landing == LandingKind.EntryConditionField)
            return ApplyToField(data, entryPath, gsq, state, assetName, patch);

        if (landing == LandingKind.EntryKeyConditions)
            return ApplyToEventKey(data, entryPath, gsq);

        return MergeOutcome.Fail($"落点 {landing} 不支持条件写入");
    }

    /// <summary>
    /// 该位置是否**真的**接受 <c>Condition</c>：它自己已有，或它的**同级**（同一容器的兄弟）有。
    /// <para>
    /// 判据的由来：`ConditionLanding.HasConditionFieldAnywhere` 是**资产级**的递归扫描，
    /// 只要资产里任意位置出现过 `Condition` 就放行 —— 但「资产里有」不等于「**这个位置**有」。
    /// <c>Data/Locations</c> 的 <c>Forage[].Condition</c> 与 <c>Data/Objects</c> 的
    /// <c>GeodeDrops[].Condition</c> **形状完全一样**，资产级判据分不开；
    /// 「同级已有」才说明这个位置的条件字段是原版机制的一部分。
    /// </para>
    /// </summary>
    private static bool AcceptsCondition(JObject entry, JToken? data = null, string? assetName = null)
    {
        if (entry["Condition"] != null) return true;

        // ⚠ **对象成员的 `Parent` 是 `JProperty`，不是 `JObject`** —— 少这一层会一律落进 `_ => false`，
        // 把**合法**的对象成员落点全误杀（实测 `Data/Weddings` 的 `Attendees.<名字>` 就是这么被拒的）。
        // 数组元素的 `Parent` 才是 `JArray`，所以只写数组那支的单测会把这个坑盖住。
        JContainer? container = entry.Parent;
        if (container is JProperty property) container = property.Parent;

        bool viaData = container switch
        {
            JArray arr => arr.OfType<JObject>().Any(x => x["Condition"] != null),
            JObject parent => parent.Properties().Any(p => p.Value is JObject x && x["Condition"] != null),
            _ => false,
        };
        if (viaData) return true;

        // 数据里一个先例都没有 ⇒ 再问**模型**：这个资产的**条目元素**自带 `Condition` 吗？
        // ⚠ 只认**卡在资产根上的那一层条目**（`entry` 的容器就是根 `data`）——
        //   没写 `TargetField` 时 `Entries` 的键就是顶层条目；写了 `TargetField` 是往嵌套位置改，
        //   那里没有模型兜底（`EntryElementConditionSlots` 只管资产自己的条目类型）。
        if (data == null || assetName == null) return false;
        if (!EntryElementConditionSlots.ContainsKey(assetName.Replace('\\', '/').Trim('/'))) return false;
        return ReferenceEquals(container, data);
    }

    private static MergeOutcome ApplyToField(JToken data, string entryPath, string gsq, LandingState? state,
        string? assetName = null, JObject? patch = null)
    {
        JToken? entry = ResolvePath(data, entryPath);

        // ★ 字典落点（与 `RefusalFor` 同一判据）：`TargetField: ["Attendees"]` 的每个
        //   `Entries` key 都指向一个带 Condition 的子对象时，写进子对象。
        //   近似说明：按子键各建一个 LandingState，避免多条补丁互相覆盖。
        if (entry is JObject dict && patch?["Entries"] is JObject dictEntries &&
            HasElementConditionSlot(assetName, entryPath))
        {
            LandingState parent = state ?? new LandingState();
            foreach (JProperty p in dictEntries.Properties())
            {
                if (dict[p.Name] is not JObject child)
                    return MergeOutcome.Fail($"`{entryPath}.{p.Name}` 找不到字典条目");
                MergeOutcome childOutcome = ApplyConditionToObject(child, gsq, parent.Child(p.Name));
                if (!childOutcome.Success) return childOutcome;
            }
            return MergeOutcome.Ok(data);
        }

        // ★ **数组落点**（与 `RefusalFor` 同一判据、同一份模型背书表）：把条件写进
        //   **本条补丁 `Entries` 里那几个 Id** 的条目自己的 `Condition`。
        //   ⚠ 只写这几个 —— 写全数组会把原版条目的条件覆盖掉。
        if (entry is JArray array)
        {
            if (patch == null) return MergeOutcome.Fail($"`{entryPath}` 是数组落点，但没拿到补丁结构（内部错误）");
            if (RefuseArrayItems(array, entryPath, patch, assetName) is string refusal)
                return MergeOutcome.Fail(refusal);

            foreach (string id in ArrayItemIds(patch))
            {
                JObject? item = FindArrayItem(array, id);
                if (item == null) continue;                     // RefuseArrayItems 已确认存在
                // 条目**自带**的条件（若有）与新条件做 AND（与单条目那条路同一个 `Combine`）。
                // ⚠ 与单条目那条路的差别：**不做跨补丁 OR 累积** —— 那条路靠 `LandingState`
                //   按「一个槽」追踪，而数组落点是**每个 Id 一个槽**，`state` 在这里是共用的、
                //   按槽分不开。语料里"两个包都给同一个目的地 Id 加条件"目前没有实例
                //   （若出现，这里是 AND、CP 是 OR ⇒ 偏保守：可能少显示一条，不会多显示）。
                CombineResult itemCombined = Combine(NullIfEmpty(item["Condition"]?.Value<string>()),
                    new List<string> { gsq });
                if (!itemCombined.Success) return MergeOutcome.Fail(itemCombined.FailureReason!);
                item["Condition"] = itemCombined.Value.Length == 0 ? JValue.CreateNull() : itemCombined.Value;
            }
            return MergeOutcome.Ok(data);
        }

        if (entry is not JObject obj)
            return MergeOutcome.Fail($"定位不到条件落点条目：`{entryPath}`");

        // ⚠ **落点校验**：`Condition` 只有在游戏**真的会读它**的位置才有意义。
        // 没有这道关，条件会被写到游戏不读的地方，而补丁仍记成「写入成功」—— 静默错。
        // 实测代价：SVE 产物 `Data/Objects.json` 里 25 个物件条目被加上条目级 `Condition`，
        // 而 `Data/Objects` **只在 `GeodeDrops[]` 下有** `Condition`（全库 11 处），
        // 条目级那个游戏根本不读 —— 那些条件等于没写。
        if (!AcceptsCondition(obj, data, assetName))
            return MergeOutcome.Fail($"`{entryPath}` 这个位置不接受 Condition（原版在该位置不读条件字段）");

        MergeOutcome outcome = ApplyConditionToObject(obj, gsq, state ?? new LandingState());
        return outcome.Success ? MergeOutcome.Ok(data) : outcome;
    }

    /// <summary>
    /// 把一条 GSQ 写进单个对象的 <c>Condition</c>，并按 <see cref="LandingState"/> 做
    /// 「资产自带条件 AND、本次补丁条件 OR」的累积。单条目与字典子条目共用这一段。
    /// </summary>
    private static MergeOutcome ApplyConditionToObject(JObject obj, string gsq, LandingState state)
    {
        // 空串表示无条件 —— 与真实数据里 Condition: null 的表示一致
        if (string.IsNullOrEmpty(gsq))
        {
            obj["Condition"] = JValue.CreateNull();
            Commit(state, null, new List<string>());
            return MergeOutcome.Ok();
        }

        // 首次触碰：数据里现有的条件就是「资产自带」（若有）
        string? baseCondition = state.Touched
            ? state.BaseCondition
            : NullIfEmpty(obj["Condition"]?.Value<string>());

        var conditions = new List<string>(state.PatchConditions);
        if (!conditions.Contains(gsq, StringComparer.Ordinal)) conditions.Add(gsq);

        CombineResult combined = Combine(baseCondition, conditions);
        if (!combined.Success) return MergeOutcome.Fail(combined.FailureReason!);

        // 只有合并成功才把这次累积提交进 state
        Commit(state, baseCondition, conditions);

        obj["Condition"] = combined.Value.Length == 0 ? JValue.CreateNull() : combined.Value;
        return MergeOutcome.Ok();
    }

    /// <summary>
    /// 事件键落点：追加 <c>/G &lt;GSQ&gt;</c> 段。
    /// <b>不做跨补丁 OR 合并</b> —— 事件条目不是一个"单槽"：同一事件 ID 的不同条件本就该各成一个 key
    /// （各自的脚本可不同），强行合并会把不同脚本压成一条、丢掉其它分支。补丁自身只带一个 When，
    /// 所以一个 key 只需一段 G。
    /// </summary>
    private static MergeOutcome ApplyToEventKey(JToken data, string entryPath, string gsq)
    {
        if (string.IsNullOrEmpty(gsq))
            return MergeOutcome.Fail($"事件键条件为空，拒绝给 `{entryPath}` 追加 `G ` 空段");

        if (data is not JObject root) return MergeOutcome.Fail("事件资产应为对象");
        if (!root.ContainsKey(entryPath)) return MergeOutcome.Fail($"找不到事件条目：{entryPath}");

        JToken value = root[entryPath]!;
        root.Remove(entryPath);
        root[$"{entryPath}/G {gsq}"] = value;
        return MergeOutcome.Ok(data);
    }

    /// <summary>条件合并结果。</summary>
    private readonly record struct CombineResult(bool Success, string Value, string? FailureReason);

    /// <summary>
    /// 按 CP 语义合并条件：资产自带条件与新条件做 <b>AND</b>（GSQ 顶层逗号）；
    /// 本次运行多条补丁的条件之间做 <b>OR</b>（<c>ANY "q1" "q2"</c>，与 <c>GsqTranslator.RenderAny</c> 一致）。
    /// </summary>
    private static CombineResult Combine(string? baseCondition, IReadOnlyList<string> patchConditions)
    {
        if (patchConditions.Count == 0)
            return new CombineResult(true, baseCondition ?? "", null);

        string orPart;
        if (patchConditions.Count == 1)
        {
            orPart = patchConditions[0];
        }
        else
        {
            // GSQ 的 ANY 只能包单个 query，包不住"与"。前序条件是"与"（含顶层逗号）
            // 或本身就是 ANY 时无法表达，判失败并说明 —— 不硬凑。
            foreach (string p in patchConditions)
            {
                if (!IsSingleQuery(p))
                    return new CombineResult(false, "",
                        $"前序补丁条件 `{p}` 是组合条件（含顶层逗号或本身就是 ANY），" +
                        "GSQ 的 ANY 只能包单个 query，无法做 OR 合并");
            }
            orPart = "ANY " + string.Join(" ", patchConditions.Select(p => $"\"{p}\""));
        }

        if (string.IsNullOrEmpty(baseCondition))
            return new CombineResult(true, orPart, null);

        // 资产自带条件与补丁条件（含 OR）做 AND —— GSQ 顶层逗号连接
        return new CombineResult(true, $"{baseCondition}, {orPart}", null);
    }

    /// <summary>把一次成功的合并结果提交进追踪状态。</summary>
    private static void Commit(LandingState state, string? baseCondition, List<string> conditions)
    {
        state.Touched = true;
        state.BaseCondition = baseCondition;
        state.PatchConditions.Clear();
        state.PatchConditions.AddRange(conditions);
    }

    /// <summary>是否为单个 GSQ query（可安全放进 <c>ANY</c> 里被引号包住）。</summary>
    private static bool IsSingleQuery(string gsq) =>
        !gsq.Contains(", ", StringComparison.Ordinal)
        && !gsq.StartsWith("ANY ", StringComparison.Ordinal);

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>
    /// 在 <c>Fields</c> 树里找出「叶子对象」的路径：只含标量字段的对象是条目本身，
    /// 还含子对象的则是容器，继续下钻。如 <c>{Attendees:{Olga:{ID,Setup}}}</c> → <c>Attendees.Olga</c>。
    /// </summary>
    private static void CollectFieldLeaves(JObject fields, string prefix, List<string> paths)
    {
        foreach (JProperty p in fields.Properties())
        {
            if (p.Value is not JObject child) continue;

            string path = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
            if (child.Properties().Any(c => c.Value is JObject))
                CollectFieldLeaves(child, path, paths);
            else
                paths.Add(path);
        }
    }

    private static JToken? Descend(JToken data, string[] path)
    {
        JToken cur = data;
        foreach (string seg in path)
        {
            if (cur is JObject o && o[seg] != null) { cur = o[seg]!; continue; }
            // ⚠ 数组段两套规则都要认，顺序是「数字下标优先，再按 `Id`」：
            //   · 数字下标：落点路径本来就可能写 `Town.Forage.0` 这种（`EntryPaths` 产生的键路径）；
            //   · `Id` / `#<数字>`：`TargetField` 那种 CP 路径段（实测聚宝盆制作 2 条：
            //     路径段是 `Cornucopia_GlassNoodles`，那是 Id 不是下标 —— 原先只认下标，
            //     于是「合并成功、写条件却报定位不到」）。
            if (cur is JArray a)
            {
                if (int.TryParse(seg, out int idx) && idx >= 0 && idx < a.Count) { cur = a[idx]!; continue; }
                if (EditDataMerger.FindInArray(a, seg) is JToken hit) { cur = hit; continue; }
            }
            return null;
        }
        return cur;
    }

    /// <summary>
    /// 按落点路径取条目：**先按原样整串找，再按 `.` 分段下钻**。
    /// <para>
    /// <b>为什么必须先整串找</b>：条目名里带点是**常态**（`FlashShifter.StardewValleyExpandedCP_JojaEmporium`、
    /// `Cornucopia.ArtisanMachines_…`），而合并器用的是**字面键**（`EditDataMerger.ApplyEntries`
    /// 就是 `obj[prop.Name] = …`）—— 只有 `TargetField` / 键路径那几种才会真正下钻。
    /// 原先这里一律 `Split('.')` ⇒ 带点的条目**永远定位不到** ⇒ 条件无处安放、整条不写。
    /// 实测代价：SVE 的 2 个被动节日 + 聚宝盆的 1 条特殊订单（`Data/PassiveFestivals` /
    /// `Data/SpecialOrders` 在产出里相对原版 **0 新增**）。
    /// </para>
    /// </summary>
    private static JToken? ResolvePath(JToken data, string path)
    {
        if (data is JObject o && o[path] != null) return o[path];
        return Descend(data, PathSegments(path));
    }
}
