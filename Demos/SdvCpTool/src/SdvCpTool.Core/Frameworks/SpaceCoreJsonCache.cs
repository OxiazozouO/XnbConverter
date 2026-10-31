using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Frameworks;

/// <summary>
/// 一轮烘焙内的 SpaceCore JSON 索引：每个内容包只枚举、读取一次，
/// 把含三类扩展目标的文件解析后共用给纹理/对象/作物三个扫描器。
/// 只缓存命中目标的文件，生命周期跟随单次 <c>Bake</c>。
/// </summary>
internal sealed class SpaceCoreJsonCache
{
    internal sealed record CachedFile(JObject Root, HashSet<string> Targets);

    private const int ScanChunk = 64 * 1024;

    private static readonly byte[] ObjectMarker =
        Encoding.UTF8.GetBytes(SpaceCoreObjectExtensions.Target);
    private static readonly byte[] CropMarker =
        Encoding.UTF8.GetBytes(SpaceCoreCropExtensions.Target);
    private static readonly byte[] TextureMarker =
        Encoding.UTF8.GetBytes(FrameworkBaker.TextureOverridesTarget);
    private static readonly int MarkerSlack =
        Math.Max(ObjectMarker.Length, Math.Max(CropMarker.Length, TextureMarker.Length)) - 1;

    private readonly ConcurrentDictionary<string, List<CachedFile>> _packs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>并行预读所有内容包；各包结果独立，不影响后续扫描顺序。</summary>
    public void Prewarm(IEnumerable<string> packRoots)
    {
        Parallel.ForEach(packRoots.Distinct(StringComparer.OrdinalIgnoreCase),
            new ParallelOptions { MaxDegreeOfParallelism = 4 }, packRoot =>
            _packs.TryAdd(packRoot, LoadFiles(packRoot)));
    }

    public IReadOnlyList<CachedFile> GetFiles(string packRoot)
        => _packs.GetOrAdd(packRoot, LoadFiles);

    private static List<CachedFile> LoadFiles(string packRoot)
    {
        var files = new List<CachedFile>();
        byte[] scanBuffer = ArrayPool<byte>.Shared.Rent(ScanChunk + MarkerSlack);
        try
        {
            foreach (string file in Directory.EnumerateFiles(packRoot, "*.json",
                         SearchOption.AllDirectories))
            {
                int flags;
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                        FileShare.Read, ScanChunk, FileOptions.SequentialScan);
                    flags = ScanMarkers(stream, scanBuffer);
                    if (flags < 0) flags = ScanText(file);
                }
                catch { continue; }
                if (flags == 0) continue;

                JObject root;
                try { root = JsoncReader.ParseFile(file); }
                catch { continue; }
                files.Add(new CachedFile(root, ToTargets(flags)));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scanBuffer);
        }

        return files;
    }

    /// <summary>
    /// 在字节流里找三个 ASCII 目标名，跨块保留 marker 长度减一的重叠。
    /// 返回 <c>-1</c> 表示首块带 UTF-16/32 BOM，需要调用方改走文本路径。
    /// </summary>
    private static int ScanMarkers(Stream stream, byte[] buffer)
    {
        int flags = 0, carry = 0;
        bool first = true;
        while (true)
        {
            int read = stream.Read(buffer, carry, ScanChunk);
            if (read <= 0) break;
            int length = carry + read;
            ReadOnlySpan<byte> span = buffer.AsSpan(0, length);
            if (first)
            {
                first = false;
                if (HasUnicodeBom(span)) return -1;
            }
            if ((flags & 1) == 0 && span.IndexOf(ObjectMarker) >= 0) flags |= 1;
            if ((flags & 2) == 0 && span.IndexOf(CropMarker) >= 0) flags |= 2;
            if ((flags & 4) == 0 && span.IndexOf(TextureMarker) >= 0) flags |= 4;
            if (flags == 7) break;

            carry = Math.Min(MarkerSlack, length);
            if (carry > 0) span[^carry..].CopyTo(buffer.AsSpan(0, carry));
        }
        return flags;
    }

    private static int ScanText(string file)
    {
        // UTF-16/32 的字节里夹杂 NUL，ASCII 字节搜索找不到；退回文本路径。
        return TextFlags(File.ReadAllText(file));
    }

    private static int TextFlags(string text)
    {
        int flags = 0;
        if (text.Contains(SpaceCoreObjectExtensions.Target, StringComparison.Ordinal)) flags |= 1;
        if (text.Contains(SpaceCoreCropExtensions.Target, StringComparison.Ordinal)) flags |= 2;
        if (text.Contains(FrameworkBaker.TextureOverridesTarget, StringComparison.Ordinal)) flags |= 4;
        return flags;
    }

    private static HashSet<string> ToTargets(int flags)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        if ((flags & 1) != 0) targets.Add(SpaceCoreObjectExtensions.Target);
        if ((flags & 2) != 0) targets.Add(SpaceCoreCropExtensions.Target);
        if ((flags & 4) != 0) targets.Add(FrameworkBaker.TextureOverridesTarget);
        return targets;
    }

    private static bool HasUnicodeBom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2
        && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))
        || bytes.Length >= 4
        && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF;
}
