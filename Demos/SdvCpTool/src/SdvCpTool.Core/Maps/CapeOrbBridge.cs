using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// CapeStardew 的 <c>TouchAction = CapeOrbTouchAction …</c> 的事件桥。
/// <para>
/// 这个动作由 `CapeStardewCode` 用 Harmony 注册（反编译
/// `OrbRuneWarpManager.TryHandleTouchAction`）：玩家物品栏里任意位置有
/// `(O)OrbOfTheTides` 时，按动作参数把玩家传送到当前地图或指定地图的坐标；
/// 没有宝珠则静默不做事。原版没有同名动作，纯 XNB 下它是死图块。
/// </para>
/// <para>
/// 原版可用的通用桥是 TouchAction `PlayEvent &lt;id&gt; &lt;checkPre&gt; &lt;checkSeen&gt; [fallback]`
/// + 事件前置条件 `HasItem`。所以先在语法层生成一张纯计划（哪些格子改成什么事件 id、
/// 事件脚本是什么），确认事件数据写得出去后，再一次性改地图属性并写 `Data/Events`。
/// </para>
/// <para>
/// 事件脚本只使用原版命令：`playSound wand` / `warp farmer &lt;x&gt; &lt;y&gt;` /
/// 跨图时 `changeLocation &lt;map&gt;`（`Event.cs` 的 <c>Warp</c> / <c>ChangeLocation</c>）。
/// 事件 id 放在 `Data/Events/&lt;当前地图&gt;`，前置条件写在 '/' 后，游戏按 `key.Split('/')[0]`
/// 匹配 `PlayEvent` 的 id。
/// </para>
/// </summary>
public static class CapeOrbBridge
{
    private const string TouchPrefix = "CapeOrbTouchAction";
    private const string OrbPrecondition = "HasItem (O)OrbOfTheTides";

    public sealed record Action(
        string EventId,
        string EventKey,
        string Script,
        Propertie Property,
        bool Rewrite);

    public sealed record Plan(string MapAsset, string MapName, IReadOnlyList<Action> Actions);

    /// <summary>
    /// 先为每张脏地图生成计划，再写 `Data/Events`；只有事件数据写成功的地图才改动作。
    /// 返回改写的图块属性数。
    /// </summary>
    public static int RewriteAndGenerate(OutputTree output,
        IEnumerable<(string MapRel, TBin10 Map)> maps, List<string> notes)
    {
        int rewritten = 0;
        foreach ((string mapRel, TBin10 map) in maps)
        {
            Plan? plan = BuildPlan(mapRel, map);
            if (plan == null || plan.Actions.Count == 0) continue;

            if (!WriteEventData(output, plan, notes)) continue;

            rewritten += RewriteProperties(plan);
        }

        return rewritten;
    }

    /// <summary>
    /// 扫产出树里**已经落盘、这轮没被 MapCache 弄脏**的地图。链式转换里前面的包写下的
    /// `CapeOrbTouchAction` 不会进入本包的 `MapCache`；只处理脏地图会漏掉它们。
    /// 先用字节搜索筛一遍，只有真的含这个动作的地图才解码。
    /// </summary>
    public static int RewriteRemaining(OutputTree output, List<string> notes)
    {
        string mapsRoot = output.ToOutputPath("Maps");
        if (!Directory.Exists(mapsRoot)) return 0;

        byte[] needle = Encoding.ASCII.GetBytes(TouchPrefix);
        byte[] generatedNeedle = Encoding.ASCII.GetBytes("PlayEvent CapeOrb_");
        int rewritten = 0;
        foreach (string file in Directory.EnumerateFiles(mapsRoot, "*.tbin", SearchOption.AllDirectories))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception ex)
            {
                notes.Add($"Cape 宝珠事件：读取 `{file}` 失败：{ex.Message}");
                continue;
            }
            if (bytes.AsSpan().IndexOf(needle) < 0 && bytes.AsSpan().IndexOf(generatedNeedle) < 0)
                continue;

            TBin10 map;
            try { map = (TBin10)TBin10Reader.Create(bytes).Read(); }
            catch (Exception ex)
            {
                notes.Add($"Cape 宝珠事件：解码 `{file}` 失败：{ex.Message}");
                continue;
            }

            string rel = Path.GetRelativePath(output.OutputRoot, file).Replace('\\', '/');
            Plan? plan = BuildPlan(rel, map, includeGenerated: true);
            if (plan == null || plan.Actions.Count == 0) continue;
            if (!WriteEventData(output, plan, notes)) continue;

            int count = RewriteProperties(plan);
            if (count == 0) continue;

            string tmp = file + ".cape-tmp";
            try
            {
                TBin10Reader.Create(Array.Empty<byte>()).Save(map, tmp);
                File.Move(tmp, file, overwrite: true);
                rewritten += count;
            }
            catch (Exception ex)
            {
                try { File.Delete(tmp); } catch { }
                notes.Add($"Cape 宝珠事件：写回 `{file}` 失败：{ex.Message}");
            }
        }

        return rewritten;
    }

    private static int RewriteProperties(Plan plan)
    {
        int count = 0;
        foreach (Action action in plan.Actions)
        {
            if (!action.Rewrite) continue;
            action.Property.Value = "PlayEvent " + action.EventId + " true false";
            count++;
        }
        return count;
    }

    /// <summary>
    /// 纯函数：扫描地图里的 `CapeOrbTouchAction`，返回事件计划；不改地图。
    /// 参数格式与反编译一致：`&lt;x&gt; &lt;y&gt;` 表示当前地图，`&lt;map&gt; &lt;x&gt; &lt;y&gt;` 表示跨图。
    /// </summary>
    public static Plan? BuildPlan(string mapRel, TBin10 map, bool includeGenerated = false)
    {
        if (map.Layers == null) return null;

        string mapName = Path.GetFileNameWithoutExtension(mapRel.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(mapName)) return null;

        var actions = new List<Action>();
        foreach (Layer layer in map.Layers)
        {
            if (layer?.Tiles == null) continue;
            foreach (BaseTile? tile in layer.Tiles)
            {
                if (tile?.Properties == null) continue;
                foreach (Propertie property in tile.Properties)
                {
                    if (property.Key != "TouchAction" || property.Value is not string raw) continue;
                    if (TryParse(raw, mapName, out string targetMap, out int x, out int y))
                    {
                        string safeMap = Regex.Replace(mapName, "[^A-Za-z0-9_]", "_");
                        string id = targetMap.Equals(mapName, StringComparison.OrdinalIgnoreCase)
                            ? $"CapeOrb_{safeMap}_{x}_{y}"
                            : $"CapeOrb_{safeMap}_to_{Regex.Replace(targetMap, "[^A-Za-z0-9_]", "_")}_{x}_{y}";
                        string key = id + "/" + OrbPrecondition;
                        string script = targetMap.Equals(mapName, StringComparison.OrdinalIgnoreCase)
                            ? $"playSound wand/warp farmer {x} {y}/end"
                            : $"playSound wand/changeLocation {targetMap}/warp farmer {x} {y}/end";
                        actions.Add(new Action(id, key, script, property, Rewrite: true));
                    }
                    else if (includeGenerated && TryParseGenerated(raw, mapName,
                                 out string generatedId, out string generatedScript))
                    {
                        actions.Add(new Action(generatedId, generatedId + "/" + OrbPrecondition,
                            generatedScript, property, Rewrite: false));
                    }
                }
            }
        }

        return actions.Count == 0 ? null : new Plan("Data/Events/" + mapName, mapName, actions);
    }

    private static bool TryParse(string raw, string currentMap, out string targetMap, out int x, out int y)
    {
        targetMap = currentMap;
        x = 0;
        y = 0;
        string[] parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[0].Equals(TouchPrefix, StringComparison.OrdinalIgnoreCase))
            return int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y);

        if (parts.Length == 4 && parts[0].Equals(TouchPrefix, StringComparison.OrdinalIgnoreCase))
        {
            targetMap = parts[1];
            return int.TryParse(parts[2], out x) && int.TryParse(parts[3], out y);
        }

        return false;
    }

    /// <summary>
    /// 已改写过的地图只留下 `PlayEvent CapeOrb_<map>_<x>_<y> true false`。
    /// 后续包可能整份 Load 掉 `Data/Events`，把早先生成的事件键冲掉；补扫时从 id
    /// 反推出同样的脚本（当前地图的同图形态；跨图 id 带 `_to_`，不做猜测）。
    /// </summary>
    private static bool TryParseGenerated(string raw, string mapName,
        out string eventId, out string script)
    {
        eventId = "";
        script = "";
        string[] parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !parts[0].Equals("PlayEvent", StringComparison.OrdinalIgnoreCase)
            || !parts[1].StartsWith("CapeOrb_", StringComparison.OrdinalIgnoreCase))
            return false;

        string id = parts[1];
        string[] segments = id.Split('_');
        if (segments.Length < 5
            || !int.TryParse(segments[^2], out int x)
            || !int.TryParse(segments[^1], out int y))
            return false;

        string prefix = string.Join('_', segments[..^2]);
        string expected = "CapeOrb_" + Regex.Replace(mapName, "[^A-Za-z0-9_]", "_");
        if (!prefix.Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;

        eventId = id;
        script = $"playSound wand/warp farmer {x} {y}/end";
        return true;
    }

    private static bool WriteEventData(OutputTree output, Plan plan, List<string> notes)
    {
        string baseRel = plan.MapAsset;
        string baseJson = baseRel + ".json";
        bool isNew = !output.Exists(baseJson);

        JObject root;
        if (isNew)
        {
            root = new JObject();
        }
        else
        {
            try
            {
                root = JObject.Parse(output.ReadTextOrNull(baseJson) ?? "{}");
            }
            catch (Exception ex)
            {
                notes.Add($"Cape 宝珠事件：`{baseJson}` 不是 JSON 对象，跳过：{ex.Message}");
                return false;
            }
        }

        foreach (Action action in plan.Actions)
        {
            if (root.Property(action.EventKey) == null)
                root[action.EventKey] = action.Script;
        }

        if (isNew)
        {
            // 先确认有可复制的 .config；否则写出去也回不了包，反而留下半成品。
            string? config = AssetCreator.CopyConfig(output, baseRel, root);
            if (config == null)
            {
                notes.Add($"Cape 宝珠事件：新建 `{baseJson}` 找不到 Data/Events 的 .config 模板");
                return false;
            }
            output.StageJson(baseJson, root);
            output.CreatedFiles.Add(baseJson);
        }
        else
        {
            output.StageJson(baseJson, root);
        }

        // 本地化事件资产会整体替换 base，新增的 id 必须同步进去；没有变体就让游戏回落 base。
        foreach (string variant in LocaleVariants.Existing(output.Exists, baseRel, ".json"))
        {
            if (variant.Equals(baseRel, StringComparison.OrdinalIgnoreCase)) continue;
            string json = variant + ".json";
            try
            {
                JObject localized = JObject.Parse(output.ReadTextOrNull(json) ?? "{}");
                bool changed = false;
                foreach (Action action in plan.Actions)
                {
                    if (localized.Property(action.EventKey) != null) continue;
                    localized[action.EventKey] = action.Script;
                    changed = true;
                }
                if (changed) output.StageJson(json, localized);
            }
            catch (Exception ex)
            {
                notes.Add($"Cape 宝珠事件：`{json}` 解析失败，未镜像：{ex.Message}");
            }
        }

        return true;
    }
}
