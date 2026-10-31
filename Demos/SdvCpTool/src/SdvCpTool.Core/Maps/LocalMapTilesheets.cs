using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 把**模组加载的地图**所引用的「**包内同级图集**」一起搬进产出树。
///
/// <para>
/// <b>为什么必须有这一步</b>：SMAPI 的 <c>ModContentManager.FixTilesheetPaths</c> /
/// <c>TryGetTilesheetAssetName</c>（反编译 <c>StardewModdingAPI.decompiled.cs</c>）会**改写**
/// 模组地图的图集名 —— 顺序是：
/// </para>
/// <list type="number">
/// <item>先去**地图文件所在的那层目录**里找同名文件（<c>LocalTilesheetExtensions = { ".png", ".xnb" }</c>），
///   找到就把图集名换成该模组的**内部资产键**（由 SMAPI 从模组目录供图）；</item>
/// <item>找不到才回落游戏内容：<c>GetContentKeyForTilesheetImageSource</c> —— 没有 <c>Maps</c> 前缀就补上、
///   去掉 <c>.png</c>，也就是 <c>Maps/&lt;名&gt;</c>。</item>
/// </list>
/// <para>
/// <b>纯 XNB 产出里没有"模组目录"这一层</b>，所以第 1 类图集必须**当成普通图片资产写进产出树**，
/// 放到游戏会去请求的名字上（也就是第 2 条算出来的那个 <c>Maps/&lt;名&gt;</c>）。
/// 不搬的话那张地图**建不出来** —— 实机报的就是
/// <c>Could not find file '…\Content\Maps\Binoculars.xnb'</c>。
/// </para>
/// <para>
/// <b>季节族要整族搬</b>：室外地图的图集名会被 <c>GameLocation.GetSeasonalTilesheetName</c>
/// 按当前季节换前缀（<c>spring_X</c> → <c>summer_X</c>…），只搬被点名的那一季，换季就找不到了。
/// </para>
/// </summary>
internal static class LocalMapTilesheets
{
    private static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

    /// <summary>SMAPI <c>LocalTilesheetExtensions</c>。</summary>
    private static readonly string[] LocalExtensions = { ".png", ".xnb" };

    /// <summary>
    /// 逐张图集处理。落不下去的只记 <paramref name="notes"/>（图集缺失不该带走整条补丁 ——
    /// 地图本身已经写好了，报出来让人看得见即可）。
    /// <para>
    /// <paramref name="mapAsset"/> 是这张地图**自己的内容键**：图集的落点要是撞上它（图集跟地图同名）
    /// 或撞上别的**非图片**资产，就得改名 —— 见 <see cref="Clashes"/>。
    /// </para>
    /// <para>
    /// <paramref name="maps"/> 是本次运行的地图内存模型（可空）：撞名判据必须**看得见还没落盘的地图** ——
    /// 实测 `[CP] The Last Smoluanu` 先 `Load Maps/NightBGDwarf`、再 `Load Maps/Summit_Night`，
    /// 后者的图集引用正好是 `NightBGDwarf.png`：地图在内存里还没落盘时判据看不见它，
    /// 图集就写到了 `Maps/NightBGDwarf` 上，随后地图又写回同一键（同一个键上两份不同类的东西）。
    /// </para>
    /// </summary>
    /// <param name="forTarget">
    /// <c>true</c>：这张地图**要被写进产出树**（`Load` 那两条路）—— 引用按**它自己那层目录**写
    /// （游戏就是这么解析的）；<c>false</c>：这张地图只是**补丁的源**（`EditMap` 的 `FromFile`）——
    /// 引用保持「相对 `Maps/`」的基准，随后由 <see cref="MapPatcher.PatchMap"/> 按目标目录定基**一次**
    /// （就地写成目标形态会被反复定基 —— 同一个源对象被 `MapCache` 缓存、被多条补丁共用）。
    /// </param>
    /// <returns>地图的图集引用被改过名时为 <c>true</c>（调用方据此决定要不要把地图重新落盘）。</returns>
    public static bool Copy(OutputTree output, ImageCache images, MapCache? maps, string packRoot,
        string sourceMapPath, string mapAsset, TBin10? map, bool forTarget, List<string> notes)
    {
        if (map?.TileSheets == null) return false;

        string mapDir = Path.GetDirectoryName(sourceMapPath) ?? "";
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool rewrote = false;

        foreach (TileSheet sheet in map.TileSheets)
        {
            string original = Normalize(sheet.Image);
            if (original.Length == 0) continue;

            // ★ **名字里任何一段开头的点，都是"别在我这层找"的记号**（Tiled 记的是同级相对路径，
            //   作者加点表示"按游戏资产名解析"）。两种写法都实测过：
            //     · 整串开头：`[CP] Integrated Minecarts` 的 `.spring_outdoorsTileSheet.png`
            //       —— 包里什么都没带 ⇒ 用游戏那张；
            //     · 路径中间那段开头：同包的 `Mines/.mine.png` —— 剥点后正好是**原版资产**
            //       `Maps/Mines/mine`（原版树里有 `mine.png`）。
            //   规矩与不带点的情形**是同一条**：**剥点后按名字找图 —— 包内同级有就用包内那份
            //   （覆盖同名资产），没有就落到游戏资产**。
            //   ⚠ 纯 XNB 产出里**没人会剥这个点**（那是 SMAPI 运行时干的）。照原样写进 tbin，
            //     游戏就会去请求 `Maps/.spring_outdoorsTileSheet.xnb` ⇒ FileNotFound ⇒
            //     `xTile.Map.LoadTileSheets` **抛异常** ⇒ **整个地点建不起来**
            //     （实机 2026-10-12：Town / Mountain / Forest / Woods / Desert / IslandWest 等 27 个）。
            string stripped = StripDots(original);
            if (stripped.Length == 0) continue;
            string reference = stripped;

            // ★ `paths.png` 是**原版 Paths 层的指令表**（树/草/灌木/矿石/木屋标记），不是视觉图集。
            //   很多包复制一份自己画图；纯 XNB 里必须统一回原版 `Maps/paths`，否则：
            //   ① 覆盖原版 64×256 图集（截成 64×160）导致黑洞；
            //   ② 自绘的 Paths 层索引与原版指令表不对应，资源生成全错。
            if (string.Equals(Path.GetFileNameWithoutExtension(reference), "paths",
                    StringComparison.OrdinalIgnoreCase))
            {
                sheet.Image = forTarget
                    ? MapPatcher.Rebase("Maps/paths", mapAsset, alreadyContent: true)
                    : MapPatcher.MapsRelative("Maps/paths");
                rewrote = true;
                notes.Add($"图集引用 `{original}` ⇒ 原版指令集 `paths`（忽略包内同名副本）");
                continue;
            }

            // ★ **每条引用都归一**：先算成**绝对内容路径**（`key` —— 落点、撞名判据、找图都用它），
            //   再按这张地图**自己的身份**写成它该有的形态：
            //     · 目标地图（要写进树）⇒ **相对它自己那层目录**。地图的图集引用就是这么解析的，
            //       包文件里那种「相对 `Maps/`」的写法落到子目录的地图上会错位
            //       （实测 `Maps/Mines/19` 里的 `Mines/mine` ⇒ 游戏读成 `Maps/Mines/Mines/mine` ⇒ 建不起来）；
            //     · 源地图（`EditMap` 的 `FromFile`）⇒ **保持「相对 `Maps/`」**，交给 `MapPatcher` 定基一次。
            //       ⚠ 这里**不能**写成目标形态：源对象被 `MapCache.TryGetSource` 共用，写进去的形态必须
            //       **幂等**，否则每条补丁都在同一引用上再加层（实测 4 条补丁把 `Mines/mine.png` 逐次写成
            //       `mine` ⇒ `../mine` ⇒ `../../mine`，指向内容根 ⇒ 那张地图建不起来）。
            //   两种形态用的是**同一份** `Rebase`，不写第二遍。
            string key = GameContentKey(reference);
            string written = forTarget
                ? MapPatcher.Rebase(key, mapAsset, alreadyContent: true)
                : MapPatcher.MapsRelative(key);
            if (!string.Equals(sheet.Image, written, StringComparison.Ordinal))
            {
                sheet.Image = written;
                rewrote = true;
                notes.Add($"图集引用 `{original}` ⇒ `{written}`"
                    + (forTarget
                        ? "（目标地图的图集引用相对它自己那层目录解析）"
                        : "（源地图的图集引用按相对 `Maps/` 的基准归一，交给 `MapPatcher` 定基一次）"));
            }

            // 一个内容键上不能同时是图与别的资产：谁后写谁赢，游戏会拿贴图当地图用（实测 `Maps/Coop`）。
            // 撞了就换一个落点，并**把地图里的引用一起改掉** —— 游戏正是按 `ImageSource` 找图的。
            // ⚠ 改的只是**落点**：包内那份图还叫原名，找文件仍按原名找（两个名字分开传）。
            // ⚠ **包内真有这份图才改**：没有那份图就没什么可挪的，改名只会把「指向地图」
            //   变成「指向不存在的资产」，报告里还多出一条假缺失。CP 那边这种引用也是原样留着。
            string? newStem = null;
            string? localFile = FindLocal(mapDir, original);
            if (localFile != null
                && (Clashes(output, maps, mapAsset, key) || DimensionsDiffer(images, key, localFile)))
            {
                key = FreeName(output, maps, mapAsset, key);
                sheet.Image = forTarget                               // 同样按地图自己的身份写回
                    ? MapPatcher.Rebase(key, mapAsset, alreadyContent: true)
                    : MapPatcher.MapsRelative(key);
                newStem = Path.GetFileNameWithoutExtension(key);
                rewrote = true;
                notes.Add($"图集 `{reference}` 的落点已被别的资产 / 不同尺寸的同名图集占用 ⇒ 改到 `{key}`（地图引用一并改掉）：" +
                          "包内同级图集在 SMAPI 里是**按包隔离**的，不能和全局同名图集挤一个键");
            }

            CopyOne(output, images, packRoot, mapDir, original, key, handled, notes);

            // 季节族：把 `spring_X` 的另外三季也搬过来（改名了就按**新名**整族搬 —— 游戏换季时
            // 是拿**改过的**引用换前缀去找的）。
            string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(reference));
            string? season = SeasonPrefix(stem);
            if (season == null) continue;

            string sub = Path.GetDirectoryName(original)?.Replace('\\', '/') ?? "";
            string baseName = stem[(season.Length + 1)..];   // 连 `_` 一起剥掉
            string newBase = newStem == null ? baseName : newStem[(season.Length + 1)..];
            foreach (string other in Seasons)
            {
                if (other == season) continue;
                string sibling = sub.Length == 0 ? $"{other}_{baseName}" : $"{sub}/{other}_{baseName}";
                string siblingKey = KeyWith(sub, $"{other}_{newBase}");
                CopyOne(output, images, packRoot, mapDir, sibling, siblingKey, handled, notes);
            }
        }

        return rewrote;
    }

    /// <summary>
    /// 这个落点是不是已经被别的资产占了：地图自己（图集与地图同名）、树里已有的非图片资产、
    /// 或**本次运行里已经生成/改过、还没落盘**的地图。
    /// </summary>
    /// <summary>
    /// 包内同级图集与树里现有的同名图集**尺寸不同** ⇒ 索引表不兼容，不能共用一个键。
    /// 这是 SMAPI 本地图集的语义：每个包的本地图集是独立资产，不是全局覆盖。
    /// </summary>
    private static bool DimensionsDiffer(ImageCache images, string key, string localFile)
    {
        if (!images.TryGetSize(key + ".png", out int existingW, out int existingH)) return false;
        // 只读 PNG 头，不解码整张图（这里每个同名图集都会被问一次）。
        return SdvCpTool.Core.Validating.PngHeader.TryReadSize(localFile, out int localW, out int localH)
               && (existingW != localW || existingH != localH);
    }

    private static bool Clashes(OutputTree output, MapCache? maps, string mapAsset, string key) =>
        string.Equals(key, Normalize(mapAsset), StringComparison.OrdinalIgnoreCase)
        || AssetCreator.NonImageOccupant(output, key) != null
        || maps?.TryGetLoaded(key + ".tbin", out _) == true;

    /// <summary>
    /// 换一个没被占用的落点：`Maps/&lt;名&gt;_tilesheet`，还撞就再缀 `2`、`3`… ——
    /// 只看树里与内存里的现状，确定性，不看时钟（同一次运行里反复跑结果一致）。
    /// </summary>
    private static string FreeName(OutputTree output, MapCache? maps, string mapAsset, string key)
    {
        for (int n = 1; ; n++)
        {
            string candidate = n == 1 ? key + "_tilesheet" : $"{key}_tilesheet{n}";
            if (!Clashes(output, maps, mapAsset, candidate)) return candidate;
        }
    }

    /// <summary>
    /// 搬一份：包内同级有就写进产出树，没有就什么都不做（那是游戏内容的图集）。
    /// <para>
    /// <paramref name="image"/> 是**地图里那条引用**（照它在包里找文件），
    /// <paramref name="key"/> 是**落点**（产出树里的内容键）—— 撞名改名时两者不一样。
    /// </para>
    /// </summary>
    private static void CopyOne(OutputTree output, ImageCache images, string packRoot, string mapDir,
        string image, string key, HashSet<string> handled, List<string> notes)
    {
        if (!handled.Add(key)) return;

        string? local = FindLocal(mapDir, image);
        if (local == null)
        {
            // `.xnb` 形式的同级图集我们搬不了（要解码）；语料里没有这种，报出来别静默跳过。
            if (FindLocal(mapDir, image, onlyXnb: true) is string xnb)
            {
                notes.Add($"地图引用的包内同级图集 `{image}` 只有 `.xnb` 形式（{Path.GetFileName(xnb)}），未搬进产出树");
            }
            return;
        }

        string relative;
        try { relative = Path.GetRelativePath(packRoot, local); }
        catch { relative = local; }

        MergeOutcome written = new ImageApplier(output, packRoot, images).Apply(key, relative, key);
        if (!written.Success)
            notes.Add($"地图引用的包内同级图集 `{image}` 没能搬进产出树：{written.FailureReason}");
    }

    /// <summary>SMAPI <c>GetContentKeyForTilesheetImageSource</c>：补 <c>Maps</c> 前缀、去掉 <c>.png</c>。</summary>
    private static string GameContentKey(string image)
    {
        // 归一：`\`→`/`、解析 `.`/`..`（**完整解析**：`../../mine` 要能一路出到内容根，
        // 只剥一层的旧写法会留成 `../mine`，落点就算错）、去掉 `.png`。
        string raw = image.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
        string path = MapPatcher.NormalizeSegments(
            raw.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? raw : "Maps/" + raw);
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path;   // ★ **绝对内容路径**：`Maps/Mines/mine` / 出到内容根的 `TileSheets/furniture`
                       //   （落点、撞名判据都要这个形态；`..` 要**完整解析**，只剥一层的旧写法会把
                       //    `../../mine` 留成 `../mine`，落点就算错了。）
    }

    /// <summary>把 <paramref name="name"/> 放在（可能为空的）子目录 <paramref name="sub"/> 下算内容键。</summary>
    private static string KeyWith(string sub, string name) =>
        GameContentKey(sub.Length == 0 ? name : $"{sub}/{name}");

    /// <summary>
    /// 按 SMAPI 的顺序找：原样、<c>+.png</c>、<c>+.xnb</c>；**再按剥掉每段前导点的名字**找一遍
    /// （包内那份常常就叫 `.mine.png`，而地图里写的可能是 `Mines/.mine.png`）。
    /// </summary>
    private static string? FindLocal(string mapDir, string image, bool onlyXnb = false)
    {
        foreach (string name in new[] { image, StripDots(image) }.Distinct(StringComparer.Ordinal))
        {
            string candidate = Path.Combine(mapDir, name.Replace('/', Path.DirectorySeparatorChar));
            if (!onlyXnb && File.Exists(candidate)) return candidate;

            foreach (string ext in LocalExtensions)
            {
                if (onlyXnb && ext != ".xnb") continue;
                if (File.Exists(candidate + ext)) return candidate + ext;
            }
        }
        return null;
    }

    /// <summary>
    /// 逐段去掉开头的点：`Mines/.mine.png` ⇒ `Mines/mine.png`、`.spring_X.png` ⇒ `spring_X.png`。
    /// 纯 `.`/`/` 之类的空段丢掉，但 **`.` 与 `..` 原样保留**（它们是目录记号，不是前导点）。
    /// </summary>
    private static string StripDots(string image)
    {
        IEnumerable<string> kept = image
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            // ⚠ **`..` 不是"前导点"，是"上一层目录"** —— 剥了它，`../TileSheets/furniture`
            //   会变成 `TileSheets/furniture`，而地图的图集引用是**相对地图自己那层目录**解析的
            //   ⇒ 游戏会去 `Maps/TileSheets/…` 找 ⇒ 地图建不起来（实测 15 张，异常链里看得一清二楚：
            //   `Content\Maps/TileSheets/furniture.xnb`）。
            .Select(part => part is "." or ".." ? part : part.TrimStart('.'))
            .Where(part => part.Length > 0);
        return string.Join('/', kept);
    }

    private static string Normalize(string? image) => (image ?? "").Replace('\\', '/').Trim();

    private static string? SeasonPrefix(string stem)
    {
        foreach (string season in Seasons)
            if (stem.StartsWith(season + "_", StringComparison.OrdinalIgnoreCase)) return stem[..season.Length];
        return null;
    }
}
