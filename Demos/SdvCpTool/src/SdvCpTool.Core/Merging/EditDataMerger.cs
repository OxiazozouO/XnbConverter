using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP <c>EditData</c> 的合并语义。依据 CP 文档 <c>action-editdata.md</c>。
/// </summary>
public static class EditDataMerger
{
    /// <summary>
    /// 把一条 EditData 补丁应用到目标数据上，返回新数据（不修改入参）。
    /// <paramref name="assetName"/> 供 TextOperations 的 Fields 判定分隔字符串用 <c>^</c> 还是 <c>/</c>。
    /// </summary>
    /// <param name="takeOwnership">
    /// 调用方把 <paramref name="target"/> 的**所有权**交过来：直接原地改，不克隆整份文档。
    /// <para>
    /// 实测「整文档克隆」是 <c>EditData</c> 里最贵的一步、而**真合并几乎不要钱**：
    /// 一个包 108 次克隆 1658 ms，同期真合并合计 **13 ms**。大资产（<c>Data/Objects</c> 三万多个节点）
    /// 被几十条补丁连着改时，克隆占了几乎全部开销。
    /// </para>
    /// <para>
    /// ⚠ **交所有权的一方必须先保证「失败时不需要回滚」**：本方法只在**所有会失败的前置检查
    /// 都过完**之后才动 <paramref name="target"/>（TargetField 的下降与 TextOperations 的形状校验
    /// 都提到了改动之前；Entries/Fields 的类型检查本就在最前面；MoveEntries 不会失败）。
    /// 所以要么原样返回失败、要么改完成功，绝不会留下改到一半的文档。
    /// 调用方只要保证「失败路径上不把它当成功用」，这个前提就成立。
    /// </para>
    /// </param>
    public static MergeOutcome Apply(JToken target, JObject patch, string? assetName = null,
        bool takeOwnership = false)
    {
        // 分开计账：`PatchApplier` 的「EditData.合并」把「整文档克隆」和「真合并」混在一起，
        // 看不出大头是哪个（高精度计时，别用 CPU 时间 —— 粒度太粗，见 `Prof`）。
        long _p = Diagnostics.Prof.Now();

        // ★ **所有会失败的前置检查都放在最前面**（`takeOwnership` 的原地改靠这个）：
        //   `TextOperations` 的「解析期」校验（形状非法 ⇒ 整条补丁不创建）原先在 `TextOperationMerger`
        //   自己开头，但那个位置**已经在 `TargetField` 下降 / `Entries` / `Fields` 之后** ——
        //   一条坏操作排在后面时，前面那几段的改动已经落进去了。实测（审查时用一个临时测试跑出来的）：
        //   `{ "TargetField": ["A"], "Entries": {"k":"v"}, "TextOperations": [{"Operation":"Bogus"}] }`
        //   打在 `{}` 上会返回失败，而 target 已经变成 `{"A":{"k":"v"}}`。
        if (TextOperationMerger.ValidateShape(patch["TextOperations"] as JArray) is string textOpError)
            return MergeOutcome.Fail(textOpError);

        // root 是整份资产；TargetField 只改变写入落点，返回的仍是整棵根文档
        JToken root = takeOwnership ? target : target.DeepClone();
        JToken data = root;
        if (Diagnostics.Prof.On) { Diagnostics.Prof.AddSince("合并.整文档克隆", _p); _p = Diagnostics.Prof.Now(); }

        // TargetField：指明写进哪个字段（缺省即根）
        if (patch["TargetField"] is JArray targetField)
        {
            var path = targetField.Select(t => t.Value<string>() ?? "").ToArray();
            var nested = Descend(data, path, create: true, TargetFieldKinds(assetName, path));
            if (nested == null)
                return MergeOutcome.Fail($"TargetField `{string.Join('.', path)}` 无法定位或创建");
            data = nested;
        }

        // 各阶段「部分跳过」的说明要**一路上报**：跳过不是失败，但必须让人看得见
        // （CP 那边是一律记警告；我们照做，只是把警告放进了报告的 writeNote）。
        var notes = new List<string>();

        if (patch["Entries"] is JObject entries)
        {
            // ★ **逐条取舍**（CP `ApplyRecords` 的粒度就是条目：逐条 Warn 后 continue）：
            //   键或值里带**代不掉的令牌**的条目只丢它自己，其余照写。
            //   整条不写会把一条坏条目放大成上千条 —— 实机症状：SVE 的
            //   `Strings/StringsFromMaps` 1037 个条目里只有 1 个（`BlueMoonVineyard.7`）带
            //   `{{FarmName}}`，整条不写 ⇒ 另 1036 条全没写 ⇒ 蓝月酒庄地图上显示裸键
            //   `Strings\StringsFromMaps:BlueMoonVineyard.7`。
            (List<string> _writable, List<string> unresolved) = ValueTokenSubstitutor.SplitEntries(entries);
            if (unresolved.Count > 0)
            {
                entries = (JObject)entries.DeepClone();
                foreach (string key in unresolved) entries.Remove(key);
                notes.Add($"含代不掉令牌的条目 {unresolved.Count} 个已跳过（"
                          + $"{string.Join("`/`", unresolved.Take(5))}"
                          + (unresolved.Count > 5 ? " …" : "") + "）");
            }

            if (entries.Count > 0)
            {
                MergeOutcome r = ApplyEntries(data, entries, assetName);
                if (!r.Success) return r;
                data = r.Data!;
                notes.AddRange(r.Notes);
            }
        }

        if (patch["Fields"] is JObject fields)
        {
            MergeOutcome r = ApplyFields(data, fields, StringFieldDelimiter(assetName));
            if (!r.Success) return r;
            data = r.Data!;
            notes.AddRange(r.Notes);
        }

        // CP 应用顺序（EditDataPatch.cs:219-225）：Entries → Fields → TextOperations → MoveEntries。
        if (patch["TextOperations"] is JArray textOperations && textOperations.Count > 0)
        {
            MergeOutcome r = TextOperationMerger.Apply(data, textOperations, assetName);
            if (!r.Success) return r;
            data = r.Data!;
            notes.AddRange(r.Notes);
        }

        // 同上顺序的最后一步。
        if (patch["MoveEntries"] is JArray moveEntries && moveEntries.Count > 0)
        {
            MergeOutcome r = ApplyMoveEntries(data, moveEntries);
            if (!r.Success) return r;
            data = r.Data!;
            notes.AddRange(r.Notes);
        }

        if (Diagnostics.Prof.On) Diagnostics.Prof.AddSince("合并.真合并", _p);
        return MergeOutcome.Ok(root, notes);
    }

    /// <summary>
    /// CP <c>MoveEntries</c>：把列表里已有的条目挪到别处。
    /// <para>
    /// <b>只对列表有效</b>：CP 先查 <c>editor.CanMoveEntries</c>，
    /// 而只有 <c>ListKeyValueEditor</c> 把它置真（字典/模型都是假）——
    /// 打在非列表上时 CP 记一条警告后**整条跳过**，我们照做（放到 <c>note</c> 里，人看得见）。
    /// </para>
    /// <para>
    /// 下标算法照抄 <c>ListKeyValueEditor.MoveEntry</c>：先删后插，
    /// 且**目标在锚点之后时下标要往回挪一位**（<c>if (index &lt; anchorIndex) target--</c>）——
    /// 少了这一步，往后挪会差一格。
    /// </para>
    /// <para>
    /// 逐条独立：一条挪不动（找不到条目/锚点、锚点是它自己）只记警告，不带走上层、
    /// 也不挡住后面几条（CP 同样是 <c>Monitor.LogOnce</c> 后继续）。
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyMoveEntries(JToken data, JArray moves)
    {
        if (data is not JArray arr)
            return MergeOutcome.Ok(data, new[] { "MoveEntries 只对**列表**有效（目标是对象），已整条跳过" });

        var notes = new List<string>();
        foreach (JToken move in moves)
        {
            if (move is not JObject record) { notes.Add("MoveEntries 有一项不是对象，已跳过"); continue; }

            string? id = Field(record, "Id");
            if (string.IsNullOrEmpty(id)) { notes.Add("MoveEntries 有一项没给 `Id`，已跳过"); continue; }

            int index = FindArrayEntry(arr, id);
            if (index < 0) { notes.Add($"MoveEntries 指的条目 `{id}` 不在列表里，已跳过"); continue; }

            // ToPosition：`Top` / `Bottom`（CP 的 `MoveEntryPosition` 只有这两档）。
            string? to = Field(record, "ToPosition");
            if (to != null && (to.Equals("Top", StringComparison.OrdinalIgnoreCase)
                               || to.Equals("Bottom", StringComparison.OrdinalIgnoreCase)))
            {
                JToken moving = arr[index];
                arr.RemoveAt(index);
                if (to.Equals("Top", StringComparison.OrdinalIgnoreCase)) arr.Insert(0, moving);
                else arr.Add(moving);
                continue;
            }

            // 否则按锚点：`AfterId` 优先（CP 先看 AfterID，再看 BeforeID）。
            string? after = Field(record, "AfterId");
            string? before = after == null ? Field(record, "BeforeId") : null;
            string? anchor = after ?? before;
            if (string.IsNullOrEmpty(anchor))
            {
                notes.Add($"MoveEntries 的 `{id}` 既没给 ToPosition 也没给 BeforeId/AfterId，已跳过");
                continue;
            }

            int anchorIndex = FindArrayEntry(arr, anchor);
            if (anchorIndex < 0) { notes.Add($"MoveEntries 的 `{id}` 找不到锚点 `{anchor}`，已跳过"); continue; }
            if (anchorIndex == index) { notes.Add($"MoveEntries 的 `{id}` 的锚点是它自己，已跳过"); continue; }

            int target = after != null ? anchorIndex + 1 : anchorIndex;
            if (index < anchorIndex) target--;      // 先删后插：锚点下标得往回挪
            JToken item = arr[index];
            arr.RemoveAt(index);
            arr.Insert(target, item);
        }
        return MergeOutcome.Ok(arr, notes);
    }

    /// <summary>
    /// 按**不区分大小写**的字段名取值 —— CP 的补丁字段名就是这么认的
    /// （语料里同一种写法有 `Id`/`ID`、`AfterId`/`AfterID`、`BeforeId`/`BeforeID` 并存）。
    /// 值为 <c>null</c> 或缺席都返回 <c>null</c>。
    /// </summary>
    private static string? Field(JObject record, string name)
    {
        JProperty? prop = record.Property(name) ?? record.Properties()
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return prop?.Value.Type == JTokenType.Null ? null : prop?.Value.Value<string>();
    }

    /// <summary>
    /// <c>Data/Events/*</c>：新键要<b>插到最前</b>，不是追加。
    /// <para>
    /// <b>依据</b>：运行时按文件里的键序取<b>首个</b>命中的事件并 <c>break</c>
    /// （<c>GameLocation.cs:15660-15666</c>）；而 CP 是「<b>后写赢</b>」——
    /// 多个补丁命中同一个事件键时后者覆盖前者。
    /// </para>
    /// <para>
    /// 要让「CP 最后写的那条」赢，就得让<b>最后写的排在最前</b>。
    /// 逐条前插正好得到与处理顺序<b>相反</b>的顺序 = CP 顺序的逆序。✓
    /// </para>
    /// <para>
    /// ⚠ 只对**新键**前插：已存在的键原地替换，位置不动（否则会打乱原版键的既有次序）。
    /// </para>
    /// </summary>
    private static bool IsEventAsset(string? assetName) =>
        assetName != null
        && assetName.Replace('\\', '/').Trim()
            .StartsWith("Data/Events/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按 <paramref name="path"/> 逐层下钻；<paramref name="create"/> 为真时缺失的层会创建。
    /// <para>
    /// <b>中途可以是数组</b>：<c>Data/WorldMap</c> 的 <c>Valley.MapAreas</c> 与
    /// <c>MapAreas[].ToolTips</c> 都是<b>列表</b>，路径必须能穿过它们。
    /// 语义照 CP 的 <c>ListKeyValueEditor</c>（<c>ParseKey</c> / <c>TryGetEntry</c>）：
    /// <list type="bullet">
    /// <item><c>#&lt;数字&gt;</c> → 按下标</item>
    /// <item>其余 → 线性扫描找 <c>Id</c> 字段相等的元素（<c>==</c> 精确比较）</item>
    /// </list>
    /// <b>数组里找不到时不创建</b>：CP 的 <c>SetEntry</c> 确实会追加，但那是「按 Id 写一条新元素」
    /// 的语义；这里只是路径导航，凭空 append 一条残缺元素就是写错数据。
    /// </para>
    /// </summary>
    /// <param name="expected">
    /// <b>每一段落点该是什么容器</b>（按游戏模型推出来的），没有就 <c>null</c>。
    /// <para>
    /// <b>为什么必须有它</b>：<c>TargetField</c> 指到一个<b>还不存在</b>的字段时，
    /// 只按「建个对象」来会建错容器。实测踩过：SVE 季节性服装用
    /// <c>TargetField: ["Alesia", "Appearance"]</c> 往<b>非原版 NPC</b> 的
    /// <c>Appearance</c> 里写条目 —— 模型里 <c>Appearance</c> 是
    /// <c>List&lt;CharacterAppearanceData&gt;</c>，我们却建成了对象，
    /// 19 个 NPC 的 <c>Appearance</c> 全成了 <c>{ "&lt;Id&gt;": {...} }</c>，
    /// 游戏按 <c>List</c> 反序列化会失败（本工具的 <c>GameDataValidator</c> 报了 19 处
    /// <c>Appearance 期望 Array 实际 object</c>）。
    /// 而同一条补丁打在**原版 NPC**（已有数组）上时是对的 —— 差别只在「容器要不要新建」。
    /// </para>
    /// </param>
    private static JToken? Descend(JToken data, string[] path, bool create, FieldKind?[]? expected = null)
    {
        // ★ **新建的容器先不挂上去，走完整条路径才挂**。
        //   原来是边建边挂：中途落空（数组里找不到那一段、或者某一段是个标量）时，
        //   前面几个空容器**已经留在调用方的文档里了**。在「克隆一份再改、失败就丢」的形态下
        //   看不出来，但 `takeOwnership`（原地改）要的是「要么原样失败、要么改完成功」——
        //   留半截空容器就不是原样了。挂接点（父对象 + 段名）与建好的节点先记着，成功才落。
        var pending = new List<(JObject Parent, JProperty? Prop, string Seg, JToken Created)>();

        JToken cur = data;
        for (int i = 0; i < path.Length; i++)
        {
            string seg = path[i];
            if (cur is JArray arr)
            {
                JToken? hit = FindInArray(arr, seg);
                if (hit == null) return null;   // 数组里不创建
                cur = hit;
                continue;
            }

            if (cur is not JObject obj) return null;

            // ⚠ 这一段是**字段名**还是**字典键**，由模型说了算：上一段的值是容器（Map/Array）时，
            //   这一段是它的键；否则是类实例的**字段名**（`expected[i-1]` 为 null）。
            //   **字段名按不区分大小写找已有键** —— 打包器与游戏都这么匹配
            //   （实测 mod 在 `Data/WorldMap` 里写 `ToolTips`、树里是 `Tooltips`，
            //    打包器照认并按 `List<WorldMapTooltipData>` 校验；认不出来就会**新建一个
            //    拼写不同的键**、还建错形状 ⇒ SVE 的 `Valley.MapAreas[7].ToolTips` 成了对象，回包失败）。
            //   **字典键仍精确匹配**：键是数据本身，大小写不同就是两个键。
            bool isFieldName = i > 0 && expected != null
                               && expected[i - 1] is not (FieldKind.Map or FieldKind.Array);

            JProperty? prop = obj.Property(seg);
            if (prop == null && isFieldName)
            {
                prop = obj.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, seg, StringComparison.OrdinalIgnoreCase));
            }

            if (prop?.Value is JToken next && next.Type != JTokenType.Null) { cur = next; continue; }

            // ⚠ **值是 JSON `null`**：按「键不存在」处理（建空容器），不能把 null 交回去。
            //   实测：原版 `Data/FarmAnimals` 的 `White Cow.Skins` 是**键在、值 null**，
            //   模型里 `Skins` 是 `List<FarmAnimalSkin>`（`GameDataSchema` `:273`）——
            //   CP 的 `SetEntry` 把它当空列表写。原来返回 null ⇒ `ApplyEntries`
            //   报「不是对象也不是数组（实际 Null）」⇒ 那个包 **35 条整包写入失败**。
            //   ⚠ **只在模型说得清时**（`expected != null`）这么做；说不清就保持失败，
            //     让上层报「定位不到」，而不是猜一个容器把标量字段写坏。
            if (prop == null && !create) return null;
            if (prop != null && (!create || expected == null)) return null;

            JToken created = expected != null && expected[i] == FieldKind.Array
                ? new JArray()
                : new JObject();
            // 命中已有键时**沿用它的拼写**（把 `Tooltips` 写成 `ToolTips` 是没必要的改动）——
            // `prop != null` 时挂接点是那个键本身（改它的值），否则是新键。
            pending.Add((obj, prop, seg, created));
            cur = created;
        }

        // 走通了才把新建的容器挂上去（顺序 = 由外向内，各挂接点的父对象都已就位）
        foreach ((JObject parent, JProperty? prop, string seg, JToken created) in pending)
        {
            if (prop != null) prop.Value = created;
            else parent[seg] = created;
        }

        return cur;
    }

    /// <summary>
    /// 沿<b>类型图</b>推出 <c>TargetField</c> 每一段落点的容器种类；推不出就 <c>null</c>。
    /// <para>
    /// 走法：资产根是「键 → 元素类」，所以第一段是**键**（类不变），之后每段是**字段名**
    /// （查 <see cref="GameDataSchema.Types"/>）。字段是数组/字典时，下一段的类是它的元素类。
    /// </para>
    /// <para>
    /// <b>模型不认得这个资产时返回全 <c>null</c></b> —— 保持原行为（建对象），
    /// 不拿猜测去覆盖。这是**如实留缺口**，不是「已经覆盖了」。
    /// </para>
    /// </summary>
    private static FieldKind?[]? TargetFieldKinds(string? assetName, string[] path)
    {
        if (string.IsNullOrEmpty(assetName)) return null;
        if (!GameDataSchema.AssetElement.TryGetValue(assetName.Trim(), out string? rootClass)) return null;

        // `kind == null` 表示「当前指向的值是一个**类实例**」（枚举里没有 Object 这一档，
        // 而 `Descend` 对类实例本来就该建 `JObject` —— 所以 null 正好表示「建对象」）。
        var kinds = new FieldKind?[path.Length];
        string cls = rootClass;
        FieldKind? kind = FieldKind.Map;    // 资产根是个容器（字典或列表）

        for (int i = 0; i < path.Length; i++)
        {
            if (kind is FieldKind.Map or FieldKind.Array)
            {
                // 这一段是键 / Id / 下标 —— 值是该容器的元素，即 `cls` 的一个实例
                kind = null;
            }
            else if (kind is null && cls.Length > 0
                     && GameDataSchema.Types.TryGetValue(cls, out Dictionary<string, FieldRef>? fields)
                     && fields.TryGetValue(path[i], out FieldRef refr))
            {
                // 这一段是**字段名**
                kind = refr.Kind;
                cls = refr.ElementType;
            }
            else
            {
                return null;
            }

            kinds[i] = kind;
        }
        return kinds;
    }

    /// <summary>
    /// 在数组里按 CP 的规则找一段：<c>#&lt;数字&gt;</c> 是下标，其余按元素的 <c>Id</c> 精确匹配。
    /// <para>
    /// <c>internal</c>：<see cref="ConditionWriter"/> 的落点下钻**必须用同一套规则** ——
    /// 它原先只认整数下标，于是「合并成功、写条件却报定位不到」（实测聚宝盆制作 2 条：
    /// 路径段是 <c>Cornucopia_GlassNoodles</c>（Id）而不是下标）。
    /// </para>
    /// </summary>
    internal static JToken? FindInArray(JArray arr, string segment)
    {
        if (segment.StartsWith('#'))
        {
            return int.TryParse(segment[1..], out int index) && index >= 0 && index < arr.Count
                ? arr[index]
                : null;
        }

        foreach (JToken item in arr)
            if (item is JObject o && string.Equals(o["Id"]?.Value<string>(), segment, StringComparison.Ordinal))
                return item;

        return null;
    }

    private static MergeOutcome ApplyEntries(JToken data, JObject entries, string? assetName)
    {
        if (data is JObject obj)
        {
            bool prepend = IsEventAsset(assetName);
            var notes = new List<string>();
            bool isCharacters = string.Equals(assetName, "Data/Characters", StringComparison.OrdinalIgnoreCase);

            foreach (JProperty prop in entries.Properties())
            {
                // CP 语义：值为 null 即删除该键
                if (prop.Value.Type == JTokenType.Null) { obj.Remove(prop.Name); continue; }

                // 事件资产的新键要插到最前 —— 理由见 IsEventAsset。
                if (prepend && obj[prop.Name] == null)
                {
                    obj.AddFirst(new JProperty(prop.Name, prop.Value.DeepClone()));
                    continue;
                }

                // ★ **`Data/Characters`：整条替换时保住已有的 `Appearance`**。
                //   为什么：CP 对所有包的补丁按**全局 Priority** 应用（`PatchManager.cs:533`），
                //   而我们按包顺序应用。实测 `[CP] Seasonal Cute Characters East Scarp` 的
                //   `Priority: Late` 外观补丁（包 84）先跑，`[CP] East Scarp NPCs` 的整条
                //   `CharacterData`（包 112、Priority 默认）后跑 ⇒ Eyvinder 的 7 条季节性外观全没了。
                //   缺 `Appearance` 字段的整条替换**语义上就是没提到这个字段**，保下来最接近
                //   CP 的最终结果；低 Precedence 的旧条目也会被新条目压过（`NPC.cs:980`）。
                JToken incoming = prop.Value.DeepClone();
                if (isCharacters && incoming is JObject newEntry && newEntry["Appearance"] == null
                    && obj[prop.Name] is JObject oldEntry && oldEntry["Appearance"] is JArray oldAppearance
                    && oldAppearance.Count > 0)
                {
                    newEntry["Appearance"] = oldAppearance.DeepClone();
                    notes.Add($"`{prop.Name}` 的新条目没写 `Appearance`，保留已有的 "
                              + $"{oldAppearance.Count} 条（跨包补丁优先级：外观补丁可能在本包之后）");
                }

                obj[prop.Name] = incoming;
            }
            return MergeOutcome.Ok(obj, notes);
        }

        if (data is JArray arr)
        {
            // 键语义照抄 CP `ListKeyValueEditor`（反编译自 `ContentPatcher.dll` 2.9.1）：
            //  · `#<整数>` 是**下标**（`ParseKey`）——**越界或负数不命中**，于是走 `SetEntry` 的
            //    `Data.Add(...)` 分支 ⇒ **追加**（`#-1` 就是这个惯用写法）；
            //  · 其余按键匹配：元素是**字符串**时**键就是字符串本身**（`GetAssetKeyFunc`），
            //    是对象时取 `Id`/`ID`（都按**大小写敏感**精确比：`TryGetEntry` 里是 `GetKey(value) == text`）；
            //  · 值为 `null` ⇒ 删掉命中的那一格。
            //
            // ★ **命中就是整格替换，CP 没有「逐字段合并」这回事**：
            //   `EditDataPatch.ApplyRecords` 一律是
            //   `value = record.Value.ToObject(entryType); editor.SetEntry(key, value);`，
            //   而 `ListKeyValueEditor.SetEntry` 命中走 `Data[index] = value`、未命中走 `Data.Add(value)`
            //   —— 两条路都是**整值**。逐字段合并只出现在 `Fields`（`ApplyFields` 里
            //   用 `Serializer.Populate` 灌进**已有条目**），不在 `Entries`。
            //   （这一段原先写的是「命中则逐字段合并」，那是照错的印象写的，已订正。）
            foreach (JProperty prop in entries.Properties())
            {
                int hitIndex = FindArrayEntry(arr, prop.Name);

                if (prop.Value.Type == JTokenType.Null)
                {
                    if (hitIndex >= 0) arr.RemoveAt(hitIndex);
                    continue;
                }

                // ⚠ **原值原样**进容器：不拆、不补字段。旧实现把非对象值 `as JObject` 成了 null，
                //   于是造了个 `new JObject()` 再填 `Id` —— 实测把 `"berry_item"` 这种
                //   **字符串元素写成了 `{"Id": "#-1"}`**：值丢了，还写出游戏读不懂的元素
                //   （`Data/Objects` 的 `ContextTags` 是**字符串数组**，打包直接失败 23 个资产）。
                //   对象值补 `Id` 同样是多的：CP 那边 `ToObject<TValue>` 之后 `Id` 是**补丁里写什么就是什么**。
                if (hitIndex < 0) arr.Add(prop.Value.DeepClone());
                else arr[hitIndex] = prop.Value.DeepClone();
            }
            return MergeOutcome.Ok(arr);
        }

        return MergeOutcome.Fail($"Entries 的目标不是对象也不是数组（实际 {data.Type}）");
    }

    /// <summary>
    /// 在数组里按 CP 的键语义找目标格：<c>#&lt;整数&gt;</c> 是下标（越界/负数**不命中** ⇒ 调用方追加），
    /// 其余按键匹配（字符串元素比字符串本身，对象元素比 <c>Id</c>/<c>ID</c>）。找不到返回 <c>-1</c>。
    /// </summary>
    private static int FindArrayEntry(JArray arr, string key)
    {
        if (key.Length > 1 && key[0] == '#' && int.TryParse(key[1..], out int index))
            return index >= 0 && index < arr.Count ? index : -1;

        for (int i = 0; i < arr.Count; i++)
        {
            string? elementKey = arr[i] switch
            {
                JValue { Type: JTokenType.String } s => s.Value<string>(),
                JObject o => o["Id"]?.Value<string>() ?? o["ID"]?.Value<string>(),
                _ => null,
            };
            if (elementKey != null && string.Equals(elementKey, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 分隔字符串资产用哪个字符分字段。依据 CP 源码 <c>EditDataPatch.cs:499-502</c>：
    /// <b>只有 <c>Data/Achievements</c> 用 <c>^</c>，其余一律 <c>/</c></b>。
    /// </summary>
    private static char StringFieldDelimiter(string? assetName) =>
        string.Equals(assetName?.Trim(), "Data/Achievements", StringComparison.OrdinalIgnoreCase) ? '^' : '/';

    /// <summary>
    /// 应用 <c>Fields</c>。语义依据 CP 源码 <c>EditDataPatch.cs:286-346</c>：
    /// <list type="bullet">
    /// <item>条目是<b>字符串</b>时，字段键必须是 <b>0 起的分隔下标</b>，改完再拼回去；</item>
    /// <item>条目是<b>对象</b>时，字段键是属性名。</item>
    /// </list>
    /// <para>
    /// <b>这两条以前只实现了后者</b> —— 遇到字符串条目会把整条覆盖成 JSON 对象，
    /// 是<b>破坏性</b>的错（语料 B 的 Cornucopia 对 <c>Data/CookingRecipes</c> 有 9 处这种写法）。
    /// </para>
    /// <b>目标可以是列表</b>：<c>TargetField</c> 指到一个数组时（实测聚宝盆作物的
    /// <c>TargetField: ["SeedShop","Items"]</c>），CP 用 <c>ListKeyValueEditor</c> 按 <c>Id</c>/下标
    /// 找到元素再合并进去 —— 原先这里只认 <c>JObject</c>，落到列表就整条判失败。
    /// </para>
    /// <para>
    /// 下标越界 / 非数字 / 条目不存在时<b>跳过那一项并记 note</b>，<b>不</b>判整条失败。
    /// 依据 CP <c>ApplyFields</c>：它对每一条都是 <c>WarnForField</c> 后 <c>continue</c>，
    /// 补丁仍算已应用。这里原先「宁可拒绝」判 <c>Fail</c>，代价有两个：
    /// ① 把**同一补丁里 `Entries` 已经落好的改动一起丢掉**；
    /// ② 与判定侧打架（判定给 `Converted...`、写到一半改口说失败）——
    ///    那正是本仓库点名要避免的「把判定不准伪装成写入出问题」。
    /// 两种口径都不会写出错数据（该跳的字段照样跳），所以就高（CP）不就低。
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyFields(JToken data, JObject fields, char delimiter)
    {
        if (data is JArray list) return ApplyFieldsToList(list, fields, delimiter);

        if (data is not JObject obj)
            return MergeOutcome.Fail($"Fields 的目标既不是对象也不是列表（实际 {data.Type}）");

        // 逐条目独立：**一条落不下去不该带走整条补丁** —— 依据 CP `ApplyFields`
        // （`EditDataPatch.cs:290-296`：`WarnForField("there's no record matching key …")` 后 `continue`）。
        var notes = new List<string>();

        foreach (JProperty entryProp in fields.Properties())
        {
            if (entryProp.Value is not JObject patchFields)
            {
                notes.Add($"Fields 里 `{entryProp.Name}` 的值不是对象，已跳过");
                continue;
            }

            JToken? entry = obj[entryProp.Name];
            if (entry == null)
            {
                // 实测这一档多是**跨包依赖**：条目由别的 mod 提供，本包单独转换时资产里没有。
                notes.Add($"Fields 指向的条目 `{entryProp.Name}` 不在目标资产里，已跳过");
                continue;
            }

            switch (entry)
            {
                case JValue { Type: JTokenType.String } str:
                    obj[entryProp.Name] = MergeDelimitedString(str.Value<string>(), patchFields,
                                                              delimiter, entryProp.Name, notes);
                    break;

                case JObject entryObj:
                    ApplyFieldsRecursive(entryObj, patchFields);
                    break;

                default:
                    notes.Add($"条目 `{entryProp.Name}` 的类型（{entry.Type}）不支持 Fields，已跳过");
                    break;
            }
        }

        // ★ 与 `TextOperations` 同一条道理：**全都落不下去也是「应用了」**。
        //   CP 的 `ApplyFields` 对每条 `WarnForField("there's no record matching key …")` 后 `continue`，
        //   补丁照标记为已应用。而这里原先判 `Fail`，还会把**同一补丁里 `Entries` 已经落好的改动一起丢掉**
        //   （`Apply` 见 `!r.Success` 就整体返回失败）—— 那才是真的丢东西。
        //   实测这一档是**跨包依赖**（条目由别的 mod 提供），链式转换下前一个包写进去就有了。
        return MergeOutcome.Ok(obj, notes.Distinct().ToList());
    }

    /// <summary>
    /// <c>Fields</c> 打在**列表**上：按键找元素（<c>Id</c>/<c>ID</c>，<c>#&lt;整数&gt;</c> 是下标），
    /// 元素是对象就合并字段、是分隔字符串就按下标改段。
    /// <para>
    /// 依据 CP <c>ApplyFields</c>：它走的还是同一套 <c>ParseKey</c>/<c>GetEntryType</c>/<c>GetEntry</c>，
    /// 只是编辑器换成了 <c>ListKeyValueEditor</c>；元素不是字符串时用 <c>Serializer.Populate</c> 合并。
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyFieldsToList(JArray list, JObject fields, char delimiter)
    {
        var notes = new List<string>();

        foreach (JProperty entryProp in fields.Properties())
        {
            if (entryProp.Value is not JObject patchFields)
            {
                notes.Add($"Fields 里 `{entryProp.Name}` 的值不是对象，已跳过");
                continue;
            }

            int index = FindArrayEntry(list, entryProp.Name);
            if (index < 0)
            {
                notes.Add($"Fields 指向的条目 `{entryProp.Name}` 不在列表里，已跳过");
                continue;
            }

            switch (list[index])
            {
                case JValue { Type: JTokenType.String } str:
                    list[index] = MergeDelimitedString(str.Value<string>(), patchFields, delimiter,
                                                       entryProp.Name, notes);
                    break;

                case JObject entryObj:
                    ApplyFieldsRecursive(entryObj, patchFields);
                    break;

                default:
                    notes.Add($"列表里 `{entryProp.Name}` 这一项的类型（{list[index].Type}）不支持 Fields，已跳过");
                    break;
            }
        }

        return MergeOutcome.Ok(list, notes.Distinct().ToList());
    }

    /// <summary>
    /// 把字段按**下标**合进一个分隔字符串条目并拼回去（CP 的 <c>DelimitedStringKeyValueEditor</c>）。
    /// 坏下标只跳过那一段并记 <c>note</c>（CP 是 <c>WarnForField</c> 后继续）。
    /// </summary>
    private static string MergeDelimitedString(string? text, JObject patchFields, char delimiter,
        string label, List<string> notes)
    {
        string[] parts = (text ?? "").Split(delimiter);
        foreach (JProperty f in patchFields.Properties())
        {
            if (!int.TryParse(f.Name, out int idx) || idx < 0 || idx >= parts.Length)
            {
                notes.Add($"条目 `{label}` 是分隔字符串（{parts.Length} 段），" +
                          $"字段键 `{f.Name}` 不是 0..{parts.Length - 1} 的下标，已跳过这一段");
                continue;
            }
            // 值侧的 null 在 CP 里写作空串，不是"删除"（字符串没有键可删）。
            parts[idx] = f.Value.Type == JTokenType.Null ? "" : f.Value.Value<string>() ?? "";
        }
        return string.Join(delimiter, parts);
    }

    /// <summary>
    /// 把补丁的字段合并进已有条目（CP `ApplyFields` 里对对象条目的那一支：
    /// <c>Serializer.Populate(reader, editor.GetEntry(obj))</c>）。
    /// <para>
    /// ⚠ <b>值为 <c>null</c> 是「把这个字段设成 null」，不是「删掉这个字段」</b> ——
    /// <c>Populate</c> 遇 JSON <c>null</c> 会把成员**赋成 null**，键还在。
    /// （对比：<c>Entries</c> 里的 <c>null</c> 才是「删条目」，那边走的是 <c>RemoveEntry</c>。）
    /// </para>
    /// </summary>
    private static void ApplyFieldsRecursive(JObject obj, JObject fields)
    {
        foreach (JProperty prop in fields.Properties())
        {
            if (prop.Value is JObject nested && obj[prop.Name] is JObject existing)
                ApplyFieldsRecursive(existing, nested);
            else
                obj[prop.Name] = prop.Value.DeepClone();      // null 也是赋值（见上）
        }
    }
}
