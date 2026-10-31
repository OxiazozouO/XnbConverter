using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP <c>TextOperations</c> 的合并语义。
/// 依据 CP 文档 <c>text-operations.md</c> 与 CP 源码 <c>Framework/TextOperations/*</c>。
///
/// <para>
/// 重要：CP 文档称 <c>Append</c>/<c>Prepend</c> 的 <c>Delimiter</c> 缺省为 <c>/</c>
/// （<c>Data/Achievements</c> 为 <c>^</c>），但 CP 源码
/// <c>AppendOrPrependTextOperation</c> 实际写成 <c>delimiter ?? string.Empty</c> ——
/// 缺省是<strong>空字符串</strong>，即不插入任何分隔符。真实模组（如 Ridgeside Village
/// 的 <c>["Fields", "set-up", 5]</c> 追加）正是按空缺省书写：Value 自带前导 <c>/</c> 且不写
/// Delimiter。若照文档用 <c>/</c> 缺省会产出 <c>//</c> 双斜杠。故本实现以源码为准。
/// </para>
/// </summary>
public static class TextOperationMerger
{
    /// <summary>
    /// 把一条补丁的 <c>TextOperations</c> 依次应用到 <paramref name="data"/>（就地修改）。
    /// <paramref name="assetName"/> 用于判定 <c>Fields</c> 的分隔字符串用 <c>^</c> 还是 <c>/</c>。
    /// </summary>
    public static MergeOutcome Apply(JToken data, JArray operations, string? assetName)
    {
        // ★ **先把「解析期」那级校验走完，再动数据**（CP 也是这么做的：`TryParseTextOperations`
        //   在补丁创建前把整张表验一遍）。原先是一边验一边改，于是一条坏操作排在中间时，
        //   它**前面**那几条已经改进去了。
        //   ⚠ 这一道在**调用方**（`EditDataMerger.Apply` 最开头）也走了一遍 —— 那里才是
        //      「整条合并的前置检查」的位置：`TargetField` 下降与 `Entries`/`Fields` 都在它之前，
        //      光在这里验，「下降已经建好容器、Entries 已经写进去」之后才失败，照样留痕。
        if (ValidateShape(operations) is string malformedByCaller)
            return MergeOutcome.Fail(malformedByCaller);

        var notes = new List<string>();

        foreach (JToken token in operations)
        {
            if (token.Type == JTokenType.Null) continue;   // CP 跳过 null 条目

            if (token is not JObject operation)
            {
                notes.Add("有一条 TextOperation 不是对象，已跳过");
                continue;
            }

            // ★ CP 在这一块有**两级**，粒度不一样，不能混成一级：
            //
            //   ① **解析期**（`PatchLoader.TryParseTextOperations`）：操作**自身形状**非法
            //      —— `Operation` 缺失/非法、`Target` 少于两段、`RemoveDelimited` 缺
            //      `Delimiter`/`Search`、`ReplaceMode` 非法 —— CP 直接 `TrackSkip`：
            //      **整条补丁不创建**，它的 `Entries`/`Fields` 也一并没了。
            //   ② **运行期**（`ApplyTextOperations` + `TryApplyTextOperation`）：条目/字段在
            //      数据里找不到、类型不是字符串、段数与根不匹配 —— CP `WarnForOperation`
            //      **记警告后继续**，其余照常应用，补丁仍算已应用。
            //
            //   原先只有一级（全失败才判失败），两种情形都没对上。
            //   ⚠ 这一级的校验已经提到循环**前面**统一做完了（见上面的前置扫描）。
            if (!TryApply(data, operation, assetName, out string? error))
                notes.Add($"有一条 TextOperation 落不下去，已跳过：{error}");
        }

        // ★ **全都落不下去也是「应用了」，不是失败**。
        //   CP 的 `EditDataPatch.Edit` 没有失败这一说：每条落下不去就 `WarnForOperation` 后继续，
        //   补丁照样标记为已应用（`IsApplied = true`）。
        //   原来这里判 `Fail`，理由是「不能报成写了」—— 但那与**判定**打架：
        //   实测语料里这一档 11 条，判定侧给的是 `ConvertedNoCondition`（「无条件，直接合并」），
        //   写到一半改口说失败，正是本仓库点名要避免的「把判定不准伪装成写入出问题」。
        //   而且这 11 条全是**跨包依赖**（`Andy`/`Claire` 等是别的 mod 的 NPC，
        //   本包单独转换时资产里没有）—— 链式转换下前一个包写进去就有了。
        //   真正的原因在 `note` 里，报告看得见；不写成「写入失败」。
        return MergeOutcome.Ok(data, notes.Distinct().ToList());
    }

    /// <summary>
    /// 解析期的形状校验（照 CP <c>TryParseTextOperations</c>）：<b>只看操作自己的字段</b>，
    /// 不碰数据。返回非 <c>null</c> 表示 CP 会把这<b>整条补丁</b>丢掉。
    /// <para>
    /// ⚠ 调用方（<c>EditDataMerger.Apply</c>）在**任何改动之前**先调它 —— 这是
    /// <c>takeOwnership</c>（原地改）「要么原样失败、要么改完成功」的前提之一。
    /// </para>
    /// </summary>
    public static string? ValidateShape(JArray? operations)
    {
        if (operations == null) return null;

        foreach (JToken token in operations)
        {
            if (token is not JObject probe) continue;   // null / 非对象：按「跳过」处理，不算解析失败
            if (ParseError(probe) is string malformed)
                return $"TextOperation 解析失败（CP 会整条跳过该补丁）：{malformed}";
        }

        return null;
    }

    /// <summary>
    /// 解析期的形状校验（照 CP <c>TryParseTextOperations</c>）：<b>只看操作自己的字段</b>，
    /// 不碰数据。返回非 <c>null</c> 表示 CP 会把这<b>整条补丁</b>丢掉。
    /// </summary>
    private static string? ParseError(JObject operation)
    {
        string name = operation["Operation"]?.Value<string>() ?? "";
        if (name.Length == 0) return "缺少 `Operation`";
        bool append = name.Equals("Append", StringComparison.OrdinalIgnoreCase);
        bool prepend = name.Equals("Prepend", StringComparison.OrdinalIgnoreCase);
        bool removeDelimited = name.Equals("RemoveDelimited", StringComparison.OrdinalIgnoreCase);
        if (!append && !prepend && !removeDelimited)
            return $"`Operation` 值 `{name}` 非法（只有 Append / Prepend / RemoveDelimited）";

        if (operation["Target"] is not JArray target) return "缺少 `Target`";
        if (target.Count < 2) return "`Target` 至少要两段（根 + 条目）";

        if (removeDelimited)
        {
            if (string.IsNullOrEmpty(operation["Delimiter"]?.Value<string>()))
                return "RemoveDelimited 必须给 `Delimiter`";
            if (string.IsNullOrWhiteSpace(operation["Search"]?.Value<string>()))
                return "RemoveDelimited 必须给 `Search`";
        }

        string? replaceMode = operation["ReplaceMode"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(replaceMode)
            && !replaceMode.Equals("First", StringComparison.OrdinalIgnoreCase)
            && !replaceMode.Equals("Last", StringComparison.OrdinalIgnoreCase)
            && !replaceMode.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return $"`ReplaceMode` 值 `{replaceMode}` 非法（只有 First / Last / All）";
        }

        return null;
    }

    private static bool TryApply(JToken data, JObject operation, string? assetName, out string? error)
    {
        if (operation["Target"] is not JArray target || target.Count == 0)
        {
            error = "TextOperation 缺少 Target";
            return false;
        }

        string[] path = target.Select(t => t.Value<string>() ?? "").ToArray();
        string root = path[0];

        if (string.Equals(root, "Entries", StringComparison.OrdinalIgnoreCase))
            return TryApplyToEntries(data, path, operation, out error);
        // 地图属性与 Entries 同形：都是「字符串键 → 字符串值」的字典。
        // 依据 CP 的 TextOperationTargetRoot 枚举里有 MapProperties（EditMapPatch 用它改地图属性）。
        if (string.Equals(root, "MapProperties", StringComparison.OrdinalIgnoreCase))
            return TryApplyToEntries(data, path, operation, out error);
        if (string.Equals(root, "Fields", StringComparison.OrdinalIgnoreCase))
            return TryApplyToFields(data, path, operation, assetName, out error);

        error = $"TextOperation 的 Target 根 `{root}` 不受支持（应为 Entries / MapProperties / Fields）";
        return false;
    }

    /// <summary>Target 形如 <c>["Entries", 条目键]</c>：作用在条目的字符串值上。</summary>
    private static bool TryApplyToEntries(JToken data, string[] path, JObject operation, out string? error)
    {
        if (path.Length != 2)
        {
            error = "Entries 的 Target 必须恰为 [\"Entries\", 条目键]";
            return false;
        }
        string key = path[1];

        if (data is JObject obj)
        {
            JToken? entry = obj[key];
            if (entry != null && entry.Type != JTokenType.String)
            {
                error = $"TextOperation 目标条目 `{key}` 不是字符串";
                return false;
            }

            if (!TryCompute(operation, entry?.Value<string>(), out string? result, out error)) return false;
            if (result != null) obj[key] = result; // 条目不存在时按空字符串处理并创建
            return true;
        }

        // 数组型资产的条目是对象/模型，CP 的 ListKeyValueEditor 只在条目本身是字符串时才允许文本操作；
        // 本工具遇到的数组资产（如 Data/TriggerActions）条目均为对象，故不支持。
        error = "TextOperation 不支持数组型资产的字符串条目";
        return false;
    }

    /// <summary>Target 形如 <c>["Fields", 条目键, 字段键/下标]</c>。</summary>
    private static bool TryApplyToFields(JToken data, string[] path, JObject operation, string? assetName, out string? error)
    {
        if (path.Length != 3)
        {
            error = "Fields 的 Target 必须恰为 [\"Fields\", 条目键, 字段键/下标]";
            return false;
        }
        if (data is not JObject obj)
        {
            error = "TextOperation 的 Fields 目标不是对象";
            return false;
        }

        string entryKey = path[1];
        string fieldKey = path[2];

        if (obj[entryKey] is not JToken entry || entry.Type == JTokenType.Null)
        {
            error = $"TextOperation 的目标条目 `{entryKey}` 不存在";
            return false;
        }

        // 条目是分隔字符串：字段键是整数下标，分隔符按资产取 ^ 或 /
        if (entry.Type == JTokenType.String)
        {
            if (!int.TryParse(fieldKey, out int index) || index < 0)
            {
                error = $"TextOperation 分隔字符串条目的字段键 `{fieldKey}` 不是有效下标";
                return false;
            }

            char delimiter = IsAchievements(assetName) ? '^' : '/';
            // entry 已在上面判过非空且 Type == String，所以 Value<string>() 必非 null；
            // 用 `!` 是为了让编译器接受（直接 `(string)entry` 会触发 CS8600/CS8602）。
            string[] fields = entry.Value<string>()!.Split(delimiter);
            string? current = index < fields.Length ? fields[index] : null;

            if (!TryCompute(operation, current, out string? result, out error)) return false;
            if (result != null)
            {
                if (index >= fields.Length)
                {
                    int oldLength = fields.Length;
                    Array.Resize(ref fields, index + 1);
                    for (int i = oldLength; i < fields.Length; i++) fields[i] = "";
                }
                fields[index] = result;
                obj[entryKey] = string.Join(delimiter, fields);
            }
            return true;
        }

        // 条目是对象：字段键是字段名，缺失则创建
        if (entry is JObject entryObj)
        {
            JToken? field = entryObj[fieldKey];
            if (field != null && field.Type != JTokenType.String)
            {
                error = $"TextOperation 目标字段 `{entryKey}.{fieldKey}` 不是字符串";
                return false;
            }

            if (!TryCompute(operation, field?.Value<string>(), out string? result, out error)) return false;
            if (result != null) entryObj[fieldKey] = result;
            return true;
        }

        error = $"TextOperation 的目标条目 `{entryKey}` 既不是字符串也不是对象";
        return false;
    }

    /// <summary>对当前文本执行一条文本操作；返回 false 表示操作本身非法。</summary>
    private static bool TryCompute(JObject operation, string? text, out string? result, out string? error)
    {
        result = null;
        error = null;

        string op = operation["Operation"]?.Value<string>() ?? "";

        if (string.Equals(op, "Append", StringComparison.OrdinalIgnoreCase))
        {
            result = AppendOrPrepend(operation, text, prepend: false);
            return true;
        }
        if (string.Equals(op, "Prepend", StringComparison.OrdinalIgnoreCase))
        {
            result = AppendOrPrepend(operation, text, prepend: true);
            return true;
        }
        if (string.Equals(op, "RemoveDelimited", StringComparison.OrdinalIgnoreCase))
            return TryApplyDelimited(operation, text, out result, out error);

        error = $"未知的 TextOperation `{op}`";
        return false;
    }

    private static string AppendOrPrepend(JObject operation, string? text, bool prepend)
    {
        string value = operation["Value"]?.Value<string>() ?? "";

        // CP 源码：Delimiter 缺省为空（文档所称的 "/"/"^" 缺省与源码不符）；原字段为空时忽略分隔符
        string delimiter = string.IsNullOrEmpty(text)
            ? ""
            : operation["Delimiter"]?.Value<string>() ?? "";

        return prepend
            ? value + delimiter + text
            : text + delimiter + value;
    }

    private static bool TryApplyDelimited(JObject operation, string? text, out string? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrEmpty(text)) return true; // 空字段无改动

        string search = operation["Search"]?.Value<string>() ?? "";
        if (string.IsNullOrWhiteSpace(search))
        {
            error = $"{operation["Operation"]?.Value<string>()} 必须指定非空 Search";
            return false;
        }

        string delimiter = operation["Delimiter"]?.Value<string>() ?? "";
        if (string.IsNullOrEmpty(delimiter))
        {
            error = $"{operation["Operation"]?.Value<string>()} 必须指定非空 Delimiter";
            return false;
        }

        string mode = operation["ReplaceMode"]?.Value<string>() ?? "All";
        if (!mode.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("First", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("Last", StringComparison.OrdinalIgnoreCase))
        {
            error = $"无效的 ReplaceMode `{mode}`（应为 First/Last/All）";
            return false;
        }

        string[] values = text.Split(delimiter);

        // 整段相等才算匹配（大小写敏感），不匹配子串
        List<int> matches = new();
        for (int i = 0; i < values.Length; i++)
            if (string.Equals(values[i], search, StringComparison.Ordinal)) matches.Add(i);

        if (matches.Count == 0) return true; // 无匹配 → 原样，不写回

        IEnumerable<int> targets = mode.Equals("First", StringComparison.OrdinalIgnoreCase)
            ? matches.Take(1)
            : mode.Equals("Last", StringComparison.OrdinalIgnoreCase)
                ? matches.TakeLast(1)
                : matches;

        var list = new List<string>(values);
        foreach (int i in targets.OrderByDescending(x => x)) list.RemoveAt(i);

        result = string.Join(delimiter, list);
        return true;
    }

    private static bool IsAchievements(string? assetName) =>
        assetName != null &&
        (assetName.Equals("Data/Achievements", StringComparison.OrdinalIgnoreCase) ||
         assetName.StartsWith("Data/Achievements.", StringComparison.OrdinalIgnoreCase));
}
