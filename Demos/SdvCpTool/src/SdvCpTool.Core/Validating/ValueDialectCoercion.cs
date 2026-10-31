using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// 把 CP 的**值写法**翻成 XNB 树要的写法。同一份数据在两边是两种 JSON 方言，
/// 打包器只认 XNB 树那一边 —— 不换就**打不出包**。
///
/// <para><b>两条规则，都不是"改取值"，只换写法：</b></para>
/// <list type="number">
/// <item><b>结构化标量</b>：CP 可以写 <c>"PositionOffset": "0, -32"</c>（游戏自己的转换器认这种糖），
/// XNB 树里一律写 <c>{"X": 0.0, "Y": -32.0}</c>；打包器见到字符串直接报
/// <c>Error converting value "0, -32" to type 'Microsoft.Xna.Framework.Vector2'</c>。
/// 实测代价（Cornucopia「聚宝盆 制作」）：<c>Data/Machines</c> 连 8 个语言变体共 **10 个文件打包失败**；
/// 换完之后同一棵小树 <b>成功 103 / 失败 0</b>。</item>
/// <item><b>整数值不写成小数记号</b>：CP 可以写 <c>"IconSpriteIndex": 0.0</c>，XNB 树这边打包器报
/// <c>Input string '0.0' is not a valid integer</c>。实测代价：<c>Data/Objects</c> 连 5 个语言变体
/// **6 个文件打包失败**（那是核心资产，丢了就没有对象改动）。<c>0.0</c> 与 <c>0</c> 是**同一个数**
/// ⇒ 换写法不改取值。</item>
/// </list>
///
/// <para>
/// <b>为什么按类型而不是按形状猜</b>：字段是 <c>Vector2</c> 还是 <c>Rectangle</c>（决定
/// <c>"0, -32"</c> 拆几个分量）、以及字段是不是整数类型，都是 <see cref="GameDataSchema"/>
/// 从**游戏源码**读出来的**声明事实**；按逗号个数、或按"看起来像整数"反推都是**推断**。
/// 所以整数那条**只在 <see cref="GameDataSchema.IntegerFields"/> 认得的字段上做**，
/// 其余一律不碰（否则满树的 <c>{"X": 0.0}</c> 都会被改写法 —— 那是浮点字段的正常写法）。
/// </para>
/// <para>
/// <b>只换写法、不改取值，也绝不硬凑</b>：分量个数与声明类型不符（<c>Vector2</c> 位上写了三个数）
/// 一律**原样不动**。
/// </para>
/// <para>
/// <b>第三条规则：装不进去的值 ⇒ 删掉那个字段并报出来</b>（2026-10-13 加）。前两条只换写法，
/// 这一条是**取舍**：整数字段上写了带小数部分的 <c>0.5</c>、枚举字段上写了布尔 <c>true</c>，
/// 打包器都会**整份资产**打不出包（实测 <c>Data/Weapons</c> 的 4 个语言变体、
/// <c>Data/Characters</c> 的 10 个 —— 后者那棵树里有 **221 个 mod 新增的 NPC 定义**，
/// 覆盖式装机下会整份退回原版）。**不改成整数**（<c>0.5</c> 是 0 还是 1 只有作者知道，
/// 猜就是改取值），**也不静默**（删了哪个字段逐条写进报告）。
/// </para>
/// </summary>
public static class ValueDialectCoercion
{
    /// <summary>结构类型名 → 分量的字段名（<b>顺序即字符串里的顺序</b>）。</summary>
    private static readonly Dictionary<string, string[]> Components = new(StringComparer.Ordinal)
    {
        ["Vector2"] = ["X", "Y"],
        ["Vector3"] = ["X", "Y", "Z"],
        ["Rectangle"] = ["X", "Y", "Width", "Height"],
    };

    /// <summary>下钻最大层数（与 <see cref="GameDataValidator"/> 同，防模型里的环）。</summary>
    private const int MaxDepth = 4;

    /// <summary>
    /// 整份资产：按 <c>资产名 → 元素类</c> 取出元素，逐个结构对一遍。认不出的资产**不动**。
    /// </summary>
    /// <returns>
    /// 要报出去的说明（目前只有"**删掉了装不进去的字段**"这一类 —— 那是**有代价的取舍**：
    /// 那个字段的意图没了，只是换来"整份资产还能打包"，不能静默做）。
    /// </returns>
    public static List<string> CoerceAsset(string assetName, JToken root)
    {
        var notes = new List<string>();
        if (!GameDataSchema.AssetElement.TryGetValue(assetName, out string? elementClass)) return notes;

        switch (root)
        {
            case JObject map:
                foreach (JProperty p in map.Properties()) Coerce(elementClass, p.Value, 1, assetName, notes);
                break;
            case JArray array:
                foreach (JToken element in array) Coerce(elementClass, element, 1, assetName, notes);
                break;
        }
        return notes.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>按 <paramref name="className"/> 的模型逐层下钻，把结构化标量位上的逗号数字串就地换成对象。</summary>
    private static void Coerce(string className, JToken value, int depth, string assetName, List<string> notes)
    {
        if (depth > MaxDepth) return;
        if (value is not JObject entry) return;
        if (!GameDataSchema.Types.TryGetValue(className, out Dictionary<string, FieldRef>? fields)) return;

        // 整数字段**不在 `Types` 里**（那张表只收容器型字段，标量被整体丢掉了），所以单独走一遍。
        if (GameDataSchema.IntegerFields.TryGetValue(className, out string[]? intFields))
        {
            foreach (string field in intFields)
            {
                // 字段名按**不区分大小写**取：打包器与游戏都这么匹配（同 GameDataValidator）。
                JProperty? property = entry.Property(field, StringComparison.OrdinalIgnoreCase);
                if (property == null || property.Value.Type == JTokenType.Null) continue;
                NormalizeIntegerNotation(property);
                DropIfNotAnInteger(entry, property, assetName, notes);
            }
        }

        // 枚举字段上写了布尔：**装不进去**（`Unexpected token Boolean when parsing enum`）⇒ 删字段 + 报。
        if (GameDataSchema.EnumFields.TryGetValue(className, out string[]? enumFields))
        {
            foreach (string field in enumFields)
            {
                JProperty? property = entry.Property(field, StringComparison.OrdinalIgnoreCase);
                if (property == null || property.Value.Type != JTokenType.Boolean) continue;
                notes.Add($"`{assetName}` 的 `{field}` 是**枚举**字段却写了布尔值 `{property.Value}`"
                          + " ⇒ 删掉这个字段（不删的话整份资产打不出包："
                          + "`Unexpected token Boolean when parsing enum`）");
                property.Remove();
            }
        }

        foreach ((string field, FieldRef expected) in fields)
        {
            if (entry[field] is not JToken item) continue;
            if (item.Type == JTokenType.Null) continue;   // 「没设」不是「设错」

            if (expected.Kind == FieldKind.Struct)
            {
                // 结构化标量内部没有可下钻的类，处理完即止。
                if (item.Type == JTokenType.String
                    && Components.TryGetValue(expected.ElementType, out string[]? names)
                    && Split(item.Value<string>(), names.Length) is double[] parts)
                {
                    var boxed = new JObject();
                    for (int i = 0; i < names.Length; i++) boxed[names[i]] = parts[i];
                    entry[field] = boxed;
                }
                continue;
            }

            if (expected.ElementType.Length == 0) continue;

            switch (item)
            {
                case JArray array:
                    foreach (JToken element in array)
                        Coerce(expected.ElementType, element, depth + 1, assetName, notes);
                    break;
                case JObject map:
                    foreach (JProperty p in map.Properties())
                        Coerce(expected.ElementType, p.Value, depth + 1, assetName, notes);
                    break;
            }
        }
    }

    /// <summary>
    /// 整数字段上的 <c>0.0</c> → <c>0</c>（**同一个数**，只换写法）。
    /// 带小数部分的**不在这里表态** —— 它改不动取值，由 <see cref="DropIfNotAnInteger"/> 处置。
    /// </summary>
    private static void NormalizeIntegerNotation(JProperty property)
    {
        JToken token = property.Value;
        if (token.Type != JTokenType.Float) return;      // 已经是整数写法 / 字符串 ⇒ 不表态

        double value = token.Value<double>();
        if (value != Math.Floor(value) || value < long.MinValue || value > long.MaxValue)
            return;                                       // 带小数 / 越界：那是另一类问题

        property.Value = new JValue((long)value);
    }

    /// <summary>
    /// 整数字段上**装不进去**的值（带小数部分的 <c>0.5</c>、不是整数的字符串）⇒ **删掉这个字段并报出来**。
    ///
    /// <para>
    /// <b>为什么不照原样留着</b>：打包器对这种值直接报
    /// <c>Input string '0.5' is not a valid integer</c> ⇒ **整份资产打不出包**
    /// （实测 `Data/Weapons` 的 `DTZ.DowntownZuzuCP_QiBlade.Precision: 0.5` 连 4 个语言变体一起报废；
    /// `Data/Characters` 类似情形会连 **221 个 mod 新增 NPC 定义**一起丢 —— 覆盖式装机下整份退回原版）。
    /// </para>
    /// <para>
    /// <b>为什么不改成整数</b>：`0.5` 是 <c>0</c> 还是 <c>1</c>，只有作者知道；猜一个就是**改取值**。
    /// 删字段至少让其余条目照常生效，而且**在报告里说得清**。
    /// </para>
    /// </summary>
    private static void DropIfNotAnInteger(JObject entry, JProperty property, string assetName, List<string> notes)
    {
        JToken token = property.Value;
        string? shown = null;

        if (token.Type == JTokenType.Float)
        {
            double value = token.Value<double>();
            if (value == Math.Floor(value) && value >= long.MinValue && value <= long.MaxValue)
                return;                                   // 整数写法（`0.0`）已经被换成整数，没装不进去的东西
            shown = value.ToString(CultureInfo.InvariantCulture);
        }
        else if (token.Type == JTokenType.String)
        {
            string? text = token.Value<string>();
            if (text != null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return;                                   // 数字串：打包器认
            shown = text;
        }
        else
        {
            return;                                       // 已经是整数 / 布尔 / 其它：不在这里表态
        }

        notes.Add($"`{assetName}` 的 `{property.Name}` 是**整数**字段却写了 `{shown}`"
                  + " ⇒ 删掉这个字段（不删的话整份资产打不出包："
                  + "`Input string '…' is not a valid integer`）");
        property.Remove();
    }

    /// <summary>逗号分隔的、恰好 <paramref name="count"/> 个数字；否则返回 <c>null</c>（不表态）。</summary>
    private static double[]? Split(string? text, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string[] parts = text.Split(',');
        if (parts.Length != count) return null;

        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return null;
        }
        return values;
    }
}
