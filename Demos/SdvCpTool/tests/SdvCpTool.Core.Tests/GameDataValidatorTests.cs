using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 模型驱动的产出校验：按 <see cref="GameDataSchema"/>（从游戏源码生成）核对容器形状。
/// <para>
/// 与「拿原版数据反推形状」那套的区别：那个只能看原版**恰好用过**的形状、噪音大到不可用
/// （实测 9145 处假阳性）；这个有**真类型**，所以判定是确定的、不依赖语料。
/// </para>
/// </summary>
public class GameDataValidatorTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvgd_" + Guid.NewGuid().ToString("N"));

    public GameDataValidatorTests() => Directory.CreateDirectory(_out);

    public void Dispose() => Directory.Delete(_out, recursive: true);

    private void Write(string rel, string json)
    {
        string full = Path.Combine(_out, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    /// <summary>
    /// 实测踩过的那一类：`LocationData.Fish` 是 `List&lt;SpawnFishData&gt;`，
    /// 却被合成了字典 —— 游戏按 List 反序列化会失败。
    /// </summary>
    [Fact]
    public void 该是数组却写成对象时报出来()
    {
        Write("Data/Locations.json", """{ "Town": { "Fish": { "a": { "Id": "a" } } } }""");

        GameDataValidator.Finding f = Assert.Single(GameDataValidator.Run(_out).Findings);
        Assert.Equal("Data/Locations", f.Asset);
        Assert.Equal("Town", f.Entry);
        Assert.Equal("Fish", f.Field);
        Assert.Equal(FieldKind.Array, f.Expected);
        Assert.Equal("object", f.Actual);
    }

    /// <summary>
    /// 结构化标量（<c>Vector2</c>/<c>Vector3</c>/<c>Rectangle</c>）**必须写成对象**。
    /// <para>
    /// 写成字符串时**打包器直接报** <c>Error converting value "28, 116" to type 'Vector2'</c>
    /// ⇒ 该资产**打不出 XNB**。实测 SVE 的 <c>Data/Buildings</c>/<c>Fences</c>/<c>FarmAnimals</c>
    /// 就是这么回包失败的。原版都写对象（<c>Data/Buildings</c> 的 <c>DrawPosition</c> = <c>{"X":32.0,"Y":96.0}</c>）。
    /// </para>
    /// <para>
    /// 这是**一层**字段。嵌套层（<c>DrawLayers[]</c> 元素内）由
    /// <see cref="嵌套层的结构化标量也查得到"/> 覆盖 —— 那一层才是 SVE 真正漏掉的地方。
    /// </para>
    /// </summary>
    [Fact]
    public void 结构化标量写成字符串时报出来()
    {
        Write("Data/Buildings.json",
            """{ "Coop": { "AnimalDoor": "28, 116" } }""");

        GameDataValidator.Finding f = Assert.Single(GameDataValidator.Run(_out).Findings);
        Assert.Equal("Data/Buildings", f.Asset);
        Assert.Equal("AnimalDoor", f.Field);
        Assert.Equal(FieldKind.Struct, f.Expected);
        Assert.Equal("scalar", f.Actual);
    }

    /// <summary>
    /// ★ <b>嵌套层</b>：<c>Data/Buildings</c> 的 <c>DrawLayers</c> 是
    /// <c>List&lt;BuildingDrawLayer&gt;</c>，而 <c>BuildingDrawLayer.DrawPosition</c> 是 <c>Vector2</c>。
    /// <para>
    /// 这正是 SVE 那 6 处回包失败的位置（写成 <c>"28, 116"</c>）—— 只按资产列一层字段的表
    /// <b>够不着它</b>，所以生成器改成「资产→元素类」+「类→字段」两张表，校验器沿类型图下钻。
    /// </para>
    /// </summary>
    [Fact]
    public void 嵌套层的结构化标量也查得到()
    {
        Write("Data/Buildings.json",
            """{ "Coop": { "DrawLayers": [ { "Id": "a", "DrawPosition": "28, 116" } ] } }""");

        GameDataValidator.Finding f = Assert.Single(GameDataValidator.Run(_out).Findings);
        Assert.Equal("Data/Buildings", f.Asset);
        Assert.Equal("Coop.DrawLayers[0]", f.Entry);   // 路径要能指到**第几个元素**
        Assert.Equal("DrawPosition", f.Field);
        Assert.Equal(FieldKind.Struct, f.Expected);
        Assert.Equal("scalar", f.Actual);
    }

    /// <summary>嵌套层写成对象就正常 —— 不能一见到数组元素就报。</summary>
    [Fact]
    public void 嵌套层写的对时不报()
    {
        Write("Data/Buildings.json",
            """{ "Coop": { "DrawLayers": [ { "DrawPosition": { "X": 1, "Y": 2 } } ] } }""");

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }

    /// <summary>
    /// 数组里混着**不是对象**的元素时不炸 —— 那种位置没有可下钻的字段，
    /// 但也不能因为「元素不是对象」就在数组本身的形状上误报。
    /// </summary>
    [Fact]
    public void 嵌套层元素类型不对时不误报上一层()
    {
        Write("Data/Buildings.json", """{ "Coop": { "DrawLayers": [ 1, "x" ] } }""");

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }

    /// <summary>写成对象就正常。</summary>
    [Fact]
    public void 结构化标量写成对象时不报()
    {
        Write("Data/Buildings.json", """{ "Coop": { "AnimalDoor": { "X": 1, "Y": 2 } } }""");

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }

    /// <summary>同一份资产里 `FishAreas` 恰恰**该**是字典 —— 两个字段类型不同，不能一概而论。</summary>
    [Fact]
    public void 该是对象却写成数组时报出来()
    {
        Write("Data/Locations.json", """{ "Town": { "FishAreas": [ "x" ] } }""");

        GameDataValidator.Finding f = Assert.Single(GameDataValidator.Run(_out).Findings);
        Assert.Equal("FishAreas", f.Field);
        Assert.Equal(FieldKind.Map, f.Expected);
    }

    [Fact]
    public void 形状正确时不报()
    {
        Write("Data/Locations.json", """
        { "Town": { "Fish": [ { "Id": "a" } ], "FishAreas": { "River": {} } } }
        """);

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }

    /// <summary>`null` 是「没设」，不是「设错形状」—— 不报，否则满地噪音。</summary>
    [Fact]
    public void 值为null时不报()
    {
        Write("Data/Locations.json", """{ "Town": { "Fish": null, "FishAreas": null } }""");

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }

    /// <summary>模型不认得的资产不查 —— 没有真类型就别猜。</summary>
    [Fact]
    public void 模型不认得的资产跳过()
    {
        Write("Data/NotInSchema.json", """{ "a": { "Whatever": [ 1 ] } }""");

        GameDataValidator.Audit a = GameDataValidator.Run(_out);

        Assert.Empty(a.Findings);
        Assert.Equal(0, a.CheckedAssets);
    }

    /// <summary>根是数组的资产（如 `Data/AdditionalFarms`）也要能查。</summary>
    [Fact]
    public void 根是数组的资产也能查()
    {
        // Data/AdditionalFarms 的元素类型是 ModFarmType；挑一个 schema 里确实有的资产来验数组根这条路。
        string[] known = GameDataSchema.AssetElement.Keys.Where(k => k.StartsWith("Data/")).ToArray();
        Assert.NotEmpty(known);
    }

    /// <summary>
    /// ★ <b>语言变体也要查</b>。变体（<c>Data/Buildings.zh-CN</c>）在游戏眼里是独立资产、
    /// 与主资产同形，按主资产的模型核对才对。
    /// <para>
    /// 不去后缀的话 <c>AssetElement</c> 查不到 ⇒ 变体被**静默跳过**。实测代价：
    /// Cornucopia 的 <c>Data/Machines</c> 连 8 个变体回包全失败，而自检只报了基础资产那 8 处 ——
    /// **自检看不见的正是打包器要报的**。
    /// </para>
    /// </summary>
    [Fact]
    public void 语言变体也按主资产的模型查()
    {
        Write("Data/Buildings.zh-CN.json", """{ "Coop": { "AnimalDoor": "28, 116" } }""");

        GameDataValidator.Finding f = Assert.Single(GameDataValidator.Run(_out).Findings);
        Assert.Equal("Data/Buildings.zh-CN", f.Asset);
        Assert.Equal("AnimalDoor", f.Field);
        Assert.Equal(FieldKind.Struct, f.Expected);
    }

    /// <summary>
    /// ★ 数据里的键按**不区分大小写**取：打包器与游戏都这么匹配字段名。
    /// <para>
    /// 实测 SVE 用 <c>TargetField: [..., "ToolTips", ...]</c>（模型字段其实是 <c>Tooltips</c>）
    /// 把地图提示写成了**对象**；打包器照认这个键、按 <c>List&lt;WorldMapTooltipData&gt;</c> 校验
    /// ⇒ 回包失败。按字面查时这里**什么都没报** —— 自检看不见打包器要报的那一处。
    /// </para>
    /// </summary>
    [Fact]
    public void 字段名大小写不同也要查()
    {
        Write("Data/WorldMap.json",
            """{ "Valley": { "MapAreas": [ { "Id": "M", "ToolTips": { "Mines": { "PixelArea": {} } } } ] } }""");

        GameDataValidator.Finding f = Assert.Single(GameDataValidator.Run(_out).Findings);
        Assert.Equal("Tooltips", f.Field);          // 报的是**模型**字段名
        Assert.Equal(FieldKind.Array, f.Expected);
        Assert.Equal("object", f.Actual);
    }

    /// <summary>大小写不同但形状对就不报 —— 别把「拼写」也当成错。</summary>
    [Fact]
    public void 字段名大小写不同但形状对时不报()
    {
        Write("Data/WorldMap.json",
            """{ "Valley": { "MapAreas": [ { "Id": "M", "ToolTips": [ { "Id": "Mines" } ] } ] } }""");

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }

    /// <summary>名字里有点但不是区域码的（<c>Mods/作者.模组/…</c>）不能误判成变体。</summary>
    [Fact]
    public void 带点但不是区域码的资产不乱去后缀()
    {
        // 去掉后缀会变成 `Mods/Bouhm.NPCMapLocations` —— 那是**另一个**资产名，
        // 误判就会去查别的资产的模型（或误报）。这里只验「没被当变体切掉」。
        Write("Mods/Bouhm.NPCMapLocations/NPCs.json", """{ "a": { "Whatever": [ 1 ] } }""");

        Assert.Empty(GameDataValidator.Run(_out).Findings);
    }
}
