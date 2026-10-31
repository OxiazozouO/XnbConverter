using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP 的 <c>EditData Data/AudioChanges</c> → **原生 XACT 音频**。
///
/// CP 的写法是把 ogg 用 <c>{{AbsoluteFilePath: assets/Music/X.ogg}}</c> 指过去，
/// 那是**绝对路径**，我们代不掉（模组一挪窝就失效），所以这条补丁一直是整条被拒。
///
/// 但 1.6 的音频本来就是 XACT：波形放 <c>Wave Bank.xwb</c>、cue 放 <c>Sound Bank.xsb</c>。
/// 于是改成三件纯文件操作，**全程在 xnb 体系里**、不依赖模组目录：
/// <list type="number">
/// <item>把 ogg 编成 **MS-ADPCM** 的 wav 写进产出树的 <c>XACT/Wave Bank/Music/</c>
///       （格式与元数据说明见 <see cref="AdpcmMusic"/> —— 编成 PCM 会让波形库超 2 GB 产不出来）；</item>
/// <item>在 <c>XACT/Wave Bank.xwb.config</c> 的 <c>Entries</c> 里加一条
///       （<c>Offset = -1</c> ⇒ 追加到数据区末尾），长度/格式/循环都按原版 ADPCM 条目写全；</item>
/// <item>在 <c>XACT/Sound Bank.xsb.json</c> 里加一条 Cue 指向那个新波形。</item>
/// </list>
///
/// ⚠ **绝不能**再产出 <c>Data/AudioChanges</c> 那份资产：游戏启动时
/// <c>AudioCueModificationManager.ApplyCueModification</c> 看到同名 cue 已存在就会去改它，
/// 然后拿 <c>FilePaths</c> 去加载音频 —— 那条路径指向的 ogg 在我们这套产出里不存在，
/// 加载失败后样本数组会被缩成空，<c>SetSound(空)</c> 反而把我们加好的 cue **覆盖成哑的**。
///
/// 地图的 <c>Music</c> 属性引用的是 cue 名，和这里新加的 cue 名同名，所以不用动地图。
/// </summary>
public static class AudioChangesMerger
{
    public const string AssetName = "Data/AudioChanges";

    private const string WaveBankManifest = "XACT/Wave Bank.xwb.config";
    private const string SoundBankView = "XACT/Sound Bank.xsb.json";
    private const string AudioEngineView = "XACT/FarmerSounds.xgs.json";
    private const string MusicDir = "XACT/Wave Bank/Music";

    /// <summary>
    /// 新 Sound 的音量字节。XACT 这里不是 dB，而是 0..255 的曲线索引：
    /// <c>0</c> 会被 MonoGame 解成约 <c>1.6e-5</c>（≈ -96 dB，等同静音），
    /// 原版音乐用的是 <c>180</c>（≈ 1.004）。写 0 会让注入的循环音乐**听不见**。
    /// </summary>
    private const byte DefaultSoundVolume = 180;

    /// <summary>
    /// 把 <c>Category</c> 名解析成 .xsb 里那个 <c>CategoryId</c>。
    ///
    /// 索引由 <c>.xgs.json</c> 的 <c>Categories</c> **顺序**决定（原版是
    /// 0 Global / 1 Default / 2 Music / 3 Sound / 4 Ambient / 5 Footsteps），
    /// **不能写死** —— 原版 .xsb 里从没出现过 0，给音乐写 0（Global）会让它走错混音参数。
    ///
    /// CP 的语义照抄 <c>AudioCueModificationManager</c>：<c>Category</c> 为空时用 <c>Default</c>。
    /// 名字在表里找不到 ⇒ 返回 null，调用方**跳过该条**（不猜一个索引塞进去）。
    /// </summary>
    private static int? ResolveCategoryId(OutputTree output, string? category)
    {
        string wanted = string.IsNullOrWhiteSpace(category) ? "Default" : category.Trim();

        string? json = output.ReadTextOrNull(AudioEngineView);
        if (json == null)
            return null;

        if (JObject.Parse(json)["Categories"] is not JArray categories)
            return null;

        for (int i = 0; i < categories.Count; i++)
        {
            if (string.Equals(categories[i]?["Name"]?.Value<string>(), wanted, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    public static bool CanHandle(ChangePatch patch) =>
        patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase) &&
        patch.Target?.Equals(AssetName, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>把一条 AudioChanges 补丁落进产出树。</summary>
    public static MergeOutcome Apply(OutputTree output, string? packRoot, JObject patch, string assetName)
    {
        if (patch["Entries"] is not JObject entries || entries.Count == 0)
            return MergeOutcome.Fail("Data/AudioChanges 补丁没有 Entries");

        if (packRoot == null)
            return MergeOutcome.Fail("Data/AudioChanges 需要内容包根目录来解析 {{AbsoluteFilePath}}，但没拿到");

        // 产出树里得先有 XACT 那两清单 —— 没有就说明这棵树不是完整资源树，宁可不写
        string? manifestJson = output.ReadTextOrNull(WaveBankManifest);
        string? soundBankJson = output.ReadTextOrNull(SoundBankView);
        if (manifestJson == null || soundBankJson == null)
            return MergeOutcome.Fail($"{WaveBankManifest} 或 {SoundBankView} 不在产出树里，无法把音频并进去");

        JObject manifest = JObject.Parse(manifestJson);
        JObject soundBank = JObject.Parse(soundBankJson);
        // 循环的 cue 记进 `LoopedCues`：打包器据此写成「简单 Cue + 带 Clip 的 Sound」
        // （原版音乐就是那个形态）。不循环的走复杂 Cue + 变体类型 0。
        if (soundBank["LoopedCues"] is not JArray)
            soundBank["LoopedCues"] = new JArray();
        if (manifest["Entries"] is not JArray waveEntries || soundBank["Sounds"] is not JArray sounds
            || soundBank["Variations"] is not JArray variations)
            return MergeOutcome.Fail("XACT 清单的结构与预期不符（缺 Entries / Sounds / Variations）");

        string manifestDir = Path.GetDirectoryName(Path.GetFullPath(output.ToOutputPath(WaveBankManifest)))!;
        var notes = new List<string>();
        int added = 0;
        int skipped = 0;
        int alreadyPresent = 0;

        // 声库里**已有的** cue 名（Sounds[].Cue + Cues[].Name）。
        // ⚠ 合并器是**逐条补丁**被调用的，而 `EditData Data/AudioChanges` 常有多条
        //   （实测 `Crystal Island` 有 8 条，其中几条无条件）—— 第二次进来时同一 cue 已经在声库里了。
        //   不加这道闸就会把它再加一遍，打包器重放 `LoopedCues` 时抛
        //   「新增的循环 Cue「X」与已有的 Cue 重名」⇒ **整份声库打不出包**（音乐全没了）。
        var existingCues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JToken sound in (JArray)soundBank["Sounds"])
            if (sound["Cue"]?.Value<string>() is string used && used.Length > 0) existingCues.Add(used);
        foreach (JToken cue in soundBank["Cues"] as JArray ?? new JArray())
            if (cue["Name"]?.Value<string>() is string used && used.Length > 0) existingCues.Add(used);

        // ★ 编码是**纯计算、彼此独立**：先把本轮要编的源文件并行编完，再按原顺序组装清单。
        //   每个文件的 ADPCM 结果只取决于它自己的 PCM，所以并行不改变任何字节；
        //   清单条目的顺序仍由下面的串行循环决定。
        //   实测 DTZ 35 条 ogg：单线程 86 s，8 线程约 12 s。
        var encodeJobs = new List<string>();
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            if (value is not JObject entry) continue;
            string cue = entry["ID"]?.Value<string>() ?? key;
            if (existingCues.Contains(cue)) continue;
            if (entry["FilePaths"] is not JArray entryPaths) continue;
            foreach (JToken? pathToken in entryPaths)
            {
                if (!TryResolveAbsoluteFilePath(pathToken?.Value<string>(), out string relative)) continue;
                string src = Path.GetFullPath(Path.Combine(packRoot, relative));
                if (File.Exists(src) && seenSources.Add(src)) encodeJobs.Add(src);
            }
        }

        var encoded = new Dictionary<string, (byte[]? Wav, AdpcmMusic.EntryShape Shape, string? Error)>(
            StringComparer.OrdinalIgnoreCase);
        if (encodeJobs.Count > 0)
        {
            long encodeT0 = SdvCpTool.Core.Diagnostics.Prof.Now();
            var results = new ConcurrentDictionary<string, (byte[]? Wav, AdpcmMusic.EntryShape Shape, string? Error)>(
                StringComparer.OrdinalIgnoreCase);
            int threads = AudioEncodeThreads();
            // 长的先编（LPT）：编码耗时基本与文件大小成正比，先啃大的能把尾巴压短。
            if (threads > 1 && encodeJobs.Count > 1)
            {
                var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (string src in encodeJobs) sizes[src] = new FileInfo(src).Length;
                encodeJobs.Sort((a, b) => sizes[b].CompareTo(sizes[a]));
            }
            if (threads > 1 && encodeJobs.Count > 1)
            {
                Parallel.ForEach(encodeJobs, new ParallelOptions { MaxDegreeOfParallelism = threads }, src =>
                {
                    bool ok = AdpcmMusic.TryEncodeWav(src, out byte[] wav, out AdpcmMusic.EntryShape shape,
                        out string? err);
                    results[src] = (ok ? wav : null, shape, err);
                });
            }
            else
            {
                foreach (string src in encodeJobs)
                {
                    bool ok = AdpcmMusic.TryEncodeWav(src, out byte[] wav, out AdpcmMusic.EntryShape shape,
                        out string? err);
                    results[src] = (ok ? wav : null, shape, err);
                }
            }

            foreach (var pair in results) encoded[pair.Key] = pair.Value;
            if (SdvCpTool.Core.Diagnostics.Prof.On)
                SdvCpTool.Core.Diagnostics.Prof.AddSince("音频.ADPCM编码", encodeT0);
        }

        foreach (var (key, value) in entries)
        {
            if (value is not JObject entry)
            {
                skipped++;
                notes.Add($"音频条目 `{key}` 不是对象，跳过");
                continue;
            }

            string cueName = entry["ID"]?.Value<string>() ?? key;

            // 同名的只留第一条（本包里同一条 cue 常被列出多次；打包器只认一份）。
            if (existingCues.Contains(cueName))
            {
                skipped++;
                alreadyPresent++;
                notes.Add($"音频条目 `{cueName}` 已在声库里，跳过（同名只留一条）");
                continue;
            }

            // FilePaths：CP 允许给多条（同一 cue 的多个随机变体）。
            // 每条都进 Wave Bank；同一 cue 在 Variations 里写多行，打包时合成一个
            // 类型 6 事件（多轨、权重相同、变体类型 Random）。
            var paths = entry["FilePaths"] as JArray;
            if (paths == null || paths.Count == 0)
            {
                skipped++;
                notes.Add($"音频条目 `{cueName}` 的 FilePaths 缺失，跳过");
                continue;
            }

            // Category 先定下来 —— 定不下来就整条跳过。放到这里是因为下面会**动文件**（复制 ogg、
            // 往波形清单加条目），半途失败会留下孤儿文件与孤儿波形。
            int? categoryId = ResolveCategoryId(output, entry["Category"]?.Value<string>());
            if (categoryId == null)
            {
                skipped++;
                notes.Add($"音频条目 `{cueName}` 的 Category `{entry["Category"]?.Value<string>()}` "
                          + "不在 .xgs 的类别表里，跳过");
                continue;
            }

            var variantTracks = new List<int>();
            foreach (JToken? pathToken in paths)
            {
                if (!TryResolveAbsoluteFilePath(pathToken?.Value<string>(), out string relative))
                {
                    notes.Add($"音频条目 `{cueName}` 有一条 FilePaths 不是 `{{{{AbsoluteFilePath: …}}}}`，跳过该变体");
                    continue;
                }

                string src = Path.GetFullPath(Path.Combine(packRoot, relative));
                if (!File.Exists(src))
                {
                    notes.Add($"音频条目 `{cueName}` 指向的文件不存在：{relative}，跳过该变体");
                    continue;
                }

                // ① ogg 编成 **MS-ADPCM** 的 wav 落进产出树。
                //   ⚠ 必须自己编：打包器那条 ogg 路把波形解成 16 位 PCM（≈4 倍体积），
                //     204 条 ≈2.6 GB 会让 `Wave Bank.xwb` 撞上 32 位上限、**整份产不出来**
                //     （见 docs/verification/2026-10-12-wave-bank-not-produced.md）；
                //     编成 ADPCM 后整库回落到 ~1.1 GB，与打包器实测产出过的尺寸同一档。
                if (!encoded.TryGetValue(src, out var job) || job.Wav == null)
                {
                    notes.Add($"音频条目 `{cueName}` 的波形编不出来（{job.Error}），跳过该变体");
                    continue;
                }

                byte[] wav = job.Wav;
                AdpcmMusic.EntryShape shape = job.Shape;

                string destRel = FreeName(output,
                    $"{MusicDir}/{Path.GetFileNameWithoutExtension(src)}.wav");
                output.StageBytes(destRel, wav);

                // ② 波形清单加一条：Offset = -1（追加），长度/格式/循环都照原版 ADPCM 条目的形态写全
                string fileName = Path.GetRelativePath(manifestDir,
                    Path.GetFullPath(output.ToOutputPath(destRel))).Replace('/', '\\');
                int trackIndex = waveEntries.Count;
                waveEntries.Add(new JObject
                {
                    ["FileName"] = fileName,
                    ["Offset"] = -1,
                    ["Length"] = shape.Length,
                    ["FlagsAndDuration"] = shape.FlagsAndDuration,
                    ["Format"] = shape.Format,
                    ["LoopOffset"] = 0,
                    ["LoopLength"] = shape.LoopLength,
                });
                variantTracks.Add(trackIndex);
            }

            if (variantTracks.Count == 0)
            {
                skipped++;
                notes.Add($"音频条目 `{cueName}` 的 FilePaths 没有一条能落，跳过");
                continue;
            }

            // ③ 声库加一条 Cue（FileOffset = 0 是「新增」的标记），每个变体一行 Variation
            sounds.Add(new JObject
            {
                ["FileOffset"] = 0,
                ["Cue"] = cueName,
                ["CategoryId"] = categoryId.Value,
                ["VolumeFlag"] = DefaultSoundVolume,
                ["Pitch"] = 0.0,
                ["Priority"] = 0,
                ["Filter"] = 0,
            });
            foreach (int trackIndex in variantTracks)
            {
                variations.Add(new JObject
                {
                    ["FileOffset"] = 0,
                    ["Cue"] = cueName,
                    ["WaveBankIndex"] = 0,
                    ["TrackIndex"] = trackIndex,
                });
            }

            // CP 的 `Looped` 决定写哪种形态：
            //   true  ⇒ 「简单 Cue → 带 Clip 的 Sound → LoopCount=255」（原版音乐那样）
            //   false ⇒ 单变体走「复杂 Cue + 变体类型 0」；多变体走简单 Cue + 类型 6、LoopCount=0
            // 少这一步音乐就播一遍就停（MonoGame 只有走 XactClip 那条路才认 loopCount）。
            if (entry["Looped"]?.Value<bool>() == true)
            {
                ((JArray)soundBank["LoopedCues"]!).Add(cueName);
            }

            added++;
        }

        if (added == 0)
        {
            // 「已经在声库里」不算失败：同一条 cue 被多条补丁列出来是常态（见上面那道闸），
            // 第二次进来无事可做 —— 报成功 + 备注，别把它记成一条写失败。
            if (skipped == alreadyPresent)
                return notes.Count == 0 ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes);
            return MergeOutcome.Fail(
                $"Data/AudioChanges 里没有一条能落（{skipped} 条跳过）：{string.Join("；", notes.Take(3))}");
        }

        output.StageJson(WaveBankManifest, manifest);
        output.StageJson(SoundBankView, soundBank);

        if (skipped > 0)
            notes.Add($"另有 {skipped} 条音频没落（见上）");
        notes.Insert(0, $"音频注入：{added} 条 cue 写进 Sound Bank，波形追加进 Wave Bank（不再产出 Data/AudioChanges）");

        // ⚠ 不返回 Data：调用方据此不写 Data/AudioChanges 那份资产
        return MergeOutcome.Ok(null, notes);
    }

    /// <summary>
    /// 产出树里已经有这个路径就换个名字（`X.wav` → `X_2.wav`）。
    ///
    /// ⚠ 必须有这道闸：原版的音乐波形就在 <c>XACT/Wave Bank/Music/*.wav</c> 里（97 个），而模组
    /// 常把自己要用的一段音频**按原版的名字**丢进来 ⇒ 直接写会盖掉原版那条波形，而原版条目记的
    /// 长度/格式还指着原来那份字节 ⇒ 打包器看到长度不符会走**重编码**那条路（可能整份库打不出包）。
    /// （以前我们写的是 `.ogg`，与原版的 `.wav` 天然错开，所以没暴露过。）
    /// </summary>
    private static string FreeName(OutputTree output, string relative)
    {
        if (!output.Exists(relative))
            return relative;

        int slash = relative.LastIndexOf('/');
        string dir = relative[..slash];
        string stem = Path.GetFileNameWithoutExtension(relative);
        for (int i = 2; ; i++)
        {
            string candidate = $"{dir}/{stem}_{i}.wav";
            if (!output.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>从 <c>{{AbsoluteFilePath: 相对路径}}</c> 里取出相对路径。</summary>
    private static bool TryResolveAbsoluteFilePath(string? value, out string relative)
    {
        relative = "";
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        const string prefix = "{{AbsoluteFilePath:";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith("}}"))
            return false;

        string inner = text[prefix.Length..^2].Trim();
        if (inner.Length == 0)
            return false;

        relative = inner.Replace('\\', '/');
        return true;
    }

    /// <summary>
    /// 音频编码线程数：默认 <c>min(逻辑核数, 8)</c>；`SDVCP_AUDIO_THREADS` 可覆盖（调优/对照用，
    /// 设成 1 就退回单线程）。
    /// </summary>
    private static int AudioEncodeThreads()
    {
        string? raw = Environment.GetEnvironmentVariable("SDVCP_AUDIO_THREADS");
        if (int.TryParse(raw, out int n) && n > 0) return n;
        return Math.Min(Environment.ProcessorCount, 8);
    }
}
