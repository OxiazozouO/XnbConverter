namespace SdvCpTool.Core.Conditions;

/// <summary>
/// 求一条 CP <c>When</c> 在**新存档初始状态**下的真假。
/// <para>
/// 只用于「大范围条件地图改动」的落点判断：这类改动没法像小范围那样逐格放
/// <c>ConditionalDoor</c> 门禁，又不能无条件烘焙 —— 那样会把后期状态提前写进初始地图
/// （实测 SVE 的 Shearwater 桥翻新：事件 2000009 还没发生，翻新桥面已经出现）。
/// </para>
/// <para>
/// 判定口径刻意保守：只认「初始存档确定成立/不成立」的叶子，其余一律 <c>null</c>。
/// 已知静态条件（<c>HasMod</c>/配置/<c>Season</c>/<c>FarmType</c>）交给调用方的
/// <c>evaluateLeaf</c>；进度类条件按新存档（事件未看、旗标未设、未结婚、社区中心未完成）求值。
/// </para>
/// </summary>
public static class InitialStateConditions
{
    public static bool? Evaluate(WhenNode? node, Func<WhenNode, bool?>? evaluateLeaf = null)
    {
        if (node == null) return null;
        return node.Kind switch
        {
            WhenNodeKind.Literal => node.LiteralValue,
            WhenNodeKind.Not => Negate(Evaluate(node.Children.FirstOrDefault(), evaluateLeaf)),
            WhenNodeKind.All => All(node.Children, evaluateLeaf),
            WhenNodeKind.Any => Any(node.Children, evaluateLeaf),
            WhenNodeKind.Key => EvaluateKey(node, evaluateLeaf),
            _ => null,
        };
    }

    private static bool? Negate(bool? value) => value.HasValue ? !value.Value : null;

    private static bool? All(List<WhenNode> children, Func<WhenNode, bool?>? evaluateLeaf)
    {
        bool sawUnknown = false;
        foreach (WhenNode child in children)
        {
            bool? value = Evaluate(child, evaluateLeaf);
            if (value == false) return false;
            if (value == null) sawUnknown = true;
        }
        return sawUnknown ? null : true;
    }

    private static bool? Any(List<WhenNode> children, Func<WhenNode, bool?>? evaluateLeaf)
    {
        bool sawUnknown = false;
        foreach (WhenNode child in children)
        {
            bool? value = Evaluate(child, evaluateLeaf);
            if (value == true) return true;
            if (value == null) sawUnknown = true;
        }
        return sawUnknown ? null : false;
    }

    private static bool? EvaluateKey(WhenNode key, Func<WhenNode, bool?>? evaluateLeaf)
    {
        // 先让调用方的静态求值器判：HasMod / 配置 / Season(冻结春天) / FarmType(冻结 Standard)…
        bool? known = evaluateLeaf?.Invoke(key);
        if (known.HasValue) return known;

        // `|contains=x` 的 JSON 值是**取反开关**（CP 官方文档：`"false"` = 不包含）。
        // 其余写法没有取反语义。
        bool negate = false;
        if (key.Arguments?.Contains("|contains=", StringComparison.OrdinalIgnoreCase) == true
            && bool.TryParse(key.Value, out bool expected))
            negate = !expected;

        string name = (key.KeyName ?? "").Trim().ToLowerInvariant();
        switch (name)
        {
            // 新存档里一律「还没有」的进度状态。
            case "hasseenevent":
            case "hasflag":
            case "hasmail":
            case "hasconversationtopic":
            case "hasdialogueanswer":
            case "relationship":
            case "roommate":
            case "spouse":
            case "haswalletitem":
            case "hascaughtfish":
            case "hasvisitedlocation":
            case "iscommunitycentercomplete":
            case "isjojamartcomplete":
            case "hasactivequest":
            case "childnames":
            case "childgenders":
            case "pregnant":
            case "havingchild":
                return negate;

            // 0 心；只有条件本身就是 0 才成立。
            case "hearts":
            case "friendship":
                return negate || HasZero(key.Value);

            // 新存档是春 1 日、年 1、周一、6:00。
            case "year":
                return negate || HasValue(key.Value, "1");
            case "day":
                return negate || HasValue(key.Value, "1");
            case "dayofweek":
                return negate || HasValue(key.Value, "monday");
            case "time":
                return negate || ValueContainsTime(key.Value, 600);
            case "daysplayed":
                return negate || HasValue(key.Value, "0");

            default:
                return null;
        }
    }

    private static bool HasZero(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(v => v == "0") == true;

    private static bool HasValue(string? value, string wanted) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(v => v.Equals(wanted, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool ValueContainsTime(string? value, int minute)
    {
        string[] parts = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            string[] bounds = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (bounds.Length >= 2
                && int.TryParse(bounds[0], out int min)
                && int.TryParse(bounds[1], out int max)
                && minute >= min && minute <= max)
                return true;
        }
        return false;
    }
}
