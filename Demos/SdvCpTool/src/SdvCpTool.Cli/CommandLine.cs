namespace SdvCpTool.Cli;

public sealed class Options
{
    public string Pack = "";
    public string Tree = "";
    public string Out = "";
    public string? Report;
    public string? ContentHashes;
    public string? Mods;

    /// <summary>
    /// `--mod-index-cache &lt;路径&gt;`：把「递归扫 Mods 目录」的结果缓存到磁盘（工具链跑批用）。
    /// ⚠ 不传就是每次真扫 —— 见 <see cref="Workspace.InstalledModIndex"/> 的失效说明。
    /// </summary>
    public string? ModIndexCache;

    /// <summary>
    /// `--out` 已存在且非空时**不重拷树、直接在它上面续写**（链式：多个内容包依次进同一棵产出树）。
    /// <para>
    /// 这不是开关，是**目录状态推出来的**：空目录 = 建树，非空 = 续写。
    /// 语义始终是 CP 的「**同一棵树**上依次应用补丁」—— 不是"每个包一棵树再合并"；
    /// 调用方负责给出正确的**包顺序**（`tools/convert-mods.py` 按 `Dependencies` 排过）。
    /// </para>
    /// </summary>
    public bool Append;

    /// <summary>
    /// `--pass loads|edits`：只应用 `Load` / 只应用其余补丁（链式工具用来复刻 CP 的两阶段语义）。
    /// <para>
    /// CP 处理一个资产时**先把所有包的 `Load` 选一个装上去，再依次应用所有 `Edit*`** ——
    /// 与"按包顺序逐条应用"不同：后加载的包创建的资产仍可被先加载的包编辑。
    /// 实测 `Lnh's Farms Convenient Road` 创建的 `Maps/Custom_ConvenientRoad`
    /// 被更早的包 `EditMap` 时，单阶段转换判 `TargetMissing` 直接丢掉那次编辑。
    /// </para>
    /// </summary>
    public string Pass = "all";

    /// <summary>只保留翻转标记，等所有包跑完后用 `bake-flips` 统一烘焙。</summary>
    public bool DeferFlips;
}

public static class CommandLine
{
    public const string Usage = """
        用法：
          SdvCpTool.Cli convert --pack <CP内容包目录> --tree <已解包资源树> --out <输出目录>
                                [--report <报告路径.json>]
                                [--mods <游戏Mods目录>]
                                [--content-hashes <ContentHashes.json>]

          SdvCpTool.Cli bake-frameworks --tree <已转换的产出树> --mods <游戏Mods目录>
                                        [--packs 子串…]
                                        [--cccb-variant Pure_Vanilla|RSV_SVE]
                                        [--dedup-cosmetics [--dedup-threshold 0] [--dedup-color-tolerance 0] [--dedup-max-pixel-diff 1.0] [--dedup-color-consistency 2.0]]
                                        [--calibrate-dedup]

          SdvCpTool.Cli trace-screenshot --image <截图.png> --tree <产出树>
                                        [--mods <游戏Mods目录>] [--base <原版树>]
                                        [--season spring|summer|fall|winter]
                                        [--crop x,y,w,h] [--map <地图名>] [--tile x,y]
                                        [--scale 1|2|3|4] [--out <目录>]
                                        [--cache <缓存目录>] [--rebuild-cache]
                                        [--min-score 0.6]

        convert 参数：
          --pack     含 content.json 的 CP 内容包目录
          --tree     已解包的资源树根目录
          --out      写入合并后的资源树。**空目录：整树拷贝后写补丁；非空目录：在它上面续写**
                     （续写 = 多个包依次进同一棵树，不是"每包一棵树再合并"）
          --report   报告输出路径，默认 <out> 的同级目录 <内容包名>.report.json
                     （刻意放在资源树外，避免被 `pack` 当成资源内容）
          --mods            游戏 Mods 目录（用于烘焙 HasMod 等条件；不提供则相关补丁判 NeedsReview）
          --mod-index-cache 把「扫 Mods 目录」的结果缓存到磁盘（**工具链跑批用**：
                            递归扫一次约 1.1 s，链式每包一个进程 ⇒ 232 包白扫 4 分钟；
                            键 = Mods 目录 mtime + 一级目录数，就地改 manifest 的 UniqueID 不会失效）
          --content-hashes  游戏的 ContentHashes.json 路径（用于区分 mod 新增资产与原版资产；
                            不提供时会警告并放宽判定）
          --pass      all（默认）/ loads / edits。`loads` 只应用 Load，`edits` 跳过 Load。
                     链式转换用两趟复刻 CP 的语义：**先装所有 Load，再应用所有 Edit** ——
                     这样后加载的包创建的资产也能被先加载的包编辑（跨包 EditMap/EditData）。
          --defer-flips  只保留翻转标记，不在本进程烘焙。链式转换必须带它，最后跑
                         `bake-flips --tree <产出树>` 统一烘焙。

        bake-flips 参数：
          --tree     全树翻转烘焙（等所有包都写完再跑，避免追加格被后续 Load 覆盖）

        bake-frameworks 参数：
          --dedup-cosmetics  对家具/大件/墙纸做去重（默认关闭）
          --dedup-threshold  dHash 汉明距离，默认 0（只去完全相同；以原版校准：
                             原版最近距离为 0，任何 >0 都可能误伤原版）
          --dedup-color-tolerance  纯变色容差，默认 0（关闭）
          --dedup-max-pixel-diff   相似去重的画面差上限（百分比），默认 1.0；
                                   防止 dHash 把 2%–8% 的不同花色/书脊/窗帘误判成同图
          --dedup-color-consistency 纯变色的颜色偏移一致性上限，默认 2.0；
                                   整图统一换色才算纯变色，局部换花色/书脊不算。
                                   实测 8.0 会误杀篱笆花期/书柜书脊等局部变化，故收紧到 2.0。
                                   另有内置单格偏移上限 20%，挡住小面积窗帘换色
          --calibrate-dedup  只校准并打印原版家具的最近距离/推荐阈值

        运行时框架的静态近似（总是尝试，按 --packs 过滤）：
          MoreGrass       每季最多 3 个变体覆盖进原版 TerrainFeatures/grass（全局换草）
          BusLocations    在 Maps/BusStop 给每个目的地复制一台售票机并加 Action: Warp，
                          沿同一行排下去、两两间隔 1 格，设施 Passable 不堵路
                          （Desert 原版已有，跳过；无票价/选站 UI）
          TrainStation    在 Maps/Railroad 给每个站点复制一个箱子并加同样的 Warp
                          （一排、间隔 1 格、Passable；无票价/条件/选站 UI）
          BushBloom       没有原版落点（花期/掉落物写死），不处理

        写完之后的几道全树自检**总是跑**，增量缓存固定放在 <out>/.audit-cache.json
        （与这一轮的树同生共死，不跨轮搬）。

        退出码：
          0  全部 patch 可合并
          1  有无法合并的 patch，或有 Include 的 When 无法求值
          2  致命错误
        """;

    /// <summary>解析失败返回 null。</summary>
    public static Options? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        if (!string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase)) return null;

        var o = new Options();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pack":
                    if (++i >= args.Length) return null;
                    o.Pack = args[i];
                    break;
                case "--tree":
                    if (++i >= args.Length) return null;
                    o.Tree = args[i];
                    break;
                case "--out":
                    if (++i >= args.Length) return null;
                    o.Out = args[i];
                    break;
                case "--report":
                    if (++i >= args.Length) return null;
                    o.Report = args[i];
                    break;
                case "--content-hashes":
                    if (++i >= args.Length) return null;
                    o.ContentHashes = args[i];
                    break;
                case "--mods":
                    if (++i >= args.Length) return null;
                    o.Mods = args[i];
                    break;
                case "--mod-index-cache":
                    if (++i >= args.Length) return null;
                    o.ModIndexCache = args[i];
                    break;
                case "--pass":
                    if (++i >= args.Length) return null;
                    o.Pass = args[i].ToLowerInvariant();
                    if (o.Pass is not ("all" or "loads" or "edits")) return null;
                    break;
                case "--defer-flips":
                    o.DeferFlips = true;
                    break;
                default:
                    return null;
            }
        }

        if (o.Pack.Length == 0 || o.Tree.Length == 0 || o.Out.Length == 0) return null;
        o.Report ??= DefaultReportPath(o.Pack, o.Out);
        return o;
    }

    /// <summary>
    /// 报告默认写到资源树<b>外</b>（<c>&lt;out&gt;</c> 的同级目录），否则整棵树交给 `pack` 时
    /// <c>report.json</c> 会被当成资源内容。文件名取内容包目录名。
    /// </summary>
    private static string DefaultReportPath(string packDirectory, string outDirectory)
    {
        string fullOut = Path.GetFullPath(outDirectory);
        string parent = Path.GetDirectoryName(fullOut) ?? fullOut;

        string packName = Path.GetFileName(packDirectory.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(packName)) packName = Path.GetFileName(fullOut);
        if (string.IsNullOrWhiteSpace(packName)) packName = "report";

        foreach (char bad in Path.GetInvalidFileNameChars())
            packName = packName.Replace(bad, '_');

        return Path.Combine(parent, packName + ".report.json");
    }
}
