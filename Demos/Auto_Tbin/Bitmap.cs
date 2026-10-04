using XnbConverter.Utilities.Png;

namespace Auto_Tbin;

/// <summary>
/// 简易 RGBA8 位图，替代原先的 SixLabors.ImageSharp <c>Image&lt;Rgba32&gt;</c>。
///
/// Auto_Tbin 只需要「加载 PNG / 新建空图 / 复制 / 把源图的 16x16 格子按 alpha 混合贴到目标图 /
/// 查某格是否全不透明 / 存成 PNG」，这些用裸数组就能做，
/// 不必为此拖进一个非 OSI 开源、且带已知 CVE 的图像库。
///
/// 像素排布与 PNG 一致：逐行 R,G,B,A，直通 alpha（非预乘）。
/// </summary>
public sealed class Bitmap
{
    /// <summary>tbin 图块集固定按 16x16 分格。</summary>
    public const int TileSize = 16;

    public readonly byte[] Data;

    public readonly int Width;

    public readonly int Height;

    public Bitmap(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new byte[width * height * 4];
    }

    private Bitmap(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }

    /// <summary>读一张 PNG（走本项目自研的编解码，不依赖 ImageSharp）。</summary>
    public static Bitmap Load(string path)
    {
        byte[] data = Png.Read(path, out int width, out int height);
        return new Bitmap(width, height, data);
    }

    public Bitmap Clone() => new(Width, Height, (byte[])Data.Clone());

    public void Save(string path) => Png.Write(path, Data, Width, Height);

    /// <summary>图块序号 → 它在本图里的像素起点（每行 16 格）。</summary>
    private static (int X, int Y) TileOrigin(int width, int id)
    {
        int perRow = width / TileSize;
        return (id % perRow * TileSize, id / perRow * TileSize);
    }

    /// <summary>
    /// 把 <paramref name="source"/> 里第 <paramref name="sourceId"/> 格（16x16）
    /// 以 source-over 方式混合到本图第 <paramref name="id"/> 格。
    /// </summary>
    public void DrawTile(int id, Bitmap source, int sourceId)
    {
        (int dx, int dy) = TileOrigin(Width, id);
        (int sx, int sy) = TileOrigin(source.Width, sourceId);

        for (int y = 0; y < TileSize; y++)
        {
            int dstRow = ((dy + y) * Width + dx) * 4;
            int srcRow = ((sy + y) * source.Width + sx) * 4;
            for (int x = 0; x < TileSize; x++)
            {
                Blend(source.Data, srcRow + x * 4, Data, dstRow + x * 4);
            }
        }
    }

    /// <summary>该格 16x16 是否完全不透明。</summary>
    public bool IsRegionOpaque(int id)
    {
        (int x0, int y0) = TileOrigin(Width, id);
        for (int y = y0; y < y0 + TileSize; y++)
        {
            int row = y * Width * 4;
            for (int x = 0; x < TileSize; x++)
            {
                if (Data[row + (x0 + x) * 4 + 3] != 255)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>source-over：把 src 像素盖到 dst 像素上，结果写回 dst。</summary>
    private static void Blend(byte[] src, int s, byte[] dst, int d)
    {
        int sa = src[s + 3];
        if (sa == 255)
        {
            // 图块以不透明居多，这里直接覆盖，省掉整条浮点路径
            dst[d] = src[s];
            dst[d + 1] = src[s + 1];
            dst[d + 2] = src[s + 2];
            dst[d + 3] = 255;
            return;
        }

        if (sa == 0)
        {
            return;
        }

        int da = dst[d + 3];
        float a = sa / 255f;
        float inv = 1f - a;
        float outA = a + da / 255f * inv;
        if (outA <= 0f)
        {
            dst[d] = dst[d + 1] = dst[d + 2] = dst[d + 3] = 0;
            return;
        }

        float back = da / 255f * inv / outA;
        float front = a / outA;
        for (int c = 0; c < 3; c++)
        {
            float v = src[s + c] * front + dst[d + c] * back;
            dst[d + c] = (byte)Math.Clamp(MathF.Round(v, MidpointRounding.AwayFromZero), 0f, 255f);
        }

        dst[d + 3] = (byte)Math.Clamp(MathF.Round(outA * 255f, MidpointRounding.AwayFromZero), 0f, 255f);
    }
}
