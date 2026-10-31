using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 把**只有装了某个 DLL 才认识的事件命令**换成原版命令序列。
/// <para>
/// 已确认的一类（2026-10-18 调查）：MistyCore 的 <c>append &lt;fragmentId&gt;</c>
/// （`Extensions.cs:42-80`）。它从同地点的 `Data/Events/&lt;loc&gt;` 里按**精确 key** 取片段，
/// 把片段的 `/` 分隔命令**插入当前事件**。纯 XNB 没有这个命令 ⇒ 整条被跳过。
/// </para>
/// <para>
/// 等价改写：转换期把片段**原样内联**到 <c>append</c> 所在位置 —— 与运行时插入的命令序列完全一致
/// （片段本身是无前缀的命令串；见 `GiEX_Javi_10h_badEnding` 等）。递归处理片段里再出现的
/// <c>append</c>，并有深度/数量上限防环。
/// </para>
/// </summary>
public static class EventCommandFixes
{
    private const int MaxDepth = 8;
    private const int MaxExpansions = 512;

    /// <summary>改写 `Data/Events*` 里的 <c>append</c>。返回内联的片段数。</summary>
    public static int InlineAppend(string assetName, JToken? data)
    {
        if (data is not JObject root) return 0;
        if (!assetName.StartsWith("Data/Events", StringComparison.OrdinalIgnoreCase)) return 0;

        int inlined = 0;
        foreach (JProperty property in root.Properties().ToList())
        {
            if (property.Value is not JValue { Type: JTokenType.String } value) continue;
            if (value.Value<string>() is not string script) continue;
            string rewritten = InlineScript(script, root, 0, ref inlined);
            if (rewritten != script) value.Value = rewritten;
        }
        return inlined;
    }

    private static string InlineScript(string script, JObject root, int depth, ref int inlined)
    {
        if (depth > MaxDepth) return script;
        List<string> parts = SplitQuoteAware(script);
        bool changed = false;

        for (int i = 0; i < parts.Count; i++)
        {
            string command = parts[i];
            if (command.StartsWith("quickQuestion ", StringComparison.OrdinalIgnoreCase))
            {
                string rewritten = RewriteQuickQuestionCommand(command, root, eventId: "",
                    wrappers: null, ref inlined, depth + 1);
                if (rewritten != command)
                {
                    parts[i] = rewritten;
                    changed = true;
                }
                continue;
            }

            string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;
            if (!string.Equals(tokens[0], "append", StringComparison.OrdinalIgnoreCase)) continue;
            if (inlined >= MaxExpansions) break;

            string fragmentId = tokens[1];
            if (root[fragmentId] is not JValue { Type: JTokenType.String } fragment) continue;
            if (fragment.Value<string>() is not string fragmentScript) continue;

            string expanded = InlineScript(fragmentScript, root, depth + 1, ref inlined);
            List<string> fragmentParts = SplitQuoteAware(expanded);
            if (fragmentParts.Count == 0) continue;

            parts.RemoveAt(i);
            parts.InsertRange(i, fragmentParts);
            i += fragmentParts.Count - 1;
            inlined++;
            changed = true;
        }

        return changed ? string.Join('/', parts) : script;
    }

    /// <summary>
    /// `quickQuestion &lt;问题&gt;#&lt;答案&gt;…(break)&lt;分支命令&gt;…` 的分支命令里也可能有
    /// <c>append</c> / <c>if</c>（实测 `GiEX_Javi_10h_areYouSure`）。分支之间用 <c>(break)</c> 分隔、
    /// **分支内多条命令用 `\` 分隔**（`Event.cs:12820`）。
    /// </summary>
    private static string RewriteQuickQuestionCommand(string command, JObject root, string eventId,
        List<(string Key, string Value)>? wrappers, ref int rewritten, int depth)
    {
        if (depth > MaxDepth) return command;
        int space = command.IndexOf(' ');
        if (space < 0) return command;
        string head = command[..space];
        string[] segments = command[(space + 1)..].Split("(break)");
        bool changed = false;

        for (int seg = 1; seg < segments.Length; seg++)
        {
            List<string> branch = segments[seg].Split('\\').ToList();
            bool branchChanged = false;
            for (int i = 0; i < branch.Count; i++)
            {
                string[] tokens = branch[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2 && string.Equals(tokens[0], "append", StringComparison.OrdinalIgnoreCase)
                    && root[tokens[1]] is JValue { Type: JTokenType.String } fragment
                    && fragment.Value<string>() is string fragmentScript)
                {
                    List<string> expanded = SplitQuoteAware(fragmentScript);
                    branch.RemoveAt(i);
                    branch.InsertRange(i, expanded);
                    i += expanded.Count - 1;
                    rewritten++;
                    branchChanged = true;
                }
            }
            if (wrappers != null)
                RewriteCommands(branch, root, eventId, wrappers, ref rewritten, depth + 1);
            if (branchChanged || wrappers != null)
            {
                string joined = string.Join('\\', branch);
                if (joined != segments[seg]) { segments[seg] = joined; changed = true; }
            }
        }

        return changed ? head + " " + string.Join("(break)", segments) : command;
    }

    /// <summary>
    /// 把**没有原版等价物、但有视觉/惩罚近似**的单条命令换掉：
    /// <list type="bullet">
    /// <item><c>addColouredSlime &lt;x&gt; &lt;y&gt; &lt;r&gt; &lt;g&gt; &lt;b&gt;</c>（MEEP，103 处）
    /// → 一条带 <c>color</c> 的 <c>temporaryAnimatedSprite</c>（原版 TAS 支持 `color #RRGGBB`，
    /// `Event.cs:1195-1211`；贴图/帧与 `GreenSlime` 一致）。不是 actor，只是精灵。</item>
    /// <item><c>SVE_BadlandsDeath</c> → <c>HospitalDeath/Money -65666</c>：SVE 是
    /// <c>min(33333 + random(0..66666), money)</c>（`SVE.decompiled.cs:5575-5590`）；
    /// 事件命令没有 RNG，取中值 + 原版 1000 的固定近似。</item>
    /// </list>
    /// </summary>
    public static int RewriteMismatchedCommands(string assetName, JToken? data)
    {
        if (data is not JObject root) return 0;
        if (!assetName.StartsWith("Data/Events", StringComparison.OrdinalIgnoreCase)) return 0;

        int rewritten = 0;
        foreach (JProperty property in root.Properties().ToList())
        {
            if (property.Value is not JValue { Type: JTokenType.String } value) continue;
            if (value.Value<string>() is not string script) continue;

            List<string> commands = SplitQuoteAware(script);
            bool changed = false;
            for (int i = 0; i < commands.Count; i++)
            {
                // SpaceCore 的恋爱状态命令：纯 XNB 下是未知命令（`setDating`/`setEngaged`），
                // 约会/结婚状态改由原版花束 458 / 吊坠 460 设置 ⇒ 直接删掉这一段，别留噪声。
                // 注意答案分支里的命令用 `\` 分隔（`quickQuestion …(break)a\setDating X\b`）。
                (string? cleaned, int removed) = RemoveStateCommands(commands[i]);
                if (removed > 0)
                {
                    rewritten += removed;
                    changed = true;
                }
                if (cleaned == null)
                {
                    commands.RemoveAt(i);
                    i--;
                    continue;
                }
                if (cleaned != commands[i]) commands[i] = cleaned;

                string[] tokens = commands[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;

                if (string.Equals(tokens[0], "addColouredSlime", StringComparison.OrdinalIgnoreCase)
                    && tokens.Length >= 6
                    && int.TryParse(tokens[1], out int x) && int.TryParse(tokens[2], out int y)
                    && int.TryParse(tokens[3], out int r) && int.TryParse(tokens[4], out int g)
                    && int.TryParse(tokens[5], out int b))
                {
                    commands[i] =
                        $"temporaryAnimatedSprite Characters\\Monsters\\Green Slime 0 0 16 24 200 4 999 {x} {y} " +
                        $"false false 1 0 1 0 0 0 color #{r:X2}{g:X2}{b:X2}";
                    rewritten++;
                    changed = true;
                }
                else if (string.Equals(tokens[0], "SVE_BadlandsDeath", StringComparison.OrdinalIgnoreCase))
                {
                    commands.RemoveAt(i);
                    commands.Insert(i, "Money -65666");
                    commands.Insert(i, "HospitalDeath");
                    i++;
                    rewritten++;
                    changed = true;
                }
            }
            if (changed) value.Value = string.Join('/', commands);
        }
        return rewritten;
    }

    /// <summary>
    /// 删掉 SpaceCore 的 <c>setDating</c>/<c>setEngaged</c>。
    /// 返回 <c>null</c> = 整条命令都该删；否则返回清掉分支内命令后的文本（没改就返回原串），
    /// 以及删掉的条数。
    /// </summary>
    private static (string? Command, int Removed) RemoveStateCommands(string command)
    {
        if (command.StartsWith("quickQuestion ", StringComparison.OrdinalIgnoreCase))
        {
            int space = command.IndexOf(' ');
            string[] segments = command[(space + 1)..].Split("(break)");
            int removed = 0;
            for (int s = 1; s < segments.Length; s++)
            {
                List<string> branch = segments[s].Split('\\').ToList();
                int before = branch.Count;
                branch.RemoveAll(IsStateCommand);
                if (branch.Count == before) continue;
                removed += before - branch.Count;
                segments[s] = string.Join('\\', branch);
            }
            return removed == 0
                ? (command, 0)
                : (command[..space] + " " + string.Join("(break)", segments), removed);
        }
        return IsStateCommand(command) ? (null, 1) : (command, 0);
    }

    private static bool IsStateCommand(string command)
    {
        string[] tokens = command.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        return tokens[0].Equals("setDating", StringComparison.OrdinalIgnoreCase)
               || tokens[0].Equals("setEngaged", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把 MistyCore 的 <c>if &lt;GSQ&gt;##&lt;真分支&gt;##&lt;假分支&gt;</c> 改写成原版 <c>fork</c>。
    /// <para>
    /// 只处理**全部是 `PLAYER_HAS_MAIL`** 的 GSQ（本语料 3 处全是；MistyCore `Extensions.cs:17-40`）。
    /// <c>fork &lt;mailId&gt; &lt;eventKey&gt;</c> 的语义（`Event.cs:1796-1864`）：邮件存在 ⇒
    /// 用目标事件**替换全部剩余命令**；不存在 ⇒ 跳过、继续下一条。
    /// ⇒ 真分支 + 原尾部写成 wrapper 片段，<c>fork</c> 后面内联「假分支 + 尾部」。
    /// 多封邮件 AND 时逐层嵌套 wrapper。
    /// </para>
    /// </summary>
    public static int RewriteMailIfs(string assetName, JToken? data)
    {
        if (data is not JObject root) return 0;
        if (!assetName.StartsWith("Data/Events", StringComparison.OrdinalIgnoreCase)) return 0;

        int rewritten = 0;
        var wrappers = new List<(string Key, string Value)>();
        foreach (JProperty property in root.Properties().ToList())
        {
            if (property.Value is not JValue { Type: JTokenType.String } value) continue;
            if (value.Value<string>() is not string script) continue;

            string eventId = property.Name.Split('/')[0];
            List<string> commands = SplitQuoteAware(script);
            if (RewriteCommands(commands, root, eventId, wrappers, ref rewritten, depth: 0))
                value.Value = string.Join('/', commands);
        }

        foreach ((string key, string body) in wrappers)
        {
            if (root.Property(key) != null) continue;   // 撞名：不覆盖作者自己的条目
            root.Add(new JProperty(key, body));
        }
        return rewritten;
    }

    /// <summary>从右往左处理命令：遇到可改写的 <c>if</c> 就把「它 + 尾部」整体换掉。</summary>
    private static bool RewriteCommands(List<string> commands, JObject root, string eventId,
        List<(string Key, string Value)> wrappers, ref int rewritten, int depth)
    {
        if (depth > MaxDepth) return false;
        bool changed = false;
        for (int i = commands.Count - 1; i >= 0; i--)
        {
            if (commands[i].StartsWith("quickQuestion ", StringComparison.OrdinalIgnoreCase))
            {
                string rewrittenCommand = RewriteQuickQuestionCommand(commands[i], root, eventId, wrappers,
                    ref rewritten, depth + 1);
                if (rewrittenCommand != commands[i]) { commands[i] = rewrittenCommand; changed = true; }
                continue;
            }

            if (!TryParseMailIf(commands[i], out string gsq, out List<string> mails, out string trueBranch,
                    out string falseBranch))
                continue;

            List<string> tail = commands.GetRange(i + 1, commands.Count - i - 1);
            List<string> trueCmds = ExpandBranch(trueBranch, root, eventId, wrappers, ref rewritten, depth + 1);
            List<string> falseCmds = ExpandBranch(falseBranch, root, eventId, wrappers, ref rewritten, depth + 1);
            List<string> trueWithTail = trueCmds.Concat(tail).ToList();
            List<string> falseWithTail = falseCmds.Concat(tail).ToList();
            RewriteCommands(trueWithTail, root, eventId, wrappers, ref rewritten, depth + 1);
            RewriteCommands(falseWithTail, root, eventId, wrappers, ref rewritten, depth + 1);

            // 从最后一封邮件往前建 wrapper；Wrap(n-1) 就是「真分支 + 尾部」。
            string wrapKey = AddWrapper(wrappers, eventId, mails.Count - 1, string.Join('/', trueWithTail));
            for (int k = mails.Count - 2; k >= 0; k--)
            {
                var body = new List<string> { $"fork {mails[k + 1]} {wrapKey}" };
                body.AddRange(falseWithTail);
                wrapKey = AddWrapper(wrappers, eventId, k, string.Join('/', body));
            }

            var replacement = new List<string> { $"fork {mails[0]} {wrapKey}" };
            replacement.AddRange(falseWithTail);
            commands.RemoveRange(i, commands.Count - i);
            commands.AddRange(replacement);
            rewritten++;
            changed = true;
        }
        return changed;
    }

    /// <summary>分支里可能还有 <c>append</c> / 嵌套 <c>if</c>：展开成命令序列。</summary>
    private static List<string> ExpandBranch(string branch, JObject root, string eventId,
        List<(string Key, string Value)> wrappers, ref int rewritten, int depth)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(branch)) return result;
        foreach (string command in SplitQuoteAware(branch))
        {
            if (command.StartsWith("quickQuestion ", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(RewriteQuickQuestionCommand(command, root, eventId, wrappers, ref rewritten, depth + 1));
                continue;
            }

            string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && string.Equals(tokens[0], "append", StringComparison.OrdinalIgnoreCase))
            {
                if (root[tokens[1]] is JValue { Type: JTokenType.String } fragment
                    && fragment.Value<string>() is string fragmentScript)
                {
                    result.AddRange(ExpandBranch(fragmentScript, root, eventId, wrappers, ref rewritten, depth + 1));
                    continue;
                }
            }
            result.Add(command);
        }
        RewriteCommands(result, root, eventId, wrappers, ref rewritten, depth + 1);
        return result;
    }

    private static string AddWrapper(List<(string Key, string Value)> wrappers, string eventId, int index,
        string body)
    {
        string key = $"{eventId}__sdvcp_if{index}";
        int n = 2;
        while (wrappers.Any(w => w.Key == key)) key = $"{eventId}__sdvcp_if{index}_{n++}";
        wrappers.Add((key, body));
        return key;
    }

    /// <summary><c>if &lt;GSQ&gt;##&lt;真&gt;##&lt;假&gt;</c>，且 GSQ 全是同号的 <c>PLAYER_HAS_MAIL</c>。</summary>
    private static bool TryParseMailIf(string command, out string gsq, out List<string> mails,
        out string trueBranch, out string falseBranch)
    {
        gsq = "";
        mails = new List<string>();
        trueBranch = "";
        falseBranch = "";
        if (!command.StartsWith("if ", StringComparison.OrdinalIgnoreCase)) return false;

        string rest = command[3..];
        string[] parts = rest.Split("##");
        if (parts.Length < 2) return false;
        gsq = parts[0];
        trueBranch = parts[1];
        falseBranch = parts.Length >= 3 ? parts[2] : "";

        bool? negate = null;
        foreach (string rawClause in gsq.Split(','))
        {
            string clause = rawClause.Trim();
            bool clauseNegate = clause.StartsWith('!');
            if (clauseNegate) clause = clause[1..].Trim();
            if (negate.HasValue && negate.Value != clauseNegate) return false;   // 混合取反：不猜
            negate = clauseNegate;

            string[] tokens = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) return false;
            if (!string.Equals(tokens[0], "PLAYER_HAS_MAIL", StringComparison.OrdinalIgnoreCase)) return false;
            mails.Add(tokens[2]);
        }
        if (mails.Count == 0) return false;

        if (negate == true)
        {
            (trueBranch, falseBranch) = (falseBranch, trueBranch);
        }
        return true;
    }

    /// <summary>按 `/` 切分，**不切引号里的**（`speak X "a/b"` 是一整条命令）。</summary>
    private static List<string> SplitQuoteAware(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                current.Append(c).Append(text[++i]);
                continue;
            }
            if (c == '"') quoted = !quoted;
            if (c == '/' && !quoted)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        // ★ 引号**没闭合**时退回朴素切分：MistyCore 的 `append` 就是 `value.Split('/')`
        //   （`Extensions.cs:42-80`），实测 `GiEX_Javi_10h_areYouSure` 的 `speak` 少了收尾引号 ——
        //   按引号感知会把后面的 `/quickQuestion` 吞进对话里，永远展开不到。
        if (quoted) return text.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        parts.Add(current.ToString());
        return parts;
    }
}
