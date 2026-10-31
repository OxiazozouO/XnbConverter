using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP <c>EditMap</c> 的<b>属性类</b>改动（Task 12a）—— <c>MapProperties</c>、<c>AddWarps</c>、
/// <c>AddNpcWarps</c>，以及指向 <c>MapProperties</c> 的 <c>TextOperations</c>。
/// <para>
/// <b>完全不碰瓦片几何</b>（<c>MapTiles</c> / <c>FromFile</c> / <c>PatchMode</c> 一律不写）。
/// 只动 <see cref="TBin10.Properties"/>，属 tbin 里最安全的一层。
/// </para>
/// <para>
/// 依据 CP 源码 <c>EditMapPatch.cs</c>：
/// <c>MapProperties</c> 值为 <c>null</c> 即<b>删除</b>该键（:105-118）；
/// <c>AddWarps</c>/<c>AddNpcWarps</c> 先 <b>Reverse</b>（:59-60）再拼串，
/// 并把新串<b>前置</b>到已有值（:300-320，<c>(新 + " " + 旧).Trim()</c>）。
/// </para>
/// </summary>
public static class MapApplier
{
    /// <summary>tbin 里字符串属性的类型字节（<c>PropertieReader.Read</c>：0=bool 1=int 2=float 3=string）。</summary>
    private const byte StringType = 3;

    /// <summary>
    /// 把一条 <c>EditMap</c> 的改动应用到产出树里的地图 tbin（原地改写）。
    /// </summary>
    /// <param name="packRoot">
    /// 内容包根目录 —— <c>FromFile</c> 相对它解析。给了 <c>FromFile</c> 却没给根目录时**失败**，
    /// 不去猜路径。
    /// </param>
    /// <param name="cache">
    /// 地图内存模型缓存。给了它 ⇒ 同一张地图**只读一次**、改动累积在内存、由调用方在
    /// 全部补丁应用完之后调 <see cref="MapCache.Flush"/> 统一写回。
    /// <para>
    /// 不给（<c>null</c>）⇒ 保持原来的「读→改→立刻写」语义，现有测试与单条调用不受影响。
    /// </para>
    /// <para>
    /// 为什么要它：实测本语料 <c>EditMap</c> 802 条打在 122 个目标上，其中 **80 个目标被多个
    /// 补丁改**（覆盖 760 条）—— <c>Maps/Town</c> 被 81 条、<c>Maps/Forest</c> 80 条。
    /// 不缓存就是读写 81 / 80 次整张 tbin。
    /// </para>
    /// </param>
    public static MergeOutcome Apply(OutputTree output, string assetName, JObject patch,
        string? packRoot = null, MapCache? cache = null, ImageCache? images = null,
        string? doorGateGsq = null, PackTranslations? translations = null,
        bool weatherDayNightOverlay = false, bool dayNightVisibleAtDay = false)
    {
        string rel = AssetCreator.RelativePath(assetName, ".tbin");

        string? invalid = output.ValidatePath(rel);
        if (invalid != null)
            return MergeOutcome.Fail($"目标地图 `{assetName}` 的输出路径非法：{invalid}");

        string path = output.ToOutputPath(rel);

        var notes = new List<string>();

        TBin10Reader reader;
        TBin10 map;
        if (cache != null)
        {
            // 缓存优先：本次运行里第一次碰它才去读（可能还在暂存里、磁盘上没有）。
            if (!cache.TryGetModel(rel, () => output.ReadBytesOrNull(rel),
                    out reader, out map, out string? cacheError))
                return MergeOutcome.Fail(cacheError!);
        }
        else
        {
            byte[]? payload = output.ReadBytesOrNull(rel);
            if (payload == null)
                return MergeOutcome.Fail($"地图 tbin 不在资源树里：{rel}");

            try
            {
                reader = TBin10Reader.Create(payload);
                map = (TBin10)reader.Read();
            }
            catch (Exception ex)
            {
                return MergeOutcome.Fail($"读取地图 tbin 失败：{ex.Message}");
            }
        }

        if (patch["FromFile"] is JToken fromFileToken)
        {
            MergeOutcome merged = ApplyFromFile(output, images!, cache, map, assetName, patch,
                fromFileToken.Value<string>() ?? "", packRoot,
                weatherDayNightOverlay, dayNightVisibleAtDay);
            if (merged.Success) notes.AddRange(merged.Notes);   // 图集搬运的提示要带上来
            if (!merged.Success) return merged;
        }

        map.Properties ??= new List<Propertie>();

        // ★ 下面五段的**顺序照抄 CP** `EditMapPatch.Edit`（反编译 :13860-13930）：
        //   FromFile → MapTiles → MapProperties → AddWarps → TextOperations。
        //   顺序会改结果（同一补丁里既有 MapProperties 又有指向同名的 TextOperations 时），
        //   见 `MapApplierTests.MapProperties先于TextOperations生效`。

        // ── MapTiles ──
        if (patch["MapTiles"] is JArray mapTiles)
        {
            MergeOutcome t = ApplyMapTiles(map, mapTiles, translations);
            if (!t.Success) return t;
            notes.AddRange(t.Notes);          // 逐块跳过的原因要往上带，报告里才看得见
        }

        // ── MapProperties：值为 null 即删除 ──
        if (patch["MapProperties"] is JObject props)
        {
            foreach (JProperty p in props.Properties())
            {
                if (p.Value.Type == JTokenType.Null) RemoveProperty(map, p.Name);
                else SetStringProperty(map, p.Name, p.Value.ToString(), translations);
            }
        }

        // ── AddWarps / AddNpcWarps ──
        MergeOutcome r = ApplyWarps(map, patch["AddNpcWarps"] as JArray, "NPCWarp");
        if (!r.Success) return r;
        notes.AddRange(r.Notes);
        r = ApplyWarps(map, patch["AddWarps"] as JArray, "Warp");
        if (!r.Success) return r;
        notes.AddRange(r.Notes);

        // ── TextOperations（只支持根为 MapProperties 的）：先把属性镜像成 JObject 交给既有合并器，
        //    再把**被点名的**属性写回，避免为 tbin 重写一遍文本操作逻辑。 ──
        if (patch["TextOperations"] is JArray ops && ops.Count > 0)
        {
            if (!ops.All(o => IsMapPropertiesRoot(o["Target"] as JArray)))
                return MergeOutcome.Fail("只支持 Target 根为 `MapProperties` 的 TextOperations（不碰瓦片几何）");

            // `TryApplyToEntries` 的 data 就是「条目字典本身」，地图属性与 Entries 同形 ——
            // 所以这里直接把属性镜像成 JObject 传进去，不用再包一层。
            var mirror = new JObject(
                map.Properties.Select(p => new JProperty(p.Key, Stringify(p.Value))));

            MergeOutcome merged = TextOperationMerger.Apply(mirror, ops, assetName);
            if (!merged.Success) return merged;

            WriteBackNamed(map, (JObject)merged.Data!, ops, translations);
        }

        // ★ **条件化地图改动的门禁**：这条补丁的条件是**持久型**（判据见 `ConditionPersistence`），
        //   而静态 XNB 的地图没有条件化 Warp ⇒ 改动**无条件落图**、条件由**图块门禁**承担
        //   （`TouchAction = ConditionalDoor <GSQ>`，游戏源码 `GameLocation.cs:3593/3907`）。
        if (doorGateGsq != null) ApplyDoorGate(map, patch, doorGateGsq, notes);
        int legacyActions = MapTileActions.Rewrite(map);
        if (legacyActions > 0)
            notes.Add($"DLL 才认识的图块动作 **{legacyActions}** 处改写成原版等价动作"
                      + "（`LoadMap`→`Warp` 等；纯 XNB 没有那些处理器，原样留着就是踩上去没反应的死图块）");

        // ★ 地图属性里的 `{{i18n:…}}`：CP 在写入时会代掉，我们原先原样落进 tbin
        //   （实测 SVE 的 DarkShrine/Townhouse/TreasureCave 对话文字）。地图是单资产，按中文冻结。
        int mapI18n = TranslateMap(map, translations);
        if (mapI18n > 0)
            notes.Add($"地图属性里的 `{{{{i18n:}}}}` 展开 **{mapI18n}** 处（中文/默认译文，地图无语言变体）");

        // 有缓存 ⇒ 不在这里写：改动已经落在内存里的那个 map 上，
        // 由调用方在所有补丁应用完之后 `MapCache.Flush()` 统一写回。
        if (cache == null)
        {
            try
            {
                reader.Save(map, path);
            }
            catch (Exception ex)
            {
                return MergeOutcome.Fail($"写回地图 tbin 失败：{ex.Message}");
            }
        }
        else
        {
            // 改成功 ⇒ 这张地图的落盘权归缓存，产出树别再写它那份刚 Load 进来的原始字节。
            cache.MarkModified(rel);
        }

        return notes.Count == 0 ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes.Distinct().ToList());
    }

    /// <summary>
    /// **条件化地图改动的门禁**：在 <c>ToArea</c> 的每一块图块上写
    /// <c>TouchAction = ConditionalDoor &lt;补丁条件的 GSQ&gt;</c>。
    /// <para>
    /// 依据（都在游戏源码里）：① `TouchAction` 是**`Back` 图层**的图块属性，玩家踩上去时处理
    /// （`GameLocation.cs:3593` "Handle a `TouchAction` property from a `Back` map tile"）；
    /// ② `performTouchAction` 的 `ConditionalDoor` 分支在 GSQ 为**假**时把玩家**弹回**（`:3907`：
    /// `if (… || CheckConditions(…)) break;` ⇒ 条件真就放行，假才走弹回）。
    /// ⇒ 门禁里要写**补丁条件本身**（条件成立 ⇒ 放行；不成立 ⇒ 弹回）。
    /// </para>
    /// <para>
    /// ⚠ 2026-10-18 订正：第一版写的是**取反**的 GSQ，方向正好反了 —— Standard 农场（条件成立）
    /// 反而被自己的门禁弹回，Grandpa's Farm 的 Backwoods 楼梯因此上不去。别再取反。
    /// </para>
    /// <para>
    /// ⚠ **不覆盖已有的 `TouchAction`**（那是原版或包自己的行为，踩上去该触发什么就触发什么）
    /// —— 那些图块跳过并记一笔。`Warp`/`Action` 是**别的键**，不受影响。
    /// </para>
    /// </summary>
    private static void ApplyDoorGate(TBin10 map, JObject patch, string gsq, List<string> notes)
    {
        // 要落门禁的格子 = `MapTiles` 的 Position（**精确**）+ `ToArea`（矩形，若给了）
        var cells = new HashSet<(int X, int Y)>();
        if (patch["MapTiles"] is JArray tiles)
        {
            foreach (JToken t in tiles)
            {
                if (t is JObject o && TryParsePosition(o["Position"], out int cx, out int cy, out _))
                    cells.Add((cx, cy));
            }
        }

        var areaPatch = new Cp.ChangePatch { Raw = patch };
        if (EditImageApplier.TryParseAreas(areaPatch, out _, out ImageArea? toArea, out _) && toArea != null)
        {
            ImageArea a = toArea.Value;
            for (int y = a.Y; y < a.Y + a.Height; y++)
                for (int x = a.X; x < a.X + a.Width; x++)
                    cells.Add((x, y));
        }

        if (cells.Count == 0)
        {
            notes.Add("门禁未落：这条补丁没给出可定位的图块（`MapTiles`/`ToArea` 都没有）");
            return;
        }

        // ⚠ `TouchAction` **只在 `Back` 图层**被读（`GameLocation.cs:3593`）——
        //    而包自己的 `Action`（如 `MinecartTransport …`）通常在 `Buildings` 层 ⇒ 两者互不干扰。
        Layer? back = map.Layers?.FirstOrDefault(
            l => string.Equals(l.Id, "Back", StringComparison.OrdinalIgnoreCase));
        if (back?.Tiles == null)
        {
            notes.Add("门禁未落：这张图没有 `Back` 图层（`TouchAction` 只在那一层生效）");
            return;
        }

        string action = "ConditionalDoor " + gsq;
        int written = 0, skipped = 0;
        foreach ((int x, int y) in cells)
        {
            if (x < 0 || y < 0 || x >= back.LayerSize.X || y >= back.LayerSize.Y)
            {
                skipped++;
                continue;
            }
            int flat = y * back.LayerSize.X + x;
            if (flat >= back.Tiles.Count || back.Tiles[flat] is not BaseTile tile)
            {
                skipped++;
                continue;
            }
            tile.Properties ??= new List<Propertie>();
            // 已有 TouchAction ⇒ 那是这块图原本的行为，不动它（`Warp`/`Action` 是别的键，同样不碰）
            if (tile.Properties.Any(p => string.Equals(p.Key, "TouchAction", StringComparison.Ordinal)))
            {
                skipped++;
                continue;
            }
            // `Propertie.Type` 是**字节**，`0x03` = 字符串（见 `TmxToTbin`）
            tile.Properties.Add(new Propertie { Key = "TouchAction", Type = 0x03, Value = action });
            written++;
        }

        notes.Add($"**门禁**：{cells.Count} 格上写了 **{written}** 块 `TouchAction = {action}`"
                  + "（条件不成立时踩上去被弹回，`Back` 层）"
                  + (skipped > 0 ? $"；{skipped} 格跳过（越界/空格子/已有 TouchAction）" : ""));
    }

    /// <summary>
    /// <c>MapTiles</c> 的 <b>SetProperties</b>（改图块属性）。
    /// <para>
    /// 只做 <c>SetProperties</c>：<c>SetIndex</c>/<c>SetTilesheet</c>/<c>Remove</c> 会改「画哪个图块 / 有没有图块」，
    /// 超出本任务范围，遇到即整条失败。
    /// </para>
    /// <para>
    /// 依 CP 源码 <c>EditMapPatch.cs:221-296</c>：
    /// <b>空位不创建</b>（"the map has no tile at ..."）；
    /// 有属性要写时<b>新建一个 <c>StaticTile</c></b> —— 原格即使是 <c>AnimatedTile</c> 也会被替换掉（照抄 CP 行为）。
    /// </para>
    /// </summary>
    /// <summary>
    /// <c>FromFile</c>：把另一份地图合进来。合并**交给 <see cref="MapPatcher"/>**（照抄 SMAPI 的
    /// <c>PatchMap</c>/<c>ExtendMap</c>），这里只负责找文件、解析参数、按 CP 的顺序调用。
    /// <para>
    /// 顺序照抄 CP <c>EditMapPatch.cs:188-217</c>：先定 <c>FromArea</c>/<c>ToArea</c>，
    /// 校验两者尺寸相等，再 <c>ExtendMap</c> 把目标撑到 <c>ToArea</c> 的右下角，最后 <c>PatchMap</c>。
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyFromFile(OutputTree output, ImageCache images, MapCache? cache,
        TBin10 target, string assetName,
        JObject patch, string fromFile, string? packRoot,
        bool weatherDayNightOverlay, bool dayNightVisibleAtDay)
    {
        if (string.IsNullOrWhiteSpace(fromFile))
            return MergeOutcome.Fail("EditMap 的 FromFile 为空");

        if (packRoot == null)
            return MergeOutcome.Fail("EditMap 带 FromFile，但写入时没有内容包根目录，无法解析来源地图");

        string resolved = LoadApplier.ResolveFirstValidFile(
            LoadApplier.ExpandFieldTokens(fromFile, assetName), packRoot);
        if (resolved.Contains("{{", StringComparison.Ordinal))
            return MergeOutcome.Fail($"FromFile 含无法静态解析的令牌：{fromFile}");

        string full;
        try { full = Path.GetFullPath(Path.Combine(packRoot, resolved.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception ex) { return MergeOutcome.Fail($"FromFile 路径无效：{ex.Message}"); }
        if (!File.Exists(full)) return MergeOutcome.Fail($"找不到 FromFile：{resolved}");

        TBin10? source;
        switch (Path.GetExtension(resolved).ToLowerInvariant())
        {
            case ".tbin":
                // 走源地图解码缓存：同一张源地图常被贴到多张目标地图上。
                // ⚠ 缓存的对象只读 —— `MapPatcher.PatchMap` 只从 source 取内容造新对象，从不改它。
                if (cache != null)
                {
                    if (!cache.TryGetSource(full, () => TBin10Reader.FormFile(full),
                            out source, out string? srcError))
                        return MergeOutcome.Fail($"来源地图 tbin 读取失败：{srcError}");
                }
                else
                {
                    try { source = TBin10Reader.FormFile(full); }
                    catch (Exception ex) { return MergeOutcome.Fail($"来源地图 tbin 读取失败：{ex.Message}"); }
                }
                break;

            case ".tmx":
                if (cache != null)
                {
                    if (!cache.TryGetSource(full, () =>
                        {
                            if (!TmxToTbin.TryConvert(full, out TBin10? converted, out string? tmxError, out _))
                                throw new InvalidOperationException(tmxError);
                            return converted!;
                        }, out source, out string? tmxCachedError))
                        return MergeOutcome.Fail($"来源地图 tmx 转换失败：{tmxCachedError}");
                }
                else if (!TmxToTbin.TryConvert(full, out source, out string? tmxError, out _))
                {
                    return MergeOutcome.Fail($"来源地图 tmx 转换失败：{tmxError}");
                }
                break;

            default:
                return MergeOutcome.Fail(
                    $"FromFile `{resolved}` 不是地图（扩展名 {Path.GetExtension(resolved)}）—— "
                    + "EditMap 的 FromFile 只处理 .tbin / .tmx");
        }

        if (source == null) return MergeOutcome.Fail($"来源地图解析为空：{resolved}");

        // 区域解析与 EditImage 共用同一个函数（形状完全一样），不另写一份。
        var areaPatch = new Cp.ChangePatch { Raw = patch };
        if (!EditImageApplier.TryParseAreas(areaPatch, out ImageArea? fromArea, out ImageArea? toArea,
                out string? areaError))
            return MergeOutcome.Fail($"FromArea/ToArea 解析失败：{areaError}");

        // CP：FromArea 缺省 = 整幅源图；ToArea 缺省 = (0,0, FromArea.W, FromArea.H)。
        (int sw, int sh) = MapSize(source);
        ImageArea sa = fromArea ?? new ImageArea(0, 0, sw, sh);
        ImageArea ta = toArea ?? new ImageArea(0, 0, sa.Width, sa.Height);
        if (sa.Width != ta.Width || sa.Height != ta.Height)
            return MergeOutcome.Fail(
                $"FromArea {sa.Width}x{sa.Height} 与 ToArea {ta.Width}x{ta.Height} 尺寸不等（同 CP 直接失败）");

        // CP 缺省 PatchMode 是 **ReplaceByLayer**（PatchLoader.cs:594 的 (PatchMapMode)1），
        // 不是 SMAPI 签名上的 Overlay。差这一步就会静默写出错地图。
        MapPatchMode mode = MapPatchMode.ReplaceByLayer;
        string? raw = patch.Value<string>("PatchMode");
        if (!string.IsNullOrWhiteSpace(raw)
            && !Enum.TryParse(raw, ignoreCase: true, out mode))
        {
            return MergeOutcome.Fail(
                $"PatchMode `{raw}` 非法；EditMap 只认 Overlay / ReplaceByLayer / Replace（同 CP）");
        }

        MapPatcher.ExtendMap(target, ta.X + ta.Width, ta.Y + ta.Height);

        // 补丁块自己的图集也可能来自**包内同级**（SMAPI 按模组目录找图集；纯 XNB 里没有那一层）。
        // 合并后 `PatchMap` 会把它们作为 `z_<id>` 图集挂到目标地图上，名字照抄块里的 ——
        // 所以那些图片也得一起搬进产出树，否则**目标地图整个建不出来**（实机：
        // `Maps/Forest` 引用了鸟会包里的 `spring_exterior` 却找不到）。
        // ⚠ 落点撞上别的资产时它会**改名，并就地改 `source` 里那条图集引用**（`PatchMap` 随后照抄新名）——
        //   改名只看树里有没有占位文件，同一份源地图反复用结果一致。
        var sheetNotes = new List<string>();
        // `images` 必传：图片的落盘权在它手里（它会把产出树那条暂存撤掉），不传就是图静默不落盘。
        LocalMapTilesheets.Copy(output,
            images ?? throw new ArgumentNullException(nameof(images), "EditMap 需要调用方传入 ImageCache"),
            cache, packRoot, full, assetName, source, forTarget: false, sheetNotes);

        if (weatherDayNightOverlay)
        {
            MergeOutcome overlay = ApplyWeatherDayNightOverlay(
                target, source, assetName, sa, ta, images!, dayNightVisibleAtDay);
            if (!overlay.Success) return overlay;
            sheetNotes.AddRange(overlay.Notes);
            return sheetNotes.Count == 0
                ? MergeOutcome.Ok()
                : MergeOutcome.Ok(null, sheetNotes.Distinct().ToList());
        }

        MergeOutcome patched = MapPatcher.PatchMap(target, source, (sa.X, sa.Y, sa.Width, sa.Height),
            (ta.X, ta.Y, ta.Width, ta.Height), mode, out string? error, assetName)
            ? (sheetNotes.Count == 0
                ? MergeOutcome.Ok()
                : MergeOutcome.Ok(null, sheetNotes.Distinct().ToList()))
            : MergeOutcome.Fail($"合并地图失败：{error}");
        return patched;
    }

    /// <summary>
    /// 把“雨天/纯夜间叠加层”落成原版 <c>DayTiles</c>/<c>NightTiles</c>。
    /// <para>
    /// 叠加层在目标地图里**默认是透明的**（每格写一个空图块索引）；
    /// <c>NightTiles</c> 在夜晚/雨天把每格换回源图块，<c>DayTiles</c> 在晴天白天换回空图块。
    /// 这样不需要任何条件图层，代价是晴天夜里也会显示（见 <see cref="WeatherMapOverlay"/>）。
    /// </para>
    /// <para>
    /// 源图块必须是静态图块；动画叠加层没有“同一图集内的空索引”可退，直接判写入失败，
    /// 不硬写。目标地图里若已有同名图层，改用一个新的 <c>AlwaysFront/Back/...&lt;n&gt;</c> 名字，
    /// 避免覆盖原地图图层。
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyWeatherDayNightOverlay(TBin10 target, TBin10 source,
        string assetName, ImageArea sa, ImageArea ta, ImageCache images, bool visibleAtDay)
    {
        var notes = new List<string>();
        var usedLayers = new List<(Layer Source, string TargetId, List<(int X, int Y, StaticTile Tile)> Cells)>();
        foreach (Layer layer in source.Layers)
        {
            var cells = new List<(int X, int Y, StaticTile Tile)>();
            for (int y = sa.Y; y < sa.Y + sa.Height; y++)
            {
                for (int x = sa.X; x < sa.X + sa.Width; x++)
                {
                    if (x < 0 || y < 0 || x >= layer.LayerSize.X || y >= layer.LayerSize.Y)
                        continue;
                    BaseTile? tile = layer.Tiles[y * layer.LayerSize.X + x];
                    if (tile == null) continue;
                    if (tile is not StaticTile st)
                        return MergeOutcome.Fail(
                            $"天气叠加层 `{layer.Id}` 含动画图块（{tile.GetType().Name}），"
                            + "原版 DayTiles/NightTiles 只能换静态索引，无法保留动画");
                    cells.Add((x, y, st));
                }
            }
            if (cells.Count == 0) continue;
            // `Paths`/`Schedule`/`MainEvent` 等是数据层或路径指令层，不是视觉层；
            // 目标地图已经有自己的一份，天气/昼夜叠加不能条件切换它们，跳过即可。
            if (IsDataLayer(layer.Id)) continue;

            string? id = FreeLayerId(target, layer.Id);
            if (id == null)
            {
                notes.Add($"天气叠加层 `{layer.Id}` 与目标图层同名且不是可排序的绘制层，已跳过");
                continue;
            }
            usedLayers.Add((layer, id, cells));
        }

        if (usedLayers.Count == 0)
            return MergeOutcome.Fail("天气叠加层源地图在 ToArea 内没有图块");

        // 每张图集找一个完全透明的图块索引；找不到就追加一行透明像素（只改产出树那张图）。
        var blanks = new Dictionary<string, TileSheet>(StringComparer.Ordinal);
        var blankIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (StaticTile tile in usedLayers.SelectMany(l => l.Cells).Select(c => c.Tile))
        {
            if (blanks.ContainsKey(tile.TileSheet)) continue;
            TileSheet? sheet = source.TileSheets?.FirstOrDefault(s => s.Id == tile.TileSheet);
            if (sheet == null)
                return MergeOutcome.Fail($"天气叠加层引用了源地图里不存在的图集 `{tile.TileSheet}`");

            if (!TryBlankIndex(sheet, assetName, images, out int blank, out TileSheet blankSheet,
                    out string? error))
                return MergeOutcome.Fail(error!);
            blanks[tile.TileSheet] = blankSheet;
            blankIndexes[tile.TileSheet] = blank;
            if (!Equals(blankSheet.SheetSize, sheet.SheetSize))
                notes.Add($"图集 `{sheet.Id}` 没有透明图块，已在产出图片末尾追加一行透明块（索引 {blank}）");
        }

        // 用“每个非空格都换成透明索引”的源地图走正常 PatchMap，目标图层里一开始就是空的。
        var blankSource = new TBin10
        {
            Format = source.Format,
            Id = source.Id,
            Description = source.Description,
            Properties = new List<Propertie>(),
            TileSheets = blanks.Values.ToList(),
            Layers = new List<Layer>(),
        };
        foreach ((Layer srcLayer, string targetId, _) in usedLayers)
        {
            var tiles = new List<BaseTile>(srcLayer.Tiles.Count);
            for (int y = 0; y < srcLayer.LayerSize.Y; y++)
            {
                for (int x = 0; x < srcLayer.LayerSize.X; x++)
                {
                    // ToArea 之外不参与 PatchMap，也不要把那些格子的图集带进来
                    // （它们可能用到本次没算 blankIndex 的图集）。
                    bool inArea = x >= sa.X && y >= sa.Y
                                  && x < sa.X + sa.Width && y < sa.Y + sa.Height;
                    BaseTile? tile = inArea ? srcLayer.Tiles[y * srcLayer.LayerSize.X + x] : null;
                    tiles.Add(tile is StaticTile st
                        ? new StaticTile
                        {
                            TileSheet = st.TileSheet,
                            TileIndex = blankIndexes[st.TileSheet],
                            BlendMode = st.BlendMode,
                            Properties = new List<Propertie>(),
                        }
                        : null!);
                }
            }
            var blankLayer = new Layer
            {
                Id = targetId,
                Visible = srcLayer.Visible,
                Description = "",
                LayerSize = new IntVector2 { X = srcLayer.LayerSize.X, Y = srcLayer.LayerSize.Y },
                TileSize = srcLayer.TileSize,
                Properties = new List<Propertie>(),
                Tiles = tiles,
                Index = new List<char>(),
                _sizeArr = new List<int>(),
                _currTileSheet = new List<string>(),
            };
            LayerRle.Rebuild(blankLayer);
            blankSource.Layers.Add(blankLayer);
        }

        if (!MapPatcher.PatchMap(target, blankSource, (sa.X, sa.Y, sa.Width, sa.Height),
                (ta.X, ta.Y, ta.Width, ta.Height), MapPatchMode.ReplaceByLayer,
                out string? mergeError, assetName))
            return MergeOutcome.Fail($"写入天气叠加层失败：{mergeError}");

        var day = new System.Text.StringBuilder();
        var night = new System.Text.StringBuilder();
        int writtenCells = 0;
        foreach ((_, string targetId, List<(int X, int Y, StaticTile Tile)> list) in usedLayers)
        {
            foreach ((int sx, int sy, StaticTile tile) in list)
            {
                int tx = ta.X + (sx - sa.X);
                int ty = ta.Y + (sy - sa.Y);
                if (tile.TileIndex == blankIndexes[tile.TileSheet]) continue;

                int blank = blankIndexes[tile.TileSheet];
                int dayIndex = visibleAtDay ? tile.TileIndex : blank;
                int nightIndex = visibleAtDay ? blank : tile.TileIndex;
                if (day.Length > 0) day.Append(' ');
                day.Append(targetId).Append(' ').Append(tx).Append(' ').Append(ty)
                   .Append(' ').Append(dayIndex);
                if (night.Length > 0) night.Append(' ');
                night.Append(targetId).Append(' ').Append(tx).Append(' ').Append(ty)
                     .Append(' ').Append(nightIndex);
                writtenCells++;
            }
        }

        if (writtenCells == 0)
            return MergeOutcome.Fail("天气叠加层全是透明图块，没有可写的可见内容");

        AppendMapProperty(target, "DayTiles", day.ToString());
        AppendMapProperty(target, "NightTiles", night.ToString());
        notes.Add($"天气叠加层：{usedLayers.Count} 个图层、{writtenCells} 格写入 DayTiles/NightTiles"
                  + (visibleAtDay
                      ? "（白天显示，雨天白天会隐藏）"
                      : "（雨天/夜晚显示，晴天夜里也会显示）"));
        return MergeOutcome.Ok(null, notes);
    }

    /// <summary>
    /// 原版地图里的数据/指令层：不是按图块绘制的视觉层，不能靠 Day/Night 条件切换。
    /// 这些层在源叠加地图里出现时直接跳过，目标地图保留自己原有的那一份。
    /// </summary>
    private static bool IsDataLayer(string id) =>
        id.Equals("Paths", StringComparison.OrdinalIgnoreCase)
        || id.Equals("Schedule", StringComparison.OrdinalIgnoreCase)
        || id.Equals("MainEvent", StringComparison.OrdinalIgnoreCase)
        || id.Equals("Set-Up", StringComparison.OrdinalIgnoreCase)
        || id.Equals("SetUp", StringComparison.OrdinalIgnoreCase);

    /// <summary>在目标地图里找一个不撞名的图层 Id；只改 `<前缀>&lt;数字&gt;` 这类原版会画的名字。</summary>
    private static string? FreeLayerId(TBin10 target, string sourceId)
    {
        if (target.Layers.All(l => !string.Equals(l.Id, sourceId, StringComparison.Ordinal)))
            return sourceId;

        string[] prefixes = { "AlwaysFront", "Back", "Buildings", "Front" };
        foreach (string prefix in prefixes)
        {
            if (!sourceId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string suffix = sourceId[prefix.Length..];
            int n = int.TryParse(suffix, out int parsed) ? parsed : 0;
            while (target.Layers.Any(l => string.Equals(l.Id, prefix + (n + 1), StringComparison.Ordinal)))
                n++;
            return prefix + (n + 1);
        }
        return null;
    }

    /// <summary>
    /// 找一张图集里的透明图块；没有就追加一行透明像素并返回新索引。
    /// 返回的 <paramref name="sheet"/> 是**克隆**（不修改源地图缓存）。
    /// </summary>
    private static bool TryBlankIndex(TileSheet sourceSheet, string assetName, ImageCache images,
        out int blankIndex, out TileSheet sheet, out string? error)
    {
        blankIndex = -1;
        sheet = CloneSheet(sourceSheet);
        error = null;

        if (sourceSheet.Margin.X != 0 || sourceSheet.Margin.Y != 0
            || sourceSheet.Spacing.X != 0 || sourceSheet.Spacing.Y != 0)
        {
            error = $"图集 `{sourceSheet.Id}` 有 margin/spacing，天气叠加层无法安全计算透明图块";
            return false;
        }

        string image = (sourceSheet.Image ?? "").Replace('\\', '/').Trim();
        if (image.Length == 0)
        {
            error = $"图集 `{sourceSheet.Id}` 没有图片引用";
            return false;
        }
        string key = MapPatcher.NormalizeSegments(
            image.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? image : "Maps/" + image);
        if (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) key = key[..^4];
        key += ".png";

        if (!images.TryGetPixels(key, out byte[] pixels, out int width, out int height, out string? readError))
        {
            error = $"图集 `{sourceSheet.Id}` 读取失败（{readError}）";
            return false;
        }

        int tw = sourceSheet.TileSize.X, th = sourceSheet.TileSize.Y;
        // SVE 的 z_Forest_mist.png 比 SheetSize 宽 10 像素（56 列 = 896，实际 906）：
        // xTile 只按 SheetSize 取格，右侧多出来的像素不参与索引。这里也以 SheetSize 为准，
        // 不能因为“图片宽度不整除”就把整层判死。
        int cols = sourceSheet.SheetSize?.X ?? width / tw;
        int rows = sourceSheet.SheetSize?.Y ?? height / th;
        if (tw <= 0 || th <= 0 || cols <= 0 || rows <= 0
            || cols * tw > width || rows * th > height)
        {
            error = $"图集 `{sourceSheet.Id}` 的图片尺寸 {width}x{height} 与图块 {tw}x{th}"
                    + $" / SheetSize {cols}x{rows} 不匹配";
            return false;
        }
        for (int idx = 0; idx < cols * rows; idx++)
        {
            if (IsTransparentTile(pixels, width, height, tw, th, cols, idx))
            {
                blankIndex = idx;
                sheet.SheetSize = new IntVector2 { X = cols, Y = rows };
                return true;
            }
        }

        // 没有空图块：追加一行透明像素，索引 = 实际图片末尾的下一格。
        // 图片可能比 SheetSize 多出底部像素（Forest 就是右侧多 10px），
        // 这时要按实际高度算索引，不能按 SheetSize 算。
        int imageRows = height / th;
        int appendRow = Math.Max(rows, imageRows);
        int newHeight = height + th;
        var expanded = ByteBufferPool.Rent(width * newHeight * 4);
        Array.Copy(pixels, expanded, pixels.Length);
        images.SetPixels(key, expanded, width, newHeight);
        blankIndex = cols * appendRow;
        sheet.SheetSize = new IntVector2 { X = cols, Y = appendRow + 1 };
        return true;
    }

    private static bool IsTransparentTile(byte[] pixels, int width, int height,
        int tw, int th, int cols, int index)
    {
        int x0 = (index % cols) * tw;
        int y0 = (index / cols) * th;
        if (x0 < 0 || y0 < 0 || x0 + tw > width || y0 + th > height) return false;
        for (int y = y0; y < y0 + th; y++)
            for (int x = x0; x < x0 + tw; x++)
                if (pixels[(y * width + x) * 4 + 3] != 0) return false;
        return true;
    }

    private static TileSheet CloneSheet(TileSheet src) => new()
    {
        Id = src.Id,
        Description = src.Description ?? "",
        Image = src.Image,
        SheetSize = src.SheetSize,
        TileSize = src.TileSize,
        Margin = src.Margin,
        Spacing = src.Spacing,
        Properties = src.Properties == null
            ? new List<Propertie>()
            : src.Properties.Select(p => new Propertie { Key = p.Key, Type = p.Type, Value = p.Value }).ToList(),
    };

    /// <summary>把新串前置到已有地图属性（与 AddWarps 同样的拼法；这些属性由游戏按空格切分）。</summary>
    private static void AppendMapProperty(TBin10 map, string key, string value)
    {
        string existing = map.Properties
            .FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))
            ?.Value?.ToString() ?? "";
        SetStringProperty(map, key, (value + " " + existing).Trim());
    }

    private static (int W, int H) MapSize(TBin10 map)
    {
        int w = 0, h = 0;
        foreach (Layer l in map.Layers)
        {
            if (l.LayerSize.X > w) w = l.LayerSize.X;
            if (l.LayerSize.Y > h) h = l.LayerSize.Y;
        }
        return (w, h);
    }

    /// <summary>
    /// 应用 `MapTiles`。**逐块独立**：一块落不下去只记 <c>note</c>、继续下一块 ——
    /// 依据 CP <c>EditMapPatch</c> 的 <c>AppliesTilePatches</c> 分支：
    /// <c>if (!TryApplyTile(...)) WarnForPatch($"MapTiles &gt; entry {n} couldn't be applied: {error}");</c>，
    /// 它**不抛也不作废补丁**（同一补丁里的 <c>MapProperties</c>/<c>AddWarps</c>/<c>TextOperations</c> 照做）。
    /// <para>
    /// ⚠ 与 CP 一样分两级：**解析期**（<c>Layer</c>/<c>Position</c>/<c>SetIndex</c>/<c>Remove</c> 本身
    /// 无法解析）CP 是 <c>TrackSkip</c>：整条补丁不创建 —— 这一档我们仍判 <c>Fail</c>。
    /// **运行期**（图层/图集不存在、位置越界、空位上没给全 SetTilesheet+SetIndex）才是逐块警告。
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyMapTiles(TBin10 map, JArray entries,
        PackTranslations? translations)
    {
        var notes = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            string label = $"MapTiles #{i + 1}";

            // ── 解析期：CP 会整条 TrackSkip ──
            if (entries[i] is not JObject entry)
                return MergeOutcome.Fail($"{label} 不是对象");

            if (!TryParsePosition(entry["Position"], out int cx, out int cy, out string? posError))
                return MergeOutcome.Fail($"{label} 的 Position 非法：{posError}");

            int? setIndex = null;
            if (entry["SetIndex"] != null)
            {
                if (!TryInt(entry["SetIndex"], out int idx))
                    return MergeOutcome.Fail($"{label} 的 SetIndex `{entry["SetIndex"]}` 不是数字");
                setIndex = idx;
            }

            // `Remove` 是**布尔**（CP `bool.TryParse`，EditMapPatch.cs:412-414），不是"删了就不再建"。
            bool remove = false;
            if (entry["Remove"] is JToken removeToken
                && !bool.TryParse(removeToken.ToString(), out remove))
                return MergeOutcome.Fail($"{label} 的 Remove `{removeToken}` 不是布尔");

            // ── 运行期：CP 逐块警告后继续 ──
            string layerName = entry["Layer"]?.Value<string>() ?? "";
            Layer? layer = map.Layers.FirstOrDefault(
                l => string.Equals(l.Id, layerName, StringComparison.Ordinal));
            if (layer == null)
            {
                notes.Add($"{label} 指定的图层 `{layerName}` 不存在，已跳过这一块");
                continue;
            }

            if (cx < 0 || cy < 0 || cx >= layer.LayerSize.X || cy >= layer.LayerSize.Y)
            {
                notes.Add($"{label} 的位置 ({cx}, {cy}) 超出地图范围，已跳过这一块");
                continue;
            }

            // SetTilesheet 按**图集 Id** 查（CP `map.GetTileSheet(setTilesheetId)`，EditMapPatch.cs:243-252）。
            string? sheetId = entry["SetTilesheet"]?.Value<string>();
            TileSheet? sheet = null;
            if (sheetId != null)
            {
                sheet = map.TileSheets?.FirstOrDefault(
                    s => string.Equals(s.Id, sheetId, StringComparison.Ordinal));
                if (sheet == null)
                {
                    notes.Add($"{label} 的 SetTilesheet `{sheetId}` 不在该地图的图集里，已跳过这一块");
                    continue;
                }
            }

            JObject? props = entry["SetProperties"] as JObject;
            bool hasWork = setIndex.HasValue || sheet != null || (props?.Count ?? 0) > 0;
            if (!hasWork && !remove)
            {
                notes.Add($"{label} 什么都没指定（SetIndex / SetTilesheet / SetProperties / Remove 至少要有一个），已跳过");
                continue;
            }

            int flat = cy * layer.LayerSize.X + cx;
            BaseTile? existing = layer.Tiles[flat];

            // CP EditMapPatch.cs:258-262：空位**可以新建**，但前提是同时给了 SetTilesheet 与 SetIndex。
            // 少任何一个就失败 —— 凭空造一个"半成品图块"比拒绝更糟。
            if (hasWork && (remove || existing == null) && (sheet == null || !setIndex.HasValue))
            {
                notes.Add($"{label}：({cx}, {cy}) 上没有图块，要新建必须同时给 SetTilesheet 与 SetIndex（同 CP），已跳过这一块");
                continue;
            }

            if (remove) layer.Tiles[flat] = null!;

            if (hasWork)
            {
                // ⚠ 顺序照抄 CP：从**移除前**的那个块上取 BlendMode 与属性（`val2` 在置 null 前就捕获了）。
                var merged = new List<Propertie>();
                if (existing?.Properties != null)
                    merged.AddRange(existing.Properties.Select(
                        p => new Propertie { Key = p.Key, Type = p.Type, Value = p.Value }));

                foreach (JProperty p in (props ?? new JObject()).Properties())
                {
                    merged.RemoveAll(x => string.Equals(x.Key, p.Name, StringComparison.Ordinal));
                    if (p.Value.Type != JTokenType.Null)
                        merged.Add(new Propertie
                        {
                            Key = p.Name,
                            Type = StringType,
                            Value = TranslateValue(p.Value.ToString(), translations),
                        });
                }

                // 照抄 CP：一律换成新的 StaticTile（动画块被替换成静态块，与原版行为一致）。
                layer.Tiles[flat] = new StaticTile
                {
                    TileSheet = sheet?.Id ?? SheetOf(existing),
                    TileIndex = setIndex ?? IndexOf(existing),
                    BlendMode = existing is StaticTile st ? st.BlendMode : (byte)0,
                    Properties = merged,
                };
            }

            // ⚠ 改了 Tiles 就必须重建 RLE —— Write 回放 Index/_sizeArr，不推导。否则二进制与模型对不上。
            LayerRle.Rebuild(layer);
        }
        return notes.Count == 0 ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes);
    }

    private static string SheetOf(BaseTile? tile) =>
        tile is StaticTile st ? st.TileSheet
        : tile is AnimatedTile at && at.Frames.Count > 0 ? at.Frames[0].TileSheet
        : "";

    /// <summary>动画块没有单一索引，取第一帧的（与 CP 用 <c>TileIndex</c> 的行为一致）。</summary>
    private static int IndexOf(BaseTile? tile) =>
        tile is StaticTile st ? st.TileIndex
        : tile is AnimatedTile at && at.Frames.Count > 0 ? at.Frames[0].TileIndex
        : 0;

    /// <summary>
    /// <c>Position</c> **两种写法 CP 都吃**（其 <c>TokenPosition</c> 是独立的 X/Y 令牌）：
    /// <c>"3,5"</c> / <c>"3, 5"</c>，以及 <c>{ "X": 3, "Y": 5 }</c>。
    /// 实测 RSV 两种都在用 —— 只认字符串那种会让对象形式**抛异常**（Cannot cast JObject to String）。
    /// </summary>
    private static bool TryParsePosition(JToken? token, out int x, out int y, out string? error)
    {
        x = y = 0;
        error = null;

        if (token is JObject obj)
        {
            if (TryInt(obj["X"], out x) && TryInt(obj["Y"], out y)) return true;
            error = "`X`/`Y` 不是整数";
            return false;
        }

        if (token is JValue { Type: JTokenType.String } v)
        {
            string[] parts = (v.Value<string>() ?? "").Split(',');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out x)
                && int.TryParse(parts[1].Trim(), out y))
            {
                return true;
            }
        }

        error = $"`{token}` 既不是 `x,y` 也不是 `{{ X, Y }}` 形式";
        return false;
    }

    private static bool TryInt(JToken? token, out int value)
    {
        value = 0;
        return token != null && int.TryParse(token.ToString(), out value);
    }

    /// <summary>TextOperations 的 <c>Target</c> 必须以 <c>MapProperties</c> 开头 —— 其它根会碰到瓦片几何。</summary>
    private static bool IsMapPropertiesRoot(JArray? target) =>
        target is { Count: > 0 }
        && target[0]?.Value<string>()?.Equals("MapProperties", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 只把 <c>TextOperations</c> **点名**的那几个属性写回镜像结果，其余属性（含非字符串类型）原样不动。
    /// <para>
    /// 依据 CP <c>EditMapPatch.TryApplyTextOperation(Map, …)</c>（反编译 :14136）：它只做
    /// <c>Properties[名字] = PropertyValue.op_Implicit(op.Apply(旧值))</c>，**不重写整张属性表**。
    /// 原先这里是把整张表写回，会把别的属性也按字符串类型重写（<see cref="Propertie.Type"/> 1/2 → 3），
    /// 与 CP 不一致 —— 见 <c>MapApplierTests.文本操作只改点名的属性其余原样</c>。
    /// </para>
    /// </summary>
    private static void WriteBackNamed(TBin10 map, JObject mirror, JArray ops,
        PackTranslations? translations)
    {
        foreach (JToken op in ops)
        {
            string? name = (op["Target"] as JArray)?[1]?.Value<string>();
            if (name == null || mirror[name] is not JToken value) continue;
            SetStringProperty(map, name, value.ToString(), translations);
        }
    }

    private static string Stringify(object? value) => value?.ToString() ?? "";

    private static void RemoveProperty(TBin10 map, string key) =>
        map.Properties.RemoveAll(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    private static void SetStringProperty(TBin10 map, string key, string value,
        PackTranslations? translations = null)
    {
        value = TranslateValue(value, translations);
        Propertie? existing = map.Properties
            .FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
        if (existing != null)
        {
            existing.Type = StringType;
            existing.Value = value;
        }
        else
        {
            map.Properties.Add(new Propertie { Key = key, Type = StringType, Value = value });
        }
    }

    /// <summary>地图属性里的 <c>{{i18n:key}}</c> → 中文译文（缺则 default，再缺保留）。</summary>
    private static string TranslateValue(string value, PackTranslations? translations)
    {
        if (translations == null || !value.Contains("{{i18n:", StringComparison.OrdinalIgnoreCase))
            return value;
        string locale = LocaleVariants.FrozenSuffix.TrimStart('.');
        return Regex.Replace(value, @"\{\{i18n:([^{}]+)\}\}", m =>
        {
            string key = m.Groups[1].Value.Trim();
            return translations.GetValue(key, locale) ?? m.Value;
        }, RegexOptions.IgnoreCase);
    }

    /// <summary>遍历地图/图集/图层/图块（含动画帧）属性，展开 i18n 令牌；返回替换处数。</summary>
    public static int TranslateMap(TBin10 map, PackTranslations? translations)
    {
        if (translations == null) return 0;
        int changed = 0;

        void Props(List<Propertie>? properties)
        {
            if (properties == null) return;
            foreach (Propertie p in properties)
            {
                if (p.Value is not string text) continue;
                string done = TranslateValue(text, translations);
                if (done != text) { p.Value = done; changed++; }
            }
        }

        Props(map.Properties);
        foreach (TileSheet sheet in map.TileSheets ?? new List<TileSheet>()) Props(sheet.Properties);
        foreach (Layer layer in map.Layers ?? new List<Layer>())
        {
            Props(layer.Properties);
            foreach (BaseTile? tile in layer.Tiles ?? new List<BaseTile>())
            {
                if (tile == null) continue;
                Props(tile.Properties);
                if (tile is AnimatedTile animated)
                    foreach (StaticTile frame in animated.Frames ?? new List<StaticTile>())
                        Props(frame.Properties);
            }
        }
        return changed;
    }

    /// <summary>
    /// <c>AddWarps</c>/<c>AddNpcWarps</c>。CP 的写法是：<b>先 Reverse</b>、拼成空格分隔的串、
    /// 再<b>前置</b>到已有值（<c>(新 + " " + 旧).Trim()</c>）。
    /// </summary>
    /// <summary>
    /// `AddWarps` / `AddNpcWarps`。**逐条独立**：一条格式非法只记 <c>note</c>，
    /// **合法的照写** —— 依据 CP <c>ApplyWarps</c>：它把校验不过的收进 <c>errors</c> 字典，
    /// 调用处 <c>foreach (errors) WarnForPatch(...)</c>，而 <c>list</c> 里那些合法的
    /// 仍会 `if (list.Any())` 写进 `Warp` 属性。
    /// <para>
    /// 原先一条非法就整条 <c>Fail</c>，把**同一补丁里合法的那些也一起丢掉了**。
    /// （解析期只有「这条 warp 串没法解析出令牌」才是 <c>TrackSkip</c>；格式本身是运行期的事。）
    /// </para>
    /// </summary>
    private static MergeOutcome ApplyWarps(TBin10 map, JArray? warps, string propertyName)
    {
        if (warps == null || warps.Count == 0) return MergeOutcome.Ok();

        var notes = new List<string>();
        var valid = new List<string>();
        foreach (JToken token in warps)
        {
            string? warp = token.Value<string>();
            if (string.IsNullOrWhiteSpace(warp))
            {
                notes.Add($"{propertyName} 里有一项是空的，已跳过");
                continue;
            }
            if (!IsValidWarp(warp!))
            {
                notes.Add($"{propertyName} 的 `{warp}` 格式非法（须恰好 5 段 `fromX fromY toMap toX toY`），已跳过");
                continue;
            }
            valid.Add(warp!);
        }

        if (valid.Count == 0)
            return MergeOutcome.Ok(null, notes);      // 全不合法 ⇒ 一个字段都不动（CP 也是 `if (list.Any())`）

        // CP 构造时就 Reverse 了（EditMapPatch.cs:59-60），这里照抄，顺序才对得上。
        valid.Reverse();

        string existing = map.Properties
            .FirstOrDefault(p => string.Equals(p.Key, propertyName, StringComparison.Ordinal))
            ?.Value?.ToString() ?? "";

        SetStringProperty(map, propertyName, (string.Join(' ', valid) + " " + existing).Trim());
        return notes.Count == 0 ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes);
    }

    /// <summary>CP <c>ValidateWarp</c>（EditMapPatch.cs:322+）：5 段，第 0/1/3/4 段是整数。</summary>
    private static bool IsValidWarp(string warp)
    {
        string[] parts = warp.Split(' ');
        if (parts.Length != 5) return false;
        return int.TryParse(parts[0], out _)
            && int.TryParse(parts[1], out _)
            && int.TryParse(parts[3], out _)
            && int.TryParse(parts[4], out _);
    }
}
