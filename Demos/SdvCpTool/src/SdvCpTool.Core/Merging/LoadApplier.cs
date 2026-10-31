using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Cp.Lexing;
using SdvCpTool.Core.Workspace;
using XnbConverter;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using SdvCpTool.Core.Maps;
using XnbConverter.Utilities;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP <c>Load</c>：把 <c>FromFile</c> 指定的文件内容作为<b>整个资产</b>写进 <c>Target</c> 资产名。
/// 这是新建资产的「打底」—— 同一资产上必须先 Load 再 EditData 叠加。
/// </summary>
public sealed class LoadApplier
{
    private readonly OutputTree _output;
    private readonly string _packRoot;
    private readonly PackTranslations? _translations;
    private readonly RawAssetCache? _rawCache;
    private readonly DynamicTokenResolver? _tokens;
    private readonly MapCache? _maps;
    private readonly ImageCache _images;
    private bool _mapConfigSearched;
    private string? _mapConfig;
    // 同一棵输入树上的地图 `.config` 模板是稳定的（任何一份 TideReader 配置都等价）；
    // 批量转换里 232 个包不必各扫一遍 17k InputFiles。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string>
        SharedMapConfigs = new(StringComparer.OrdinalIgnoreCase);

    public LoadApplier(OutputTree output, string packRoot, PackTranslations? translations = null,
        RawAssetCache? rawCache = null, DynamicTokenResolver? tokens = null, MapCache? maps = null,
        ImageCache? images = null)
    {
        _output = output;
        _packRoot = packRoot;
        _translations = translations;
        _rawCache = rawCache;
        _tokens = tokens;
        _maps = maps;
        // ⚠ **不给自己造一个**：图片的落盘权在 `ImageCache` 手里（它 `DropPending` 掉产出树那条），
        //   自建的那个没人 `Flush()` ⇒ 图**静默不落盘**。谁调谁把缓存传进来、谁负责 flush。
        _images = images ?? throw new ArgumentNullException(nameof(images),
            "LoadApplier 需要调用方传入 ImageCache（它负责落盘，自建的那个没人 flush）");
    }

    /// <summary>
    /// 应用一条 <c>Load</c>。
    /// <paramref name="assetName"/> 是 <c>Target</c>（也是字段引用令牌的展开值）；
    /// <paramref name="fromFile"/> 是**相对内容包根目录**的路径，可含
    /// <c>{{Target}}</c> / <c>{{TargetWithoutPath}}</c> / <c>{{TargetPathOnly}}</c> 三个字段引用令牌；
    /// <paramref name="tokenTarget"/> 省略时等同 <paramref name="assetName"/>。
    /// </summary>
    public MergeOutcome Apply(string assetName, string fromFile, string? tokenTarget = null)
    {
        long p0 = SdvCpTool.Core.Diagnostics.Prof.Now();
        void Lap(string k)
        {
            if (SdvCpTool.Core.Diagnostics.Prof.On)
                SdvCpTool.Core.Diagnostics.Prof.AddSince(k, p0);
            p0 = SdvCpTool.Core.Diagnostics.Prof.Now();
        }

        string asset = Normalize(assetName);
        string tokenName = Normalize(tokenTarget ?? assetName);

        // 坏路径（非法字符 / 保留设备名 / 超长）在这里直接降级为该条补丁写入失败 ——
        // 不落到文件系统上抛异常，也就不会带走整次转换（实测：SVE 的 `Characters/Dialogue/**Martin**`）。
        string? invalidPath = _output.ValidatePath(AssetCreator.RelativePath(asset, ".json"));
        if (invalidPath != null)
            return MergeOutcome.Fail($"目标资产 `{asset}` 的输出路径非法：{invalidPath}");

        string resolved = ResolveFirstValidFile(ExpandFieldTokens(fromFile, tokenName), _packRoot);
        if (resolved.Contains("{{", StringComparison.Ordinal))
            return MergeOutcome.Fail($"FromFile 含无法静态解析的令牌：{fromFile}");

        string full;
        try { full = Path.GetFullPath(Path.Combine(_packRoot, resolved)); }
        catch (Exception ex) { return MergeOutcome.Fail($"FromFile 路径无效：{ex.Message}"); }

        if (!File.Exists(full))
            return MergeOutcome.Fail($"找不到 FromFile：{resolved}");

        string ext = Path.GetExtension(resolved);
        // 地图：`.tbin` 本身就是地图数据；`.tmx` 要先转成 tbin。
        if (ext.Equals(".tbin", StringComparison.OrdinalIgnoreCase))
        {
            MergeOutcome map = LoadMapTbin(asset, full);
            Lap("Load.地图");
            return map;
        }
        if (ext.Equals(".tmx", StringComparison.OrdinalIgnoreCase))
        {
            MergeOutcome map = LoadMapTmx(asset, full);
            Lap("Load.地图");
            return map;
        }

        JToken data;
        if (ext.Equals(".xnb", StringComparison.OrdinalIgnoreCase))
        {
            // `.xnb` **按载荷类型分流**，不靠 Target 名字猜 ——
            // 实测 SVE 的 `assets/XNBs/*.xnb` 打的其实是 `Data/Events/*`（一个预编译好的字典），
            // 名字看着像地图，实际不是。
            MergeOutcome decoded = DecodeXnb(full, out data, out byte[]? mapPayload, out Texture2D? picture);
            if (!decoded.Success) return decoded;
            if (mapPayload != null)
            {
                MergeOutcome map = PlaceMap(asset, mapPayload, full);
                Lap("Load.地图");
                return map;
            }
            // 贴图 XNB ⇒ **图片资产**（`<资产>.png` + 图片 `.config`）。
            // 实测 `[CP] Lunna` 的 `Load Characters/Lunna_Shadowfolk ← assets/Events/Sprites/Lunna_Shadowfolk.xnb`：
            // 原来按"数据"写成一份 `.json` + 贴图 `.config`，**图本身没写出来** ⇒
            // 整树回包报 `Could not find file '…Lunna_Shadowfolk.png'`。
            if (picture != null)
            {
                MergeOutcome pic = PlacePicture(asset, picture);
                Lap("Load.图片");
                return pic;
            }
        }
        else if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            try { data = JToken.Parse(File.ReadAllText(full)); }
            catch (Exception ex) { return MergeOutcome.Fail($"FromFile JSON 解析失败：{ex.Message}"); }
        }
        else
        {
            return MergeOutcome.Fail(
                $"FromFile `{resolved}` 不是数据资产（扩展名 {ext}），本任务只处理 .json / .tbin / .xnb / .tmx");
        }

        // ★ `Load` 把**文件内容**整个拷进资产 —— 令牌可能只在**那个文件**里，
        //   补丁的值里一处都看不到（实测语料剩的 13 处全是这种：`Data/Events` 的事件键里带
        //   `{{FarmName}}`/`{{PlayerName}}`）。游戏不认识 `{{…}}`，写出去就是**永远不生效的条目**。
        //   与「补丁值里带令牌就整条不写」同一条规矩：**宁可少写，不可写错**。
        if (ValueTokenSubstitutor.HasUnresolvedTokenIn(data))
            return MergeOutcome.Fail(
                $"FromFile `{resolved}` 的内容里含**无法静态解析**的 CP 令牌（CP 会在写入时代掉，"
                + "我们代不了）—— 写出去就是永远不生效的条目，故整条不写");
        Lap("Load.令牌检查");

        string rel = AssetCreator.RelativePath(asset, ".json");
        bool isNew = !_output.Exists(rel);

        // 新建资产必须先确认有可复制的 .config —— 否则不写任何文件，避免产出回不了包的半成品
        if (isNew && AssetCreator.FindConfigTemplate(_output, asset, data) == null)
            return MergeOutcome.Fail($"新建资产 `{asset}` 找不到可复制的 .config 模板");
        Lap("Load.选config");

        // Load 来的数据文件同样可能带 {{i18n:...}}（如对话/物件资产）—— 与 EditData 走同一条按语言展开的写入路径
        MergeOutcome written = TranslationApplier.WriteLocalized(_output, asset, data, _translations, isNew, _tokens);
        Lap("Load.写数据");
        if (!written.Success) return written;

        _rawCache?.Set(rel, data);
        return MergeOutcome.Ok(data);
    }

    /// <summary>
    /// <c>Load</c> 一份 <c>.tbin</c>：文件本身就是地图数据，<b>逐字节放进产出树</b>，
    /// 再配一份地图的 <c>.config</c>。
    /// <para>
    /// <c>.config</c> 编码的是 XNB 反序列化类型 —— 地图必须是
    /// <c>xTile.Pipeline.TideReader</c> 那一份。实测原版 472 份地图 <c>.config</c> 只有<b>一种</b>
    /// （另一种是 png 贴图的），所以从树里<b>任意一张地图</b>复制即可 ——
    /// <b>不硬编码</b>，免得游戏版本换代后失配。
    /// </para>
    /// </summary>
    private MergeOutcome LoadMapTbin(string asset, string sourcePath)
    {
        byte[]? payload = null;
        try
        {
            int length = checked((int)new FileInfo(sourcePath).Length);
            payload = ByteBufferPool.RentUninitialized(length);
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.ReadExactly(payload, 0, length);
        }
        catch (Exception ex)
        {
            if (payload != null) ByteBufferPool.Return(payload);
            return MergeOutcome.Fail($"读取 tbin 失败：{ex.Message}");
        }
        return PlaceMap(asset, payload, sourcePath);
    }

    /// <summary>
    /// <c>Load</c> 一份 <c>.xnb</c> 地图：解出容器里的 tbin 载荷，再走 <see cref="LoadMapTbin"/> 那条路。
    /// <para>
    /// 依据：XNB 里地图的载荷<b>就是</b> tbin 字节 —— 导入侧 <c>XNB.cs:564-569</c>
    /// 把 <c>.tbin</c> 包成 <c>TBin10 { Data = 原始字节 }</c>，解码侧正好对称。
    /// <b>实测逐字节相等</b>：用原版 <c>Island_House_Bin.xnb</c> 解出来，与解包出的
    /// <c>Island_House_Bin.tbin</c> 完全一致（250 B 压缩 → 382 B）。
    /// </para>
    /// <para>
    /// <c>XnbConfig</c> 是行内初始化的、<c>ConfigHelper.Config</c> 是懒加载默认值 ——
    /// 实测<b>不需要</b>引入 <c>RootPath</c> 那套配置基建。
    /// </para>
    /// </summary>
    private MergeOutcome DecodeXnb(string sourcePath, out JToken data, out byte[]? mapPayload,
        out Texture2D? picture)
    {
        data = new JObject();
        mapPayload = null;
        picture = null;
        try
        {
            using var xnb = new XNB();
            xnb.Decode(sourcePath);

            // 地图：载荷就是 tbin 字节（与导入侧 `TBin10 { Data = 原始字节 }` 对称，实测逐字节一致）
            if (xnb.Data is TBin10 map)
            {
                if (map.Data == null)
                    return MergeOutcome.Fail($"`{Path.GetFileName(sourcePath)}` 是空地图");
                mapPayload = map.Data;
                return MergeOutcome.Ok();
            }

            // 贴图：载荷是一张 `Texture2D`（`Data` 就是 RGBA 逐行排布，与解包侧 `SaveAsPng` 同一口径）。
            // 判据用**类型**，不看 Target 名字 —— 名字看不出来。
            if (xnb.Data is Texture2D texture)
            {
                if (texture.Data == null || texture.Width <= 0 || texture.Height <= 0)
                    return MergeOutcome.Fail($"`{Path.GetFileName(sourcePath)}` 里那张贴图是空的");
                // Data 必须是**逐像素 RGBA**（`Width×Height×4`）：本仓库的解码侧按这个口径写成 PNG。
                // 不是这个形状（比如某种压缩格式没解开）就宁可失败，也别写出一张坏图。
                if (texture.Data.Length != (long)texture.Width * texture.Height * 4)
                    return MergeOutcome.Fail(
                        $"`{Path.GetFileName(sourcePath)}` 里那张贴图不是逐像素 RGBA"
                        + $"（{texture.Data.Length} 字节 vs {texture.Width}×{texture.Height}×4）—— 不写");
                picture = texture;
                return MergeOutcome.Ok();
            }

            if (xnb.Data == null)
                return MergeOutcome.Fail($"`{Path.GetFileName(sourcePath)}` 解出来是空的");

            // 其余（如预编译的 `Data/Events` 字典）转成 JSON 走普通数据资产那条路。
            // `XNB.cs:293` 对 `.json` 产物就是这么做的。
            data = JToken.Parse(xnb.Data.ToJson());
            return MergeOutcome.Ok();
        }
        catch (Exception ex)
        {
            return MergeOutcome.Fail($"XNB 解码失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 把一张 <c>Texture2D</c> 落成**图片资产**：<c>&lt;资产&gt;.png</c> + 一份图片 <c>.config</c>
    /// （与 `ImageApplier` 新建图片资产那条路同口径，包括"不许盖掉另一类资产"那道闸）。
    /// </summary>
    private MergeOutcome PlacePicture(string asset, Texture2D picture)
    {
        if (AssetCreator.NonImageOccupant(_output, asset) is string occupant)
            return MergeOutcome.Fail(
                $"目标 `{asset}` 处已有非图片资产（`{asset}{occupant}`），图片不能盖上去");

        string pngRel = AssetCreator.RelativePath(asset, ".png");
        string? invalid = _output.ValidatePath(pngRel);
        if (invalid != null)
            return MergeOutcome.Fail($"目标资产 `{asset}` 的输出路径非法：{invalid}");

        bool isNew = !_images.Exists(pngRel);
        long pCfg = SdvCpTool.Core.Diagnostics.Prof.Now();
        if (isNew && AssetCreator.FindImageConfigTemplate(_output, asset) == null)
            return MergeOutcome.Fail($"新建图片资产 `{asset}` 找不到可复制的图片 .config 模板（Texture2DReader）");
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.图片.选config", pCfg);

        try
        {
            // 走 `ImageCache.SetPixels`（像素进内存、`Flush()` 时编码成 PNG）——
            // **不用** `Texture2D.SaveAsPng`：那是异步队列，我们要文件按 `ImageCache` 的时序就位。
            _images.SetPixels(pngRel, picture.Data, picture.Width, picture.Height);
        }
        catch (Exception ex)
        {
            return MergeOutcome.Fail($"写入图片资产 `{asset}` 失败：{ex.Message}");
        }

        if (isNew)
        {
            _output.CreatedFiles.Add(pngRel);
            AssetCreator.CopyImageConfig(_output, asset);
        }
        return MergeOutcome.Ok();
    }

    /// <summary>
    /// <c>Load</c> 一份 <c>.tmx</c>：转成 tbin 后放进产出树。
    /// <para>
    /// <b>转完必须回读校验</b>：把结果序列化一遍再用 <see cref="TBin10Reader.FormFile"/>
    /// 读回来，尺寸与图层数都要与原图对得上。只比「文件非空」证明不了内容是地图而不是垃圾。
    /// 校验不过就删掉临时文件、整条失败 —— <b>宁可少写，不可写错地图</b>。
    /// </para>
    /// </summary>
    private MergeOutcome LoadMapTmx(string asset, string sourcePath)
    {
        long pTmx = SdvCpTool.Core.Diagnostics.Prof.Now();
        bool converted = TmxToTbin.TryConvert(sourcePath, out TBin10? map, out string? error,
            out int flippedTiles);
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.TMX转换", pTmx);
        if (!converted)
            return MergeOutcome.Fail($"TMX 转换失败：{error}");

        int rewritten = MapTileActions.Rewrite(map!);

        string rel = AssetCreator.RelativePath(asset, ".tbin");
        string? invalid = _output.ValidatePath(rel);
        if (invalid != null)
            return MergeOutcome.Fail($"目标资产 `{asset}` 的输出路径非法：{invalid}");

        long pCfg = SdvCpTool.Core.Diagnostics.Prof.Now();
        string? configSource = FindMapConfig();
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.选config", pCfg);
        if (configSource == null)
            return MergeOutcome.Fail("树里找不到地图的 .config 模板（需要 TideReader 那一份）");

        // ★ 与 `PlaceMap` 同一条规则：** spouseRoom 共享图不能被后写的小图覆盖**。
        //   这条路径（TMX → MapCache）原来完全绕开了 `PlaceMap`，于是全量链里
        //   `[CP] East Scarp NPCs` 的 12×9 把 `East Scarp NPCs` 的 30×27 盖掉了，
        //   CorwinLK/DaleWaede/JadeMalic 的矩形（0,18 / 6,18 / 18,9）全部越界。
        if (rel.Contains("spouseRoom", StringComparison.OrdinalIgnoreCase))
        {
            byte[]? existing = _output.ReadBytesOrNull(rel);
            int oldArea = existing == null ? 0 : MapArea(existing);
            int newArea = map!.Layers?.FirstOrDefault() is Layer first
                ? first.LayerSize.X * first.LayerSize.Y : 0;
            if (oldArea > 0 && newArea > 0 && oldArea > newArea)
            {
                return MergeOutcome.Ok(null, new[]
                {
                    $"`{asset}` 已有更大的配偶房间地图（{oldArea} 格 vs 新图 {newArea} 格），"
                    + "保留原图（后写的小图会让其它 NPC 的 `MapSourceRect` 越界）",
                });
            }
        }

        string dest = _output.ToOutputPath(rel);
        // 回读校验写在**产出树之外**的临时文件里：产出树里的 tbin 由内存模型独家持有，
        // 这里不该往里放半成品（延迟写模式下它会跟暂存打架）。
        string tmp = Path.Combine(Path.GetTempPath(),
            "sdvcp_tmx_" + Guid.NewGuid().ToString("N") + ".tbin");

        long pSave = SdvCpTool.Core.Diagnostics.Prof.Now();
        try
        {
            TBin10Reader.Create(Array.Empty<byte>()).Save(map!, tmp);
            TBin10 back = TBin10Reader.FormFile(tmp);
            if (!LooksSameShape(map!, back))
                return MergeOutcome.Fail(
                    $"TMX 转换后回读校验不通过（图层 {back.Layers.Count} 对 {map!.Layers.Count}）");
        }
        catch (Exception ex)
        {
            return MergeOutcome.Fail($"写出地图 tbin 失败：{ex.Message}");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            if (SdvCpTool.Core.Diagnostics.Prof.On)
                SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.回读校验", pSave);
        }

        // 包内同级的图集要一起搬（见 `LocalMapTilesheets`）。
        // ⚠ 必须在**地图落盘之前**：图集落点撞上别的资产时它会改名，而改名要跟着地图一起写出去。
        var notes = new List<string>();
        pSave = SdvCpTool.Core.Diagnostics.Prof.Now();
        LocalMapTilesheets.Copy(_output, _images, _maps, _packRoot, sourcePath, asset, map, forTarget: true, notes);
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.搬图集", pSave);

        if (rewritten > 0)
            notes.Add($"DLL 才认识的图块动作 **{rewritten}** 处改写成原版等价动作"
                      + "（`LoadMap`→`Warp` 等；纯 XNB 没有那些处理器，原样留着就是踩上去没反应的死图块）");

        // 整张地图被换掉了 ⇒ 交给地图内存模型（`Set` 会作废旧模型并接管落盘）。
        // 没有缓存层（测试里的直跑路径）才自己写。
        long pSet = SdvCpTool.Core.Diagnostics.Prof.Now();
        if (_maps != null)
        {
            _maps.Set(rel, TBin10Reader.Create(Array.Empty<byte>()), map!, replacedByLoad: true);
        }
        else
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                TBin10Reader.Create(Array.Empty<byte>()).Save(map!, dest);
            }
            catch (Exception ex)
            {
                return MergeOutcome.Fail($"写出地图 tbin 失败：{ex.Message}");
            }
        }

        if (!_output.HasInputFile(rel))
            _output.CreatedFiles.Add(rel);
        _output.WriteText(Path.ChangeExtension(rel, ".config"), File.ReadAllText(configSource));
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.交给缓存", pSet);

        // xTile 不认翻转；纯 XNB 也没有 SMAPI 的 SDisplayDevice ⇒ 落盘前烘进图集（见 FlipBaker）。
        if (flippedTiles != 0)
            notes.Add($"地图有 {flippedTiles} 个翻转/旋转图块：落盘时烘进图集"
                      + "（纯 XNB 没有 SMAPI 的 SDisplayDevice，只能把变换画死）");

        return notes.Count == 0 ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes.Distinct().ToList());
    }

    /// <summary>回读校验的判据：图层数与每层尺寸/图块数都要一致。</summary>
    private static bool LooksSameShape(TBin10 a, TBin10 b)
    {
        if (a.Layers.Count != b.Layers.Count) return false;
        for (int i = 0; i < a.Layers.Count; i++)
        {
            Layer x = a.Layers[i], y = b.Layers[i];
            if (x.LayerSize.X != y.LayerSize.X || x.LayerSize.Y != y.LayerSize.Y) return false;
            if (x.Tiles.Count != y.Tiles.Count) return false;
        }
        return true;
    }

    /// <summary>旧载荷被新数组替换时，旧数组若来自池就立刻归还；返回新载荷。</summary>
    private static byte[] SwapPayload(byte[] oldPayload, byte[] newPayload)
    {
        if (!ReferenceEquals(oldPayload, newPayload)) ByteBufferPool.Return(oldPayload);
        return newPayload;
    }

    private MergeOutcome PlaceMap(string asset, byte[] payload, string sourcePath)
    {
        string rel = AssetCreator.RelativePath(asset, ".tbin");
        string? invalid = _output.ValidatePath(rel);
        if (invalid != null)
            return MergeOutcome.Fail($"目标资产 `{asset}` 的输出路径非法：{invalid}");

        string? configSource = FindMapConfig();
        if (configSource == null)
            return MergeOutcome.Fail("树里找不到地图的 .config 模板（需要 TideReader 那一份）");

        // 包内同级的图集：先搬（可能要改名），改过名就得把地图**重新序列化**再落盘 ——
        // 见 `LocalMapTilesheets` 的说明。
        var notes = new List<string>();
        long pMap = SdvCpTool.Core.Diagnostics.Prof.Now();
        payload = SwapPayload(payload, CopyLocalTilesheets(asset, payload, sourcePath, notes));
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.搬图集", pMap);
        pMap = SdvCpTool.Core.Diagnostics.Prof.Now();
        payload = SwapPayload(payload, NormalizeTileSheetImages(payload, notes));
        if (SdvCpTool.Core.Diagnostics.Prof.On)
            SdvCpTool.Core.Diagnostics.Prof.AddSince("Load.地图.归一图集", pMap);

        // ★ **配偶房间共享图不能被后写的小图覆盖**：`Data/Characters.SpouseRoom.MapSourceRect`
        //   是绝对坐标，后写一张更小的图会让其他 NPC 的矩形越界（实测 East Scarp：
        //   旧包 30×27、新包 12×9；CorwinLK 0,18 / DaleWaede 6,18 / JadeMalic 18,9 全部越界，
        //   婚后进农舍时 `ApplyMapOverride` 读越界抛异常）。
        //   规则只对名字里带 `spouseRoom` 的地图生效，且只挡“更小”的覆盖（更大的更新图照常写）。
        if (rel.Contains("spouseRoom", StringComparison.OrdinalIgnoreCase))
        {
            byte[]? existing = _output.ReadBytesOrNull(rel);
            int oldArea = existing == null ? 0 : MapArea(existing);
            int newArea = MapArea(payload);
            if (oldArea > 0 && newArea > 0 && oldArea > newArea)
            {
                notes.Add($"`{asset}` 已有更大的配偶房间地图（{oldArea} 格 vs 新图 {newArea} 格），"
                          + "保留原图（后写的小图会让其它 NPC 的 `MapSourceRect` 越界）");
                payload = SwapPayload(payload, existing!);
            }
        }

        // ⚠ 走 `StageBytes`：延迟写模式下先进内存、最后统一落盘（同一资产被多个 Load
        //   覆盖时只落最终那份）。
        _output.StageBytes(rel, payload);
        if (!_output.HasInputFile(rel))
            _output.CreatedFiles.Add(rel);

        _output.WriteText(Path.ChangeExtension(rel, ".config"), File.ReadAllText(configSource));

        return notes.Count == 0 ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes.Distinct().ToList());
    }

    /// <summary>
    /// 解析 tbin 载荷取出图集表并搬包内同级图集。图集落点撞名、地图里的引用被改过时，
    /// <b>重新序列化</b>并把新字节交回去（原样字节那条路上，改了内存对象不重新落盘就等于没改）；
    /// 解析不了就原样返回（地图已经写好了，不值得为它整条失败）。
    /// <para>
    /// 顺带把 TMXL 的 <c>LoadMap</c> 图块动作改写成原版自带的 <c>Warp</c>
    /// （见 <see cref="MapTileActions"/>）—— 纯 XNB 装机没有注册那个动作的 DLL。
    /// </para>
    /// </summary>
    private byte[] CopyLocalTilesheets(string asset, byte[] payload, string sourcePath, List<string> notes)
    {
        TBin10 map;
        try
        {
            map = (TBin10)TBin10Reader.Create(payload).Read();
        }
        catch (Exception ex)
        {
            notes.Add($"地图 `{Path.GetFileName(sourcePath)}` 的图集表解析失败，包内同级图集未搬：{ex.Message}");
            return payload;
        }

        bool tilesheetsRenamed = LocalMapTilesheets.Copy(_output, _images, _maps, _packRoot, sourcePath, asset,
            map, forTarget: true, notes);

        int rewritten = MapTileActions.Rewrite(map);
        if (rewritten > 0)
            notes.Add($"DLL 才认识的图块动作 **{rewritten}** 处改写成原版等价动作"
                      + "（`LoadMap`→`Warp` 等；纯 XNB 没有那些处理器，原样留着就是踩上去没反应的死图块）");

        // ★ `Load` 的是**现成 tbin/xnb** 时也要做这两步 —— TMX 那条路在 MapCache.Flush，
        //   这里原来只归一图集引用，于是包里自带的 tbin 会漏掉地图属性 i18n 与自定义图层名
        //   （`Back+1` / `Always Front` 整层不渲染）。顺序与 MapCache.Flush 一致：先 i18n、后改名。
        int i18n = MapApplier.TranslateMap(map, _translations);
        if (i18n > 0)
            notes.Add($"地图属性里的 `{{{{i18n:}}}}` 展开 **{i18n}** 处（中文/默认译文，地图无语言变体）");
        int layersRenamed = LayerNameNormalizer.Normalize(map);
        if (layersRenamed > 0)
            notes.Add($"自定义图层名归一 **{layersRenamed}** 层（`Back+1`/`Always Front` 等原版不渲染）");

        if (!tilesheetsRenamed && rewritten == 0 && i18n == 0 && layersRenamed == 0)
            return payload;      // 没改名/没改写 ⇒ 原样字节，不白跑一遍序列化

        string tmp = Path.Combine(Path.GetTempPath(),
            "sdvcp_tilesheet_" + Guid.NewGuid().ToString("N") + ".tbin");
        try
        {
            TBin10Reader.Create(Array.Empty<byte>()).Save(map, tmp);
            return File.ReadAllBytes(tmp);
        }
        catch (Exception ex)
        {
            notes.Add($"图集改名后地图 `{asset}` 没能重新序列化（图集已按新名字搬好）：{ex.Message}");
            return payload;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// 树里第一份「<c>X.tbin</c> 有同名 <c>X.config</c>」的那份配置。
    /// 判据是<b>成对存在</b>，不是「目录里随便一份 .config」—— 后者可能拿到贴图那份（<c>.png</c>）。
    /// </summary>
    private string? FindMapConfig()
    {
        // 同一个 LoadApplier/同一个输入树里，地图 `.config` 模板是固定的；
        // 一张地图扫一遍 17k InputFiles 没必要，缓存首次结果。
        if (_mapConfigSearched) return _mapConfig;
        _mapConfigSearched = true;
        if (SharedMapConfigs.TryGetValue(_output.InputRoot, out string? shared)) return _mapConfig = shared;

        // ⚠ 这里原来是「外层遍历 InputFiles、内层再 Contains 一次」的 O(N²)，
        //    13506 条 × 数千个 `.config` —— SVE 的地图 Load 就是被它拖垮的。改用 O(1) 集合查询。
        foreach (string file in _output.InputFiles)
        {
            if (!file.EndsWith(".config", StringComparison.OrdinalIgnoreCase)) continue;

            string tbin = Path.ChangeExtension(file, ".tbin");
            if (_output.HasInputFile(tbin))
            {
                _mapConfig = Path.Combine(_output.InputRoot, file);
                SharedMapConfigs[_output.InputRoot] = _mapConfig;
                break;
            }
        }
        return _mapConfig;
    }

    /// <summary>
    /// 展开字段引用令牌（大小写不敏感，与 CP 的令牌解析一致）：
    /// <c>{{Target}}</c> = 完整资产名；<c>{{TargetWithoutPath}}</c> = 最后一段；<c>{{TargetPathOnly}}</c> = 目录部分。
    /// <para>
    /// <b>判定（<c>PatchJudge.JudgeLoad</c>）与写入（<see cref="Apply"/>）必须调用这一个实现，不得各写一份。</b>
    /// 2026-10-05 实测代价：判定曾自己拿原始未展开的字符串查文件存在性，于是 101 条 FromFile 含
    /// <c>{{TargetWithoutPath}}</c> 的 Load 被判 <c>TargetMissing</c> 拦在门外，而写入器本可处理。
    /// 两处对同一数据的不同解释，就是这类静默失败的温床。
    /// </para>
    /// <para>
    /// 2026-10-07：**实现只剩一份** —— 搬到 <see cref="Conditions.FieldReferenceTokens"/>（那里还多认
    /// <c>{{FromFile}}</c>，并有 <c>WhenNode</c> 版本）。这里保留同签名转发，是因为已经有 8 个调用点，
    /// 逐个改是纯 churn；再说一遍同样的三条 <c>Replace</c> 才是真问题。
    /// </para>
    /// </summary>
    public static string ExpandFieldTokens(string fromFile, string target)
        => Conditions.FieldReferenceTokens.Expand(fromFile, fromFile: null, target) ?? fromFile;

    /// <summary>
    /// 再展开 CP 的 <c>{{FirstValidFile: a, b}}</c>：**取第一个在包内存在的文件**。
    /// <para>
    /// 它是<strong>离线可定</strong>的（文件集在一次转换里不变），但必须在
    /// <c>{{TargetWithoutPath}}</c> 等字段令牌**之后**做 —— 实参里常嵌着它们
    /// （RSV 的 `assets/LooseSprites/{{TargetWithoutPath}}_zh.png`）。
    /// 一个都找不到就原样保留（下游判 TargetMissing，不猜）。
    /// </para>
    /// </summary>
    public static string ResolveFirstValidFile(string text, string? packRoot)
    {
        if (packRoot == null || !text.Contains("FirstValidFile", StringComparison.OrdinalIgnoreCase))
            return text;
        if (!CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens)) return text;

        var hits = new List<(int Start, int End, string Value)>();
        Collect(tokens);
        if (hits.Count == 0) return text;

        var sb = new StringBuilder(text);
        foreach ((int start, int end, string value) in hits.OrderByDescending(h => h.Start))
        {
            sb.Remove(start, end - start);
            sb.Insert(start, value);
        }
        return sb.ToString();

        void Collect(IReadOnlyList<ILexToken> list)
        {
            foreach (ILexToken token in list)
            {
                if (token is not LexTokenToken tk) continue;
                if (tk.InputArgs == null
                    || !tk.Name.Equals("FirstValidFile", StringComparison.OrdinalIgnoreCase))
                {
                    if (tk.InputArgs != null) Collect(tk.InputArgs.Parts);
                    continue;
                }
                string args = tk.InputArgs.ToString();
                // 实参里还有代不掉的令牌 ⇒ 判断不了，原样留着。
                if (args.Contains("{{", StringComparison.Ordinal)) continue;
                foreach (string candidate in args.Split(','))
                {
                    string rel = candidate.Trim().Trim('"');
                    if (rel.Length == 0) continue;
                    string full;
                    try { full = Path.GetFullPath(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))); }
                    catch { continue; }
                    if (File.Exists(full))
                    {
                        hits.Add((tk.Start, tk.End, rel));
                        break;
                    }
                }
            }
        }
    }

    private static string Normalize(string name) => name.Replace('\\', '/').Trim('/');

    /// <summary>
    /// `Load` 的 tbin 里图集引用也按原版形状归一（见 <see cref="MapTileActions.NormalizeTileSheetImages"/>）。
    /// 有改动才重新序列化；解析失败就原样放行（后面的 `.config` / 自检会报）。
    /// </summary>
    private static byte[] NormalizeTileSheetImages(byte[] payload, List<string> notes)
    {
        try
        {
            var map = (TBin10)TBin10Reader.Create(payload).Read();
            int changed = MapTileActions.NormalizeTileSheetImages(map);
            if (changed == 0) return payload;

            string tmp = Path.Combine(Path.GetTempPath(),
                "sdvcp_ts_" + Guid.NewGuid().ToString("N") + ".tbin");
            try
            {
                TBin10Reader.Create(Array.Empty<byte>()).Save(map, tmp);
                notes.Add($"地图图集引用 {changed} 处归一成原版写法（`../Maps/x` → `x`）");
                return File.ReadAllBytes(tmp);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
        catch
        {
            return payload;
        }
    }

    /// <summary>地图面积（第一层宽×高）；解析不了返回 0（不参与“保留更大图”的判断）。</summary>
    private static int MapArea(byte[] payload)
    {
        try
        {
            var map = (TBin10)TBin10Reader.Create(payload).Read();
            Layer? first = map.Layers?.FirstOrDefault();
            return first == null ? 0 : first.LayerSize.X * first.LayerSize.Y;
        }
        catch
        {
            return 0;
        }
    }
}
