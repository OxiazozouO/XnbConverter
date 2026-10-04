using System.Buffers.Binary;

namespace XnbConverter.Utilities;

/// <summary>
/// MonoGame 的 MGFX 效果容器（.mgfxo，本工具导出成 .cso）的**拆壳**工具。
///
/// 目的只有一个：把里面的**着色器源码**单独拿出来，变成一个任何编辑器/IDE 都能直接打开的文本文件，
/// 免得为了改一行 shader 去手改二进制。容器剩下的部分（头、常量缓冲、采样器、技术/通道表）原样保留，
/// 打包时再按新长度拼回去。
///
/// 格式（对照 MonoGame 的 <c>Effect.MGFXHeader</c> / <c>Shader</c> 读取器）：
/// <code>
/// [0:4]   签名 "MGFX"
/// [4]     版本（本工具见过的：8 = MG3.7 时代，9 = MG3.8.0，10 = MG3.8.1）
/// [5]     平台档
/// [6:10]  效果缓存键
/// ...     常量缓冲 / 着色器表
/// [p:p+4] uint32 着色器字节数
/// [s:s+L] 着色器本身（GL 平台是明文 GLSL，DX 平台是编译后的字节码）
/// ...     采样器、参数、技术、通道
/// </code>
///
/// 拆壳**不依赖**我对头部之后那段布局的完整理解：只在头部之后扫第一个「自洽的长度字段」，
/// 也就是 <c>s = p + 4</c> 且 <c>u32(p) == 区间长度</c> 且区间内容是像样的着色器。
/// 偏移会随清单一起存下来，所以拼回去与我的理解无关，只跟偏移有关。
///
/// 任何一步不符合预期就返回 false，调用方照旧输出原始 .cso —— 宁可这个功能不生效，
/// 也不能影响既有内容。
/// </summary>
public static class Mgfx
{
    /// <summary>能识别的版本区间。之外的一律不碰。</summary>
    private const int MinVersion = 8;

    private const int MaxVersion = 10;

    /// <summary>着色器起码这么大才可能是有意义的源码/字节码。</summary>
    private const int MinShaderLength = 16;

    /// <summary>
    /// 试着把 <paramref name="mgfx"/> 拆成「外壳」和「着色器源码」。
    /// </summary>
    /// <param name="mgfx">完整的 MGFX 字节。</param>
    /// <param name="shell">外壳：原始字节挖掉着色器区间之后的样子。</param>
    /// <param name="shaderOffset">着色器原本的起始偏移；拼回去时就插在这个位置。</param>
    /// <param name="shader">着色器字节（GL 平台下是可读文本）。</param>
    public static bool TrySplit(byte[] mgfx, out byte[] shell, out int shaderOffset, out byte[] shader)
    {
        shell = System.Array.Empty<byte>();
        shaderOffset = 0;
        shader = System.Array.Empty<byte>();

        if (mgfx.Length < 14
            || mgfx[0] != (byte)'M' || mgfx[1] != (byte)'G' || mgfx[2] != (byte)'F' || mgfx[3] != (byte)'X')
        {
            return false;
        }

        int version = mgfx[4];
        if (version < MinVersion || version > MaxVersion)
        {
            return false;
        }

        // 从头部之后找第一个自洽的长度字段：它后面紧跟的区间必须正好落在文件内，
        // 且内容得像个着色器（明文文本，或 DX 的编译字节码）。
        for (int p = 10; p + 4 + MinShaderLength <= mgfx.Length; p++)
        {
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(mgfx.AsSpan(p, 4));
            int s = p + 4;
            if (len < MinShaderLength || (long)s + len > mgfx.Length)
            {
                continue;
            }

            ReadOnlySpan<byte> body = mgfx.AsSpan(s, (int)len);
            if (!LooksLikeShader(body, out bool isText))
            {
                continue;
            }

            // 目前只导出文本型着色器（GL 平台）。DX 平台是字节码，导成文本没有意义，
            // 留着原样打包反而更安全。
            if (!isText)
            {
                return false;
            }

            shaderOffset = s;
            shader = body.ToArray();

            shell = new byte[mgfx.Length - (int)len];
            mgfx.AsSpan(0, s).CopyTo(shell);
            mgfx.AsSpan(s + (int)len).CopyTo(shell.AsSpan(s));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从外壳里扫出可读的标识符（技术名、通道名、参数名…）。
    /// 采样器的 ps_/vs_ 前缀名是 MojoShader 生成的寄存器名，不是原作者命名，过滤掉。
    /// </summary>
    public static List<string> ReadableNames(byte[] shell)
    {
        List<string> names = new();
        // 从头部之后开始扫：签名 "MGFX" 本身可打印，别当成技术名收进来
        int i = shell.Length > 10 ? 10 : shell.Length;
        while (i < shell.Length)
        {
            if (shell[i] < 32 || shell[i] > 126)
            {
                i++;
                continue;
            }

            int start = i;
            while (i < shell.Length && shell[i] >= 32 && shell[i] <= 126)
            {
                i++;
            }

            string text = System.Text.Encoding.ASCII.GetString(shell, start, i - start);
            if (text.Length >= 3 && !text.StartsWith("ps_", StringComparison.Ordinal)
                && !text.StartsWith("vs_", StringComparison.Ordinal))
            {
                names.Add(text);
            }
        }

        return names;
    }

    /// <summary>
    /// 把外壳和（可能被编辑过的）着色器拼回完整的 MGFX。
    /// 着色器长度字段就在 <paramref name="shaderOffset"/> 前面 4 字节，按新长度重写。
    /// </summary>
    public static byte[] Join(byte[] shell, int shaderOffset, ReadOnlySpan<byte> shader)
    {
        if (shaderOffset < 4 || shaderOffset > shell.Length)
        {
            throw new ArgumentException("着色器偏移超出外壳范围", nameof(shaderOffset));
        }

        byte[] result = new byte[shell.Length + shader.Length];
        shell.AsSpan(0, shaderOffset).CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(shaderOffset - 4), (uint)shader.Length);
        shader.CopyTo(result.AsSpan(shaderOffset));
        shell.AsSpan(shaderOffset).CopyTo(result.AsSpan(shaderOffset + shader.Length));
        return result;
    }

    /// <summary>
    /// 判断这段字节像不像着色器：符合其一即可。
    /// 文本：无控制字符（除制表/换行/回车）、含换行、几乎全是可打印字符 —— GLSL/HLSL 源码就长这样。
    /// 字节码：DX 的 "DXBC"、或 MonoGame 旧版 GL 平台用的 "MGXU"。
    /// </summary>
    private static bool LooksLikeShader(ReadOnlySpan<byte> body, out bool isText)
    {
        isText = false;
        if (body.StartsWith("DXBC"u8) || body.StartsWith("MGXU"u8))
        {
            return true;
        }

        int printable = 0;
        bool hasNewline = false;
        foreach (byte c in body)
        {
            if (c == (byte)'\n')
            {
                hasNewline = true;
                printable++;
            }
            else if (c == (byte)'\r' || c == (byte)'\t' || (c >= 32 && c != 127))
            {
                printable++;
            }
            else
            {
                // 控制字符：不是源码
                return false;
            }
        }

        // 允许极少量异常字节（比如注释里的非 ASCII），但必须绝大部分是可打印的
        if (!hasNewline || printable < body.Length * 99 / 100)
        {
            return false;
        }

        isText = true;
        return true;
    }
}
