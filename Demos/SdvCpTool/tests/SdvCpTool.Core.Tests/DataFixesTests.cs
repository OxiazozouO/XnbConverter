using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// DLL 专有数据字段 → 原版写法（<see cref="DataFixes"/>）。
/// <para>
/// 2026-10-18 调查：原版只有三种订单板（`""`/`Qi`/`DesertFestivalMarlon`），
/// `Action = SpecialOrders <id>` 的参数被忽略（`GameLocation.cs:9020-9030`）；
/// `Data/Machines.OutputMethod` 靠 `Type.GetType` 找 mod 类型，纯 XNB 必然失败
/// （`MachineDataUtility.cs:204-210` 返回 null）。
/// </para>
/// </summary>
public class DataFixesTests
{
    [Fact]
    public void 自定义订单板并回默认板()
    {
        var data = JObject.Parse("""
        {
          "CustomOrder": { "OrderType": "SunberryBoard", "Text": "x" },
          "QiOrder": { "OrderType": "Qi" },
          "VanillaOrder": { "OrderType": "" }
        }
        """);

        Assert.Equal(1, DataFixes.Normalize("Data/SpecialOrders", data));
        Assert.Equal("", data["CustomOrder"]!["OrderType"]!.Value<string>());
        Assert.Equal("Qi", data["QiOrder"]!["OrderType"]!.Value<string>());
    }

    [Fact]
    public void 喂食器OutputMethod展开成加权OutputItem()
    {
        var data = JObject.Parse("""
        {
          "(BC)Feeder": {
            "OutputRules": [
              {
                "Id": "Corn",
                "OutputItem": [
                  {
                    "IncrementMachineParentSheetIndex": 1,
                    "OutputMethod": "OrnithologistsGuild.SpecializedMethods, OrnithologistsGuild:GetFeederOutput"
                  }
                ],
                "DaysUntilReady": 1
              }
            ]
          }
        }
        """);

        Assert.Equal(1, DataFixes.Normalize("Data/Machines", data));
        JObject rule = (JObject)data["(BC)Feeder"]!["OutputRules"]![0]!;
        var outputs = (JArray)rule["OutputItem"]!;
        Assert.Equal(29, outputs.Count);
        Assert.Equal(25, outputs.Count(o => o["ItemId"]!.Value<string>() == "(O)747"));
        Assert.Equal(3, outputs.Count(o => o["ItemId"]!.Value<string>() == "(O)335"));
        Assert.Equal(1, outputs.Count(o => o["ItemId"]!.Value<string>() == "(O)336"));
        Assert.All(outputs, o => Assert.Null(o["OutputMethod"]));
        Assert.False(rule["UseFirstValidOutput"]!.Value<bool>());
    }

    [Fact]
    public void 其它资产不动()
    {
        var data = JObject.Parse("""{ "x": 1 }""");
        Assert.Equal(0, DataFixes.Normalize("Data/Objects", data));
    }
}
