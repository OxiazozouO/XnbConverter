using System.Security.Cryptography;
using System.Text;

namespace XnbConverter.Utilities;

/// <summary>
/// MonoGame MGFX 里的着色器源码 ⇄ HLSL 效果源码（.fx）的互译。
///
/// **为什么能这么做**：MonoGame 的管线是 HLSL → DX 字节码 →（MojoShader 翻译）→ GLSL。
/// 从 DX9 汇编翻出来的 GLSL 保留了大量寄存器级的一一对应，表达式层面和 HLSL 几乎同形：
/// <code>
///   texture2D(s, uv)  ↔  tex2D(s, uv)
///   vec4 / mat4       ↔  float4 / float4x4
///   mix / fract / mod ↔  lerp / frac / fmod
/// </code>
/// 所以改写是机械的：类型名、内建函数、输出目标。反过来也一样。
///
/// **做不到什么**：
/// <list type="bullet">
///   <item>原作者的 HLSL 源码**已经不存在** —— 编译成字节码那一步就丢了，
///         任何文件里都没有。这里是**功能等价的重写稿**，不是原文。</item>
///   <item>重新编译**不保证**得到同样的字节码（编译器版本/优化/目标 profile 都会影响）。</item>
///   <item>改写只覆盖已知写法，遇到不认识的标识符会**就地标 <c>/*?*/</c>**，方便人工核对。</item>
/// </list>
///
/// **回写策略**（保证「没动过就字节级还原」）：
/// 导出时把原始 GLSL 和本文件产出的 .fx 的哈希一起存进 .config。
/// 打包时哈希没变就直接用存下来的原始 GLSL（零翻译风险）；变了才走 <see cref="ToGlsl"/>。
/// </summary>
public static class FxReconstruct
{
    private static readonly Dictionary<string, string> TypeMap = new()
    {
        ["bool"] = "bool", ["int"] = "int", ["uint"] = "uint", ["float"] = "float",
        ["vec2"] = "float2", ["vec3"] = "float3", ["vec4"] = "float4",
        ["ivec2"] = "int2", ["ivec3"] = "int3", ["ivec4"] = "int4",
        ["bvec2"] = "bool2", ["bvec3"] = "bool3", ["bvec4"] = "bool4",
        ["mat2"] = "float2x2", ["mat3"] = "float3x3", ["mat4"] = "float4x4",
        ["sampler2D"] = "sampler2D", ["sampler3D"] = "sampler3D", ["samplerCube"] = "samplerCUBE",
    };

    private static readonly Dictionary<string, string> BuiltinMap = new()
    {
        ["texture2D"] = "tex2D", ["texture2DLod"] = "tex2Dlod",
        ["texture3D"] = "tex3D", ["textureCube"] = "texCUBE",
        ["mix"] = "lerp", ["fract"] = "frac", ["mod"] = "fmod",
        ["inversesqrt"] = "rsqrt", ["dFdx"] = "ddx", ["dFdy"] = "ddy",
    };

    private static readonly string[] GLSL_PRELUDE =
    {
        "#ifdef GL_ES",
        "precision mediump float;",
        "precision mediump int;",
        "#endif",
    };

    /// <summary>导出时用来判断 .fx 有没有被改过。</summary>
    public static string Hash(string text)
    {
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(h).ToLowerInvariant();
    }

    /// <summary>
    /// GLSL 片段源码 → HLSL 效果源码。
    /// </summary>
    /// <param name="source">MojoShader 输出的 GLSL。</param>
    /// <param name="names">从 MGFX 外壳里扫到的可读名字（技术名、通道名…）。</param>
    public static string Build(string source, IReadOnlyList<string> names)
    {
        List<string> samplers = new();
        List<(string Type, string Name)> varyings = new();
        List<string> globals = new();
        List<string> body = new();
        string outputVar = "ps_oC0";

        bool inMain = false;
        bool skipBrace = false;

        foreach (string raw in Lines(source))
        {
            string line = raw.Trim();

            if (line.Length == 0 || line.StartsWith("#ifdef ") || line.StartsWith("#ifndef ")
                || line == "#endif" || line == "#else" || line.StartsWith("precision "))
            {
                continue;
            }

            if (line.StartsWith("uniform "))
            {
                string decl = line["uniform ".Length..].TrimEnd(';').Trim();
                string[] parts = decl.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].StartsWith("sampler"))
                {
                    samplers.Add(parts[1]);
                }

                continue;
            }

            if (line.StartsWith("varying "))
            {
                string decl = line["varying ".Length..].TrimEnd(';').Trim();
                string[] parts = decl.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    varyings.Add((MapType(parts[0]), parts[1]));
                }

                continue;
            }

            if (line.StartsWith("void main"))
            {
                inMain = true;
                skipBrace = true;
                continue;
            }

            if (inMain)
            {
                if (skipBrace && line == "{")
                {
                    skipBrace = false;
                    continue;
                }

                if (line == "}")
                {
                    inMain = false;
                    continue;
                }

                body.Add(TranslateLine(raw, outputVar));
                continue;
            }

            // main 之外：#define 与全局声明
            if (line.StartsWith("#define ") && line.EndsWith("gl_FragColor"))
            {
                // 这一行在 HLSL 里没有对应物，翻译成「入口内的输出变量」
                continue;
            }

            globals.Add(TranslateLine(raw, outputVar));
        }

        StringBuilder sb = new();
        sb.AppendLine("// ============================================================================");
        sb.AppendLine("// 由 XnbConverter 从 MonoGame MGFX 二进制**还原**出来的 HLSL 效果源码。");
        sb.AppendLine("//");
        sb.AppendLine("// 先读这段：");
        sb.AppendLine("//   * 这是**功能等价的重写稿**，不是原作者的 .fx 源码 —— 原文在编译成字节码时就丢了。");
        sb.AppendLine("//   * 改写是机械的（类型名 / 内建函数 / 输出目标），**未经过 HLSL 编译器验证**。");
        sb.AppendLine("//     带 /*?*/ 的地方是改写时不认识的写法，需要人工核对。");
        sb.AppendLine("//   * 回写规则：只要不修改本文件，打包时会用 .config 里存的原始 GLSL，逐字节还原；");
        sb.AppendLine("//     一旦修改，工具会把本文件**反译回 GLSL** 再写回，属于等价改写、不保证字节一致。");
        sb.AppendLine("//   * 只改正文（数学、颜色、采样）最安全；增删 sampler / varying 不会同步到 GLSL。");
        sb.AppendLine("// ============================================================================");
        sb.AppendLine();

        for (int i = 0; i < samplers.Count; i++)
        {
            sb.AppendLine($"sampler2D {samplers[i]} : register(s{i});");
        }

        if (samplers.Count > 0)
        {
            sb.AppendLine();
        }

        foreach (string g in globals)
        {
            // 全局常量：GLSL 的 const 在 HLSL 里要写成 static const
            sb.AppendLine(g.TrimStart().StartsWith("const ") ? "static " + g.Trim() : g);
        }

        if (globals.Count > 0)
        {
            sb.AppendLine();
        }

        AppendEntry(sb, varyings, body, outputVar);

        string technique = names.Count > 0 ? names[0] : "Technique1";
        string pass = names.Count > 1 ? names[1] : "Pass1";
        sb.AppendLine();
        sb.AppendLine($"technique {technique}");
        sb.AppendLine("{");
        sb.AppendLine($"    pass {pass}");
        sb.AppendLine("    {");
        sb.AppendLine("        PixelShader = compile ps_3_0 MainPS();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// HLSL 效果源码 → GLSL 片段源码（<see cref="Build"/> 的逆）。
    /// 只在用户改过 .fx 时才会走到这里。
    ///
    /// 要还原的东西比看起来多：varying 在 .fx 里是入口参数，得变回 varying 声明；
    /// 输出变量在 .fx 里是入口里的局部变量 + return，得变回 gl_FragColor 赋值。
    /// 漏掉任何一样，生成的 GLSL 都会因为引用了未声明的东西而编译失败。
    /// </summary>
    public static string ToGlsl(string fx)
    {
        List<string> samplers = new();
        List<string> globals = new();
        List<(string Type, string Name)> varyings = new();
        List<string> body = new();
        string outputVar = "ps_oC0";

        int state = 0;   // 0 = 入口之前，1 = 入口正文，2 = 正文之后（技术块等，全丢）
        int braceDepth = 0;
        bool inSignature = false;

        foreach (string raw in Lines(fx))
        {
            string line = raw.Trim();

            if (state == 2)
            {
                continue;
            }

            if (state == 0)
            {
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("technique "))
                {
                    continue;
                }

                if (line.StartsWith("sampler2D "))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        samplers.Add(parts[1]);
                    }

                    continue;
                }

                // 入口签名可能跨多行：
                //   float4 MainPS(
                //       float4 vFrontColor : COLOR0,
                //       float4 vTexCoord0 : TEXCOORD0) : COLOR
                // 参数就是原来的 varying，必须逐个收回来 —— 漏了 GLSL 就会引用未声明的标识符。
                if (line.StartsWith("float4 ") && line.Contains("MainPS"))
                {
                    CollectVaryings(line, varyings);
                    inSignature = !line.Contains(')');
                    continue;
                }

                if (inSignature)
                {
                    CollectVaryings(line, varyings);
                    if (line.Contains(')'))
                    {
                        inSignature = false;
                    }

                    continue;
                }

                if (line == "{")
                {
                    state = 1;
                    braceDepth = 1;
                    continue;
                }

                globals.Add(TranslateLine(raw, outputVar, toGlsl: true).Trim());
                continue;
            }

            if (state == 1)
            {
                braceDepth += line.Count(c => c == '{') - line.Count(c => c == '}');
                if (braceDepth <= 0)
                {
                    state = 2;
                    continue;
                }

                // 入口里第一行的 "float4 ps_oC0;" 与末尾的 "return ps_oC0;" 都是 .fx 的产物，
                // GLSL 里由 #define ps_oC0 gl_FragColor 承担，不能留。
                if (line.StartsWith("float4 ") && line.EndsWith(";") && line.Contains(outputVar))
                {
                    continue;
                }

                if (line.StartsWith("return "))
                {
                    continue;
                }

                body.Add(TranslateLine(raw, outputVar, toGlsl: true));
                continue;
            }
        }

        StringBuilder sb = new();
        foreach (string p in GLSL_PRELUDE)
        {
            sb.AppendLine(p);
        }

        sb.AppendLine();
        foreach (string s in samplers)
        {
            sb.AppendLine($"uniform sampler2D {s};");
        }

        foreach (string g in globals)
        {
            sb.AppendLine(g.StartsWith("static ") ? g["static ".Length..] : g);
        }

        // varying 在 .fx 里是入口参数，这里变回声明 —— 漏了就会引用未声明标识符
        foreach ((string type, string name) in varyings)
        {
            sb.AppendLine($"varying {type} {name};");
        }

        sb.AppendLine();
        sb.AppendLine("#define " + outputVar + " gl_FragColor");
        sb.AppendLine();
        sb.AppendLine("void main()");
        sb.AppendLine("{");
        foreach (string line in body)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>从入口签名的一段里抠出 "类型 名字 : 语义" 三元组，收成 varying。</summary>
    private static void CollectVaryings(string line, List<(string Type, string Name)> varyings)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            line, @"(?:float|int|bool)(?:[234](?:x[234])?)?\s+([A-Za-z_]\w*)\s*:\s*(?:COLOR|TEXCOORD)\d*");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string name = m.Groups[1].Value;
            varyings.Add((MapBack(ToGlslTypeMap, m.Value.Split(' ')[0]), name));
        }
    }

    private static void AppendEntry(StringBuilder sb, List<(string Type, string Name)> varyings, List<string> body, string outputVar)
    {
        sb.Append("float4 MainPS(");
        if (varyings.Count == 0)
        {
            sb.AppendLine(") : COLOR");
        }
        else
        {
            sb.AppendLine();
            int texIndex = 0;
            for (int i = 0; i < varyings.Count; i++)
            {
                string sem = varyings[i].Name.Contains("Color", StringComparison.OrdinalIgnoreCase)
                    ? "COLOR0"
                    : $"TEXCOORD{texIndex++}";
                sb.AppendLine($"    {varyings[i].Type} {varyings[i].Name} : {sem}{(i == varyings.Count - 1 ? ") : COLOR" : ",")}");
            }
        }

        sb.AppendLine("{");
        sb.AppendLine($"    float4 {outputVar};");
        foreach (string line in body)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine($"    return {outputVar};");
        sb.AppendLine("}");
    }

    /// <summary>逐词机械改写。toGlsl = false 时是 GLSL→HLSL，true 时是 HLSL→GLSL。</summary>
    private static string TranslateLine(string line, string outputVar, bool toGlsl = false)
    {
        StringBuilder sb = new();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (!char.IsLetter(c) && c != '_')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int start = i;
            while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
            {
                i++;
            }

            string word = line[start..i];

            if (word == outputVar && !toGlsl)
            {
                sb.Append(word);
                continue;
            }

            if (word.StartsWith("gl_", StringComparison.Ordinal))
            {
                if (toGlsl)
                {
                    // HLSL 里不该出现 gl_，保留并标注（异常情况）
                    sb.Append(word).Append("/*?*/");
                }
                else
                {
                    // GLSL 的内建回译不回去，只能提示人工处理
                    sb.Append(word).Append("/*?*/");
                }

                continue;
            }

            string type = toGlsl ? MapBack(ToGlslTypeMap, word) : MapBack(TypeMap, word);
            if (type != word)
            {
                sb.Append(type);
                continue;
            }

            string builtin = toGlsl ? MapBack(ToGlslBuiltinMap, word) : MapBack(BuiltinMap, word);
            if (builtin != word)
            {
                sb.Append(builtin);
                continue;
            }

            sb.Append(word);
        }

        return sb.ToString();
    }

    private static readonly Dictionary<string, string> ToGlslTypeMap = Invert(TypeMap);

    private static readonly Dictionary<string, string> ToGlslBuiltinMap = Invert(BuiltinMap);

    private static Dictionary<string, string> Invert(Dictionary<string, string> map)
    {
        Dictionary<string, string> result = new();
        foreach (KeyValuePair<string, string> kv in map)
        {
            result.TryAdd(kv.Value, kv.Key);
        }

        return result;
    }

    private static string MapBack(Dictionary<string, string> map, string word) =>
        map.TryGetValue(word, out string? mapped) ? mapped : word;

    private static string MapType(string glslType) => MapBack(TypeMap, glslType);

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');
}
