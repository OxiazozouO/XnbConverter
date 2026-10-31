using System.Buffers.Binary;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// 只读 PNG **头里的宽高**（IHDR），不解码像素。
///
/// <para>
/// **为什么要有它**：<c>SpriteSheetValidator</c> 原先用 <c>Png.Read</c> 拿尺寸 ——
/// 那是把整张图解出来（zlib 解压 + 反滤波），只为读 8 个字节。实测每包要解几百张图，
/// 光这一项 0.4 秒上下，而那几道自检每包都要跑一遍。
/// </para>
///
/// <para>
/// PNG 的结构是死的：8 字节签名，紧跟 IHDR 块（4 字节长度 + 4 字节类型 + 4 字节宽 + 4 字节高），
/// 全大端。读 24 个字节就够 —— 宽度在偏移 16、高度在 20。
/// </para>
/// </summary>
public static class PngHeader
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// 读宽高。不是 PNG、文件读不出来、头不合规都返回 <c>false</c>（与 <c>Png.Read</c> 抛异常同义，
    /// 调用方照常跳过）。
    /// </summary>
    public static bool TryReadSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            Span<byte> head = stackalloc byte[24];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length) return false;

            if (!head[..8].SequenceEqual(Signature)) return false;
            if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
                return false;

            width = (int)BinaryPrimitives.ReadUInt32BigEndian(head[16..20]);
            height = (int)BinaryPrimitives.ReadUInt32BigEndian(head[20..24]);
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }
}
