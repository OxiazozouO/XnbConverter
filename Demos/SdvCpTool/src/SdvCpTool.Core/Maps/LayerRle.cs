using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 从 <see cref="Layer.Tiles"/> 重建 tbin 的游程编码。
/// <para>
/// <b>为什么必须重建</b>：<c>LayerReader.Write</c>（XnbConverter）<b>回放</b>存好的
/// <c>Index</c>（每格一个操作码：<c>N</c>+计数 / <c>S</c> / <c>A</c> / <c>T</c> 切换图集）
/// 与 <c>_sizeArr</c>，<b>不</b>从 <c>Tiles</c> 推导。所以改完 <c>Tiles</c> 而不重建，
/// 写出来的二进制与内存里的模型对不上 —— 轻则图块错位，重则写出抛错。
/// </para>
/// <para>
/// 操作码与 <c>LayerReader.Read</c> 严格镜像：<c>'N'</c> 后跟 int32 计数（一段空格），
/// <c>'S'</c>/<c>'A'</c> 各消费一个 <c>Tiles</c> 项，<c>'T'</c> 只切换当前图集、不消费格子。
/// </para>
/// </summary>
public static class LayerRle
{
    /// <summary>按 <paramref name="layer"/> 的 <c>Tiles</c> 重算 <c>Index</c> / <c>_sizeArr</c> / <c>_currTileSheet</c>。</summary>
    public static void Rebuild(Layer layer)
    {
        int width = layer.LayerSize.X;
        int height = layer.LayerSize.Y;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"图层 `{layer.Id}` 的尺寸非法：{width}x{height}");

        int expected = width * height;
        if (layer.Tiles.Count != expected)
            throw new InvalidOperationException(
                $"图层 `{layer.Id}` 的 Tiles 有 {layer.Tiles.Count} 项，与尺寸 {width}x{height} 不符");

        var index = new List<char>();
        var sizes = new List<int>();
        var sheets = new List<string> { "" };   // 镜像 Read：先塞空串，之后每个 'T' 追加一个
        string currentSheet = "";

        // ⚠ **逐行**生成，空段绝不跨行。
        // `LayerReader.Write` 对跨行的 N 段直接抛错（`num5 + num6 > x` → "Max value of i is ..."），
        // 而 `Read` 是允许跨行的 —— 读写不对称。按行切才两边都成立（原版数据也是每行一段）。
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                if (layer.Tiles[y * width + x] is not BaseTile tile)
                {
                    int run = 0;
                    while (x < width && layer.Tiles[y * width + x] is not BaseTile) { run++; x++; }
                    index.Add('N');
                    sizes.Add(run);
                    continue;
                }

                switch (tile)
                {
                    case StaticTile st:
                        WriteSheetSwitch(st.TileSheet, ref currentSheet, index, sheets);
                        index.Add('S');
                        break;

                    case AnimatedTile at:
                        // 动画块的图集在它自己的 Frames 里，'T' 用第一帧的。
                        WriteSheetSwitch(at.Frames.Count > 0 ? at.Frames[0].TileSheet : "", ref currentSheet, index, sheets);
                        index.Add('A');
                        break;

                    default:
                        throw new InvalidOperationException($"图层 `{layer.Id}` 里有未知的图块类型 {tile.GetType().Name}");
                }
                x++;
            }
        }

        layer.Index = index;
        layer._sizeArr = sizes;
        layer._currTileSheet = sheets;
    }

    /// <summary>
    /// 造一个<b>自带 RLE 段</b>的 <c>AnimatedTile</c>。
    /// <para>
    /// ⚠ <b>动画块有自己的 <c>Index</c>/<c>_currTileSheet</c></b>（<c>AnimatedTilerReader/Writer</c>
    /// 按它回放），与图层那一份是两回事。忘了建就会写出一个**帧数为 0** 的动画块，
    /// 回读时越界 —— 实测代价：SVE/RSV 各多出 4/12 条「写回地图 tbin 失败：Index was out of range」。
    /// </para>
    /// </summary>
    public static AnimatedTile BuildAnimatedTile(int frameInterval, List<StaticTile> frames)
    {
        var index = new List<char>();
        var sheets = new List<string> { "" };   // 镜像 Read：先塞空串，之后每个 'T' 追加一个
        string current = "";

        foreach (StaticTile frame in frames)
        {
            if (!string.Equals(current, frame.TileSheet, StringComparison.Ordinal))
            {
                index.Add('T');
                sheets.Add(frame.TileSheet);
                current = frame.TileSheet;
            }
            index.Add('S');
        }

        return new AnimatedTile
        {
            FrameInterval = frameInterval,
            _frameCount = frames.Count,
            Frames = frames,
            Index = index,
            _currTileSheet = sheets,
            Properties = new List<Propertie>(),
        };
    }

    private static void WriteSheetSwitch(string sheet, ref string current, List<char> index, List<string> sheets)
    {
        string want = sheet ?? "";
        if (string.Equals(current, want, StringComparison.Ordinal)) return;

        // 图集名缺失时无法生成 'T' 段 —— 那是模型没建好（TmxToTbin 会给每个 StaticTile 填上）。
        if (want.Length == 0)
            throw new InvalidOperationException("图块没有 TileSheet，无法生成 tbin 的图集切换段");

        index.Add('T');
        sheets.Add(want);
        current = want;
    }
}
