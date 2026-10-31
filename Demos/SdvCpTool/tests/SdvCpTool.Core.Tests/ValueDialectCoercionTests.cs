using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 结构化标量的**值方言**转换：CP 允许写 <c>"0, -32"</c>，XNB 打包器只认
/// <c>{"X":0.0,"Y":-32.0}</c> —— 后者不换就**打不出包**。
/// <para>
/// 受控实测（Cornucopia「聚宝盆 制作」，同参数）：换之前该小树打包
/// 成功 93 / 失败 10（全是 <c>Data/Machines</c> 及其 8 个语言变体）；手工换掉那 80 处后
/// 成功 103 / 失败 0。所以这组测试守的是「真能回包」而不是「看着更整齐」。
/// </para>
/// </summary>
public class ValueDialectCoercionTests
{
    private static JToken Coerce(string asset, string json)
    {
        JToken data = JToken.Parse(json);
        ValueDialectCoercion.CoerceAsset(asset, data);
        return data;
    }

    /// <summary>一层字段：<c>Data/Buildings</c> 的 <c>AnimalDoor</c> 是 <c>Rectangle</c>。</summary>
    [Fact]
    public void 矩形字符串换成四分量对象()
    {
        JToken data = Coerce("Data/Buildings", """{ "Coop": { "AnimalDoor": "28, 116, 16, 32" } }""");

        JToken boxed = data["Coop"]!["AnimalDoor"]!;
        Assert.Equal(JTokenType.Object, boxed.Type);
        Assert.Equal(28.0, boxed["X"]!.Value<double>());
        Assert.Equal(116.0, boxed["Y"]!.Value<double>());
        Assert.Equal(16.0, boxed["Width"]!.Value<double>());
        Assert.Equal(32.0, boxed["Height"]!.Value<double>());
    }

    /// <summary>
    /// ★ 嵌套层：<c>DrawLayers[]</c> 的元素内才是 <c>DrawPosition</c>。
    /// 这正是 SVE 回包失败的 6 处；只到「资产元素的一层字段」够不着。
    /// </summary>
    [Fact]
    public void 嵌套层的结构化标量也换得到()
    {
        JToken data = Coerce("Data/Buildings",
            """{ "Coop": { "DrawLayers": [ { "Id": "a", "DrawPosition": "28, 116" } ] } }""");

        JToken boxed = data["Coop"]!["DrawLayers"]![0]!["DrawPosition"]!;
        Assert.Equal(JTokenType.Object, boxed.Type);
        Assert.Equal(28.0, boxed["X"]!.Value<double>());
        Assert.Equal(116.0, boxed["Y"]!.Value<double>());
    }

    /// <summary>
    /// 分量数与声明类型不符时**不动** —— 那是另一类问题，硬凑只会写出更难查的东西。
    /// （两个数给 <c>Rectangle</c>：既不是合法的 Rectangle，也不该被当成 Vector2。）
    /// </summary>
    [Fact]
    public void 分量数不符时原样不动()
    {
        JToken data = Coerce("Data/Buildings", """{ "Coop": { "AnimalDoor": "28, 116" } }""");

        Assert.Equal("28, 116", data["Coop"]!["AnimalDoor"]!.Value<string>());
    }

    /// <summary>不是数字的字符串不动（比如本来就是个名字）。</summary>
    [Fact]
    public void 非数字字符串不动()
    {
        JToken data = Coerce("Data/Buildings", """{ "Coop": { "AnimalDoor": "yes, please" } }""");

        Assert.Equal("yes, please", data["Coop"]!["AnimalDoor"]!.Value<string>());
    }

    /// <summary>已经是对象的重复过一遍不该被改坏。</summary>
    [Fact]
    public void 已经是对象的不动()
    {
        JToken data = Coerce("Data/Buildings",
            """{ "Coop": { "AnimalDoor": { "X": 1, "Y": 2, "Width": 3, "Height": 4 } } }""");

        Assert.Equal(1.0, data["Coop"]!["AnimalDoor"]!["X"]!.Value<double>());
        Assert.Equal(4.0, data["Coop"]!["AnimalDoor"]!["Height"]!.Value<double>());
    }

    /// <summary>模型里没有的资产（别的 mod 的自定义资产）一律不表态。</summary>
    [Fact]
    public void 认不出的资产不动()
    {
        JToken data = Coerce("Mods/Someone/Thing", """{ "a": { "PositionOffset": "0, -32" } }""");

        Assert.Equal("0, -32", data["a"]!["PositionOffset"]!.Value<string>());
    }

    /// <summary>
    /// 非结构化字段上长得像坐标的字符串**不能**动：<c>DrawLayers</c> 声明为 <c>List</c>，
    /// 写成字符串是**形状**问题（由 <see cref="GameDataValidator"/> 报），不是值方言问题。
    /// </summary>
    [Fact]
    public void 非结构化字段上的坐标样式字符串不动()
    {
        JToken data = Coerce("Data/Buildings", """{ "Coop": { "DrawLayers": "1, 2" } }""");

        Assert.Equal("1, 2", data["Coop"]!["DrawLayers"]!.Value<string>());
    }

    /// <summary>整份资产（含语言变体要用的同一份数据）换一次即可 —— 调用方是落盘前那一个漏斗。</summary>
    [Fact]
    public void 整份资产一次换完()
    {
        JToken data = Coerce("Data/Machines",
            """
            {
              "(BC)One": { "LoadEffects": [ { "TemporarySprites": [ { "PositionOffset": "0, -32" } ] } ] },
              "(BC)Two": { "LoadEffects": [ { "TemporarySprites": [ { "PositionOffset": "4, 8" } ] } ] }
            }
            """);

        Assert.Equal(-32.0, data["(BC)One"]!["LoadEffects"]![0]!["TemporarySprites"]![0]!["PositionOffset"]!["Y"]!.Value<double>());
        Assert.Equal(4.0, data["(BC)Two"]!["LoadEffects"]![0]!["TemporarySprites"]![0]!["PositionOffset"]!["X"]!.Value<double>());
    }

    /// <summary>
    /// ★ **整数值写成小数记号**：<c>Data/Objects</c> 里 <c>Buffs[].IconSpriteIndex</c> 写的是
    /// <c>0.0</c>（<c>ObjectBuffData.IconSpriteIndex</c> 声明为 <c>int</c>），打包器直接报
    /// <c>Input string '0.0' is not a valid integer</c> ⇒ 连 5 个语言变体共 **6 个文件打不出包**
    /// （那是核心资产）。<c>0.0</c> 与 <c>0</c> 是**同一个数** ⇒ 换成整数写法不改取值。
    /// </summary>
    [Fact]
    public void 整数字段上的小数记号换成整数写法()
    {
        JToken data = Coerce("Data/Objects",
            """{ "Item": { "Buffs": [ { "Id": "x", "IconSpriteIndex": 0.0, "Duration": 600 } ] } }""");

        JToken sprite = data["Item"]!["Buffs"]![0]!["IconSpriteIndex"]!;
        Assert.Equal(JTokenType.Integer, sprite.Type);
        Assert.Equal(0, sprite.Value<int>());
        Assert.Equal(JTokenType.Integer, data["Item"]!["Buffs"]![0]!["Duration"]!.Type);   // 本来就是整数写法：不动
    }

    /// <summary>负数也一样：<c>-2.0</c> 与 <c>-2</c> 是同一个数。</summary>
    [Fact]
    public void 负的整数值同样换写法()
    {
        JToken data = Coerce("Data/Objects", """{ "Item": { "Buffs": [ { "IconSpriteIndex": -2.0 } ] } }""");

        Assert.Equal(-2, data["Item"]!["Buffs"]![0]!["IconSpriteIndex"]!.Value<int>());
    }

    /// <summary>
    /// ★ **带小数部分的值 ⇒ 删掉那个字段并报出来**（2026-10-13 改的口径）。
    /// <para>
    /// 以前写的是"一律不动，属于已知、且不是我们的账" —— 那句话只算了**这一条**的账，
    /// 没算**整份资产**的账：打包器对 <c>Precision: 0.5</c> 直接报
    /// <c>Input string '0.5' is not a valid integer</c> ⇒ <c>Data/Weapons</c> 连 4 个语言变体
    /// 一起打不出包；同类的 <c>Data/Characters</c> 更狠 —— 那棵树里有
    /// **221 个 mod 新增的 NPC 定义**，覆盖式装机下整份退回原版。
    /// </para>
    /// <para>
    /// 仍然**不改成整数**（<c>0.5</c> 是 0 还是 1 只有作者知道 ⇒ 猜就是改取值），
    /// 但也不静默：删了哪个字段逐条进报告。
    /// </para>
    /// </summary>
    [Fact]
    public void 带小数部分的整数值删掉字段并报出来()
    {
        JToken data = JToken.Parse("""{ "QiBlade": { "Precision": 0.5, "Speed": 3 } }""");
        List<string> notes = ValueDialectCoercion.CoerceAsset("Data/Weapons", data);

        Assert.Null(data["QiBlade"]!["Precision"]);                 // 删了
        Assert.Equal(3, data["QiBlade"]!["Speed"]!.Value<int>());   // 别的字段照旧
        Assert.Contains(notes, n => n.Contains("Precision") && n.Contains("整数"));
    }

    /// <summary>枚举字段上写布尔 ⇒ 删字段 + 报（打包器 <c>Unexpected token Boolean when parsing enum</c>）。</summary>
    [Fact]
    public void 枚举字段上的布尔删掉字段并报出来()
    {
        JToken data = JToken.Parse(
            """{ "Antiunn": { "EndSlideShow": true, "Manner": "Polite" } }""");
        List<string> notes = ValueDialectCoercion.CoerceAsset("Data/Characters", data);

        Assert.Null(data["Antiunn"]!["EndSlideShow"]);                        // 删了
        Assert.Equal("Polite", data["Antiunn"]!["Manner"]!.Value<string>());  // 枚举的**字符串**写法不动
        Assert.Contains(notes, n => n.Contains("EndSlideShow") && n.Contains("枚举"));
    }

    /// <summary>数字串（打包器认）与真整数都不动 —— 只有**装不进去**的才删。</summary>
    [Fact]
    public void 整数字段上的数字串与整数都不动()
    {
        JToken data = JToken.Parse("""{ "QiBlade": { "Precision": "3", "Speed": 7 } }""");

        Assert.Empty(ValueDialectCoercion.CoerceAsset("Data/Weapons", data));
        Assert.Equal("3", data["QiBlade"]!["Precision"]!.Value<string>());
        Assert.Equal(7, data["QiBlade"]!["Speed"]!.Value<int>());
    }

    /// <summary>
    /// ★ **浮点字段不碰**：<c>WeaponData.CritChance</c> 声明为 <c>float</c>，
    /// 原版满树都是 <c>0.0</c> 这种写法 —— 没有那张整数表就分不清，
    /// 一刀切会把所有浮点字段的写法都改掉（树里几千个文件的无谓差异）。
    /// </summary>
    [Fact]
    public void 浮点字段上的小数记号不动()
    {
        JToken data = Coerce("Data/Weapons", """{ "QiBlade": { "CritChance": 0.0, "Knockback": 1.0 } }""");

        Assert.Equal(JTokenType.Float, data["QiBlade"]!["CritChance"]!.Type);
        Assert.Equal(0.0, data["QiBlade"]!["CritChance"]!.Value<double>());
        Assert.Equal(JTokenType.Float, data["QiBlade"]!["Knockback"]!.Type);
    }

    /// <summary>字段名大小写随 mod 写：打包器与游戏都不区分大小写，这里也得照取。</summary>
    [Fact]
    public void 字段名大小写不敏感()
    {
        JToken data = Coerce("Data/Objects", """{ "Item": { "Buffs": [ { "iconspriteindex": 1.0 } ] } }""");

        Assert.Equal(1, data["Item"]!["Buffs"]![0]!["iconspriteindex"]!.Value<int>());
    }

    /// <summary>模型里没有的资产照样不表态。</summary>
    [Fact]
    public void 认不出的资产上整数也不表态()
    {
        JToken data = Coerce("Mods/Someone/Thing", """{ "a": { "IconSpriteIndex": 0.0 } }""");

        Assert.Equal(JTokenType.Float, data["a"]!["IconSpriteIndex"]!.Type);
    }
}
