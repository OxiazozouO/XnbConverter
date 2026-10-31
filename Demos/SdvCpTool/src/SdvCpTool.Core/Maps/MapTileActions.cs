using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 把**只有装了某个 DLL 才认识**的图块动作改写成原版自带的等价动作。
/// <para>
/// 纯 XNB 装机没有 SMAPI、没有这些 DLL，游戏遇到未知动作是**静默跳过**：
/// <c>performAction</c> 的 <c>default: return false;</c>（`GameLocation.cs:10175`），
/// <c>performTouchAction</c> 的 <c>switch</c> 没有 default、外层 <c>catch</c> 还是空的
/// （`:3605-4012`）。症状就是「地图上看着有路/有门，踩上去什么都不发生」。
/// </para>
/// <para>
/// 已确认的替换（每条都对着反编译的 DLL 源码核过参数顺序）：
/// <list type="bullet">
/// <item><c>TouchAction = LoadMap &lt;loc&gt; &lt;x&gt; &lt;y&gt; [facing]</c>（TMXL Map Toolkit）
/// → <c>Warp &lt;loc&gt; &lt;x&gt; &lt;y&gt;</c>。选 <c>Warp</c> 而不是 <c>MagicWarp</c>：
/// TMXL 的处理器（`TMXLoaderMod.cs:1740`）和 SVE 的兜底（`TouchActionProperties.cs:47`）
/// 都是**立即** <c>warpFarmer</c>，<c>MagicWarp</c> 会多 1 秒延迟和闪屏。
/// 第 4 参是朝向，必须丢 —— 原版 <c>Warp</c>/<c>MagicWarp</c> 的第 4 参是邮件条件。</item>
/// <item><c>Action = RSVTeleport &lt;loc&gt; &lt;x&gt; &lt;y&gt;</c>（RidgesideVillage）
/// → <c>Warp &lt;x&gt; &lt;y&gt; &lt;loc&gt;</c>。原版 **Action** 版 <c>Warp</c> 是
/// <c>Warp &lt;x&gt; &lt;y&gt; &lt;loc&gt; [facing]</c>（`GameLocation.cs:9474`），**参数顺序与
/// TouchAction 版相反**，不能只换名字。RSV 的处理函数直接 <c>warpFarmer(loc,x,y)</c>
/// （`SpiritRealm.cs:264-330`）。</item>
/// <item><c>Action = SunberryTeam.SBVSMAPI_MineLadder &lt;x&gt; &lt;y&gt; &lt;loc&gt;</c>
/// （SunberryVillage）→ <c>Warp &lt;x&gt; &lt;y&gt; &lt;loc&gt;</c>，参数顺序本来就一致
/// （`MapManager.cs:370-390` 的处理函数直接 warp，额外只播一个 `stairsdown` 音效）。</item>
/// <item><c>Action = Spiderbuttons.BETAS_Action Spiderbuttons.BETAS_PlaySound &lt;cue&gt;</c>
/// （BETAS 的动作代理）→ <c>playSound &lt;cue&gt;</c>（原版 Action，`GameLocation.cs:9745`）。</item>
/// </list>
/// <para>
/// ⚠ `TouchAction = SVE_PlaySound &lt;cue&gt;` **不替换**：唯一能借的 <c>PlayEvent</c> 会启动事件
/// （淡入淡出、锁玩家），走过即触发会打断移动，比丢失环境音更糟。
/// </para>
/// </para>
/// </summary>
public static class MapTileActions
{
    /// <summary>原地改写地图里所有已知的 DLL 动作。返回改写的图块数。</summary>
    public static int Rewrite(TBin10 map, string? mapRel = null)
    {
        NormalizeTileSheetImages(map, mapRel);
        int rewritten = 0;
        if (map.Layers == null) return 0;
        foreach (Layer layer in map.Layers)
        {
            if (layer?.Tiles == null) continue;
            foreach (BaseTile? tile in layer.Tiles)
            {
                if (tile?.Properties == null) continue;
                foreach (Propertie property in tile.Properties)
                {
                    if (property.Value is not string value) continue;
                    string? replacement = property.Key switch
                    {
                        "TouchAction" => RewriteTouchAction(value),
                        "Action" => RewriteAction(value),
                        _ => null,
                    };
                    if (replacement == null) continue;
                    property.Value = replacement;
                    rewritten++;
                }
            }
        }
        return rewritten;
    }

    /// <summary>
    /// 把 <c>TileSheets[].Image</c> 归一成**原版地图的写法**。返回改动的图集数。
    /// <para>
    /// 原版地图存的是**相对 <c>Maps/</c> 的名字**（`Maps/Greenhouse` 是 `paths`/`townInterior`，
    /// `Maps/Mines/19` 是 `mine`），游戏在加载图集时自己补 <c>Maps\</c>。TMX 里更常见的是
    /// `../Maps/paths` 这种**相对 TMX 文件**的写法；原样写进 tbin 后：
    /// </para>
    /// <list type="bullet">
    /// <item>SMAPI 的 `GameContentManager` 会把它归一成 `Maps/paths`，所以 GameSmoke 的
    ///   2500 张地图探针**全绿**；</item>
    /// <item>**原版 exe** 不做这层归一 ⇒ `Maps\../Maps\paths` 找不到 ⇒ `GameLocation` 构造时
    ///   `map.LoadTileSheets` 抛异常 ⇒ `AddLocations` 跳过该地点。实测症状：
    ///   `Greenhouse` 没建出来，新游戏 `Farm.AddDefaultBuildings` 里
    ///   `RequireLocation("Greenhouse")` 直接崩掉。</item>
    /// </list>
    /// <para>
    /// 归一规则：`\`→`/`、解析 `.`/`..`、起点补 `Maps/`、去掉 `.png`、最后**剥掉 `Maps/` 前缀**
    /// （非 `Maps/` 下的引用原样保留）。幂等。
    /// </para>
    /// </summary>
    public static int NormalizeTileSheetImages(TBin10 map, string? mapRel = null)
    {
        if (map.TileSheets == null) return 0;
        int changed = 0;
        foreach (TileSheet sheet in map.TileSheets)
        {
            if (sheet == null) continue;
            string image = (sheet.Image ?? "").Replace('\\', '/').Trim();
            if (image.Length == 0) continue;

            // 引用按**地图自己那层目录**解析（`Maps/Mines/19` 里的 `mine` ⇒ `Maps/Mines/mine`），
            // 已经带 `Maps/` 的绝对写法直接用。先还原成内容键，再按目标地图相对化。
            string mapDir = mapRel == null
                ? "Maps"
                : Path.GetDirectoryName(mapRel.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
            string path = image.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
                ? image
                : mapDir + "/" + image;
            path = MapPatcher.NormalizeSegments(path);
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path = path[..^4];

            // 出到内容根的资产（`TileSheets/furniture`）必须写成 `../…`；
            // 没有地图路径时退回旧的「相对 Maps/」形态（只有单测/旧调用会走这里）。
            string normalized = mapRel != null
                ? MapPatcher.Rebase(path, mapRel, alreadyContent: true)
                : path.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
                    ? path["Maps/".Length..]
                    : path;
            if (normalized == sheet.Image) continue;
            sheet.Image = normalized;
            changed++;
        }
        return changed;
    }

    /// <summary><c>LoadMap &lt;loc&gt; &lt;x&gt; &lt;y&gt; [facing]</c> ⇒ <c>Warp &lt;loc&gt; &lt;x&gt; &lt;y&gt;</c>。</summary>
    private static string? RewriteTouchAction(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        // LoadMap <loc> <x> <y> [facing] ⇒ Warp <loc> <x> <y>
        if (string.Equals(parts[0], "LoadMap", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 4) return null;                 // 参数不够：不是可识别的 LoadMap
            return "Warp " + parts[1] + " " + parts[2] + " " + parts[3];
        }

        // ⚠ `SVE_PlaySound` / `playsound` / `PlaySound`（TouchAction）**没有原版等价物**：
        //   唯一能借的 `PlayEvent` 会 `startEvent` —— 淡入淡出、锁玩家、隐藏 HUD
        //   （`GameLocation.cs:15717-15760`），走过即触发会把移动打断，比"没有环境音"更糟。
        //   故保持原告警：纯 XNB 下没有声音，但也不破坏移动。

        // `PoolExit` 在 1.6.15 的游戏/任何已装 DLL 里都没有处理器（agent 全量扫描 239 个 DLL）。
        // 原版 `PoolEntrance` 是**按当前状态切换**：游泳中踩上去就是出水（`:3794-3830`）——
        // 泳池出口的语义正是"出水"，所以映射过去在正常路径上等价。
        if (string.Equals(parts[0], "PoolExit", StringComparison.OrdinalIgnoreCase))
            return "PoolEntrance";

        // 原版动作名大小写敏感；这些是语料里的笔误（agent 扫描：ChangeIntoSwimSuit 53 处等）。
        if (string.Equals(parts[0], "ChangeIntoSwimSuit", StringComparison.OrdinalIgnoreCase))
            return "ChangeIntoSwimsuit";
        if (string.Equals(parts[0], "ChangeOutOFSwimsuit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parts[0], "ChangeOutOfSwimSuit", StringComparison.OrdinalIgnoreCase))
            return "ChangeOutOfSwimsuit";

        return null;
    }

    /// <summary>各种只存在于 DLL 里的 <c>Action</c>。</summary>
    private static string? RewriteAction(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        // 原版大小写敏感：`playsound` / `PlaySound` 是笔误（语料各 8/7 处）。
        if (parts.Length >= 2 &&
            (string.Equals(parts[0], "playsound", StringComparison.Ordinal)
             || string.Equals(parts[0], "PlaySound", StringComparison.Ordinal)))
            return "playSound " + string.Join(' ', parts[1..]);

        // TMXL 的 `Say <text>` 是**原始文本**；原版 `Dialogue <raw text>`（`GameLocation.cs:9620-9628`）。
        // ⚠ 不要映射成 `Message`：后者要的是 `Strings\StringsFromMaps` 的 key。
        if (string.Equals(parts[0], "Say", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            return "Dialogue " + string.Join(' ', parts[1..]);

        // RSVTeleport <loc> <x> <y> ⇒ 原版 Action `Warp <x> <y> <loc>`。
        // ⚠ 不要映射成 `ObeliskWarp`：原版 **Action** 里没有这个名字（只有 `Building.PerformObeliskWarp`），
        //   地图加载时 `GetWarp` 会报 `unknown warp property 'ObeliskWarp …'` 并走 legacy 解析。
        if (string.Equals(parts[0], "RSVTeleport", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length != 4 || !IsInt(parts[2]) || !IsInt(parts[3])) return null;
            return "Warp " + parts[2] + " " + parts[3] + " " + parts[1];
        }

        // 旧产出里已经写成 ObeliskWarp 的（参数是 <loc> <x> <y>）一并改回原版 Warp。
        if (string.Equals(parts[0], "ObeliskWarp", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length != 4 || !IsInt(parts[2]) || !IsInt(parts[3])) return null;
            return "Warp " + parts[2] + " " + parts[3] + " " + parts[1];
        }

        // Sword & Sorcery：`DN.SnS_DuskspireWarp` 的 C# 实现是
        // `Game1.warpFarmer("EastScarp_DuskspireLair", 3, 15, 2)`（反编译 3728 行）。
        if (string.Equals(parts[0], "DN.SnS_DuskspireWarp", StringComparison.OrdinalIgnoreCase))
            return "Warp 3 15 EastScarp_DuskspireLair 2";

        // RSVShop <shopId> ⇒ OpenShop <shopId>（RSV 的 Data/Shops 里就有这些 ID）
        if (string.Equals(parts[0], "RSVShop", StringComparison.OrdinalIgnoreCase) && parts.Length == 2)
            return "OpenShop " + parts[1];

        // Downtown Zuzu 的理发店 ⇒ 原版 WizardShrine（都是 500G + CharacterCustomization(Source.Wizard)）
        if (string.Equals(parts[0], "ZuzuBarber", StringComparison.OrdinalIgnoreCase))
            return "WizardShrine";

        // SBVSMAPI_MineLadder <x> <y> <loc> ⇒ Warp <x> <y> <loc>（顺序已经一致）
        if (string.Equals(parts[0], "SunberryTeam.SBVSMAPI_MineLadder", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length != 4 || !IsInt(parts[1]) || !IsInt(parts[2])) return null;
            return "Warp " + parts[1] + " " + parts[2] + " " + parts[3];
        }

        // BETAS_Action BETAS_PlaySound <cue...> ⇒ playSound <cue...>
        if (string.Equals(parts[0], "Spiderbuttons.BETAS_Action", StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 3
            && string.Equals(parts[1], "Spiderbuttons.BETAS_PlaySound", StringComparison.OrdinalIgnoreCase))
            return "playSound " + string.Join(' ', parts[2..]);

        return null;
    }

    private static bool IsInt(string text) => int.TryParse(text, out _);
}
