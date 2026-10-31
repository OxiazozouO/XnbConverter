namespace SdvCpTool.Core.Cp.Lexing;

/// <summary>
/// <b>CP 词法器的移植</b>：把一段文本切成 <c>{{…}}</c> 令牌树。
/// <para>
/// <b>为什么不再手写字符串匹配</b>：本仓库此前对「<c>{{</c> 的结构」写了**四份**各自的
/// <c>IndexOf</c> 扫描（判定的 <c>HasTokenText</c>、i18n 的 <c>FindTokenEnd</c>/<c>TopLevelPipe</c>、
/// 动态令牌的 <c>Expand</c>、Include 的 JSON 替换），**四份里三份的括号配对是错的** ——
/// <c>{{i18n:KEY |PlayerName={{PlayerName}}}}</c> 这种**嵌套**会被「找第一个 <c>}}</c>」截断，
/// 于是整段被当成长度到内层令牌为止的东西。三次静默丢内容都是它：
/// 13 处令牌落进产出、20 条 <c>HasFile:{{FromFile}}</c> 被丢掉、内嵌参数一律代不掉。
/// </para>
/// <para>
/// 结构只有一处定义 —— <b>CP 自己的词法器</b>（<c>ContentPatcher.Framework.Lexing/Lexer.cs</c>，
/// 350 行）。这里按同样的名字、同样的顺序、同样的边界条件移植过来，包括
/// <c>ParseBitQueue</c> 那段「合并相邻字面量、在令牌输入边界上修剪空白」的后处理 ——
/// 少了它，参数解析的边界就与 CP 不一致。
/// </para>
/// <para>
/// <b>移植自</b> CP 的 <c>Lexer.cs</c>（<c>TokenizeString</c> / <c>ParseBits</c> / <c>ParseBitQueue</c> /
/// <c>ExtractToken</c> / <c>ExtractInputArguments</c>），逐段对应。
/// 未移植的只有 <c>SplitLexically</c>（本仓库没有对应的调用点）。
/// </para>
/// </summary>
public sealed class CpLexer
{
    private const char StartTokenChar = '{';
    private const char EndTokenChar = '}';
    private const char NamedInputChar = '|';
    private const char PositionalInputChar = ':';

    private static readonly char[] SplitPattern = { '{', '}', ':', '|' };

    private static readonly LexBit EmptyStringBit = new(LexBitType.Literal, "", 0);
    private static readonly LexBit StartTokenBit = new(LexBitType.StartToken, "{{", 0);
    private static readonly LexBit EndTokenBit = new(LexBitType.EndToken, "}}", 0);
    private static readonly LexBit NamedInputSeparatorBit = new(LexBitType.NamedInputArgSeparator, "|", 0);
    private static readonly LexBit PositionalInputSeparatorBit = new(LexBitType.PositionalInputArgSeparator, ":", 0);

    public static CpLexer Instance { get; } = new();

    /// <summary>文本里有没有可能含令牌（与 CP 的 <c>MightContainTokens</c> 同）。</summary>
    public bool MightContainTokens(string? rawText) =>
        !string.IsNullOrEmpty(rawText) && rawText.Contains("{{", StringComparison.Ordinal);

    /// <summary>
    /// 按分隔符切分，但**切不动令牌内部** —— 与 CP 的 <c>SplitLexically</c> 同。
    /// <para>
    /// <b>为什么非它不可</b>：i18n 的具名参数形如 <c>|name=value</c>，其中 <c>value</c> 本身
    /// 常常是个令牌，而令牌**输入里也有 <c>|</c>**：
    /// <c>{{i18n:A |default={{i18n:B |default={{i18n:C}}}}}}</c>、
    /// <c>{{HasSeenEvent|contains=1234}}</c>。裸 <c>Split('|')</c> 会把它们切成碎片，
    /// 切出来的「参数名」是垃圾（实测在 SVE 的 Claire 婚姻对话上产出过一个叫 <c>contains</c> 的
    /// 假占位符，并据此把整条补丁判了失败）。
    /// </para>
    /// </summary>
    public IEnumerable<string> SplitLexically(string? str, string delimiter = ",",
        bool ignoreEmpty = true, bool trim = true)
    {
        if (str == null) return Array.Empty<string>();

        IEnumerable<string> parts = RawSplit(str, delimiter);
        if (trim) parts = parts.Select(p => p.Trim());
        if (ignoreEmpty) parts = parts.Where(p => !string.IsNullOrEmpty(p));
        return parts;
    }

    private IEnumerable<string> RawSplit(string text, string delimiter)
    {
        if (!text.Contains(delimiter, StringComparison.Ordinal))
        {
            yield return text;
            yield break;
        }

        if (!MightContainTokens(text))
        {
            foreach (string part in text.Split(delimiter)) yield return part;
            yield break;
        }

        var current = new System.Text.StringBuilder(text.Length);
        foreach (ILexToken bit in ParseBits(text, impliedBraces: false))
        {
            if (bit is LexTokenLiteral literal && literal.Text.Contains(delimiter, StringComparison.Ordinal))
            {
                string[] pieces = literal.Text.Split(delimiter);
                current.Append(pieces[0]);
                yield return current.ToString();
                current.Clear();
                for (int i = 1; i < pieces.Length - 1; i++) yield return pieces[i];
                current.Append(pieces[^1]);
            }
            else
            {
                current.Append(bit.ToString());
            }
        }
        yield return current.ToString();
    }

    /// <summary>
    /// 找 <paramref name="contentStart"/> 处令牌**配对**的 <c>}}</c> 的起点下标；找不到返回 <c>-1</c>。
    /// <para>
    /// 走的是**同一份**比特流（不是第二套括号匹配）：比特自带偏移，按 <c>{{</c>/<c>}}</c> 计数到
    /// 深度归零即可。<c>{{i18n:k{{X}}}}</c> 的结尾是最外层那个。
    /// </para>
    /// </summary>
    public int FindTokenEnd(string text, int contentStart)
    {
        int depth = 0;
        foreach (LexBit bit in TokenizeString(text))
        {
            if (bit.Start < contentStart) continue;
            switch (bit.Type)
            {
                case LexBitType.StartToken:
                    depth++;
                    break;
                case LexBitType.EndToken:
                    if (depth == 0) return bit.Start;
                    depth--;
                    break;
            }
        }
        return -1;
    }

    // ---------------------------------------------------------------- 切比特

    public IEnumerable<LexBit> TokenizeString(string? rawText)
    {
        if (rawText == null) yield break;
        if (rawText.Length == 0) { yield return EmptyStringBit; yield break; }

        int length = rawText.Length;
        int maxIndex = length - 1;
        int lastMatch = 0;
        int start = 0;

        while (lastMatch < length)
        {
            int index = rawText.IndexOfAny(SplitPattern, start);
            if (index == -1)
            {
                if (lastMatch < length)
                    yield return new LexBit(LexBitType.Literal, rawText[lastMatch..], lastMatch);
                break;
            }

            char match = rawText[index];
            switch (match)
            {
                case StartTokenChar:
                case EndTokenChar:
                    // 单个 `{` / `}` 不是结构字符，当字面量继续走
                    if (index >= maxIndex || match != rawText[index + 1]) { start = index + 1; break; }

                    if (lastMatch != index)
                        yield return new LexBit(LexBitType.Literal, rawText[lastMatch..index], lastMatch);
                    yield return (match == StartTokenChar ? StartTokenBit : EndTokenBit) with { Start = index };
                    start = index + 2;
                    lastMatch = start;
                    break;

                case NamedInputChar:
                    if (lastMatch != index)
                        yield return new LexBit(LexBitType.Literal, rawText[lastMatch..index], lastMatch);
                    yield return NamedInputSeparatorBit with { Start = index };
                    start = index + 1;
                    lastMatch = start;
                    break;

                case PositionalInputChar:
                    if (lastMatch != index)
                        yield return new LexBit(LexBitType.Literal, rawText[lastMatch..index], lastMatch);
                    yield return PositionalInputSeparatorBit with { Start = index };
                    start = index + 1;
                    lastMatch = start;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Found invalid lexical token '{match}' while parsing string '{rawText}'.");
            }
        }
    }

    // ---------------------------------------------------------------- 解析成树

    /// <param name="impliedBraces">
    /// 真 = 整串就是一个令牌体（不含外层 <c>{{ }}</c>）—— CP 解析令牌**输入参数**时用这个模式。
    /// </param>
    public IEnumerable<ILexToken> ParseBits(string? rawText, bool impliedBraces, bool trim = false)
        => ParseBits(TokenizeString(rawText), impliedBraces, trim);

    public IEnumerable<ILexToken> ParseBits(IEnumerable<LexBit> bits, bool impliedBraces, bool trim = false)
        => ParseBitQueue(new Queue<LexBit>(bits), impliedBraces, trim);

    /// <summary>
    /// <b>宽松版</b>：解析失败不抛异常，改为返回 <c>false</c> 并把整串当字面量。
    /// <para>
    /// 本仓库要拿词法器去**裁决**（「这段文本里还有没有代不掉的令牌」），而待裁决的文本来自
    /// 各种第三方包 —— 合法与畸形混在一起。畸形按 CP 的语义本来就会被判非法，
    /// 调用方需要的是一句「解析不了」，不是异常。
    /// </para>
    /// </summary>
    public bool TryParse(string? rawText, out IReadOnlyList<ILexToken> tokens)
    {
        try
        {
            tokens = ParseBits(rawText, impliedBraces: false).ToList();
            return true;
        }
        catch (Exception)
        {
            tokens = Array.Empty<ILexToken>();
            return false;
        }
    }

    private IEnumerable<ILexToken> ParseBitQueue(Queue<LexBit> input, bool impliedBraces, bool trim)
    {
        LinkedList<ILexToken> tokens;
        try
        {
            tokens = new LinkedList<ILexToken>(RawParse());
        }
        catch (Exception inner)
        {
            string text = string.Concat(input.Select(p => p.Text));
            throw new LexFormatException($"Error parsing '{text}' as a tokenizable string", inner);
        }

        // 合并相邻字面量、在令牌输入的两侧修剪空白、必要时丢掉空的边缘字面量。
        // `LinkedListNode<T>` 没重写 Equals ⇒ 默认比较器就是引用比较（等价 CP 的 ObjectReferenceComparer）。
        var toRemove = new HashSet<LinkedListNode<ILexToken>>();
        for (LinkedListNode<ILexToken>? node = tokens.First; node != null; node = node.Next)
        {
            if (node.Value is not LexTokenLiteral literal) continue;

            ILexToken? previous = node.Previous?.Value;
            ILexToken? next = node.Next?.Value;
            string text = literal.Text;

            if (previous is LexTokenLiteral previousLiteral)
            {
                text = previousLiteral.Text + text;
                toRemove.Add(node.Previous!);
            }
            if (next != null && next.Type == LexTokenType.TokenInput)
                text = text.TrimEnd();
            if (previous != null && previous.Type == LexTokenType.TokenInput)
                text = text.TrimStart();

            if (trim && (previous == null || next == null))
            {
                if (previous == null) text = text.TrimStart();
                if (next == null) text = text.TrimEnd();
                if (text == "") toRemove.Add(node);
            }

            literal.Text = text;
        }
        foreach (LinkedListNode<ILexToken> node in toRemove)
            tokens.Remove(node);

        return tokens;

        IEnumerable<ILexToken> RawParse()
        {
            if (impliedBraces)
            {
                if (input.Any())
                {
                    yield return ExtractToken(input, impliedBraces: true);
                    if (input.Any())
                    {
                        LexBit next = input.Peek();
                        throw new LexFormatException($"Unexpected {next.Type}, expected {LexBitType.Literal}");
                    }
                }
                yield break;
            }

            while (input.Any())
            {
                LexBit next = input.Peek();
                switch (next.Type)
                {
                    case LexBitType.StartToken:
                        yield return ExtractToken(input, impliedBraces: false);
                        break;
                    case LexBitType.Literal:
                    case LexBitType.PositionalInputArgSeparator:
                    case LexBitType.NamedInputArgSeparator:
                        input.Dequeue();
                        yield return new LexTokenLiteral(next.Text);
                        break;
                    default:
                        throw new LexFormatException(
                            $"Unexpected {next.Type}, expected {LexBitType.StartToken} or {LexBitType.Literal}");
                }
            }
        }
    }

    private LexTokenToken ExtractToken(Queue<LexBit> input, bool impliedBraces)
    {
        int sourceStart = input.Any() ? input.Peek().Start : -1;   // 供「按原文位置替换」用

        if (!impliedBraces)
        {
            LexBit startBit = GetNextAndAssert("start of token ('{{')");
            if (startBit.Type != LexBitType.StartToken)
                throw new LexFormatException($"Unexpected {startBit.Type} at start of token.");
        }

        LexBit name = GetNextAndAssert("token name");
        if (name.Type != LexBitType.Literal)
            throw new LexFormatException($"Unexpected {name.Type} where token name should be.");

        LexTokenInput? inputArgs = null;
        if (input.Any())
        {
            LexBitType type = input.Peek().Type;
            if (type is LexBitType.PositionalInputArgSeparator or LexBitType.NamedInputArgSeparator)
            {
                if (type == LexBitType.PositionalInputArgSeparator) input.Dequeue();
                inputArgs = ExtractInputArguments(input);
            }
        }

        int sourceEnd = -1;
        if (!impliedBraces)
        {
            LexBit endBit = GetNextAndAssert("end of token ('}}')");
            if (endBit.Type != LexBitType.EndToken)
                throw new LexFormatException($"Unexpected {endBit.Type} before end of token.");
            sourceEnd = endBit.Start + endBit.Text.Length;
        }

        return new LexTokenToken(name.Text.Trim(), inputArgs, impliedBraces)
        {
            Start = sourceStart,
            End = sourceEnd,
        };

        LexBit GetNextAndAssert(string expected)
            => input.Any() ? input.Dequeue() : throw new LexFormatException($"Reached end of input, expected {expected}.");
    }

    private LexTokenInput ExtractInputArguments(Queue<LexBit> input)
    {
        var queue = new Queue<LexBit>();
        int depth = 0;
        bool done = false;

        while (!done && input.Any())
        {
            switch (input.Peek().Type)
            {
                case LexBitType.StartToken:
                    depth++;
                    queue.Enqueue(input.Dequeue());
                    break;
                case LexBitType.EndToken:
                    depth--;
                    if (depth < 0) done = true;      // 这个 `}}` 属于外层的令牌，不归参数段
                    else queue.Enqueue(input.Dequeue());
                    break;
                default:
                    queue.Enqueue(input.Dequeue());
                    break;
            }
        }

        return new LexTokenInput(ParseBitQueue(queue, impliedBraces: false, trim: true).ToArray());
    }
}
