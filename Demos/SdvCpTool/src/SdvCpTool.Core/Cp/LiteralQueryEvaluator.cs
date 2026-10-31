namespace SdvCpTool.Core.Cp;

/// <summary>
/// CP 的 <c>{{Query: 15 + 2}}</c>：**只认纯字面量算式**（整数、`+ - * / %`、括号、一元负号）。
///
/// <para>
/// <b>为什么要有它</b>：实测某包的 <c>AddWarps</c> 写成
/// <c>"26 35 Farm {{Query:58}} {{Query:15 + 2}} 27 35 …"</c> —— CP 会把它们算成数字写出去，
/// 我们没代 ⇒ **整条 <c>Warp</c> 带着花括号进产物** ⇒ 游戏
/// <c>Failed parsing warp '…': got a non-numeric value for one of the X/Y positions</c>
/// ⇒ **那一整串传送全废**（实机日志 10 行，位置 `Custom_ConvenientRoad`）。
/// </para>
///
/// <para>
/// ⚠ 只要算式里出现**别的东西**（令牌 <c>{{…}}</c>、<c>@(</c>、字母、比较运算符…）就**不求值**
/// —— 那是运行时语义，猜不得。与 <see cref="ValueTokenSubstitutor"/> 的
/// "代不掉就原样留着、由审计报出来"是同一条纪律。
/// </para>
///
/// <para>
/// 整数语义：`/` 与 `%` 按 C# 的整数运算（CP 的计算也是整数）；`10 / 4` ⇒ `2`。
/// 溢出、除零、多余字符一律**不求值**。
/// </para>
/// </summary>
public static class LiteralQueryEvaluator
{
    /// <summary>求值成功返回 <c>true</c> 并给出十进制结果；任何不确定的情况返回 <c>false</c>。</summary>
    public static bool TryEvaluate(string? expression, out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(expression)) return false;

        // ★ 先按"允许的字符集"卡一道：出现字母/花括号/引号等一律不求值。
        foreach (char c in expression)
        {
            if (!char.IsDigit(c) && c is not ('+' or '-' or '*' or '/' or '%' or '(' or ')' or ' ' or '\t' or '\r' or '\n'))
                return false;
        }

        long result;
        try
        {
            int pos = 0;
            result = ParseExpression(expression, ref pos);
            SkipSpace(expression, ref pos);
            if (pos != expression.Length) return false;    // 尾部多余字符 ⇒ 不认识，不代
        }
        catch (FormatException)
        {
            return false;
        }

        value = result.ToString();
        return true;
    }

    private static long ParseExpression(string s, ref int pos)
    {
        long left = ParseTerm(s, ref pos);
        while (true)
        {
            SkipSpace(s, ref pos);
            if (pos >= s.Length) return left;
            char op = s[pos];
            if (op != '+' && op != '-') return left;
            pos++;
            long right = ParseTerm(s, ref pos);
            left = op == '+' ? left + right : left - right;
        }
    }

    private static long ParseTerm(string s, ref int pos)
    {
        long left = ParseFactor(s, ref pos);
        while (true)
        {
            SkipSpace(s, ref pos);
            if (pos >= s.Length) return left;
            char op = s[pos];
            if (op != '*' && op != '/' && op != '%') return left;
            pos++;
            long right = ParseFactor(s, ref pos);
            left = op switch
            {
                '*' => left * right,
                '/' => right == 0 ? throw new FormatException() : left / right,
                _ => right == 0 ? throw new FormatException() : left % right,
            };
        }
    }

    private static long ParseFactor(string s, ref int pos)
    {
        SkipSpace(s, ref pos);
        if (pos < s.Length && s[pos] == '-')          // 一元负号（`-3` / `-(1 + 2)`）
        {
            pos++;
            return -ParseFactor(s, ref pos);
        }
        if (pos < s.Length && s[pos] == '(')
        {
            pos++;
            long inner = ParseExpression(s, ref pos);
            SkipSpace(s, ref pos);
            if (pos >= s.Length || s[pos] != ')') throw new FormatException();
            pos++;
            return inner;
        }

        int start = pos;
        while (pos < s.Length && char.IsDigit(s[pos])) pos++;
        if (pos == start) throw new FormatException();      // 空的/不是数字 ⇒ 不认识
        return long.Parse(s[start..pos]);
    }

    private static void SkipSpace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }
}
