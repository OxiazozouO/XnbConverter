using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Frameworks;

/// <summary>
/// 纯观赏内容（家具/大件/墙纸等）的近似去重。**默认关闭**：
/// 逐对比对要花时间，而且“差不多”是主观的，必须由装机的人显式打开。
/// <para>
/// 判据三层：
/// <list type="number">
/// <item>完全相同的像素块 → 直接重复；</item>
/// <item>16×16 灰度图 + 8×8 dHash 的汉明距离 ≤ 阈值，且 alpha 轮廓重合 → 样式相近；</item>
/// <item>alpha 轮廓相同、去均值后的灰度差很小 → 纯变色（同一张图换颜色）。</item>
/// </list>
/// 只在**同一个基础物品组**里比较（例如同一件家具的多个变体），避免把“椅子”和“床”
/// 这种轮廓相似但语义不同的东西误删。
/// </para>
/// </summary>
public sealed class CosmeticDeduper
{
    public bool Enabled { get; }
    public int Threshold { get; }
    public double PureRecolorTolerance { get; }
    /// <summary>纯变色一致性上限（百分比）：整图颜色偏移越统一越像“换色”，局部变化越大越不像。</summary>
    public double PureRecolorConsistency { get; }
    /// <summary>相似去重额外的像素差上限（百分比）；dHash 通过但画面差超过它就不算重复。</summary>
    public double MaxPixelDifference { get; }
    public int Deduped { get; private set; }

    private sealed record Entry(string Id, Fingerprint Fp);
    private readonly Dictionary<string, List<Entry>> _kept = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _exact = new();
    private readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Skipped => _skipped;

    /// <summary>
    /// 上一轮因去重没写盘的 ID。重复跑时直接跳过，保证开关幂等；
    /// 换了阈值就由调用方不加载这份旧记录。
    /// </summary>
    public void SeedSkipped(IEnumerable<string> ids)
    {
        foreach (string id in ids) _skipped.Add(id);
    }

    public CosmeticDeduper(bool enabled, int threshold, double pureRecolorTolerance = 0,
        double maxPixelDifference = 1.0, double pureRecolorConsistency = 2.0)
    {
        Enabled = enabled;
        Threshold = Math.Clamp(threshold, 0, 32);
        PureRecolorTolerance = Math.Max(0, pureRecolorTolerance);
        MaxPixelDifference = Math.Max(0, maxPixelDifference);
        PureRecolorConsistency = Math.Max(0, pureRecolorConsistency);
    }

    /// <summary>
    /// 把**上一轮已经落盘**的生成项喂进来：重复跑时即使本轮的新 ID 还没出现，
    /// 也能按“同图”跳过，保持幂等。
    /// </summary>
    public void Seed(string id, Fingerprint? fp)
    {
        if (!Enabled || fp == null) return;
        _exact.Add(new Entry(id, fp));
    }

    /// <summary>
    /// 把已落盘的生成项连同**分组**一起喂进来。重跑时只有 `_exact` 不够：
    /// 缺失的变体要跟同组的既有条目做相似/纯变色比较，否则会绕过判据被过度补回。
    /// </summary>
    public void Seed(string group, string id, Fingerprint? fp)
    {
        if (!Enabled || fp == null) return;
        Seed(id, fp);
        if (!_kept.TryGetValue(group, out List<Entry>? list))
            _kept[group] = list = new List<Entry>();
        list.Add(new Entry(id, fp));
    }

    public bool TryAccept(string group, string id, Fingerprint? fp, out string? duplicateOf)
    {
        duplicateOf = null;
        if (!Enabled) return true;
        if (_skipped.Contains(id))
        {
            duplicateOf = "(上轮已去重)";
            Deduped++;
            return false;
        }
        if (fp == null) return true;
        // 先查全量精确重复（包含上一轮已落盘的项）。
        foreach (Entry e in _exact)
        {
            if (!fp.Exact(e.Fp)) continue;
            duplicateOf = e.Id;
            _skipped.Add(id);
            Deduped++;
            return false;
        }
        if (!_kept.TryGetValue(group, out List<Entry>? list))
        {
            _kept[group] = new List<Entry> { new(id, fp) };
            _exact.Add(new Entry(id, fp));
            return true;
        }

        foreach (Entry e in list)
        {
            if (fp.Exact(e.Fp) ||
                (PureRecolorTolerance > 0 &&
                 fp.PureRecolor(e.Fp, PureRecolorTolerance, PureRecolorConsistency)) ||
                fp.Similar(e.Fp, Threshold, MaxPixelDifference))
            {
                duplicateOf = e.Id;
                _skipped.Add(id);
                Deduped++;
                return false;
            }
        }
        list.Add(new Entry(id, fp));
        _exact.Add(new Entry(id, fp));
        return true;
    }

    /// <summary>从产出树/包里的一张 PNG 的指定区域取指纹。</summary>
    public static Fingerprint? Capture(ImageCache images, string source,
        int sx, int sy, int sw, int sh)
    {
        if (!images.TryReadSource(source, out byte[] pixels, out int width, out int height, out _))
            return null;
        if (sx < 0 || sy < 0 || sw <= 0 || sh <= 0 || sx + sw > width || sy + sh > height)
            return null;
        return Fingerprint.FromPixels(pixels, width, height, sx, sy, sw, sh);
    }

    public sealed record Calibration(
        int Entries, int ExactUnique, int RecommendedThreshold, int MinHamming,
        double MinPureMad, double RecommendedPureTolerance, int NearPairs);

    /// <summary>
    /// 用**原版家具**反推阈值：找所有非完全相同的原版条目里最近的 dHash 距离，
    /// 推荐阈值 = 这个距离（`Similar` 用严格小于，所以刚好不会命中原版）。
    /// 纯变色容差同理取比最近原版 MAD 再小一点的值。
    /// </summary>
    public static Calibration CalibrateVanillaFurniture(string treeRoot)
    {
        JObject furniture;
        string path = System.IO.Path.Combine(treeRoot, "Data", "Furniture.json");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 Data/Furniture.json", path);
        furniture = JObject.Parse(File.ReadAllText(path));

        var fps = new List<Fingerprint>();
        foreach (JProperty p in furniture.Properties())
        {
            if (p.Value.Type != JTokenType.String) continue;
            string[] f = p.Value.Value<string>()!.Split('/');
            if (f.Length < 3) continue;
            string tex = f.Length > 9 && !string.IsNullOrWhiteSpace(f[9])
                ? f[9] : @"TileSheets\furniture";
            int sprite = f.Length > 8 && int.TryParse(f[8], out int si) ? si
                : int.TryParse(p.Name, out int id) ? id : 0;
            int[] size = f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out int n) ? n : 1).ToArray();
            int sw = Math.Max(1, size.ElementAtOrDefault(0)) * 16;
            int sh = Math.Max(1, size.ElementAtOrDefault(1)) * 16;
            string png = System.IO.Path.Combine(treeRoot,
                tex.Replace('\\', '/') + ".png");
            if (!File.Exists(png)) continue;
            byte[] pixels;
            int width, height;
            try { pixels = Png.Read(png, out width, out height); }
            catch { continue; }
            int x = sprite * 16 % width;
            int y = sprite * 16 / width * 16;
            if (x + 16 > width || y + 16 > height) continue;
            sw = Math.Min(sw, width - x);
            sh = Math.Min(sh, height - y);
            if (sw <= 0 || sh <= 0) continue;
            fps.Add(Fingerprint.FromPixels(pixels, width, height, x, y, sw, sh));
        }

        var unique = new List<Fingerprint>();
        foreach (Fingerprint fp in fps)
            if (!unique.Any(u => u.Exact(fp))) unique.Add(fp);

        int minHamming = 99;
        double minMad = double.MaxValue;
        int nearPairs = 0;
        for (int i = 0; i < unique.Count; i++)
            for (int j = i + 1; j < unique.Count; j++)
            {
                Fingerprint a = unique[i], b = unique[j];
                double overlap = a.MaskOverlap(b);
                if (overlap >= 0.85)
                {
                    nearPairs++;
                    minHamming = Math.Min(minHamming, a.Hamming(b));
                }
                if (overlap >= 0.95)
                    minMad = Math.Min(minMad, a.PureMad(b));
            }

        int threshold = minHamming == 99 ? 6 : Math.Max(0, minHamming);
        double pureTolerance = minMad == double.MaxValue
            ? 18 : Math.Max(0, Math.Ceiling(minMad) - 1);
        return new Calibration(fps.Count, unique.Count, threshold,
            minHamming, minMad, pureTolerance, nearPairs);
    }

    public sealed class Fingerprint
    {
        private readonly ulong _pixelHash;
        private readonly ulong _dHash;
        private readonly byte[] _gray;      // 16×16
        private readonly byte[] _rgb;       // 16×16×3，用于像素差保护
        private readonly bool[] _mask;      // 16×16
        private readonly int _coverage;

        private Fingerprint(ulong pixelHash, ulong dHash, byte[] gray, byte[] rgb,
            bool[] mask, int coverage)
        {
            _pixelHash = pixelHash;
            _dHash = dHash;
            _gray = gray;
            _rgb = rgb;
            _mask = mask;
            _coverage = coverage;
        }

        public static Fingerprint FromPixels(byte[] pixels, int width, int height,
            int sx, int sy, int sw, int sh)
        {
            ulong hash = 1469598103934665603UL;
            var gray = new byte[16 * 16];
            var rgb = new byte[16 * 16 * 3];
            var mask = new bool[16 * 16];
            int coverage = 0;

            for (int gy = 0; gy < 16; gy++)
            {
                int y0 = sy + gy * sh / 16;
                int y1 = sy + Math.Max(y0 + 1, (gy + 1) * sh / 16);
                y1 = Math.Min(y1, sy + sh);
                for (int gx = 0; gx < 16; gx++)
                {
                    int x0 = sx + gx * sw / 16;
                    int x1 = sx + Math.Max(x0 + 1, (gx + 1) * sw / 16);
                    x1 = Math.Min(x1, sx + sw);
                    long sum = 0, sr = 0, sg = 0, sb = 0;
                    int n = 0;
                    int opaque = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * width * 4;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x * 4;
                            byte a = pixels[i + 3];
                            if (a >= 16) opaque++;
                            int l = (pixels[i] * 77 + pixels[i + 1] * 150 + pixels[i + 2] * 29) >> 8;
                            sum += l;
                            sr += pixels[i];
                            sg += pixels[i + 1];
                            sb += pixels[i + 2];
                            n++;
                            // 只对实际采样到的像素做精确哈希（区域尺寸也进哈希）
                            hash = (hash ^ pixels[i]) * 1099511628211UL;
                            hash = (hash ^ pixels[i + 1]) * 1099511628211UL;
                            hash = (hash ^ pixels[i + 2]) * 1099511628211UL;
                            hash = (hash ^ pixels[i + 3]) * 1099511628211UL;
                        }
                    }
                    int idx = gy * 16 + gx;
                    gray[idx] = n == 0 ? (byte)0 : (byte)(sum / n);
                    rgb[idx * 3] = n == 0 ? (byte)0 : (byte)(sr / n);
                    rgb[idx * 3 + 1] = n == 0 ? (byte)0 : (byte)(sg / n);
                    rgb[idx * 3 + 2] = n == 0 ? (byte)0 : (byte)(sb / n);
                    mask[idx] = n > 0 && opaque * 2 >= n;
                    if (mask[idx]) coverage++;
                }
            }

            // 8×8 dHash：横向相邻的 9 列采样
            var small = new byte[9 * 8];
            for (int gy = 0; gy < 8; gy++)
                for (int gx = 0; gx < 9; gx++)
                {
                    int x = Math.Min(15, gx * 16 / 9);
                    int y = Math.Min(15, gy * 16 / 8);
                    small[gy * 9 + gx] = gray[y * 16 + x];
                }
            ulong d = 0;
            int bit = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (small[y * 9 + x] > small[y * 9 + x + 1])
                        d |= 1UL << bit++;
                    else
                        bit++;

            hash = (hash ^ (uint)sw) * 1099511628211UL;
            hash = (hash ^ (uint)sh) * 1099511628211UL;
            return new Fingerprint(hash, d, gray, rgb, mask, coverage);
        }

        public bool Exact(Fingerprint other) => _pixelHash == other._pixelHash;

        public bool Similar(Fingerprint other, int threshold, double maxPixelDifference)
        {
            // 严格小于：校准值 = 原版最近的非重复距离时，阈值正好不会命中原版。
            if (threshold <= 0) return false;
            if (_coverage == 0 || other._coverage == 0) return _coverage == other._coverage;
            if (MaskOverlap(other) < 0.85) return false;
            if (Hamming(other) >= threshold) return false;
            // dHash 只看明暗结构，6/18 会把“换花色/换书脊/换窗帘”误判成同图；
            // 再加一道画面差上限（默认 1%），把这类变体挡回去。
            return ColorDifference(other) <= maxPixelDifference;
        }

        /// <summary>
        /// 纯变色时单个格子允许的最大偏移（百分比）。平均一致性会稀释小面积变化：
        /// 实测 Basic Window 2/3 只换窗帘，平均一致性 1.65% 很像换色，但单格偏移 33%，
        /// 必须靠这道局部上限挡住。真换色的单格偏移通常在 10% 以内。
        /// </summary>
        public const double PureRecolorMaxDeviation = 20.0;

        public bool PureRecolor(Fingerprint other, double tolerance, double consistency)
        {
            if (tolerance <= 0) return false;
            if (_coverage == 0 || other._coverage == 0) return false;
            if (MaskOverlap(other) < 0.95) return false;
            if (PureMad(other) > tolerance) return false;
            // 换花色/书脊/窗帘是“局部变化”，颜色偏移不统一；真换色是整图近似同一偏移。
            if (RecolorDeviation(other) > consistency) return false;
            return RecolorMaxDeviation(other) <= PureRecolorMaxDeviation;
        }

        public int Hamming(Fingerprint other) =>
            System.Numerics.BitOperations.PopCount(_dHash ^ other._dHash);

        /// <summary>按 16×16 网格平均 RGB 的差（百分比，0..100）。用于给相似判断加画面差上限。</summary>
        public double ColorDifference(Fingerprint other)
        {
            long sum = 0;
            int n = 0;
            for (int i = 0; i < 256; i++)
            {
                if (!_mask[i] || !other._mask[i]) continue;
                sum += Math.Abs(_rgb[i * 3] - other._rgb[i * 3]);
                sum += Math.Abs(_rgb[i * 3 + 1] - other._rgb[i * 3 + 1]);
                sum += Math.Abs(_rgb[i * 3 + 2] - other._rgb[i * 3 + 2]);
                n += 3;
            }
            return n == 0 ? 100 : (double)sum / n / 255 * 100;
        }

        /// <summary>RGB 偏移的一致性（百分比）：先求平均偏移，再量每个格子的偏移离它有多远。</summary>
        public double RecolorDeviation(Fingerprint other)
        {
            if (!TryRecolorOffset(other, out double mr, out double mg, out double mb, out int n))
                return 100;
            double dev = 0;
            for (int i = 0; i < 256; i++)
            {
                if (!_mask[i] || !other._mask[i]) continue;
                dev += Math.Abs(other._rgb[i * 3] - _rgb[i * 3] - mr);
                dev += Math.Abs(other._rgb[i * 3 + 1] - _rgb[i * 3 + 1] - mg);
                dev += Math.Abs(other._rgb[i * 3 + 2] - _rgb[i * 3 + 2] - mb);
            }
            return dev / (n * 3) / 255 * 100;
        }

        /// <summary>单格最大偏移（百分比）：小面积局部变化在平均值里会被稀释，这里单独兜底。</summary>
        public double RecolorMaxDeviation(Fingerprint other)
        {
            if (!TryRecolorOffset(other, out double mr, out double mg, out double mb, out _))
                return 100;
            double max = 0;
            for (int i = 0; i < 256; i++)
            {
                if (!_mask[i] || !other._mask[i]) continue;
                double d = (Math.Abs(other._rgb[i * 3] - _rgb[i * 3] - mr) +
                            Math.Abs(other._rgb[i * 3 + 1] - _rgb[i * 3 + 1] - mg) +
                            Math.Abs(other._rgb[i * 3 + 2] - _rgb[i * 3 + 2] - mb)) / 3.0 / 255 * 100;
                if (d > max) max = d;
            }
            return max;
        }

        private bool TryRecolorOffset(Fingerprint other,
            out double mr, out double mg, out double mb, out int n)
        {
            long sr = 0, sg = 0, sb = 0;
            n = 0;
            for (int i = 0; i < 256; i++)
            {
                if (!_mask[i] || !other._mask[i]) continue;
                sr += other._rgb[i * 3] - _rgb[i * 3];
                sg += other._rgb[i * 3 + 1] - _rgb[i * 3 + 1];
                sb += other._rgb[i * 3 + 2] - _rgb[i * 3 + 2];
                n++;
            }
            if (n == 0)
            {
                mr = mg = mb = 0;
                return false;
            }
            mr = (double)sr / n;
            mg = (double)sg / n;
            mb = (double)sb / n;
            return true;
        }

        public double PureMad(Fingerprint other)
        {
            double meanA = MeanGray(), meanB = other.MeanGray();
            double sum = 0;
            int n = 0;
            for (int i = 0; i < 256; i++)
            {
                if (!_mask[i] || !other._mask[i]) continue;
                sum += Math.Abs((_gray[i] - meanA) - (other._gray[i] - meanB));
                n++;
            }
            return n == 0 ? double.MaxValue : sum / n;
        }

        private double MeanGray()
        {
            double sum = 0;
            int n = 0;
            for (int i = 0; i < 256; i++)
                if (_mask[i]) { sum += _gray[i]; n++; }
            return n == 0 ? 0 : sum / n;
        }

        public double MaskOverlap(Fingerprint other)
        {
            int inter = 0, union = 0;
            for (int i = 0; i < 256; i++)
            {
                if (_mask[i] || other._mask[i]) union++;
                if (_mask[i] && other._mask[i]) inter++;
            }
            return union == 0 ? 1 : (double)inter / union;
        }
    }
}
