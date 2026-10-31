using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Conditions;

/// <summary>
/// CP 的**字段引用令牌** —— <c>{{FromFile}}</c> / <c>{{Target}}</c> / <c>{{TargetPathOnly}}</c> /
/// <c>{{TargetWithoutPath}}</c>。补丁的 <c>When</c> 里可以用它们，**逐补丁绑定**
/// （<c>PatchLoader.cs:617-625</c>：只要该补丁**有** <c>FromFile</c>/<c>Target</c> 字段就允许；
/// 取值见 <c>Patch.cs:126</c>）。
/// <para>
/// <b>惯用写法是守卫</b>：<c>"When": { "HasFile:{{FromFile}}": true }</c>
/// ——「这个可选文件在，才打这条」。不展开的后果是 <b>静默丢内容</b>：
/// <c>HasFile</c> 会拿**字面量** <c>{{FromFile}}</c> 去 <c>File.Exists</c>，恒为假。
/// </para>
/// <para>
/// <b>为什么非抽出来不可</b>：同一件事有**两处**求值 ——
/// 判定期的常量折叠（<c>PatchJudge</c>）与写入期的烘焙（<c>Program.RunBake</c>）。
/// 只在折叠那一处展开，烘焙那边就会报「<c>{{FromFile}}</c> 里还有未展开的令牌」，
/// 两处对同一份数据给出不同解释。实战代价：SVE 季节性服装 13 条补丁被静默丢掉，
/// 其中 9 条先是被误判「本就不适用」，改成「不知道」之后又在烘焙处报错。
/// </para>
/// <para>
/// ⚠ <c>ContentPack</c> 里另有一份「Include 的 <c>When</c> 换 <c>{{FromFile}}</c>」——
/// 那是在**解析成树之前**对原始 JSON 走键与值，形状不同（且它只处理 <c>{{FromFile}}</c>，
/// Include 没有 <c>Target</c>），故未合并；改它要另做受控对比。
/// </para>
/// </summary>
public static class FieldReferenceTokens
{
    /// <summary>把一段文本里的字段引用令牌换成值；没得换时原样返回。</summary>
    public static string? Expand(string? text, string? fromFile, string? target)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal)) return text;

        // ★ `{{FromFile}}` **展开出来的东西自己还可能带令牌**，得先把它也展开一遍。
        //   实测 SVE 季节性服装的惯用写法：
        //     "FromFile": "assets/Characters/Monsters/{{TargetWithoutPath}}.png",
        //     "When": { "HasFile:{{FromFile}}": true }
        //   直接把**原始** FromFile 换进去，得到的还是 `…/{{TargetWithoutPath}}.png`，
        //   `File.Exists` 必然假 ⇒ 10 条 `EditImage` 全部判「不知道」。
        //   （CP 那边这两个字段各自都是令牌串，求值时都会先解开。）
        if (fromFile != null && fromFile.Contains("{{", StringComparison.Ordinal))
            fromFile = Expand(fromFile, fromFile: null, target);

        // `{{TargetWithoutPath}}` / `{{TargetPathOnly}}` 必须先换，否则 `{{Target}}`
        // 会把它们的前缀吃掉（`{{TargetWithoutPath}}` 里含子串 `{{Target`）。
        if (target != null)
        {
            int slash = target.LastIndexOf('/');
            string withoutPath = slash < 0 ? target : target[(slash + 1)..];
            string pathOnly = slash < 0 ? "" : target[..slash];
            text = text
                .Replace("{{TargetPathOnly}}", pathOnly, StringComparison.OrdinalIgnoreCase)
                .Replace("{{TargetWithoutPath}}", withoutPath, StringComparison.OrdinalIgnoreCase)
                .Replace("{{Target}}", target, StringComparison.OrdinalIgnoreCase);
        }
        if (fromFile != null)
            text = text.Replace("{{FromFile}}", fromFile, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>
    /// 返回一棵**新树**：每个 Key 叶子的参数与值都换过字段引用令牌。未改动的叶子原样复用。
    /// <para>不改原树 —— 残留条件仍按原样交给 GSQ 翻译，免得「顺手展开」把别处的行为一起改了。</para>
    /// </summary>
    public static WhenNode Expand(WhenNode node, ChangePatch patch) => Expand(node, patch, patch.Target);

    /// <summary>
    /// 同上，但**指定用哪个目标名**展开 <c>{{Target}}</c> 族 —— 多目标补丁要逐目标求值时用
    /// （见 <c>Program.RunBake</c> 里那段：CP 把逗号列表拆成互相独立的补丁，每个目标各展开一次）。
    /// </summary>
    public static WhenNode Expand(WhenNode node, ChangePatch patch, string? target)
    {
        return ExpandCore(node, patch.FromFile, target);
    }

    private static WhenNode ExpandCore(WhenNode node, string? fromFile, string? target)
    {
        if (node.Kind == WhenNodeKind.Key)
        {
            string? args = Expand(node.Arguments, fromFile, target);
            string? value = Expand(node.Value, fromFile, target);
            if (args == node.Arguments && value == node.Value) return node;

            return new WhenNode
            {
                Kind = node.Kind,
                Children = node.Children.ToList(),
                KeyName = node.KeyName,
                Arguments = args,
                Value = value,
                LiteralValue = node.LiteralValue,
            };
        }

        if (node.Children.Count == 0 || (fromFile == null && target == null)) return node;

        var children = node.Children.Select(c => ExpandCore(c, fromFile, target)).ToList();
        if (children.SequenceEqual(node.Children)) return node;

        return new WhenNode
        {
            Kind = node.Kind,
            Children = children,
            KeyName = node.KeyName,
            Arguments = node.Arguments,
            Value = node.Value,
            LiteralValue = node.LiteralValue,
        };
    }
}
