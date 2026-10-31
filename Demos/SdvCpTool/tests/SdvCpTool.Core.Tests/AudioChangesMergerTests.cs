using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// `Data/AudioChanges` 的音频注入：波形进 Wave Bank、cue 进 Sound Bank、**不**产出该资产。
///
/// ⚠ 这一层现在**自己解 ogg 并编成 MS-ADPCM**（见 `AdpcmMusic`；编成 PCM 会让波形库超 2 GB、
/// 整份产不出来），所以夹具必须是**真的 .ogg**：`fixtures/Kicksound.ogg`（2 声道 24 kHz、8.4 KB）。
/// </summary>
public sealed class AudioChangesMergerTests : IDisposable
{
    private readonly string _root;

    /// <summary>
    /// 真 ogg 夹具 —— 本层要解码，假文件走不通。
    /// 取自语料里某个内容包注入的音乐（2 声道 24 kHz、8.4 KB，语料注入集里最小的那个）。
    /// </summary>
    private static string OggFixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "Kicksound.ogg");

    /// <summary>把夹具拷成内容包里的某个音频文件。</summary>
    private static void PutAudio(string packRoot, string relPath)
    {
        string dest = Path.Combine(packRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(OggFixture, dest, overwrite: true);
    }


    public AudioChangesMergerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "audioc_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* 清理失败无所谓 */ }
    }

    private OutputTree MakeTree(bool withXact = true)
    {
        string inRoot = Path.Combine(_root, "in");
        string outRoot = Path.Combine(_root, "out");
        Directory.CreateDirectory(inRoot);
        Directory.CreateDirectory(outRoot);

        // XACT 清单放在**输入树**里 —— OutputTree 的构造会把输入树整份拷到输出目录，
        // 这也正是真实流程：产出树就是"原版解包树 + 补丁"。
        if (withXact)
        {
            Directory.CreateDirectory(Path.Combine(inRoot, "XACT"));
            File.WriteAllText(Path.Combine(inRoot, "XACT", "Wave Bank.xwb.config"),
                new JObject
                {
                    ["Prefix"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                    ["DataOffset"] = 3,
                    ["OriginalSize"] = 3,
                    ["Entries"] = new JArray(),
                }.ToString());
            File.WriteAllText(Path.Combine(inRoot, "XACT", "Sound Bank.xsb.json"),
                new JObject { ["Sounds"] = new JArray(), ["Variations"] = new JArray() }.ToString());
            // 类别表：索引由**顺序**决定，和原版 FarmingSounds.xgs 一致
            File.WriteAllText(Path.Combine(inRoot, "XACT", "FarmerSounds.xgs.json"),
                new JObject
                {
                    ["Categories"] = new JArray(
                        new JObject { ["Name"] = "Global" },    // 0
                        new JObject { ["Name"] = "Default" },   // 1
                        new JObject { ["Name"] = "Music" },     // 2
                        new JObject { ["Name"] = "Sound" },     // 3
                        new JObject { ["Name"] = "Ambient" }),  // 4
                }.ToString());
        }

        return new OutputTree(inRoot, outRoot);
    }

    private static JObject PatchFor(string cueId, string relPath) => new()
    {
        ["Entries"] = new JObject
        {
            [cueId] = new JObject
            {
                ["ID"] = cueId,
                ["Category"] = "Music",
                ["FilePaths"] = new JArray($"{{{{AbsoluteFilePath: {relPath}}}}}"),
                ["Looped"] = true,
                ["StreamedVorbis"] = true,
            },
        },
    };

    [Fact]
    public void 名字与原版那条波形撞上时改名_不盖掉别人的字节()
    {
        // ⚠ 必须先把这个「原版波形」摆进**输入树**、再建树：OutputTree 只在产出目录为空时整树拷贝，
        //   建完树再往输入树里塞文件，产出树里是看不到的（第一版就摆错了，于是这条测试假绿）。
        string vanilla = Path.Combine(_root, "in", "XACT", "Wave Bank", "Music", "Kicksound.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(vanilla)!);
        File.WriteAllBytes(vanilla, new byte[] { 1, 2, 3 });

        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        PutAudio(packRoot, "assets/Music/Kicksound.ogg");       // 与上面那份同名

        Assert.True(AudioChangesMerger.Apply(tree, packRoot,
            PatchFor("CueK", "assets/Music/Kicksound.ogg"), AudioChangesMerger.AssetName).Success);

        Flush();
        JObject manifest = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank.xwb.config")));
        string fileName = ((JArray)manifest["Entries"]!)[0]!["FileName"]!.Value<string>();
        Assert.EndsWith("Kicksound_2.wav", fileName);
        Assert.True(File.Exists(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank", "Music", "Kicksound_2.wav")));

        // 原版那份**一个字节都没动**
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(vanilla));
    }

    [Fact]
    public void 一条音频进库_波形编成ADPCM落进树_两个清单各加一条_且不产出该资产()
    {
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        PutAudio(packRoot, "assets/Music/Theme.ogg");

        MergeOutcome outcome = AudioChangesMerger.Apply(
            tree, packRoot,
            PatchFor("SomeMod_Theme", "assets/Music/Theme.ogg"),
            AudioChangesMerger.AssetName);

        Assert.True(outcome.Success, outcome.FailureReason);

        // ① 波形编成 MS-ADPCM 的 wav 落进树（**不是**原来的 ogg —— 打包器那条 ogg 路会解成 PCM）
        Flush();
        string wavPath = Path.Combine(tree.OutputRoot, "XACT", "Wave Bank", "Music", "Theme.wav");
        Assert.True(File.Exists(wavPath));
        Assert.False(File.Exists(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank", "Music", "Theme.ogg")));

        // ② 波形清单加了一条：Offset = -1（追加），长度/格式/循环都按原版 ADPCM 条目的形态写好。
        //    夹具是 2 声道 24 kHz ⇒ 格式字 = (ADPCM, 2ch, 24000, 块对齐字段 48) = 0x180BB80A。
        JObject manifest = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank.xwb.config")));
        var entries = (JArray)manifest["Entries"]!;
        Assert.Single(entries);
        Assert.Equal(-1, entries[0]!["Offset"]!.Value<int>());
        Assert.Contains("Music", entries[0]!["FileName"]!.Value<string>());
        Assert.EndsWith(".wav", entries[0]!["FileName"]!.Value<string>());
        Assert.Equal(0x180BB80Au, entries[0]!["Format"]!.Value<uint>());

        int length = entries[0]!["Length"]!.Value<int>();
        int loop = entries[0]!["LoopLength"]!.Value<int>();
        Assert.Equal(0, length % 140);                                   // 整块
        Assert.Equal(length / 140 * AdpcmMusic.SamplesPerBlock, loop);   // 循环长度单位 = 采样数
        Assert.Equal((uint)(loop * 16), entries[0]!["FlagsAndDuration"]!.Value<uint>());
        Assert.Equal(0, entries[0]!["LoopOffset"]!.Value<int>());

        // 写进清单的字节数必须是 wav 里 data 块的字节数 —— 不然打包器会**重编码**（就不再是我们编的）
        byte[] wav = File.ReadAllBytes(wavPath);
        Assert.Equal(90 + length, wav.Length);       // 12 + fmt(8+50) + fact(8+4) + data(8)

        // ③ 声库加了 cue + 变体，变体指向第 0 个波形（原有 0 条）
        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        Assert.Single((JArray)bank["Sounds"]!);
        Assert.Equal("SomeMod_Theme", ((JArray)bank["Sounds"]!)[0]!["Cue"]!.Value<string>());
        Assert.Equal(0, ((JArray)bank["Sounds"]!)[0]!["FileOffset"]!.Value<int>());

        // ⚠ CategoryId 必须按 .xgs 的**类别表顺序**解析：Music 在表里是第 2 个。
        //   写死 0 的话会落到 Global —— 原版 .xsb 里从没出现过 0，音乐走错混音参数。
        Assert.Equal(2, ((JArray)bank["Sounds"]!)[0]!["CategoryId"]!.Value<int>());

        // ⚠ Sound.Volume 的 0 不是 0 dB，而是 XACT 曲线最底端（MonoGame 解成 ≈1.6e-5，等同静音）。
        //   循环音乐走简单 Cue 这条路时，最终音量会乘上这个字节；原版音乐都用 180（≈1.004）。
        Assert.Equal(180, ((JArray)bank["Sounds"]!)[0]!["VolumeFlag"]!.Value<int>());

        var variations = (JArray)bank["Variations"]!;
        Assert.Single(variations);
        Assert.Equal(0, variations[0]!["TrackIndex"]!.Value<int>());
        Assert.Equal(0, variations[0]!["WaveBankIndex"]!.Value<int>());

        // ④ **不**产出 Data/AudioChanges —— 写出去的话，游戏启动时会拿加载不到的
        //    FilePaths 把这条新 cue 覆盖成哑的
        Assert.False(File.Exists(Path.Combine(tree.OutputRoot, "Data", "AudioChanges.json")));
        Assert.Null(outcome.Data);
    }

    [Fact]
    public void 多FilePaths会变成同一cue的多个变体()
    {
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        PutAudio(packRoot, "assets/Music/VariantA.ogg");
        PutAudio(packRoot, "assets/Music/VariantB.ogg");

        var patch = new JObject
        {
            ["Entries"] = new JObject
            {
                ["MultiCue"] = new JObject
                {
                    ["ID"] = "MultiCue",
                    ["Category"] = "Music",
                    ["FilePaths"] = new JArray(
                        "{{AbsoluteFilePath: assets/Music/VariantA.ogg}}",
                        "{{AbsoluteFilePath: assets/Music/VariantB.ogg}}"),
                    ["Looped"] = true,
                },
            },
        };

        Assert.True(AudioChangesMerger.Apply(tree, packRoot, patch, AudioChangesMerger.AssetName).Success);
        Flush();

        JObject manifest = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank.xwb.config")));
        Assert.Equal(2, ((JArray)manifest["Entries"]!).Count);

        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        JArray sounds = (JArray)bank["Sounds"]!;
        Assert.Single(sounds);                               // 一个 cue
        Assert.Equal(180, sounds[0]!["VolumeFlag"]!.Value<int>());
        JArray variations = (JArray)bank["Variations"]!;
        Assert.Equal(2, variations.Count);                   // 两个波形变体
        Assert.Equal(new[] { 0, 1 }, variations.Select(v => v["TrackIndex"]!.Value<int>()).ToArray());
        Assert.Equal(new[] { "MultiCue", "MultiCue" },
            variations.Select(v => v["Cue"]!.Value<string>()).ToArray());
        Assert.Contains("MultiCue", ((JArray)bank["LoopedCues"]!).Select(t => t.Value<string>()));
    }

    /// <summary>
    /// ★ 同名 cue **只留第一条**：合并器是逐条补丁被调用的，而 `EditData Data/AudioChanges`
    /// 常有多条（实测 `Crystal Island` 8 条）—— 第二次进来时同一 cue 已经在声库里了。
    /// 不加这道闸就会再加一遍，打包器重放 `LoopedCues` 时抛
    /// 「新增的循环 Cue「X」与已有的 Cue 重名」⇒ **整份声库打不出包**（音乐全没了）。
    /// </summary>
    [Fact]
    public void 同名cue重复注入时只留第一条()
    {
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        PutAudio(packRoot, "assets/Music/Theme.ogg");

        Assert.True(AudioChangesMerger.Apply(tree, packRoot,
            PatchFor("PIRATE_THEME", "assets/Music/Theme.ogg"), AudioChangesMerger.AssetName).Success);
        MergeOutcome second = AudioChangesMerger.Apply(tree, packRoot,
            PatchFor("PIRATE_THEME", "assets/Music/Theme.ogg"), AudioChangesMerger.AssetName);

        Assert.True(second.Success, second.FailureReason);
        Assert.Contains(second.Notes, n => n.Contains("已在声库里"));

        Flush();
        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        Assert.Single((JArray)bank["Sounds"]!);          // 声库里只有一条
        Assert.Single((JArray)bank["Variations"]!);
    }

    [Fact]
    public void 两条音频_第二条的TrackIndex接着第一条()
    {
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        PutAudio(packRoot, "assets/Music/A.ogg");
        PutAudio(packRoot, "assets/Music/B.ogg");

        JObject patch = PatchFor("CueA", "assets/Music/A.ogg");
        ((JObject)patch["Entries"]!)["CueB"] = new JObject
        {
            ["ID"] = "CueB",
            ["FilePaths"] = new JArray("{{AbsoluteFilePath: assets/Music/B.ogg}}"),
        };

        Assert.True(AudioChangesMerger.Apply(tree, packRoot, patch, AudioChangesMerger.AssetName).Success);
Flush();

        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        var variations = (JArray)bank["Variations"]!;
        Assert.Equal(2, variations.Count);
        Assert.Equal(0, variations[0]!["TrackIndex"]!.Value<int>());
        Assert.Equal(1, variations[1]!["TrackIndex"]!.Value<int>());
    }

    [Fact]
    public void 产出树里没有XACT清单时_整条失败而不是写出半成品()
    {
        OutputTree tree = _tree = MakeTree(withXact: false);
        MergeOutcome outcome = AudioChangesMerger.Apply(
            tree, _root, PatchFor("CueX", "whatever.ogg"), AudioChangesMerger.AssetName);

        Assert.False(outcome.Success);
        Assert.Contains("不在产出树里", outcome.FailureReason);
    }

    [Fact]
    public void 源文件不存在时_跳过该条()
    {
        OutputTree tree = _tree = MakeTree();
        MergeOutcome outcome = AudioChangesMerger.Apply(
            tree, Path.Combine(_root, "nope"),
            PatchFor("CueX", "assets/Music/Missing.ogg"), AudioChangesMerger.AssetName);

        Assert.False(outcome.Success);
        Assert.Contains("没有一条能落", outcome.FailureReason);
    }

    [Fact]
    public void 值不是AbsoluteFilePath时不猜()
    {
        OutputTree tree = _tree = MakeTree();
        JObject patch = new()
        {
            ["Entries"] = new JObject
            {
                ["CueX"] = new JObject { ["ID"] = "CueX", ["FilePaths"] = new JArray("some/relative.ogg") },
            },
        };

        MergeOutcome outcome = AudioChangesMerger.Apply(tree, _root, patch, AudioChangesMerger.AssetName);

        Assert.False(outcome.Success);
        Assert.Contains("没有一条能落", outcome.FailureReason);
    }

    [Fact]
    public void FilePaths全部不存在时不硬写空cue()
    {
        OutputTree tree = _tree = MakeTree();
        JObject patch = new()
        {
            ["Entries"] = new JObject
            {
                ["CueX"] = new JObject
                {
                    ["ID"] = "CueX",
                    ["FilePaths"] = new JArray(
                        "{{AbsoluteFilePath: a.ogg}}", "{{AbsoluteFilePath: b.ogg}}"),
                },
            },
        };

        MergeOutcome outcome = AudioChangesMerger.Apply(tree, _root, patch, AudioChangesMerger.AssetName);

        Assert.False(outcome.Success);
        Assert.Contains("没有一条能落", outcome.FailureReason);
    }

    [Fact]
    public void Category为空时按游戏的默认走Default()
    {
        // CP 的语义照抄 AudioCueModificationManager：Category 为空 ⇒ GetCategoryIndex("Default")
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        Directory.CreateDirectory(Path.Combine(packRoot, "assets", "Music"));
        PutAudio(packRoot, "assets/Music/C.ogg");

        JObject patch = new()
        {
            ["Entries"] = new JObject
            {
                ["CueC"] = new JObject
                {
                    ["ID"] = "CueC",
                    ["FilePaths"] = new JArray("{{AbsoluteFilePath: assets/Music/C.ogg}}"),
                },
            },
        };

        Assert.True(AudioChangesMerger.Apply(tree, packRoot, patch, AudioChangesMerger.AssetName).Success);
Flush();

        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        Assert.Equal(1, ((JArray)bank["Sounds"]!)[0]!["CategoryId"]!.Value<int>());   // Default
    }

    [Fact]
    public void Category不在类别表里时跳过该条_不猜一个索引()
    {
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        Directory.CreateDirectory(Path.Combine(packRoot, "assets", "Music"));
        PutAudio(packRoot, "assets/Music/D.ogg");

        JObject patch = new()
        {
            ["Entries"] = new JObject
            {
                ["CueD"] = new JObject
                {
                    ["ID"] = "CueD",
                    ["Category"] = "NoSuchCategory",
                    ["FilePaths"] = new JArray("{{AbsoluteFilePath: assets/Music/D.ogg}}"),
                },
            },
        };

        MergeOutcome outcome = AudioChangesMerger.Apply(tree, packRoot, patch, AudioChangesMerger.AssetName);

        Assert.False(outcome.Success);
        Assert.Contains("不在 .xgs 的类别表里", outcome.FailureReason);
        // 不能留下孤儿：文件没搬、波形清单没动
        Assert.False(File.Exists(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank", "Music", "D.ogg")));
        JObject manifest = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Wave Bank.xwb.config")));
        Assert.Empty((JArray)manifest["Entries"]!);
    }

    [Fact]
    public void Looped为真时登记进LoopedCues()
    {
        // CP 的 `Looped` 决定写哪种形态：true 走「简单 Cue → 带 Clip 的 Sound → LoopCount=255」
        // （原版音乐那样），false 走「复杂 Cue + 变体类型 0」。
        // 少这一步音乐就播一遍就停 —— MonoGame 只有走 XactClip 那条路才认 loopCount。
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        Directory.CreateDirectory(Path.Combine(packRoot, "assets", "Music"));
        PutAudio(packRoot, "assets/Music/L.ogg");

        MergeOutcome outcome = AudioChangesMerger.Apply(
            tree, packRoot, PatchFor("CueL", "assets/Music/L.ogg"), AudioChangesMerger.AssetName);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();
        Assert.DoesNotContain(outcome.Notes, n => n.Contains("表达不了循环"));   // 老警告该没了

        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        Assert.Equal(new[] { "CueL" },
            ((JArray)bank["LoopedCues"]!).Select(t => t.Value<string>()).ToArray());
    }

    [Fact]
    public void Looped为假或不写时不登记循环()
    {
        OutputTree tree = _tree = MakeTree();
        string packRoot = Path.Combine(_root, "pack");
        Directory.CreateDirectory(Path.Combine(packRoot, "assets", "Music"));
        PutAudio(packRoot, "assets/Music/M.ogg");

        JObject patch = new()
        {
            ["Entries"] = new JObject
            {
                ["CueM"] = new JObject
                {
                    ["ID"] = "CueM",
                    ["Looped"] = false,
                    ["FilePaths"] = new JArray("{{AbsoluteFilePath: assets/Music/M.ogg}}"),
                },
            },
        };

        MergeOutcome outcome = AudioChangesMerger.Apply(tree, packRoot, patch, AudioChangesMerger.AssetName);

        Assert.True(outcome.Success, outcome.FailureReason);
Flush();

        JObject bank = JObject.Parse(
            File.ReadAllText(Path.Combine(tree.OutputRoot, "XACT", "Sound Bank.xsb.json")));
        Assert.Empty((JArray)bank["LoopedCues"]!);
    }

    [Fact]
    public void 只认这个目标_别的EditData不拦()
    {
        Assert.True(AudioChangesMerger.CanHandle(new Cp.ChangePatch
        {
            Action = "EditData",
            Target = "Data/AudioChanges",
        }));
        Assert.False(AudioChangesMerger.CanHandle(new Cp.ChangePatch
        {
            Action = "EditData",
            Target = "Data/Objects",
        }));
        Assert.False(AudioChangesMerger.CanHandle(new Cp.ChangePatch
        {
            Action = "Load",
            Target = "Data/AudioChanges",
        }));
    }
}
