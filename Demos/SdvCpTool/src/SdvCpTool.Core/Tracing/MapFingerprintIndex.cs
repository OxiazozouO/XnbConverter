using System.Security.Cryptography;
using System.Text;
using SdvCpTool.Core.Maps;

namespace SdvCpTool.Core.Tracing;

/// <summary>全树地图的图块指纹索引；磁盘缓存，截图反查直接二分查表。</summary>
public sealed class MapFingerprintIndex
{
    private const string Magic = "SDVTRACE1";

    public sealed record MapEntry(string Name, int TilesWide, int TilesHigh, ulong[] Hashes);

    public sealed record TileHit(int Map, int X, int Y);

    public IReadOnlyList<MapEntry> Maps => _maps;
    private readonly List<MapEntry> _maps;
    private readonly TileRecord[] _sorted;

    private readonly record struct TileRecord(ulong Hash, int Map, int X, int Y);

    public MapFingerprintIndex(IEnumerable<MapEntry> maps) : this(maps.ToList())
    {
    }

    private MapFingerprintIndex(List<MapEntry> maps)
    {
        _maps = maps;
        long total = maps.Sum(m => (long)m.Hashes.Length);
        _sorted = new TileRecord[total];
        int k = 0;
        for (int mi = 0; mi < maps.Count; mi++)
        {
            MapEntry m = maps[mi];
            for (int i = 0; i < m.Hashes.Length; i++)
                _sorted[k++] = new TileRecord(m.Hashes[i], mi, i % m.TilesWide, i / m.TilesWide);
        }
        Array.Sort(_sorted, (a, b) => a.Hash != b.Hash
            ? a.Hash.CompareTo(b.Hash)
            : a.Map != b.Map ? a.Map.CompareTo(b.Map) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
    }

    public IEnumerable<TileHit> Lookup(ulong hash, int max = 64)
    {
        int lo = LowerBound(hash), hi = UpperBound(hash);
        int n = Math.Min(max, hi - lo);
        for (int i = 0; i < n; i++)
        {
            TileRecord r = _sorted[lo + i];
            yield return new TileHit(r.Map, r.X, r.Y);
        }
    }

    public int CountOf(ulong hash) => UpperBound(hash) - LowerBound(hash);

    private int LowerBound(ulong hash)
    {
        int lo = 0, hi = _sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_sorted[mid].Hash < hash) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private int UpperBound(ulong hash)
    {
        int lo = 0, hi = _sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_sorted[mid].Hash <= hash) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>确保缓存存在并载入；首次会渲染全树地图并写索引。</summary>
    public static MapFingerprintIndex LoadOrBuild(string tree, string season, string cacheDir,
        bool rebuild, Action<string>? log = null)
    {
        Directory.CreateDirectory(cacheDir);
        string key = TreeKey(tree, season);
        string cache = Path.Combine(cacheDir, "index-" + key + ".bin");
        if (!rebuild && File.Exists(cache))
        {
            try
            {
                log?.Invoke($"载入地图指纹缓存：{cache}");
                return Read(cache);
            }
            catch (Exception ex)
            {
                log?.Invoke($"缓存读失败，重建：{ex.Message}");
            }
        }

        log?.Invoke("扫描地图并渲染指纹（首次较慢）……");
        List<MapEntry> maps = Build(tree, season, log);
        Write(cache, maps);
        log?.Invoke($"指纹索引已写入 {cache}（{maps.Count} 张地图）");
        return new MapFingerprintIndex(maps);
    }

    private static List<MapEntry> Build(string tree, string season, Action<string>? log)
    {
        string mapsDir = Path.Combine(tree, "Maps");
        string[] files = Directory.EnumerateFiles(mapsDir, "*.tbin", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var result = new List<MapEntry>(files.Length);
        object sync = new();
        int done = 0;

        Parallel.ForEach(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1)),
        }, file =>
        {
            string rel = Path.GetRelativePath(mapsDir, file).Replace('\\', '/');
            string asset = rel[..^".tbin".Length];
            var renderer = new MapRenderer(tree, season);
            MapRenderer.RenderedMap? map = renderer.Render(asset);
            if (map == null || map.Width < TileFingerprint.Tile || map.Height < TileFingerprint.Tile)
                return;

            int tw = map.Width / TileFingerprint.Tile;
            int th = map.Height / TileFingerprint.Tile;
            ulong[] hashes = new ulong[tw * th];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    hashes[y * tw + x] = TileFingerprint.Compute(
                        map.Rgba, map.Width, map.Height, x * TileFingerprint.Tile, y * TileFingerprint.Tile);

            lock (sync)
            {
                result.Add(new MapEntry(rel[..^".tbin".Length], tw, th, hashes));
                done++;
                if (done % 100 == 0) log?.Invoke($"  指纹进度 {done}/{files.Length}");
            }
        });

        result.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        return result;
    }

    /// <summary>树指纹：地图与图集文件的相对路径、长度、mtime。地图/图集变了缓存自动失效。</summary>
    private static string TreeKey(string tree, string season)
    {
        string maps = Path.Combine(tree, "Maps");
        var files = Directory.EnumerateFiles(maps, "*.tbin", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(maps, "*.png", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal);
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Path.GetFullPath(tree).ToLowerInvariant());
        writer.Write(season.ToLowerInvariant());
        foreach (string file in files)
        {
            writer.Write(Path.GetRelativePath(tree, file).Replace('\\', '/').ToLowerInvariant());
            writer.Write(new FileInfo(file).Length);
            writer.Write(File.GetLastWriteTimeUtc(file).Ticks);
        }
        writer.Flush();
        stream.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant()[..16];
    }

    private static void Write(string path, List<MapEntry> maps)
    {
        using var f = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(f, Encoding.UTF8);
        w.Write(Magic);
        w.Write(maps.Count);
        foreach (MapEntry m in maps)
        {
            w.Write(m.Name);
            w.Write(m.TilesWide);
            w.Write(m.TilesHigh);
            foreach (ulong h in m.Hashes) w.Write(h);
        }
    }

    private static MapFingerprintIndex Read(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var r = new BinaryReader(f, Encoding.UTF8);
        if (r.ReadString() != Magic) throw new InvalidDataException("不是指纹索引文件");
        int count = r.ReadInt32();
        var maps = new List<MapEntry>(count);
        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString();
            int tw = r.ReadInt32(), th = r.ReadInt32();
            ulong[] hashes = new ulong[tw * th];
            for (int k = 0; k < hashes.Length; k++) hashes[k] = r.ReadUInt64();
            maps.Add(new MapEntry(name, tw, th, hashes));
        }
        return new MapFingerprintIndex(maps);
    }
}
