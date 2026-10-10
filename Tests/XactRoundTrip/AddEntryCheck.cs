using System.Security.Cryptography;
using Newtonsoft.Json;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

namespace XactRoundTrip;

/// <summary>
/// 追加条目的端到端检验：解包 → 清单里多挂一条波形（Offset 写 AppendToEnd）→ 重建 →
/// 把重建结果**重新解包**，逐条比对。
///
/// 要证明三件事：
///   ① 重建结果仍是一份能被解包器读通的 .xwb（头部重建没写坏）；
///   ② 已有的条目载荷逐字节不变（只该整体平移，内容不动）；
///   ③ 新条目的载荷就是挂进去那个 wav 的载荷。
/// </summary>
public static class AddEntryCheck
{
    public static int Run(string input)
    {
        string dir = Path.Combine("add", Path.GetFileNameWithoutExtension(input));
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }

        Directory.CreateDirectory(dir);

        WaveBank wb = WaveBankReader.Read(input);
        wb.OutputPath = dir;
        for (int i = 0; i < wb.Entries.Count; i++)
        {
            wb.Entries[i].FilePath = dir + Path.DirectorySeparatorChar;
            wb.Entries[i].FileName ??= $"{i:x8}";
        }

        WaveBankReader.Save(wb);

        string manifest = wb.OutputPath + ".xwb.config";
        string[] before = wb.Entries.Select(e => Sha(e.Data)).ToArray();
        int oldCount = wb.Entries.Count;
        int oldDataLength = (int)(wb.Header.Segments[4].Length);

        // 新波形就用第 0 条复制一份 —— 格式/长度必然与已有条目同规格
        var src = wb.Entries[0];
        string newWav = Path.Combine(dir, "appended.wav");
        File.Copy(src.GetPath(), newWav);

        WaveBankManifest m = JsonConvert.DeserializeObject<WaveBankManifest>(File.ReadAllText(manifest));
        string manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifest));
        m.Entries.Add(new WaveBankManifest.Entry
        {
            // 清单里的 FileName 一律相对「清单所在目录」
            FileName = Path.GetRelativePath(manifestDir, Path.GetFullPath(newWav)).Replace('/', '\\'),
            Offset = WaveBankWriter.AppendToEnd,
            Length = (int)src.PlayRegion.Length,
            FlagsAndDuration = src.FlagsAndDuration,
            Format = src.Format,
            LoopOffset = 0,
            LoopLength = 0
        });
        File.WriteAllText(manifest, JsonConvert.SerializeObject(m, Formatting.Indented));

        byte[] rebuilt = WaveBankWriter.Build(manifest);
        string outXwb = Path.Combine(dir, "rebuilt.xwb");
        File.WriteAllBytes(outXwb, rebuilt);

        WaveBank wb2 = WaveBankReader.Read(outXwb);

        bool countOk = wb2.Entries.Count == oldCount + 1;

        bool keptOk = true;
        for (int i = 0; i < Math.Min(oldCount, wb2.Entries.Count); i++)
        {
            if (Sha(wb2.Entries[i].Data) != before[i])
            {
                keptOk = false;
                Console.WriteLine($"    已有条目 {i} 的载荷变了");
                break;
            }
        }

        bool appendOk = wb2.Entries.Count > oldCount && Sha(wb2.Entries[^1].Data) == Sha(src.Data);

        // 新条目的数据必须落在原数据区之后，且相对偏移按 Alignment 对齐
        long newRel = wb2.Entries.Count > oldCount
            ? wb2.Entries[^1].PlayRegion.Offset - wb2.Header.Segments[4].Offset
            : -1;
        bool relOk = newRel >= oldDataLength && newRel % 4 == 0;

        bool sizeOk = rebuilt.Length > new FileInfo(input).Length;

        // 新条目的元数据（Format / FlagsAndDuration / 长度）也要按写进去的值读回来
        bool metaOk = wb2.Entries.Count > oldCount &&
                      wb2.Entries[^1].Format == src.Format &&
                      wb2.Entries[^1].FlagsAndDuration == src.FlagsAndDuration &&
                      wb2.Entries[^1].PlayRegion.Length == src.PlayRegion.Length;

        Console.WriteLine($"  条目 {oldCount} → {wb2.Entries.Count}  {(countOk ? "✓" : "✗")}   " +
                          $"已有载荷不变 {(keptOk ? "✓" : "✗")}   " +
                          $"新条目载荷 {(appendOk ? "✓" : "✗")}   " +
                          $"新条目元数据 {(metaOk ? "✓" : "✗")}   " +
                          $"新条目相对偏移={newRel}（原数据区 {oldDataLength}）{(relOk ? "✓" : "✗")}   " +
                          $"文件 {new FileInfo(input).Length} → {rebuilt.Length} {(sizeOk ? "✓" : "✗")}");

        return countOk && keptOk && appendOk && metaOk && relOk && sizeOk ? 0 : 1;
    }

    private static string Sha(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data));
    }
}
