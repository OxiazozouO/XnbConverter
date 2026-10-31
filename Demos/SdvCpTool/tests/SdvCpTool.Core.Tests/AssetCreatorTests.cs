using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class AssetCreatorTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvac_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvaco_" + Guid.NewGuid().ToString("N"));

    public AssetCreatorTests() => Directory.CreateDirectory(_src);

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Touch(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private OutputTree Build() => _tree = new(_src, _out);

    [Fact]
    public void 优先选与数据形状相同的同类模板()
    {
        // 同目录两份 config：一份对应字典字符串，一份对应字典整数
        Touch("Data/Festivals/spring13.json", """{ "a": "b" }""");
        Touch("Data/Festivals/spring13.config", """{"Readers":[{"Type":"DictStringString"}]}""");
        Touch("Data/Festivals/Other.json", """{ "a": 1 }""");
        Touch("Data/Festivals/Other.config", """{"Readers":[{"Type":"DictIntString"}]}""");

        // 要写的数据是「字符串值的字典」→ 应选中 spring13.config
        string? picked = AssetCreator.FindConfigTemplate(
            Build(), "Data/Festivals/winter28", new JObject { ["x"] = "y" });

        Assert.EndsWith("spring13.config", picked);
    }

    [Fact]
    public void 数据是数组时选数组形状的模板()
    {
        Touch("Data/A.json", """{ "a": "b" }""");
        Touch("Data/A.config", """{"Readers":[{"Type":"DictStringString"}]}""");
        Touch("Data/B.json", """[ { "Id": "x" } ]""");
        Touch("Data/B.config", """{"Readers":[{"Type":"ListThing"}]}""");

        string? picked = AssetCreator.FindConfigTemplate(
            Build(), "Data/C", new JArray { new JObject { ["Id"] = "y" } });

        Assert.EndsWith("B.config", picked);
    }

    [Fact]
    public void 找不到形状匹配的模板时退到同目录任一份()
    {
        Touch("Data/A.json", """[ 1 ]""");
        Touch("Data/A.config", """{"Readers":[{"Type":"ListInt"}]}""");

        // 数据是对象，但没有对象形状的模板 —— 退到同目录任一份，而不是直接失败
        string? picked = AssetCreator.FindConfigTemplate(
            Build(), "Data/Z", new JObject { ["x"] = "y" });

        Assert.EndsWith("A.config", picked);
    }

    /// <summary>造一份真实形态的字典 .config（Reader 泛型带程序集限定名）。</summary>
    private static string DictConfig(string keyType, string valueType) =>
        "{\"Readers\":[{\"Type\":\"Microsoft.Xna.Framework.Content.DictionaryReader`2[["
        + keyType + ", mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],["
        + valueType + ", mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]\"}]}";

    [Fact]
    public void 字符串键数据选字符串键字典模板_而非整数键()
    {
        // 两份候选的形状完全一样（对象 + 字符串值），只有 Reader 泛型的键类型不同 ——
        // 形状判据区分不出，必须靠解析 Readers[].Type 的泛型
        Touch("Data/Achievements.json", """{ "0": "a" }""");
        Touch("Data/Achievements.config", DictConfig("System.Int32", "System.String"));
        Touch("Data/ExtraDialogue.json", """{ "Abigail": "hi" }""");
        Touch("Data/ExtraDialogue.config", DictConfig("System.String", "System.String"));

        // 要写的数据是字符串键字典（键形如 NPC 名）→ 必须选字符串键模板，否则回包器写崩
        string? picked = AssetCreator.FindConfigTemplate(
            Build(), "Data/CustomNPCExclusions",
            new JObject { ["Kiwi"] = "TownEvent", ["Undreya"] = "ALL" });

        Assert.EndsWith("ExtraDialogue.config", picked);
    }

    [Fact]
    public void 整数键数据选整数键字典模板()
    {
        Touch("Data/Achievements.json", """{ "0": "a" }""");
        Touch("Data/Achievements.config", DictConfig("System.Int32", "System.String"));
        Touch("Data/ExtraDialogue.json", """{ "Abigail": "hi" }""");
        Touch("Data/ExtraDialogue.config", DictConfig("System.String", "System.String"));

        string? picked = AssetCreator.FindConfigTemplate(
            Build(), "Data/NewIntAsset",
            new JObject { ["0"] = "x", ["1"] = "y" });

        Assert.EndsWith("Achievements.config", picked);
    }

    [Fact]
    public void 键类型一致但值类型不一致的候选被降级()
    {
        Touch("Data/A.json", """{ "a": "b" }""");
        Touch("Data/A.config", DictConfig("System.String", "System.Int32"));
        Touch("Data/B.json", """{ "a": "b" }""");
        Touch("Data/B.config", DictConfig("System.String", "System.String"));

        string? picked = AssetCreator.FindConfigTemplate(
            Build(), "Data/NewDict", new JObject { ["x"] = "y" });

        Assert.EndsWith("B.config", picked);
    }

    [Fact]
    public void 图片模板只认Texture2DReader而非同目录数据config()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.config", DictConfig("System.String", "System.String"));
        Touch("Characters/Abigail.config",
            """{"Readers":[{"Type":"Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0"}]}""");

        // 图片资产：Data/ 下的数据 config 不能用，必须上溯到 Characters/ 的图片 config
        string? picked = AssetCreator.FindImageConfigTemplate(Build(), "Data/Icons/RSVxWN");

        Assert.EndsWith("Characters/Abigail.config", picked);
    }

    [Fact]
    public void 没有图片config时返回空()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.config", DictConfig("System.String", "System.String"));

        Assert.Null(AssetCreator.FindImageConfigTemplate(Build(), "LooseSprites/NewSprite"));
    }

    [Fact]
    public void 新建数组型资产产出数组形状()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.config", "{}");

        var applier = new PatchApplier(Build());
        // 补丁的 Entries 用对象写；但如果目标资产是数组型且不存在，产物必须是数组
        var outcome = applier.ApplyEditData(
            "Data/NewArray", """{ "Entries": { "0": { "Id": "a" } } }""", isArrayAsset: true);

        Assert.True(outcome.Success);
        Flush();
        JToken written = JToken.Parse(File.ReadAllText(Path.Combine(_out, "Data", "NewArray.json")));
        Assert.Equal(JTokenType.Array, written.Type);
    }
}
