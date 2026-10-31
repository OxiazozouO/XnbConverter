using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 把地图里**画死**的树/灌木/石头/矿/杂草识别成 <c>Paths</c> 指令。
/// <para>
/// 识别单位是**实例**而不是单张图集：先把所有非地面层（Buildings/Front/AlwaysFront…）
/// 合成一张对象图，再用游戏自己的树木/灌木精灵做 alpha 形状匹配；命中后按完整 footprint
/// 删除所有非 <c>Back</c> 层，在底部中心补一个 Paths 标记。
/// 这样多层叠出来的树不会被拆成残片。
/// </para>
/// </summary>
public static class HardDrawnResourceRepair
{
    private const int S = 16;

    private sealed record Block(int W, int H, byte[] Px, string Kind, int Marker)
    {
        /// <summary>不透明像素的位图（每行一个 <see cref="ulong"/> 序列），用于整块做 AND+popcount。</summary>
        public ulong[] Mask { get; } = BuildMask(W, H, Px);
        public int Opaque { get; } = CountOpaque(W, H, Px);
        public int Words => (W + 63) / 64;

        private static ulong[] BuildMask(int w, int h, byte[] px)
        {
            int words = (w + 63) / 64;
            var mask = new ulong[words * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] != 0)
                    mask[y * words + (x >> 6)] |= 1UL << (x & 63);
            return mask;
        }

        private static int CountOpaque(int w, int h, byte[] px)
        {
            int n = 0;
            for (int i = 3; i < w * h * 4; i += 4)
                if (px[i] != 0) n++;
            return n;
        }
    }

    public static (int Maps, int Instances) Run(string root, IEnumerable<string>? onlyMaps = null,
        bool apply = true, Action<string>? log = null)
    {
        string full = Path.GetFullPath(root);
        string mapsDir = Path.Combine(full, "Maps");
        var refs = LoadReferences(full);
        var objHashes = LoadObjectHashes(full);
        int mapsChanged = 0, instances = 0;
        bool profile = Environment.GetEnvironmentVariable("SDVCP_HARD_PROFILE") == "1";
        foreach (string file in Directory.EnumerateFiles(mapsDir, "*.tbin", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(full, file).Replace('\\', '/');
            if (onlyMaps != null && !onlyMaps.Any(m => rel.EndsWith(m, StringComparison.OrdinalIgnoreCase)))
                continue;
            long t = profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            bool changedMap = RepairMap(full, file, refs, objHashes, apply, out int changed);
            if (profile)
                Console.WriteLine($"[hard-prof] {rel} {(System.Diagnostics.Stopwatch.GetTimestamp() - t)
                    * 1000 / System.Diagnostics.Stopwatch.Frequency}ms {changed}");
            if (changedMap)
            {
                mapsChanged++;
                instances += changed;
                log?.Invoke($"{rel}  {changed}");
            }
        }
        return (mapsChanged, instances);
    }

    private static List<Block> LoadReferences(string root)
    {
        var refs = new List<Block>();
        void Add(byte[] src, int sw, int x, int y, int w, int h, string kind, int marker)
        {
            if (x < 0 || y < 0 || x + w > sw || (y + h) * sw > src.Length / 4) return;
            var b = new byte[w * h * 4];
            bool any = false;
            for (int j = 0; j < h; j++)
            {
                Array.Copy(src, ((y + j) * sw + x) * 4, b, j * w * 4, w * 4);
                for (int i = 3; i < w * 4; i += 4) if (b[j * w * 4 + i] != 0) any = true;
            }
            if (any) refs.Add(new Block(w, h, b, kind, marker));
        }

        string tf = Path.Combine(root, "TerrainFeatures");
        if (Directory.Exists(tf))
            foreach (string f in Directory.EnumerateFiles(tf, "tree*.png"))
            {
                byte[] px = Png.Read(f, out int w, out _);
                for (int x = 0; x + 48 <= w; x += 48) Add(px, w, x, 0, 48, 96, "tree", 23);
            }

        string bushes = Path.Combine(root, "TileSheets", "bushes.png");
        if (File.Exists(bushes))
        {
            byte[] px = Png.Read(bushes, out int w, out _);
            for (int season = 0; season < 4; season++)
            for (int off = 0; off < 2; off++)
            {
                Add(px, w, season * 32 + off * 16, 224, 16, 32, "bush", 26);
                Add(px, w, season * 32, 96, 32, 32, "bush", 25);
                int n2 = season * 64 + off * 32;
                Add(px, w, n2 % w, (n2 / w) * 48, 32, 48, "bush", 25);
                Add(px, w, off * 32, 320, 32, 32, "bush", 33);
            }
            Add(px, w, 0, 128, 48, 48, "bush", 24);
            Add(px, w, 48, 128, 48, 48, "bush", 24);
            Add(px, w, 0, 176, 48, 48, "bush", 24);
            Add(px, w, 48, 176, 48, 48, "bush", 24);
            for (int season = 0; season < 4; season++)
            for (int off = 0; off < 2; off++)
            {
                int yy = season < 2 ? 256 : 288;
                int xx = (season % 2 == 0 ? 0 : 64) + off * 16;
                Add(px, w, xx, yy, 16, 32, "bush", 26);
            }
        }
        return refs;
    }

    private static Dictionary<ulong, int> LoadObjectHashes(string root)
    {
        var map = new Dictionary<ulong, int>();
        string p = Path.Combine(root, "Maps", "springobjects.png");
        if (!File.Exists(p)) return map;
        byte[] px = Png.Read(p, out int w, out _);
        foreach ((int id, int marker) in new[]
                 {
                     (343, 16), (450, 16), (294, 18), (295, 18),
                     (784, 13), (674, 13), (675, 13), (785, 13), (676, 13), (677, 13),
                     (786, 13), (678, 13), (679, 13),
                 })
        {
            int col = id % 24, row = id / 24;
            if ((col + 1) * S > w) continue;
            ulong h = HashCell(px, w, col * S, row * S);
            map.TryAdd(h, marker);
        }
        return map;
    }

    private static ulong HashCell(byte[] px, int w, int x, int y)
    {
        ulong h = 1469598103934665603UL;
        for (int j = 0; j < S; j++)
        for (int i = 0; i < S; i++)
        {
            int p = ((y + j) * w + x + i) * 4;
            for (int c = 0; c < 4; c++)
            {
                h ^= px[p + c];
                h *= 1099511628211UL;
            }
        }
        return h;
    }

    private static bool RepairMap(string root, string file, List<Block> refs,
        Dictionary<ulong, int> objHashes, bool apply, out int converted)
    {
        converted = 0;
        TBin10 map;
        try { map = (TBin10)TBin10Reader.Create(File.ReadAllBytes(file)).Read(); }
        catch { return false; }
        Layer? paths = map.Layers?.FirstOrDefault(l => l.Id == "Paths");
        Layer? first = map.Layers?.FirstOrDefault();
        if (paths?.Tiles == null || first == null) return false;

        // Paths 层的图集 Id 不一定是 "Paths"（很多 mod 图是 "paths"）。
        var mapSheetIds = new HashSet<string>(
            (map.TileSheets ?? new List<TileSheet>()).Select(s => s.Id), StringComparer.Ordinal);
        string pathsSheetId = mapSheetIds.FirstOrDefault(id =>
                                  id.Equals("paths", StringComparison.OrdinalIgnoreCase))
                              ?? paths.Tiles.OfType<StaticTile>()
                                  .Select(t => t.TileSheet)
                                  .FirstOrDefault(id => mapSheetIds.Contains(id))
                              ?? mapSheetIds.FirstOrDefault()
                              ?? "Paths";

        string mapDir = Path.GetDirectoryName(file)!;
        var sheets = new Dictionary<string, TileSheet>(StringComparer.Ordinal);
        foreach (TileSheet s in map.TileSheets ?? new List<TileSheet>()) sheets.TryAdd(s.Id, s);
        var pixels = new Dictionary<string, (byte[] Px, int W, int H)>(StringComparer.OrdinalIgnoreCase);
        (byte[] Px, int W, int H) SheetPixels(TileSheet sh)
        {
            string img = (sh.Image ?? "").Replace('\\', '/');
            if (img.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) img = img[..^4];
            if (pixels.TryGetValue(img, out var hit)) return hit;
            string? p = new[]
            {
                Path.Combine(mapDir, img + ".png"),
                Path.Combine(root, img + ".png"),
                Path.Combine(root, "Maps", img + ".png"),
            }.FirstOrDefault(File.Exists);
            if (p == null) { pixels[img] = (Array.Empty<byte>(), 0, 0); return pixels[img]; }
            byte[] px = Png.Read(p, out int w, out int h);
            pixels[img] = (px, w, h);
            return pixels[img];
        }

        int mw = first.LayerSize.X, mh = first.LayerSize.Y;
        int W = mw * S, H = mh * S;
        var comp = new byte[W * H * 4];
        bool hasComp = false;
        foreach (Layer layer in map.Layers ?? new List<Layer>())
        {
            if (layer.Tiles == null || layer.Id == "Back" || layer.Id == "Paths") continue;
            if (!(layer.Id.StartsWith("Buildings", StringComparison.OrdinalIgnoreCase)
                  || layer.Id.StartsWith("Front", StringComparison.OrdinalIgnoreCase)
                  || layer.Id.StartsWith("AlwaysFront", StringComparison.OrdinalIgnoreCase))) continue;
            for (int i = 0; i < layer.Tiles.Count; i++)
            {
                if (layer.Tiles[i] is not StaticTile st) continue;
                if (!sheets.TryGetValue(st.TileSheet ?? "", out TileSheet? sh)) continue;
                var sp = SheetPixels(sh);
                if (sp.W == 0) continue;
                int cols = sp.W / S;
                int sx = st.TileIndex % cols * S, sy = st.TileIndex / cols * S;
                if (sx < 0 || sy < 0 || sx + S > sp.W || sy + S > sp.H) continue;
                int dx = i % mw * S, dy = i / mw * S;
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int si = ((sy + y) * sp.W + sx + x) * 4;
                    int di = ((dy + y) * W + dx + x) * 4;
                    if (sp.Px[si + 3] == 0) continue;
                    comp[di] = sp.Px[si]; comp[di + 1] = sp.Px[si + 1];
                    comp[di + 2] = sp.Px[si + 2]; comp[di + 3] = 255;
                    hasComp = true;
                }
            }
        }

        var picks = new List<(int X, int Y, int W, int H, int Marker, double Score)>();
        if (hasComp)
        {
            // 形状匹配的两层加速，都不改变像素级判定：
            //   1) 合成图的不透明像素做成位图；候选处与参考块按 64 位 AND + popcount
            //      直接算出交集，不再逐像素比较 48×96；
            //   2) 不透明像素二维前缀和给出 footprint 内的 B（合成图不透明数）。
            //      交集 ≤ min(A,B)、并集 ≥ max(A,B) ⇒ B < 0.9A 时 score 必 <0.9，可跳过。
            int wordsW = (W + 63) / 64;
            var compRows = new ulong[wordsW * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (comp[(y * W + x) * 4 + 3] != 0)
                    compRows[y * wordsW + (x >> 6)] |= 1UL << (x & 63);

            int stride = W + 1;
            var alphaPrefix = new int[(H + 1) * stride];
            for (int y = 0; y < H; y++)
            {
                int row = 0;
                for (int x = 0; x < W; x++)
                {
                    if (comp[(y * W + x) * 4 + 3] != 0) row++;
                    alphaPrefix[(y + 1) * stride + x + 1] = alphaPrefix[y * stride + x + 1] + row;
                }
            }

            foreach (Block r in refs)
            {
                int rw = r.Words, shift = 0, word = 0;
                for (int y = 0; y + r.H <= H; y += S)
                for (int x = 0; x + r.W <= W; x += S)
                {
                    int x1 = x + r.W, y1 = y + r.H;
                    int b = alphaPrefix[y1 * stride + x1]
                          - alphaPrefix[y * stride + x1]
                          - alphaPrefix[y1 * stride + x]
                          + alphaPrefix[y * stride + x];
                    if (b == 0 || (long)b * 10 < (long)r.Opaque * 9) continue;

                    int inter = 0;
                    shift = x & 63;
                    word = x >> 6;
                    for (int j = 0; j < r.H; j++)
                    {
                        int cb = (y + j) * wordsW;
                        ulong carry = 0;
                        for (int k = 0; k < rw; k++)
                        {
                            ulong v = r.Mask[j * rw + k];
                            if (shift == 0)
                            {
                                inter += System.Numerics.BitOperations.PopCount(v & compRows[cb + word + k]);
                            }
                            else
                            {
                                ulong lo = (v << shift) | carry;
                                inter += System.Numerics.BitOperations.PopCount(lo & compRows[cb + word + k]);
                                carry = v >> (64 - shift);
                            }
                        }
                        if (shift != 0 && carry != 0)
                            inter += System.Numerics.BitOperations.PopCount(carry & compRows[cb + word + rw]);
                    }

                    int uni = r.Opaque + b - inter;
                    if (uni == 0) continue;
                    double score = (double)inter / uni;
                    if (score < 0.90) continue;
                    if (r.Kind == "tree")
                    {
                        int brown = 0;
                        for (int j = 0; j < r.H; j++)
                        for (int k = 0; k < r.W; k++)
                        {
                            int ci = ((y + j) * W + x + k) * 4;
                            if (comp[ci + 3] == 0) continue;
                            if (comp[ci] > comp[ci + 1] * 1.15 && comp[ci] > comp[ci + 2] * 1.2
                                && comp[ci] > 60)
                                brown++;
                        }
                        double bf = (double)brown / Math.Max(1, b);
                        if (bf < 0.03 || bf > 0.30) continue;
                    }
                    picks.Add((x, y, r.W, r.H, r.Marker, score));
                }
            }
        }

        // 单格 object（石头/矿/杂草）：仍然用整格 RGBA 精确匹配。
        // 同一张图集里的同一个 index 只哈希一次；大幅减少 Back 层重复格子的开销。
        var cellMarkers = new Dictionary<(string Sheet, int Index), int?>();
        foreach (Layer layer in map.Layers ?? new List<Layer>())
        {
            if (layer.Id == "Paths" || layer.Tiles == null) continue;
            for (int i = 0; i < layer.Tiles.Count; i++)
            {
                if (layer.Tiles[i] is not StaticTile st) continue;
                string sheetId = st.TileSheet ?? "";
                var key = (sheetId, st.TileIndex);
                if (!cellMarkers.TryGetValue(key, out int? marker))
                {
                    marker = null;
                    if (sheets.TryGetValue(sheetId, out TileSheet? sh))
                    {
                        var sp = SheetPixels(sh);
                        if (sp.W != 0)
                        {
                            int cols = sp.W / S;
                            int sx = st.TileIndex % cols * S, sy = st.TileIndex / cols * S;
                            if (sx >= 0 && sy >= 0 && sx + S <= sp.W && sy + S <= sp.H)
                            {
                                ulong h = HashCell(sp.Px, sp.W, sx, sy);
                                if (objHashes.TryGetValue(h, out int m)) marker = m;
                            }
                        }
                    }
                    cellMarkers[key] = marker;
                }
                if (marker is int hit)
                    picks.Add((i % mw * S, i / mw * S, S, S, hit, 1.0));
            }
        }

        // 大的先选；同一个 footprint 只保留一个实例。
        var selected = new List<(int X, int Y, int W, int H, int Marker)>();
        foreach (var p in picks.OrderByDescending(p => p.W * p.H).ThenByDescending(p => p.Score))
        {
            int tx = p.X / S, ty = p.Y / S, tw = p.W / S, th = p.H / S;
            bool overlap = selected.Any(s => tx < s.X + s.W && tx + tw > s.X && ty < s.Y + s.H && ty + th > s.Y);
            if (overlap) continue;
            int ax = tx + (tw - 1) / 2, ay = ty + th - 1;
            if (ax < 0 || ay < 0 || ax >= mw || ay >= mh) continue;
            int ai = ay * mw + ax;
            if (ai >= paths.Tiles.Count || paths.Tiles[ai] != null) continue;
            int bi = ay * mw + ax;
            if (first.Tiles == null || bi >= first.Tiles.Count || first.Tiles[bi] == null) continue;
            selected.Add((tx, ty, tw, th, p.Marker));
        }

        if (selected.Count == 0) return false;
        if (!apply)
        {
            converted = selected.Count;
            return true;
        }
        foreach (var s in selected)
        {
            for (int y = s.Y; y < s.Y + s.H; y++)
            for (int x = s.X; x < s.X + s.W; x++)
            {
                int i = y * mw + x;
                foreach (Layer layer in map.Layers)
                {
                    if (layer.Id == "Back" || layer.Id == "Paths" || layer.Tiles == null) continue;
                    if (i < layer.Tiles.Count) layer.Tiles[i] = null!;
                }
            }
            int ax = s.X + (s.W - 1) / 2, ay = s.Y + s.H - 1;
            paths.Tiles[ay * mw + ax] = new StaticTile
            {
                TileSheet = pathsSheetId, TileIndex = s.Marker, Properties = new List<Propertie>(),
            };
            converted++;
        }
        foreach (Layer layer in map.Layers)
            if (layer.Tiles != null) LayerRle.Rebuild(layer);
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, file);
        return true;
    }
}
