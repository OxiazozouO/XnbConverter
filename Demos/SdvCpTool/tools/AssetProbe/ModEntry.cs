using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using xTile;
using Microsoft.Xna.Framework.Graphics;

namespace AssetProbe;

/// <summary>
/// 装机探针：**逐张地图试着建一个 <see cref="GameLocation"/>**，把建不起来的报出来。
///
/// <para>
/// 为什么要有它：产出树打成了 XNB、装进 `Content` 之后，"能不能用"剩下的问题几乎只有一类 ——
/// **某张地图建不出来**（图集缺失、tbin 结构不对、地点数据指向不存在的图）。逐张手点太慢，
/// 而这一步**只能实机做**（本机跑不了它：游戏那份 MonoGame 是 x86，探针得在游戏进程里跑）。
/// </para>
///
/// <para>
/// 用法：进游戏（**进了存档之后**再跑，见下），在 SMAPI 控制台敲
/// <c>probe_maps</c>（可选给一个清单文件路径，每行一个资产名，如 <c>Maps/Town</c>）。
/// 不给清单就自己扫 `Content\Maps\**\*.xnb`。
/// </para>
///
/// <para>
/// ⚠ **判读口径**（老探针踩过的坑，这里直接判掉）：<c>Maps/</c> 底下**不全是地图** ——
/// 图集图片（`spring_outdoorsTileSheet` 之类）也在那儿。它们**不算失败**，
/// 所以这里先按 <see cref="Map"/> 试、失败再按 <see cref="Texture2D"/> 试：
/// 能当贴图读出来的记成「是贴图，跳过」，两个都读不出来才记 FAIL。
/// </para>
///
/// <para>
/// ⚠ **存档要先进**：主菜单那些内容管理器是空转的（老探针的教训：主菜单**不加载声库**，
/// 于是"启动零报错"什么都证明不了）。没进存档时这里会先警告一句。
/// </para>
/// </summary>
public class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        helper.ConsoleCommands.Add("probe_maps",
            "逐张地图试着建 GameLocation，报 PASS/FAIL。用法：probe_maps [清单文件（每行一个资产名）]",
            Probe);

        // 自动化验收：设了环境变量就等存档稳定后自己跑一次，不需要人敲命令。
        if (Environment.GetEnvironmentVariable("SDVCP_PROBE_AUTORUN") != "1") return;
        helper.Events.GameLoop.SaveLoaded += (_, _) =>
        {
            int wait = 300;
            helper.Events.GameLoop.UpdateTicked += OnTick;
            void OnTick(object? _, UpdateTickedEventArgs e)
            {
                if (--wait > 0) return;
                helper.Events.GameLoop.UpdateTicked -= OnTick;
                string? list = Environment.GetEnvironmentVariable("SDVCP_PROBE_LIST");
                Probe("probe_maps", string.IsNullOrWhiteSpace(list)
                    ? Array.Empty<string>()
                    : new[] { list });

            }
        };
    }

    private void Probe(string command, string[] args)
    {
        if (!Context.IsWorldReady)
            Monitor.Log("⚠ 还没进存档 —— 这个结果**不可信**（主菜单下内容管理器是空转的）。建议读档后再跑一次。",
                LogLevel.Warn);

        string[] assets = args.Length > 0 ? ReadList(args[0]) : ScanMaps();
        if (assets.Length == 0)
        {
            Monitor.Log("没找到任何地图资产（既没给清单文件，`Content\\Maps` 下也没扫到 .xnb）。", LogLevel.Error);
            return;
        }

        int pass = 0, skip = 0;
        var failed = new List<string>();

        foreach (string asset in assets)
        {
            string name = asset.Replace('/', '\\').Trim().TrimEnd('\\');
            if (name.Length == 0) continue;

            // 1) 真建一个地点，并**碰一下 `.Map`** —— 这一步才会走游戏自己的地图加载
            //    （`GameLocation.loadMap` → `getMapLoader()` → `Game1.game1.xTileContent`，
            //     见 `GameLocation.cs:1941/1599`），顺带解析图集。
            //    ⚠ 别自己 `Game1.content.Load<Map>`：1.6 里地图走的是 **xTileContent**，
            //      用普通内容管理器读会得到假失败。
            //    ⚠ `name` 用固定值（不是资产名）：构造里按名字判断 isFarm/isStructure
            //      （`name.Contains("Farm")` 之类，`GameLocation.cs:1536`）——
            //      资产名里带 `Farm` 的会被当成农场走另一条路，探针要的是**中性**的构造。
            try
            {
                GameLocation location = new(name, "Probe");
                Map map = location.Map;                     // ← 真正的加载在这里发生
                if (map == null) throw new InvalidOperationException("`.Map` 读出来是 null");

                pass++;
                Monitor.Log($"PASS  {name}", LogLevel.Trace);
                continue;
            }
            catch (Exception ex)
            {
                // 2) 不是地图、是图集图片？那不算失败（`Maps/` 底下两者混着放）。
                //    贴图本来就走普通内容管理器。
                try
                {
                    Texture2D _ = Game1.content.Load<Texture2D>(name);
                    skip++;
                    Monitor.Log($"跳过  {name}（是贴图，不是地图）", LogLevel.Trace);
                    continue;
                }
                catch
                {
                    failed.Add($"{name} —— {ex.GetType().Name}: {ex.Message}");
                    Monitor.Log($"FAIL  {name} —— {ex.GetType().Name}: {ex.Message}", LogLevel.Error);
                    Monitor.Log(ex.ToString(), LogLevel.Trace);
                }
            }
        }

        Monitor.Log($"—— 探针结果：PASS {pass} / 贴图跳过 {skip} / **FAIL {failed.Count}**（共 {assets.Length} 个）",
            failed.Count == 0 ? LogLevel.Info : LogLevel.Error);
        if (failed.Count > 0)
        {
            Monitor.Log("失败清单（拿这个去对照产出树里有没有那张 .tbin / 那些图集）：", LogLevel.Error);
            foreach (string line in failed) Monitor.Log("    · " + line, LogLevel.Error);
        }
    }

    /// <summary>清单文件：一行一个资产名（`Maps/Town` 或 `Maps\Town` 都行；`#` 开头当注释）。</summary>
    private string[] ReadList(string path)
    {
        if (!File.Exists(path))
        {
            Monitor.Log($"清单文件不存在：{path}", LogLevel.Error);
            return Array.Empty<string>();
        }
        return File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#"))
            .ToArray();
    }

    /// <summary>不给清单就自己扫 `Content\Maps\**\*.xnb`（相对路径去掉扩展名就是资产名）。</summary>
    private string[] ScanMaps()
    {
        string root = Path.Combine(Constants.ContentPath, "Maps");   // SMAPI: 游戏的 Content 目录
        if (!Directory.Exists(root))
        {
            Monitor.Log($"没找到 {root} —— 游戏目录不对？", LogLevel.Error);
            return Array.Empty<string>();
        }
        return Directory.GetFiles(root, "*.xnb", SearchOption.AllDirectories)
            .Select(f =>
            {
                string rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
                return "Maps/" + rel.Substring(0, rel.Length - ".xnb".Length);
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }
}
