using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

public class WritePolicyTests
{
    private static PatchVerdict V(Verdict verdict, string action = "EditData") =>
        new() { Patch = new ChangePatch { Action = action, Target = "Data/X" }, Verdict = verdict };

    [Theory]
    [InlineData(Verdict.CreatesNewAsset)]
    [InlineData(Verdict.ConvertedNoCondition)]
    public void 无条件的EditData要写入(Verdict verdict) =>
        Assert.True(WritePolicy.ShouldWrite(V(verdict)));

    /// <summary>
    /// ★ 令牌闸的判据必须与「会不会写」**同一处**。
    /// <para>
    /// 原先那道闸只枚举 <c>Converted</c>/<c>ConvertedNoCondition</c>，于是 <c>CreatesNewAsset</c>
    /// 从旁边漏过去 —— 实测语料 **19 条**新建资产补丁把**有损猜出来的** i18n 键写进了产物
    /// （`{{i18n:kid1_intro1_Andy{{Baby1Gender}}}}`，而 `Baby1Gender` 依存档状态、判不了）。
    /// </para>
    /// </summary>
    [Fact]
    public void 令牌闸覆盖所有会写的判定()
    {
        var raw = Newtonsoft.Json.Linq.JObject.Parse(
            """{ "Entries": { "k": "{{i18n:key{{Baby1Gender}}}}" } }""");

        // 会写 + 值里有代不掉的令牌 ⇒ 拦
        var created = new PatchVerdict
        {
            Patch = new ChangePatch { Action = "EditData", Target = "Characters/Dialogue/Andy", Raw = raw },
            Verdict = Verdict.CreatesNewAsset,
        };
        Assert.True(WritePolicy.ShouldWrite(created));
        Assert.True(WritePolicy.TokenGateBlocks(created));

        // 本来就不写的判定 ⇒ 不归这道闸管（免得把更具体的原因盖掉）
        var skipped = new PatchVerdict
        {
            Patch = new ChangePatch { Action = "EditData", Target = "Characters/Dialogue/Andy", Raw = raw },
            Verdict = Verdict.NeedsReview,
        };
        Assert.False(WritePolicy.TokenGateBlocks(skipped));

        // 会写但没有代不掉的令牌 ⇒ 放行
        var clean = new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Action = "EditData", Target = "Data/X",
                Raw = Newtonsoft.Json.Linq.JObject.Parse("""{ "Entries": { "k": "v" } }"""),
            },
            Verdict = Verdict.CreatesNewAsset,
        };
        Assert.False(WritePolicy.TokenGateBlocks(clean));
    }

    /// <summary>
    /// `Data/AudioChanges` **免检**这道令牌闸：它的 `{{AbsoluteFilePath}}` 确实代不掉，
    /// 但根本不需要代 —— 落法是「ogg 并进 Wave Bank + cue 加进 Sound Bank」（见 AudioChangesMerger），
    /// 不是写数据资产。
    /// <para>
    /// ⚠ 对照必须用**字符串值**的 `Entries`：`AudioChanges` 真实形状的值是**对象**
    /// （`{"ID":…,"FilePaths":[…]}`），而 `SplitEntries` 只对字符串值判令牌 ——
    /// 拿对象当对照，两边都不会被拦，就证明不了"是按目标名豁免的"。
    /// 这里让两边 **raw 完全相同**、只有 `Target` 不同，才是一次干净的对照。
    /// </para>
    /// </summary>
    [Fact]
    public void 音频注入免检令牌闸_但换个目标仍照拦()
    {
        var raw = Newtonsoft.Json.Linq.JObject.Parse(
            """{ "Entries": { "k": "{{AbsoluteFilePath: assets/Music/a.ogg}}" } }""");

        var audio = new PatchVerdict
        {
            Patch = new ChangePatch { Action = "EditData", Target = "Data/AudioChanges", Raw = raw },
            Verdict = Verdict.Converted,
            Channel = Judging.Channel.NativeKey,
        };
        Assert.True(AudioChangesMerger.CanHandle(audio.Patch));   // 先确认豁免条件本身认得它
        Assert.True(WritePolicy.ShouldWrite(audio));          // 判定放行了
        Assert.False(WritePolicy.TokenGateBlocks(audio));     // 闸也不拦（豁免）

        // 对照一：**同一份 raw**，只有 Target 不同 ⇒ 照拦
        var otherTarget = new PatchVerdict
        {
            Patch = new ChangePatch { Action = "EditData", Target = "Data/Objects", Raw = raw },
            Verdict = Verdict.Converted,
            Channel = Judging.Channel.NativeKey,
        };
        Assert.True(WritePolicy.TokenGateBlocks(otherTarget));

        // 对照二：同一 Target，但 Action 不是 EditData ⇒ 不豁免，照拦。
        // （无条件的 Load 本来就判 Converted 且会写，所以 ShouldWrite 是真 —— 期望别写反。）
        var otherAction = new PatchVerdict
        {
            Patch = new ChangePatch { Action = "Load", Target = "Data/AudioChanges", Raw = raw },
            Verdict = Verdict.Converted,
            Channel = Judging.Channel.NativeKey,
        };
        Assert.True(WritePolicy.ShouldWrite(otherAction));
        Assert.True(WritePolicy.TokenGateBlocks(otherAction));
    }

    /// <summary>
    /// ★ EditMap 没写时**说判定阶段的话**，不要拿旧口径顶。
    /// <para>
    /// 这条以前一律印「含瓦片几何字段…本任务只做属性类改动」，可 `IsWritableMapEdit` 早就把
    /// `MapTiles`/`FromFile` 收进来了 —— 走到这里的 EditMap 另有原因（最多的是**条件化**地图改动），
    /// 而判定阶段已经把原因写清楚了。原文案让 74 个资产被记到错的原因上。
    /// </para>
    /// </summary>
    [Fact]
    public void EditMap没写时说判定阶段的真实原因()
    {
        var pv = new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Action = "EditMap", Target = "Maps/Town",
                Raw = Newtonsoft.Json.Linq.JObject.Parse("""{ "MapTiles": [ ] }"""),
            },
            Verdict = Verdict.LocalEdit,
            Reason = "条件化的地图改动在原版没有通道（图块级只有 ConditionalDoor，且不适用于此）",
        };

        Assert.False(WritePolicy.ShouldWrite(pv));
        Assert.Contains("没有通道", WritePolicy.SkipReason(pv));
    }

    [Fact]
    public void 有条件的Converted本任务不写()
    {
        var pv = V(Verdict.Converted);

        Assert.False(WritePolicy.ShouldWrite(pv));
        Assert.Contains("Task 6", WritePolicy.SkipReason(pv));
    }

    [Fact]
    public void 通道A的Converted要写入()
    {
        // Task 6：条件可翻译（Channel.Gsq）的 EditData 进写入路径。
        var pv = V(Verdict.Converted);
        pv.Channel = SdvCpTool.Core.Judging.Channel.Gsq;

        Assert.True(WritePolicy.ShouldWrite(pv));
        Assert.Null(WritePolicy.SkipReason(pv));
    }

    [Fact]
    public void Load即使新建也不写()
    {
        var pv = V(Verdict.CreatesNewAsset, "Load");

        Assert.False(WritePolicy.ShouldWrite(pv));
        Assert.NotNull(WritePolicy.SkipReason(pv));
    }

    [Fact]
    public void 无条件的Load要写入()
    {
        // JudgeLoad 给无条件 Load 判 Converted —— 它必须进写入路径（新建资产的打底）
        var pv = V(Verdict.Converted, "Load");

        Assert.True(WritePolicy.ShouldWrite(pv));
        Assert.Null(WritePolicy.SkipReason(pv));
    }

    [Fact]
    public void NeedsReview不写() =>
        Assert.False(WritePolicy.ShouldWrite(V(Verdict.NeedsReview)));

    [Fact]
    public void 要写入时没有跳过缘由() =>
        Assert.Null(WritePolicy.SkipReason(V(Verdict.ConvertedNoCondition)));

    // ---- Task 10a：图片 Load ----

    [Fact]
    public void Load的FromFile以png结尾判为图片Load()
    {
        var pv = V(Verdict.Converted, "Load");
        pv.Patch.FromFile = "Assets/LooseSprites/RSVMap.PNG";   // 大小写不敏感

        Assert.True(WritePolicy.IsImageLoad(pv.Patch));
    }

    [Fact]
    public void 数据Load不是图片Load()
    {
        var pv = V(Verdict.Converted, "Load");
        pv.Patch.FromFile = "Assets/Base.json";

        Assert.False(WritePolicy.IsImageLoad(pv.Patch));
    }

    [Fact]
    public void 非Load动作即使png也不走图片路径()
    {
        var pv = V(Verdict.Converted, "EditImage");
        pv.Patch.FromFile = "Assets/Sprites/X.png";

        Assert.False(WritePolicy.IsImageLoad(pv.Patch));
    }

    // ---- Task 11a：EditImage ----

    [Fact]
    public void 无条件的EditImage要写入()
    {
        var pv = V(Verdict.ConvertedNoCondition, "EditImage");

        Assert.True(WritePolicy.ShouldWrite(pv));
        Assert.Null(WritePolicy.SkipReason(pv));
    }

    [Fact]
    public void 有条件的EditImage不写()
    {
        // 图片层没有通用条件落点（即使通道是 Gsq/NativeKey 也不该漏过去）—— 只有折叠后无条件的才写。
        var pv = V(Verdict.Converted, "EditImage");
        pv.Channel = SdvCpTool.Core.Judging.Channel.Gsq;

        Assert.False(WritePolicy.ShouldWrite(pv));
    }

    [Fact]
    public void NoLandingPoint的EditImage不写() =>
        Assert.False(WritePolicy.ShouldWrite(V(Verdict.NoLandingPoint, "EditImage")));

    // ---- Task 4c：应用顺序 ----

    private static PatchVerdict P(string action, int priority, string target = "Data/X") =>
        new()
        {
            Patch = new ChangePatch { Action = action, Target = target, Priority = priority },
            Verdict = Verdict.ConvertedNoCondition
        };

    [Fact]
    public void Load恒排在编辑补丁之前()
    {
        // Load 与 Edit 的优先级来自不同枚举，数值不可比，Load 必须恒定打底。
        var editEarly = P("EditData", -1000, "Data/Early");
        var load = P("Load", 1000, "Data/Base");

        var order = ApplyOrder.Sort(new[] { editEarly, load }).ToList();

        Assert.Same(load, order[0]);
    }

    [Fact]
    public void 编辑补丁按Priority升序()
    {
        var late = P("EditData", 1000, "Data/Late");
        var def = P("EditData", 0, "Data/Default");
        var early = P("EditData", -1000, "Data/Early");

        var order = ApplyOrder.Sort(new[] { late, def, early }).ToList();

        Assert.Equal(new[] { early, def, late }, order);
    }

    [Fact]
    public void 同优先级的编辑补丁保持原有顺序()
    {
        // 稳定性：OrderBy 稳定，同 Priority 内不得重排。
        var a = P("EditData", 0, "Data/A");
        var b = P("EditData", 0, "Data/B");
        var c = P("EditData", 0, "Data/C");

        var order = ApplyOrder.Sort(new[] { a, b, c }).ToList();

        Assert.Equal(new[] { a, b, c }, order);
    }

    [Fact]
    public void 多条Load保持原有顺序()
    {
        var a = P("Load", 1000, "Data/A");
        var b = P("Load", -1000, "Data/B");

        var order = ApplyOrder.Sort(new[] { a, b }).ToList();

        Assert.Equal(new[] { a, b }, order);
    }

    /// <summary>
    /// ★ **个别**条目带代不掉的令牌，不该把整条补丁拦下来 —— 只丢那几条。
    /// <para>
    /// 依据 CP <c>ApplyRecords</c>：它是**逐条** <c>WarnForRecord(…)</c> 后 <c>continue</c>
    /// （<c>EditDataPatch.cs:13242+</c>），取舍粒度就是**条目**。
    /// </para>
    /// <para>
    /// <b>旧行为的代价（实机看到的）</b>：SVE 的 <c>Strings/StringsFromMaps</c> 一条补丁带
    /// <b>1037</b> 个条目，其中**只有 1 个**（`BlueMoonVineyard.7`）的值里有 <c>{{FarmName}}</c>；
    /// 整条不写 ⇒ 另外 **1036 条**全没写 ⇒ 玩家在蓝月酒庄地图上看到的是**裸键**
    /// <c>Strings\StringsFromMaps:BlueMoonVineyard.7</c>。
    /// </para>
    /// </summary>
    [Fact]
    public void 个别条目带代不掉的令牌时只丢那几条()
    {
        var mixed = new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Action = "EditData",
                Target = "Strings/StringsFromMaps",
                Raw = Newtonsoft.Json.Linq.JObject.Parse(
                    """{ "Entries": { "Good1": "好的", "Bad": "{{FarmName}} 的农场", "Good2": "也好" } }"""),
            },
            Verdict = Verdict.ConvertedNoCondition,
        };
        Assert.True(WritePolicy.ShouldWrite(mixed));
        Assert.False(WritePolicy.TokenGateBlocks(mixed));      // 有能写的 ⇒ 不整条拦

        // 全坏才整条不写
        var allBad = new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Action = "EditData",
                Target = "Strings/StringsFromMaps",
                Raw = Newtonsoft.Json.Linq.JObject.Parse("""{ "Entries": { "Bad": "{{FarmName}}" } }"""),
            },
            Verdict = Verdict.ConvertedNoCondition,
        };
        Assert.True(WritePolicy.TokenGateBlocks(allBad));
    }
}
