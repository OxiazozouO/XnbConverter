using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 结婚可用性最小修复（<see cref="MarriageReadiness"/>）。
/// <para>
/// 两条硬性断点：`CanBeRomanced` 必须是布尔；`Data/NPCGiftTastes` 缺键时花束/吊坠送不出去
/// （`NPC.cs:1391-1398`）。语料实例：Sterling 的 `"true"` 字符串；Lance / CorwinLK 缺礼物条目。
/// </para>
/// </summary>
public class MarriageReadinessTests
{
    [Fact]
    public void 字符串布尔改成真布尔()
    {
        var chars = JObject.Parse("""
        {
          "Sterling": { "CanBeRomanced": "true", "CanReceiveGifts": "false", "SpawnIfMissing": "true" },
          "Other": { "CanBeRomanced": true },
          "Bad": { "CanBeRomanced": "yes" }
        }
        """);

        Assert.Equal(3, MarriageReadiness.NormalizeBooleanFlags(chars));
        Assert.Equal(JTokenType.Boolean, chars["Sterling"]!["CanBeRomanced"]!.Type);
        Assert.True(chars["Sterling"]!["CanBeRomanced"]!.Value<bool>());
        Assert.False(chars["Sterling"]!["CanReceiveGifts"]!.Value<bool>());
        Assert.True(chars["Sterling"]!["SpawnIfMissing"]!.Value<bool>());
        Assert.Equal("yes", chars["Bad"]!["CanBeRomanced"]!.Value<string>());
    }

    [Fact]
    public void 可婚NPC缺礼物条目时补中性条目()
    {
        var chars = JObject.Parse("""
        {
          "Roman": { "CanBeRomanced": true },
          "Platonic": { "CanBeRomanced": false }
        }
        """);
        var gifts = JObject.Parse("""{ "Platonic": "x/y/z" }""");

        Assert.Equal(1, MarriageReadiness.AddMissingGiftTastes(chars, gifts));
        Assert.NotNull(gifts["Roman"]);
        Assert.Equal(10, gifts["Roman"]!.Value<string>()!.Split('/').Length);
        Assert.Equal("x/y/z", gifts["Platonic"]!.Value<string>());
    }

    [Fact]
    public void 婚后对话资产名别名取最长前缀且不抢别的NPC()
    {
        var counts = new Dictionary<string, int>
        {
            ["JadeMalic"] = 0,     // 空资产
            ["Jade"] = 79,         // 作者写错的目标
            ["JadeMalicAlt"] = 5,  // 不是前缀，不参与
            ["Abigail"] = 40,      // 是某个 NPC 的资产，不能借用
        };
        string[] npcs = { "JadeMalic", "Abigail" };

        Assert.Equal("Jade", MarriageReadiness.FindDialogueAlias("JadeMalic", npcs, counts));
        Assert.Null(MarriageReadiness.FindDialogueAlias("Abigail", npcs, counts));
        Assert.Null(MarriageReadiness.FindDialogueAlias("Nobody", npcs, counts));
    }

    [Fact]
    public void 已有婚后对话不找别名()
    {
        var counts = new Dictionary<string, int> { ["JadeMalic"] = 3, ["Jade"] = 79 };
        Assert.Null(MarriageReadiness.FindDialogueAlias("JadeMalic", new[] { "JadeMalic" }, counts));
    }

    [Fact]
    public void 已有礼物条目不覆盖()
    {
        var chars = JObject.Parse("""{ "Roman": { "CanBeRomanced": true } }""");
        var gifts = JObject.Parse("""{ "Roman": "custom" }""");

        Assert.Equal(0, MarriageReadiness.AddMissingGiftTastes(chars, gifts));
        Assert.Equal("custom", gifts["Roman"]!.Value<string>());
    }

    [Fact]
    public void 本地化角色补缺行并同步婚姻字段但保留译文()
    {
        var baseChars = JObject.Parse("""
        {
          "NewNpc": { "DisplayName": "New", "CanBeRomanced": true,
                      "SpouseRoom": { "MapAsset": "NewRoom", "MapSourceRect": { "X": 0, "Y": 0, "Width": 6, "Height": 9 } } },
          "Sandy": { "DisplayName": "Sandy", "CanBeRomanced": true,
                     "SpouseRoom": { "MapAsset": "Custom_SandysRoom", "MapSourceRect": { "X": 0, "Y": 0, "Width": 6, "Height": 9 } } }
        }
        """);
        var localized = JObject.Parse("""
        {
          "Sandy": { "DisplayName": "桑迪", "CanBeRomanced": false, "SpouseRoom": null }
        }
        """);

        int changed = MarriageReadiness.MirrorLocalizedCharacters(baseChars, localized);

        Assert.True(changed >= 3); // 新增 NewNpc + Sandy 的 CanBeRomanced/SpouseRoom
        Assert.NotNull(localized["NewNpc"]);
        Assert.Equal("New", localized["NewNpc"]!["DisplayName"]!.Value<string>());
        Assert.True(localized["Sandy"]!["CanBeRomanced"]!.Value<bool>());
        Assert.Equal("桑迪", localized["Sandy"]!["DisplayName"]!.Value<string>());
        Assert.Equal("Custom_SandysRoom", localized["Sandy"]!["SpouseRoom"]!["MapAsset"]!.Value<string>());
        Assert.Equal(0, MarriageReadiness.MirrorLocalizedCharacters(baseChars, localized));
    }

    [Fact]
    public void 本地化角色补缺Appearance变体且保留已有外观()
    {
        var baseChars = JObject.Parse("""
        {
          "Abigail": {
            "Appearance": [
              { "Id": "Spring", "Sprite": "Characters/Abigail_Spring" },
              { "Id": "Winter", "Sprite": "Characters/Abigail_Winter" }
            ]
          }
        }
        """);
        var localized = JObject.Parse("""
        {
          "Abigail": {
            "Appearance": [
              { "Id": "Spring", "Sprite": "Characters/Abigail_Spring_Localized" }
            ]
          }
        }
        """);

        int changed = MarriageReadiness.MirrorLocalizedCharacters(baseChars, localized);

        Assert.Equal(1, changed);
        Assert.Equal(2, localized["Abigail"]!["Appearance"]!.Count());
        Assert.Equal("Characters/Abigail_Spring_Localized",
            localized["Abigail"]!["Appearance"]![0]!["Sprite"]!.Value<string>());
        Assert.Equal("Characters/Abigail_Winter",
            localized["Abigail"]!["Appearance"]![1]!["Sprite"]!.Value<string>());
        Assert.Equal(0, MarriageReadiness.MirrorLocalizedCharacters(baseChars, localized));
    }

    [Fact]
    public void 本地化婚礼补齐缺的Attendee和EventScript()
    {
        var baseWeddings = JObject.Parse("""
        {
          "Attendees": { "Gunnar": { "Setup": "addActor Gunnar" }, "Abigail": { "Setup": "addActor Abigail" } },
          "EventScript": { "default": "wedding", "Gunnar": "wedding-gunnar" }
        }
        """);
        var localized = JObject.Parse("""
        {
          "Attendees": { "Abigail": { "Setup": "translations" } },
          "EventScript": { "default": "本地婚礼" }
        }
        """);

        int changed = MarriageReadiness.MirrorLocalizedWeddings(baseWeddings, localized);

        Assert.Equal(2, changed); // 新增 Gunnar attendee + Gunnar script
        Assert.Equal("addActor Gunnar", localized["Attendees"]!["Gunnar"]!["Setup"]!.Value<string>());
        Assert.Equal("translations", localized["Attendees"]!["Abigail"]!["Setup"]!.Value<string>());
        Assert.Equal("wedding-gunnar", localized["EventScript"]!["Gunnar"]!.Value<string>());
        Assert.Equal("本地婚礼", localized["EventScript"]!["default"]!.Value<string>());
        Assert.Equal(0, MarriageReadiness.MirrorLocalizedWeddings(baseWeddings, localized));
    }
}
