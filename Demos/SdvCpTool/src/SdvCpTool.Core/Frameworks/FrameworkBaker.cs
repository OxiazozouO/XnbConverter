using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Frameworks;

/// <summary>
/// 把非 CP/FTM 框架的内容按「原版数据表新增行」烘进产出树。
/// <para>
/// 不是保留框架的运行时替换：AT 的每个变体变成一条新家具/作物/栅栏；
/// MFM 变成 <c>Data/Mail</c> + <c>Data/TriggerActions</c>；CCCB 替换
/// <c>Data/Bundles</c>；SF 把建筑/皮肤写进 <c>Data/Buildings</c>。
/// 口径见 <c>docs/research/2026-10-20-additive-baking-reassessment.md</c>。
/// </para>
/// <para>
/// **必须在 CP/FTM 转换之后跑**：它要读最终产出树里的基础条目（AT 的 ItemName
/// 对应哪件家具、MFM 的配方在不在、CCCB 的捆绑包键）。所以它是 CLI 的独立命令
/// <c>bake-frameworks</c>，不塞进 <c>ContentPack.Load</c>。
/// </para>
/// </summary>
public static class FrameworkBaker
{
    private const string AtUid = "peacefulend.alternativetextures";
    private const string CpUid = "pathoschild.contentpatcher";
    private const string MfmUid = "digus.mailframeworkmod";
    private const string CccbUid = "alja.cccb";
    private const string SfUid = "peacefulend.solidfoundations";
    private const string CcUid = "peacefulend.customcompanions";
    private const string MoreGrassUid = "epicbellyflop45.moregrass";
    private const string BusLocationsUid = "hootless.buslocations";
    private const string TrainStationUid = "cherry.trainstation";

    public sealed record PackResult(
        string Framework,
        string Name,
        string Path,
        int Furniture = 0,
        int Decoration = 0,
        int Crop = 0,
        int Craftable = 0,
        int Mail = 0,
        int Bundles = 0,
        int Buildings = 0,
        int Pets = 0,
        int Grass = 0,
        int Transit = 0,
        int Textures = 0,
        IReadOnlyList<string>? Notes = null);

    public sealed record Result(IReadOnlyList<PackResult> Packs, string ReportPath)
    {
        public int Deduped { get; init; }
        public int Furniture => Packs.Sum(p => p.Furniture);
        public int Decoration => Packs.Sum(p => p.Decoration);
        public int Crop => Packs.Sum(p => p.Crop);
        public int Craftable => Packs.Sum(p => p.Craftable);
        public int Mail => Packs.Sum(p => p.Mail);
        public int Bundles => Packs.Sum(p => p.Bundles);
        public int Buildings => Packs.Sum(p => p.Buildings);
        public int Pets => Packs.Sum(p => p.Pets);
        public int Grass => Packs.Sum(p => p.Grass);
        public int Transit => Packs.Sum(p => p.Transit);
        public int Textures => Packs.Sum(p => p.Textures);
    }

    private sealed record PackInfo(string Name, string Path, string? ContentPackForUid, string UniqueId);

    /// <summary>
    /// 去重侧车的判据版本。判据/分组算法一改就递增：旧侧车里的 `ids` 是旧算法删的，
    /// 不能拿来短路新算法，否则重跑同一命令也补不回已经被误杀的条目。
    /// </summary>
    private const int DedupStateVersion = 2;

    public static Result Bake(string treeRoot, string modsRoot,
        IReadOnlyCollection<string>? only = null, string cccbVariant = "Pure_Vanilla",
        bool dedupCosmetics = false, int dedupThreshold = 0,
        double dedupColorTolerance = 0, double dedupMaxPixelDifference = 1.0,
        double dedupColorConsistency = 2.0)
    {
        treeRoot = System.IO.Path.GetFullPath(treeRoot);
        modsRoot = System.IO.Path.GetFullPath(modsRoot);
        if (!Directory.Exists(treeRoot)) throw new DirectoryNotFoundException($"产出树不存在：{treeRoot}");
        if (!Directory.Exists(modsRoot)) throw new DirectoryNotFoundException($"Mods 目录不存在：{modsRoot}");

        // OutputTree 同根构造 = Append（不重拷树）；这里只借用它的路径校验/暂存/Flush。
        var output = new OutputTree(treeRoot, treeRoot);
        var images = new ImageCache(output);
        // Mods 的 manifest 树只扫一次；下面 8 个框架分支 + SpaceCore 三次遍历共用。
        List<PackInfo> allPacks = LoadPacks(modsRoot, only).ToList();
        // 框架分支按 ContentPackFor 分桶：8 个分支各扫一遍 allPacks 无谓；
        // 这里一次分组，之后每个 uid 只取自己的小列表。
        Dictionary<string, List<PackInfo>> packsByFramework = allPacks
            .Where(p => p.ContentPackForUid != null)
            .GroupBy(p => p.ContentPackForUid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // SpaceCore 的三类扩展数据只可能由 Content Patcher 的 EditData 写入；
        // 其它框架包的 JSON 不经过 CP，扫它们只会重复读 i18n/贴图清单。
        List<PackInfo> spaceCorePacks = PacksFor(packsByFramework, CpUid).ToList();
        HashSet<string>? vanillaCues = packsByFramework.ContainsKey(CcUid)
            ? LoadVanillaCueNames(output)
            : null;
        var assets = new List<string>();
        var newDataAssets = new List<string>();
        var results = new List<PackResult>();
        var dedup = new CosmeticDeduper(dedupCosmetics, dedupThreshold,
            dedupColorTolerance, dedupMaxPixelDifference, dedupColorConsistency);
        var prof = Environment.GetEnvironmentVariable("SDVCP_PROFILE") != null
            ? System.Diagnostics.Stopwatch.StartNew()
            : null;
        void Mark(string phase)
        {
            if (prof == null) return;
            Console.Error.WriteLine(
                $"[fw-profile] {phase} {prof.ElapsedMilliseconds}ms "
                + $"mem={GC.GetTotalMemory(false) / 1048576}MB");
            prof.Restart();
        }
        string reportDir = System.IO.Path.GetDirectoryName(treeRoot.TrimEnd('\\', '/')) ?? treeRoot;
        string reportPath = System.IO.Path.Combine(reportDir, "_frameworks-report.json");
        string textureOverrideState = System.IO.Path.Combine(reportDir, "_texture-overrides.json");
        string textureOverrideFileState = System.IO.Path.Combine(reportDir,
            "_texture-overrides-files.json");
        string companionSheetState = System.IO.Path.Combine(reportDir, "_cc-sheets.json");
        string dedupPath = System.IO.Path.Combine(reportDir, "_frameworks-dedup.json");
        if (dedupCosmetics && File.Exists(dedupPath))
        {
            try
            {
                JObject saved = JObject.Parse(File.ReadAllText(dedupPath));
                // 阈值/判据版本变了就不复用旧决定，让用户可以用更严格的阈值重新生成。
                if (saved.Value<int?>("version") == DedupStateVersion &&
                    saved.Value<int?>("threshold") == dedupThreshold &&
                    Math.Abs((saved.Value<double?>("colorTolerance") ?? 0) - dedupColorTolerance) < 0.001 &&
                    Math.Abs((saved.Value<double?>("maxPixelDifference") ?? 1.0) - dedupMaxPixelDifference) < 0.001 &&
                    Math.Abs((saved.Value<double?>("colorConsistency") ?? 2.0) - dedupColorConsistency) < 0.001 &&
                    saved["ids"] is JArray ids)
                    dedup.SeedSkipped(ids.Select(x => x.ToString()));
            }
            catch { /* 旧文件坏了就当没有 */ }
        }

        var furniture = LoadObject(output, "Data/Furniture.json");
        var awf = LoadArray(output, "Data/AdditionalWallpaperFlooring.json");
        var crops = LoadObject(output, "Data/Crops.json");
        var objects = LoadObject(output, "Data/Objects.json");
        var fences = LoadObject(output, "Data/Fences.json");
        var big = LoadObject(output, "Data/BigCraftables.json");
        var recipes = LoadObject(output, "Data/CraftingRecipes.json");
        var cooking = LoadObject(output, "Data/CookingRecipes.json");
        var shops = LoadObject(output, "Data/Shops.json");
        var mail = LoadObject(output, "Data/Mail.json");
        var mailZh = LoadObject(output, "Data/Mail.zh-CN.json");
        var triggers = LoadArray(output, "Data/TriggerActions.json");
        var bundles = LoadObject(output, "Data/Bundles.json");
        var buildings = LoadObject(output, "Data/Buildings.json");
        var pets = LoadObject(output, "Data/Pets.json");
        Mark("load-data");

        JToken[] dataDocs =
        {
            furniture, awf, crops, objects, fences, big, recipes, cooking, shops,
            mail, mailZh, triggers, bundles, buildings, pets,
        };
        byte[][] beforeHashes = HashAll(dataDocs);
        var beforeFurniture = beforeHashes[0];
        var beforeAwf = beforeHashes[1];
        var beforeCrops = beforeHashes[2];
        var beforeObjects = beforeHashes[3];
        var beforeFences = beforeHashes[4];
        var beforeBig = beforeHashes[5];
        var beforeRecipes = beforeHashes[6];
        var beforeCooking = beforeHashes[7];
        var beforeShops = beforeHashes[8];
        var beforeMail = beforeHashes[9];
        var beforeMailZh = beforeHashes[10];
        var beforeTriggers = beforeHashes[11];
        var beforeBundles = beforeHashes[12];
        var beforeBuildings = beforeHashes[13];
        var beforePets = beforeHashes[14];
        Mark("hash-data");

        if (dedupCosmetics)
        {
            SeedFurnitureFingerprints(dedup, output, images, furniture);
            SeedTextureEntries(dedup, output, images, fences, 0, 0, spriteField: false);
            SeedTextureEntries(dedup, output, images, big, 16, 32, spriteField: true);
            foreach (JObject deco in awf.OfType<JObject>())
            {
                string? id = deco.Value<string>("Id");
                string? tex = deco.Value<string>("Texture");
                if (id == null || tex == null) continue;
                string? full = output.ResolveReadablePath(tex.Replace('\\', '/') + ".png");
                if (full != null && TrySize(images, full, out int dw, out int dh))
                    dedup.Seed(id, CosmeticDeduper.Capture(images, full, 0, 0, dw, dh));
            }
        }

        // ---------------------------------------------------------------- AT
        var atPacks = PacksFor(packsByFramework, AtUid).ToList();
        var atModels = new List<TextureModel>?[atPacks.Count];
        Parallel.For(0, atPacks.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            try { atModels[i] = TextureModels(atPacks[i].Path).ToList(); }
            catch { atModels[i] = null; }
        });
        // 扫描/解析失败时按原顺序重跑，保持原来的异常行为。
        for (int i = 0; i < atModels.Length; i++)
            atModels[i] ??= TextureModels(atPacks[i].Path).ToList();
        Mark("at-preload");

        for (int packIndex = 0; packIndex < atPacks.Count; packIndex++)
        {
            PackInfo pack = atPacks[packIndex];
            var notes = new List<string>();
            int f = 0, d = 0, c = 0, cr = 0;
            foreach (TextureModel model in atModels[packIndex]!)
            {
                string? modelType = model.Model.Value<string>("Type")?.ToLowerInvariant();
                try
                {
                    switch (modelType)
                    {
                        case "decoration":
                            d += ProcessDecoration(output, images, pack.UniqueId, model, awf, assets, notes, dedup);
                            break;
                        case "furniture":
                            f += ProcessFurniture(output, images, pack.UniqueId, model, furniture, assets, notes, dedup);
                            break;
                        case "crop":
                            c += ProcessCrop(output, images, pack.UniqueId, model, crops, objects, shops, assets, notes);
                            break;
                        case "craftable":
                            cr += ProcessCraftable(output, images, pack.UniqueId, model, objects, fences, big,
                                recipes, shops, assets, notes, dedup);
                            break;
                        default:
                            notes.Add($"{model.Model.Value<string>("ItemName") ?? model.Dir}: 未处理的 AT 类型 {model.Model.Value<string>("Type")}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"{model.Model.Value<string>("ItemName") ?? model.Dir}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            results.Add(new PackResult("AlternativeTextures", pack.Name, pack.Path, f, d, c, cr, Notes: notes));
        }
        Mark("fw-at");

        // ---------------------------------------------------------------- SF
        foreach (PackInfo pack in PacksFor(packsByFramework, SfUid))
        {
            var notes = new List<string>();
            int n = ProcessSolidFoundations(output, images, pack, buildings, assets, notes);
            results.Add(new PackResult("SolidFoundations", pack.Name, pack.Path, Buildings: n, Notes: notes));
        }
        Mark("fw-sf");

        // ---------------------------------------------------------------- CC
        CompanionSheetState ccSheets = CompanionSheetState.Load(companionSheetState);
        foreach (PackInfo pack in PacksFor(packsByFramework, CcUid))
        {
            var notes = new List<string>();
            int n = ProcessCompanions(output, images, pack, pets, assets, notes, vanillaCues, ccSheets);
            results.Add(new PackResult("CustomCompanions", pack.Name, pack.Path, Pets: n, Notes: notes));
        }
        Mark("fw-cc");

        // ---------------------------------------------------------------- CCCB
        foreach (PackInfo pack in PacksFor(packsByFramework, CccbUid))
        {
            var notes = new List<string>();
            int n = ProcessCccb(pack, bundles, cccbVariant, notes);
            results.Add(new PackResult("CCCB", pack.Name, pack.Path, Bundles: n, Notes: notes));
        }
        Mark("fw-cccb");

        // ---------------------------------------------------------------- MFM
        foreach (PackInfo pack in PacksFor(packsByFramework, MfmUid))
        {
            var notes = new List<string>();
            int n = ProcessMail(pack, mail, mailZh, triggers, cooking, recipes, notes);
            results.Add(new PackResult("MailFramework", pack.Name, pack.Path, Mail: n, Notes: notes));
        }
        Mark("fw-mfm");

        // ---------------------------------------------------------------- 运行时框架的静态近似
        foreach (PackInfo pack in PacksFor(packsByFramework, MoreGrassUid))
        {
            var notes = new List<string>();
            int n = ProcessMoreGrass(output, images, pack, assets, notes);
            results.Add(new PackResult("MoreGrass", pack.Name, pack.Path, Grass: n, Notes: notes));
        }
        Mark("fw-grass");
        foreach (PackInfo pack in PacksFor(packsByFramework, BusLocationsUid))
        {
            var notes = new List<string>();
            int n = ProcessBusLocations(output, pack, notes);
            results.Add(new PackResult("BusLocations", pack.Name, pack.Path, Transit: n, Notes: notes));
        }
        Mark("fw-bus");
        foreach (PackInfo pack in PacksFor(packsByFramework, TrainStationUid))
        {
            var notes = new List<string>();
            int n = ProcessTrainStation(output, pack, notes);
            results.Add(new PackResult("TrainStation", pack.Name, pack.Path, Transit: n, Notes: notes));
        }

        Mark("fw-train");
        var spaceCoreJson = new SpaceCoreJsonCache();
        spaceCoreJson.Prewarm(spaceCorePacks.Select(p => p.Path));
        Mark("spacecore-scan");

        // ---------------------------------------------------------------- SpaceCore 纹理覆盖
        // 它不是 ContentPackFor：普通 CP 包用 EditData 往 `spacechase0.SpaceCore/TextureOverrides`
        // 写条目，SpaceCore 在渲染时把动画源图当前帧画到目标贴图的 TargetRect 上。
        // 纯 XNB 静态取**第一帧**烘进目标图（与文档里“第一帧”近似同一条口径）。
        TextureOverrideFileHashes textureFileHashes =
            TextureOverrideFileHashes.Load(textureOverrideFileState);
        var appliedTextureTargets = LoadTextureOverrideTargets(textureOverrideState);
        bool textureStateDirty = false;
        foreach (PackInfo pack in spaceCorePacks)
        {
            var notes = new List<string>();
            int n = ProcessTextureOverrides(output, images, pack, notes, appliedTextureTargets,
                ref textureStateDirty, textureFileHashes, spaceCoreJson);
            if (n > 0 || notes.Count > 0)
                results.Add(new PackResult("SpaceCore", pack.Name, pack.Path, Textures: n, Notes: notes));
        }
        if (textureStateDirty)
            SaveTextureOverrideTargets(textureOverrideState, appliedTextureTargets);

        Mark("spacecore-texture");

        // SpaceCore 的对象扩展：只把原版 ObjectData 真读的字段映射回 Data/Objects。
        // 分类文字/颜色、MaxStack、TotemWarp 等没有原版槽位，仍在 notes 里逐条报丢失。
        foreach (PackInfo pack in spaceCorePacks)
        {
            var notes = new List<string>();
            int n = SpaceCoreObjectExtensions.Apply(objects, pack.Path, pack.UniqueId, notes,
                spaceCoreJson);
            if (n > 0) notes.Insert(0, $"SpaceCore 对象扩展映射 {n} 条");
            if (n > 0 || notes.Count > 0)
                results.Add(new PackResult("SpaceCore", pack.Name, pack.Path, Notes: notes));
        }

        Mark("spacecore-object");

        // SpaceCore 作物扩展里只有 RegrowDays 有原版单值槽位；Drops/Experience 等仍报损失。
        foreach (PackInfo pack in spaceCorePacks)
        {
            var notes = new List<string>();
            int n = SpaceCoreCropExtensions.Apply(crops, pack.Path, pack.UniqueId, notes,
                spaceCoreJson);
            if (n > 0) notes.Insert(0, $"SpaceCore 作物扩展映射 {n} 条");
            if (n > 0 || notes.Count > 0)
                results.Add(new PackResult("SpaceCore", pack.Name, pack.Path, Notes: notes));
        }

        // 原版 `SetBundleData` 只认 `Area/Index` 键且要求值至少三段；任何残留的
        // `CCCB_x` / 半截数据都会让新存档在 CommunityCenter 构造时崩掉。
        int invalidBundles = RemoveInvalidBundles(bundles);
        if (invalidBundles > 0)
            results.Add(new PackResult("CCCB", "（Data/Bundles 归一）", "", Bundles: 0,
                Notes: new List<string> { $"删除 {invalidBundles} 条原版无法解析的捆绑包键" }));

        Mark("spacecore");

        // ---------------------------------------------------------------- 落盘
        // ★ 本地化 Data 资产是**整份替换**加载的（`LocalizedContentManager.Load` 选
        //   `Data/Furniture.zh-CN` 而不是 base）。框架新增行如果只写 base，中文客户端
        //   完全看不到这些物品/建筑/配方。这里只镜像**本轮真的改过**的 base；
        //   base 没变的语言变体不可能需要改，避免每轮把十几份 2 MB 文档重解析。
        var localizedTargets = new List<(string Asset, JToken Current)>();
        byte[][] afterHashes = HashAll(dataDocs);
        void Stage(string rel, byte[] beforeHash, byte[] afterHash, JToken current)
        {
            if (beforeHash.AsSpan().SequenceEqual(afterHash)) return;
            bool existed = output.Exists(rel);
            output.StageJson(rel, current);
            if (!existed) newDataAssets.Add(rel);
            localizedTargets.Add((rel, current));
        }

        Stage("Data/Furniture.json", beforeFurniture, afterHashes[0], furniture);
        Stage("Data/AdditionalWallpaperFlooring.json", beforeAwf, afterHashes[1], awf);
        Stage("Data/Crops.json", beforeCrops, afterHashes[2], crops);
        Stage("Data/Objects.json", beforeObjects, afterHashes[3], objects);
        Stage("Data/Fences.json", beforeFences, afterHashes[4], fences);
        Stage("Data/BigCraftables.json", beforeBig, afterHashes[5], big);
        Stage("Data/CraftingRecipes.json", beforeRecipes, afterHashes[6], recipes);
        Stage("Data/CookingRecipes.json", beforeCooking, afterHashes[7], cooking);
        Stage("Data/Shops.json", beforeShops, afterHashes[8], shops);
        Stage("Data/Mail.json", beforeMail, afterHashes[9], mail);
        Stage("Data/TriggerActions.json", beforeTriggers, afterHashes[11], triggers);
        Stage("Data/Bundles.json", beforeBundles, afterHashes[12], bundles);
        Stage("Data/Buildings.json", beforeBuildings, afterHashes[13], buildings);
        Stage("Data/Pets.json", beforePets, afterHashes[14], pets);

        if (!beforeMailZh.AsSpan().SequenceEqual(afterHashes[10]))
        {
            // 先问「原来有没有」再 StageJson；StageJson 之后 Exists 可能已看到 pending。
            output.StageJson("Data/Mail.zh-CN.json", mailZh);
            // 即使基础树里已有该变体，也要确保 ContentHashes 有键（旧树可能漏）。
            newDataAssets.Add("Data/Mail.zh-CN.json");
            string? cfg = output.ReadTextOrNull("Data/Mail.config");
            if (cfg != null && !output.Exists("Data/Mail.zh-CN.config"))
                output.WriteText("Data/Mail.zh-CN.config", cfg);
        }

        MirrorLocalizedData(output, localizedTargets);
        Mark("localized-mirror");

        AddContentHashes(output, assets.Concat(newDataAssets));
        images.Flush();
        output.Flush();
        textureFileHashes.Save(output, textureOverrideFileState);
        ccSheets.Save(output, companionSheetState);
        images.Release();
        Mark("flush");

        int total = results.Sum(r => r.Furniture + r.Decoration + r.Crop + r.Craftable +
                                     r.Mail + r.Bundles + r.Buildings + r.Pets + r.Grass + r.Transit +
                                     r.Textures);
        // 幂等重跑（本次 0 新增）时保留上一份报告，不要用 0 覆盖掉“做了什么”。
        if (total > 0 || !File.Exists(reportPath))
            JsonFile.Write(reportPath, BuildReport(results, dedup.Deduped));
        if (dedupCosmetics)
            JsonFile.Write(dedupPath, new JObject
            {
                ["version"] = DedupStateVersion,
                ["threshold"] = dedupThreshold,
                ["colorTolerance"] = dedupColorTolerance,
                ["maxPixelDifference"] = dedupMaxPixelDifference,
                ["colorConsistency"] = dedupColorConsistency,
                ["ids"] = new JArray(dedup.Skipped.OrderBy(x => x, StringComparer.Ordinal)),
            });
        return new Result(results, reportPath) { Deduped = dedup.Deduped };
    }

    // ================================================================== 通用

    private sealed record TextureModel(string Dir, JObject Model, IReadOnlyList<string> Pngs);

    /// <summary>
    /// 扫一次 Mods 树，按 `ContentPackFor` 过滤；每个包只解析一次 manifest。
    /// <para>
    /// 之前每个框架各调一次 `EnumerateFiles(..., AllDirectories)`，同一棵十万级
    /// Mods 树被扫 8~11 遍；实测单次全量烘焙的固定成本主要在这里。
    /// </para>
    /// </summary>
    private static IEnumerable<PackInfo> LoadPacks(string modsRoot, IReadOnlyCollection<string>? only)
    {
        foreach (string manifestPath in Directory.EnumerateFiles(modsRoot, "manifest.json", SearchOption.AllDirectories))
        {
            JObject manifest;
            try { manifest = JsoncReader.ParseFile(manifestPath); }
            catch { continue; }
            string path = System.IO.Path.GetDirectoryName(manifestPath)!;
            string name = manifest.Value<string>("Name") ?? System.IO.Path.GetFileName(path);
            if (only is { Count: > 0 } &&
                !only.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase) ||
                               System.IO.Path.GetFileName(path).Contains(x, StringComparison.OrdinalIgnoreCase)))
                continue;
            string? framework = (manifest["ContentPackFor"] as JObject)?.Value<string>("UniqueID");
            yield return new PackInfo(name, path, framework,
                manifest.Value<string>("UniqueID") ?? name);
        }
    }

    private static IEnumerable<PackInfo> PacksFor(
        IReadOnlyDictionary<string, List<PackInfo>> packs, string uid)
        => packs.TryGetValue(uid, out List<PackInfo>? list)
            ? list
            : Array.Empty<PackInfo>();

    internal const string TextureOverridesTarget = "spacechase0.SpaceCore/TextureOverrides";

    /// <summary>
    /// SpaceCore 的 <c>TextureOverrides</c>：把动画源图的**第一帧**静态贴进目标贴图。
    /// <para>
    /// 依据反编译 SpaceCore：<c>TextureOverridePackData</c> 的 <c>SourceTexture</c> 解析成
    /// <c>TextureAnimation</c>，取 <c>Frames[0]</c>，按 <c>SpriteIndex</c> 从源图切出
    /// <c>TargetRect.Width×Height</c> 的一格，再由 <c>SpriteBatchPatcher</c> 叠画到
    /// <c>TargetRect</c>。纯 XNB 没有 SpriteBatch 补丁，取第一帧烘进底图，
    /// 动画本身丢失（与既有“第一帧”近似同口径）。
    /// </para>
    /// </summary>
    private static int ProcessTextureOverrides(OutputTree output, ImageCache images, PackInfo pack,
        List<string> notes, Dictionary<string, string> appliedTargets, ref bool stateDirty,
        TextureOverrideFileHashes fileHashes, SpaceCoreJsonCache spaceCoreJson)
    {
        int applied = 0;
        var appliedNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runningTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JObject? config = null;
        string configPath = System.IO.Path.Combine(pack.Path, "config.json");
        if (File.Exists(configPath))
        {
            try { config = JsoncReader.ParseFile(configPath); }
            catch { /* 配置坏了就只看无条件的条目 */ }
        }
        foreach (SpaceCoreJsonCache.CachedFile file in spaceCoreJson.GetFiles(pack.Path))
        {
            if (!file.Targets.Contains(TextureOverridesTarget)) continue;

            foreach (JObject change in Descendants(file.Root).OfType<JObject>())
            {
                // 递归遍历会进入 `Entries` 里的对象；它们可能有 `Target` 数组，
                // 直接 `Value<string>` 会抛 InvalidCastException。
                if (change["Target"]?.Type != JTokenType.String
                    || !string.Equals(change["Target"]!.Value<string>(), TextureOverridesTarget,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TextureOverrideWhenAllows(change["When"] as JObject, config)) continue;
                if (change["Entries"] is not JObject entries) continue;

                foreach (JProperty entry in entries.Properties())
                {
                    try
                    {
                        if (entry.Value is not JObject data) continue;
                        string? targetTexture = data.Value<string>("TargetTexture");
                        string? sourceTexture = data.Value<string>("SourceTexture");
                        JObject? rect = data["TargetRect"] as JObject;
                        if (targetTexture == null || sourceTexture == null || rect == null)
                        {
                            notes.Add($"{entry.Name}: TextureOverrides 缺少 TargetTexture/SourceTexture/TargetRect");
                            continue;
                        }
                        // 同一个 EditData 可能同时出现在 content.json 的 Include 与 Data/*.json；
                        // 以条目内容去重，避免同一块像素被贴两次。
                        string dedupKey = string.Join('|', entry.Name, targetTexture, sourceTexture,
                            rect.ToString(Formatting.None));
                        if (!seen.Add(dedupKey)) continue;

                        int rx = rect.Value<int?>("X") ?? 0;
                        int ry = rect.Value<int?>("Y") ?? 0;
                        int rw = rect.Value<int?>("Width") ?? 0;
                        int rh = rect.Value<int?>("Height") ?? 0;
                        if (rw <= 0 || rh <= 0) continue;

                        string targetRel = targetTexture.Trim().Replace('\\', '/').Trim('/');
                        // ImageCache 的键就是产出树里的**文件名**（带 `.png`）。早先这里把
                        // `.png` 去掉再交给缓存，Flush 会写出一个无扩展名的文件，TryGetPixels
                        // 也会按无扩展名去磁盘找 ⇒ 所有目标都被误判为“产出树里没有这张图”。
                        if (!targetRel.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            targetRel += ".png";
                        string? targetFull = Path.Combine(output.OutputRoot,
                            targetRel.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(targetFull)) targetFull = null;
                        string? targetKey = targetFull == null ? null : targetRel;
                        string? sourceFull = ResolveOverrideImage(output, sourceTexture, pack, out _);
                        if (targetFull == null)
                        {
                            notes.Add($"{entry.Name}: 目标贴图不存在 `{targetTexture}`");
                            continue;
                        }
                        if (sourceFull == null)
                        {
                            notes.Add($"{entry.Name}: 源贴图不存在 `{sourceTexture}`");
                            continue;
                        }

                        int frame = FirstFrameIndex(sourceTexture);
                        if (!images.TryReadSource(sourceFull, out byte[] src, out int sw, out int sh,
                                out string? srcError))
                        {
                            notes.Add($"{entry.Name}: {srcError}");
                            continue;
                        }

                        // 目标图如果还有语言变体（`springobjects.zh-CN.png`），同一块像素也要烤进去。
                        // 本地化图片是单独资产，游戏按语言加载变体，不会回落到 base。
                        bool any = false;
                        foreach (string variant in TargetVariants(output, targetKey!))
                        {
                            // 同一张目标图一旦烤过就不要再叠加：TextureOverrides 是 alpha 混合，
                            // 重跑同一命令会把半透明帧越叠越暗。状态里存“应用后像素的 SHA-256”。
                            if (skippedTargets.Contains(variant)) continue;
                            if (!runningTargets.Contains(variant))
                            {
                                // 先比文件字节的 SHA-256：命中说明这张图就是上次写出的完整文件，
                                // 不用解码整张 PNG、也不用重新算像素哈希。
                                if (fileHashes.Matches(output, variant))
                                {
                                    skippedTargets.Add(variant);
                                    continue;
                                }
                                if (appliedTargets.TryGetValue(variant, out string? storedHash)
                                    && images.TryGetPixels(variant, out byte[] current, out _, out _, out _)
                                    && string.Equals(HashPixels(current), storedHash, StringComparison.Ordinal))
                                {
                                    skippedTargets.Add(variant);
                                    fileHashes.Mark(variant);
                                    continue;
                                }

                                runningTargets.Add(variant);
                            }

                            if (ApplyTextureOverride(images, variant, src, sw, sh, frame,
                                    rw, rh, rx, ry, notes, entry.Name))
                            {
                                appliedNow.Add(variant);
                                fileHashes.Mark(variant);
                                any = true;
                            }
                        }

                        if (any) applied++;
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"{entry.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        if (appliedNow.Count > 0)
        {
            foreach (string target in appliedNow)
            {
                if (images.TryGetPixels(target, out byte[] pixels, out _, out _, out _))
                    appliedTargets[target] = HashPixels(pixels);
            }
            stateDirty = true;
        }

        return applied;
    }

    /// <summary>
    /// 目标图本身 + 同目录下的语言变体（`springobjects.zh-CN.png`）。
    /// 本地化图片是独立资产，游戏按当前语言直接加载变体，不会回落到无语言后缀的 base。
    /// </summary>
    private static IEnumerable<string> TargetVariants(OutputTree output, string baseKey)
    {
        yield return baseKey;

        string full = output.ToOutputPath(baseKey);
        string? dir = Path.GetDirectoryName(full);
        if (dir == null || !Directory.Exists(dir)) yield break;

        string stem = Path.GetFileNameWithoutExtension(full);
        string baseName = Path.GetFileName(full);
        var localeSuffix = new Regex(
            "^" + Regex.Escape(stem) + @"\.[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})?\.png$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (string file in Directory.EnumerateFiles(dir, stem + ".*.png"))
        {
            string name = Path.GetFileName(file);
            if (name.Equals(baseName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!localeSuffix.IsMatch(name)) continue;
            yield return Path.GetRelativePath(output.OutputRoot, file).Replace('\\', '/');
        }
    }

    private static bool ApplyTextureOverride(ImageCache images, string targetKey, byte[] src,
        int sw, int sh, int frame, int rw, int rh, int rx, int ry, List<string> notes, string entryName)
    {
        if (!images.TryGetPixels(targetKey, out byte[] dst, out int dw, out int dh,
                out string? dstError))
        {
            notes.Add($"{entryName}: 目标贴图读不出：{dstError}");
            return false;
        }

        int fx = frame * rw % sw;
        int fy = frame * rw / sw * rh;
        if (fx + rw > sw || fy + rh > sh)
        {
            notes.Add($"{entryName}: 源图帧 {frame} 越界（{sw}×{sh}，帧 {rw}×{rh}）");
            return false;
        }

        for (int y = 0; y < rh; y++)
        {
            int ty = ry + y;
            if (ty < 0 || ty >= dh) continue;
            int sy = fy + y;
            for (int x = 0; x < rw; x++)
            {
                int tx = rx + x;
                if (tx < 0 || tx >= dw) continue;
                int si = (sy * sw + fx + x) * 4;
                int ti = (ty * dw + tx) * 4;
                int a = src[si + 3];
                if (a == 0) continue;
                if (a == 255)
                {
                    dst[ti] = src[si];
                    dst[ti + 1] = src[si + 1];
                    dst[ti + 2] = src[si + 2];
                    dst[ti + 3] = 255;
                    continue;
                }
                int inv = 255 - a;
                dst[ti] = (byte)((src[si] * a + dst[ti] * inv) / 255);
                dst[ti + 1] = (byte)((src[si + 1] * a + dst[ti + 1] * inv) / 255);
                dst[ti + 2] = (byte)((src[si + 2] * a + dst[ti + 2] * inv) / 255);
                dst[ti + 3] = (byte)Math.Min(255, a + dst[ti + 3] * inv / 255);
            }
        }

        images.SetPixels(targetKey, dst, dw, dh);
        return true;
    }

    /// <summary>
    /// CC 帧表缓存：源文件哈希 + 产出帧表哈希都匹配时，整段重排/解码/写盘都可以跳过。
    /// 版本号在生成逻辑变化时必须提升，否则旧帧表会被误认为仍然有效。
    /// </summary>
    private sealed class CompanionSheetState
    {
        private const int StateVersion = 1;

        private sealed class Entry
        {
            public string Source = "";
            public string Sheet = "";
        }

        private readonly Dictionary<string, Entry> _entries;
        private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);
        private bool _dirty;

        private CompanionSheetState(Dictionary<string, Entry> entries) => _entries = entries;

        public static CompanionSheetState Load(string path)
        {
            var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return new CompanionSheetState(entries);
            try
            {
                JObject state = JObject.Parse(File.ReadAllText(path));
                if (state.Value<int?>("version") != StateVersion) return new CompanionSheetState(entries);
                if (state["entries"] is JObject saved)
                {
                    foreach (JProperty p in saved.Properties())
                    {
                        if (p.Value is not JObject e) continue;
                        string? sourceHash = e.Value<string>("source");
                        string? sheetHash = e.Value<string>("sheet");
                        if (sourceHash == null || sheetHash == null) continue;
                        entries[p.Name.Replace('\\', '/')] = new Entry
                        {
                            Source = sourceHash, Sheet = sheetHash,
                        };
                    }
                }
            }
            catch { /* 缓存坏了就整轮重建 */ }
            return new CompanionSheetState(entries);
        }

        public bool Matches(OutputTree output, string petRel, string source)
        {
            if (_touched.Contains(petRel) || !_entries.TryGetValue(petRel, out Entry? entry))
                return false;
            try
            {
                string sheetPath = output.ToOutputPath(petRel);
                return File.Exists(sheetPath)
                    && string.Equals(HashFile(source), entry.Source, StringComparison.Ordinal)
                    && string.Equals(HashFile(sheetPath), entry.Sheet, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        public void Mark(string petRel, string source)
        {
            _touched.Add(petRel);
            _pending[petRel] = source;
        }

        public void Forget(string petRel)
        {
            _touched.Add(petRel);
            _pending.Remove(petRel);
            if (_entries.Remove(petRel)) _dirty = true;
        }

        public void Save(OutputTree output, string path)
        {
            foreach (var (petRel, source) in _pending)
            {
                try
                {
                    string sheetPath = output.ToOutputPath(petRel);
                    if (!File.Exists(sheetPath) || !File.Exists(source))
                    {
                        if (_entries.Remove(petRel)) _dirty = true;
                        continue;
                    }
                    string sourceHash = HashFile(source);
                    string sheetHash = HashFile(sheetPath);
                    if (!_entries.TryGetValue(petRel, out Entry? old)
                        || !string.Equals(old.Source, sourceHash, StringComparison.Ordinal)
                        || !string.Equals(old.Sheet, sheetHash, StringComparison.Ordinal))
                    {
                        _entries[petRel] = new Entry { Source = sourceHash, Sheet = sheetHash };
                        _dirty = true;
                    }
                }
                catch { /* 读不到就下次再补 */ }
            }
            _pending.Clear();
            if (!_dirty) return;

            var saved = new JObject();
            foreach (var (petRel, entry) in _entries.OrderBy(x => x.Key, StringComparer.Ordinal))
                saved[petRel] = new JObject
                {
                    ["source"] = entry.Source,
                    ["sheet"] = entry.Sheet,
                };
            JsonFile.Write(path, new JObject
            {
                ["version"] = StateVersion,
                ["entries"] = saved,
            });
            _dirty = false;
        }
    }

    /// <summary>
    /// 目标 PNG 文件字节的 SHA-256 缓存。命中时说明磁盘上就是上次写出的完整文件，
    /// 可以直接跳过“解码 PNG → 重算像素哈希”；不命中才回退到像素级状态。
    /// </summary>
    private sealed class TextureOverrideFileHashes
    {
        private readonly Dictionary<string, string> _hashes;
        private readonly HashSet<string> _needs = new(StringComparer.OrdinalIgnoreCase);
        private bool _dirty;

        private TextureOverrideFileHashes(Dictionary<string, string> hashes) => _hashes = hashes;

        public static TextureOverrideFileHashes Load(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return new TextureOverrideFileHashes(map);
            try
            {
                JObject state = JObject.Parse(File.ReadAllText(path));
                if (state["targets"] is JObject targets)
                {
                    foreach (JProperty p in targets.Properties())
                        if (p.Value.Type == JTokenType.String && p.Value.Value<string>() is string hash)
                            map[p.Name.Replace('\\', '/')] = hash;
                }
            }
            catch { /* 缓存损坏就重新建立 */ }
            return new TextureOverrideFileHashes(map);
        }

        public bool Matches(OutputTree output, string target)
        {
            if (!_hashes.TryGetValue(target, out string? stored)) return false;
            try
            {
                string full = output.ToOutputPath(target);
                return File.Exists(full)
                    && string.Equals(HashFile(full), stored, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        public void Mark(string target) => _needs.Add(target);

        public void Save(OutputTree output, string path)
        {
            foreach (string target in _needs)
            {
                try
                {
                    string full = output.ToOutputPath(target);
                    if (File.Exists(full))
                    {
                        string hash = HashFile(full);
                        if (!_hashes.TryGetValue(target, out string? old)
                            || !string.Equals(old, hash, StringComparison.Ordinal))
                        {
                            _hashes[target] = hash;
                            _dirty = true;
                        }
                    }
                    else if (_hashes.Remove(target)) _dirty = true;
                }
                catch { /* 读不到就下次再补，不影响本轮输出 */ }
            }
            _needs.Clear();
            if (!_dirty) return;

            var entries = new JObject();
            foreach (var (target, hash) in _hashes.OrderBy(x => x.Key, StringComparer.Ordinal))
                entries[target] = hash;
            JsonFile.Write(path, new JObject
            {
                ["version"] = 1,
                ["targets"] = entries,
            });
            _dirty = false;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Dictionary<string, string> LoadTextureOverrideTargets(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;
        try
        {
            JObject state = JObject.Parse(File.ReadAllText(path));
            if (state["targets"] is JObject targets)
            {
                foreach (JProperty p in targets.Properties())
                    if (p.Value.Type == JTokenType.String && p.Value.Value<string>() is string hash)
                        map[p.Name.Replace('\\', '/')] = hash;
            }
        }
        catch
        {
            // 状态损坏只会导致重新应用一次；不让整轮烘焙失败。
        }

        return map;
    }

    private static void SaveTextureOverrideTargets(string path, Dictionary<string, string> targets)
    {
        var entries = new JObject();
        foreach (var (target, hash) in targets.OrderBy(x => x.Key, StringComparer.Ordinal))
            entries[target] = hash;
        var state = new JObject
        {
            ["version"] = 2,
            ["targets"] = entries,
        };
        JsonFile.Write(path, state);
    }

    private static string HashPixels(byte[] pixels) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));

    /// <summary>
    /// 只看**包内 config 的布尔开关**：`When: { "Albacore": true }` 在 config=false 时跳过。
    /// 其它条件（HasMod/存档状态/令牌）不猜，照旧处理 —— 这至少能挡掉 Animated Fish 里
    /// 5 条关闭的鱼图覆盖，避免把关闭的动画帧贴回底图。
    /// </summary>
    private static bool TextureOverrideWhenAllows(JObject? when, JObject? config)
    {
        if (when == null || config == null) return true;
        foreach (JProperty p in when.Properties())
        {
            bool expected;
            if (p.Value.Type == JTokenType.Boolean)
                expected = p.Value.Value<bool>();
            else if (p.Value.Type == JTokenType.String &&
                     bool.TryParse(p.Value.Value<string>(), out bool parsed))
                expected = parsed;
            else
                continue;

            JProperty? setting = config.Property(p.Name)
                ?? config.Properties().FirstOrDefault(x =>
                    x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (setting == null) continue;
            bool? actual = setting.Value.Type switch
            {
                JTokenType.Boolean => setting.Value.Value<bool>(),
                JTokenType.String when bool.TryParse(setting.Value.Value<string>(), out bool b) => b,
                _ => null,
            };
            if (actual != null && actual.Value != expected) return false;
        }
        return true;
    }

    /// <summary>把 SpaceCore 的资产名/包内 token 解析成产出树里的绝对图片路径。</summary>
    private static string? ResolveOverrideImage(OutputTree output, string value, PackInfo pack,
        out string? contentKey)
    {
        contentKey = null;
        string raw = value.Trim().Replace('\\', '/');

        // `{{InternalAssetKey: assets/foo.png}}` ⇒ `SMAPI/<uid>/assets/foo.png`
        int token = raw.IndexOf("{{InternalAssetKey:", StringComparison.OrdinalIgnoreCase);
        if (token >= 0)
        {
            int colon = raw.IndexOf(':', token);
            int end = raw.LastIndexOf("}}", StringComparison.Ordinal);
            if (colon > 0 && end > colon)
                raw = "SMAPI/" + pack.UniqueId.ToLowerInvariant() + "/" + raw[(colon + 1)..end].Trim();
        }

        // SpaceCore 的动画描述符：`texture:0..3@9`，资产名里不允许 `:`/`,`/`@`。
        int frameColon = raw.IndexOf(':');
        if (frameColon > 0 && raw[(frameColon + 1)..].All(c => char.IsDigit(c) || c is '.' or '@'))
            raw = raw[..frameColon];

        raw = raw.Trim('/');
        if (raw.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) raw = raw[..^4];
        if (raw.Length == 0) return null;

        // bake-frameworks 是在**已落盘的产出树**上跑，直接按输出根找文件；
        // `ResolveReadablePath` 走的是暂存索引，对同根 Append 的磁盘文件不适用。
        foreach (string ext in new[] { ".png", ".png.png" })
        {
            string direct = System.IO.Path.Combine(output.OutputRoot,
                (raw + ext).Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(direct))
            {
                contentKey = raw;
                return direct;
            }
        }
        return null;
    }

    private static int FirstFrameIndex(string sourceTexture)
    {
        int colon = sourceTexture.IndexOf(':');
        if (colon < 0) return 0;
        string tail = sourceTexture[(colon + 1)..];
        int dot = tail.IndexOf("..", StringComparison.Ordinal);
        if (dot >= 0) tail = tail[..dot];
        int at = tail.IndexOf('@');
        if (at >= 0) tail = tail[..at];
        return int.TryParse(tail.Trim(), out int index) ? index : 0;
    }

    private static IEnumerable<JToken> Descendants(JToken node)
    {
        switch (node)
        {
            case JObject obj:
                yield return obj;
                foreach (JProperty p in obj.Properties())
                    foreach (JToken d in Descendants(p.Value)) yield return d;
                break;
            case JArray arr:
                foreach (JToken item in arr)
                    foreach (JToken d in Descendants(item)) yield return d;
                break;
        }
    }

    private static IEnumerable<TextureModel> TextureModels(string pack)
    {
        string packRoot = System.IO.Path.GetFullPath(pack);
        foreach (string modelPath in Directory.EnumerateFiles(pack, "texture.json",
                     SearchOption.AllDirectories))
        {
            string dir = System.IO.Path.GetDirectoryName(modelPath)!;
            // 旧实现只枚举子目录；保留“包根的 texture.json 不算模型”的语义。
            if (string.Equals(dir, packRoot, StringComparison.OrdinalIgnoreCase)) continue;
            JObject model;
            try { model = JsoncReader.ParseFile(modelPath); }
            catch { continue; }

            var pngs = new List<string>();
            string single = System.IO.Path.Combine(dir, "texture.png");
            if (File.Exists(single))
            {
                pngs.Add(single);
            }
            else
            {
                pngs.AddRange(Directory.EnumerateFiles(dir, "texture_*.png")
                    .Select(p => (Path: p, Match: Regex.Match(System.IO.Path.GetFileName(p), @"^texture_(\d+)\.png$", RegexOptions.IgnoreCase)))
                    .Where(x => x.Match.Success)
                    .OrderBy(x => int.Parse(x.Match.Groups[1].Value))
                    .Select(x => x.Path));
            }
            yield return new TextureModel(dir, model, pngs);
        }
    }

    private static JObject LoadObject(OutputTree output, string rel)
    {
        string? text = output.ReadTextOrNull(rel);
        if (text == null) return new JObject();
        JToken token = JsoncReader.ParseToken(text);
        return token as JObject ?? new JObject();
    }

    private static JArray LoadArray(OutputTree output, string rel)
    {
        string? text = output.ReadTextOrNull(rel);
        if (text == null) return new JArray();
        JToken token = JsoncReader.ParseToken(text);
        return token as JArray ?? new JArray();
    }

    /// <summary>
    /// 数据文档的变更基线：把 token 以固定格式写入内存流后取 SHA-256。
    /// 以前每个文档 DeepClone 一份完整 JObject 做 DeepEquals；实测 15 份文档
    /// 克隆约 200–300 ms，两次序列化+哈希合计约 40–50 ms，且基线只占 32 字节/文档。
    /// </summary>
    private static byte[][] HashAll(JToken[] docs)
    {
        var hashes = new byte[docs.Length][];
        Parallel.For(0, docs.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            i => hashes[i] = HashJson(docs[i]));
        return hashes;
    }

    private static byte[] HashJson(JToken token)
    {
        using var buffer = new MemoryStream();
        using (var text = new StreamWriter(buffer, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        using (var json = new JsonTextWriter(text) { Formatting = Formatting.None })
            token.WriteTo(json);
        buffer.Position = 0;
        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// 把框架新增/改动的行同步到已有的 `Data/&lt;asset&gt;.&lt;locale&gt;.json`。
    /// <para>
    /// 游戏本地化数据是**整份替换**：中文会加载 `Data/Furniture.zh-CN`，而不是 base。
    /// 只写 base 的话，中文客户端看不到 AT 家具/大件、CC 宠物之外的物品、SF 建筑等。
    /// </para>
    /// <para>
    /// 新增行直接克隆；`Data/Buildings` 的已有条目保留本地化 Name/Description，
    /// 只替换非文本字段；`Data/Shops` 合并新增的货架 item。
    /// </para>
    /// </summary>
    private static void MirrorLocalizedData(OutputTree output,
        IReadOnlyList<(string Asset, JToken Current)> assets)
    {
        foreach (var (asset, current) in assets)
        {
            string dirRel = Path.GetDirectoryName(asset)?.Replace('\\', '/') ?? "Data";
            string stem = Path.GetFileNameWithoutExtension(asset);
            string dir = Path.Combine(output.OutputRoot, dirRel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;

            foreach (string file in Directory.EnumerateFiles(dir, stem + ".*.json"))
            {
                string rel = Path.GetRelativePath(output.OutputRoot, file).Replace('\\', '/');
                string? text = output.ReadTextOrNull(rel);
                if (text == null) continue;
                JToken localized;
                try { localized = JsoncReader.ParseToken(text); }
                catch { continue; }
                // `changed` 只在真正写入/替换时置真；不再为每个语言变体再 DeepClone
                // 一份完整文档做末尾比较（这里原本是 O(locales × 文档大小) 的额外内存）。
                bool changed = false;

                if (current is JObject co && localized is JObject lo)
                {
                    foreach (JProperty p in co.Properties())
                    {
                        if (lo.Property(p.Name) == null)
                        {
                            lo[p.Name] = p.Value.DeepClone();
                            changed = true;
                            continue;
                        }

                        // 建筑结构字段以 base 为准；翻译字段保留本地化。
                        if (stem.Equals("Buildings", StringComparison.OrdinalIgnoreCase)
                            && lo[p.Name] is JObject localizedBuilding && p.Value is JObject baseBuilding)
                        {
                            var merged = (JObject)baseBuilding.DeepClone();
                            foreach (string textKey in new[] { "Name", "Description", "DisplayName" })
                                if (localizedBuilding[textKey] != null)
                                    merged[textKey] = localizedBuilding[textKey]!.DeepClone();
                            if (!JToken.DeepEquals(lo[p.Name], merged))
                            {
                                lo[p.Name] = merged;
                                changed = true;
                            }
                        }

                        // 对象的结构字段（不是翻译文本）以 base 为准；否则中文变体里的
                        // CanBeTrashed / CanBeGivenAsGift / ExcludeFromShippingCollection 会停留在旧值。
                        if (stem.Equals("Objects", StringComparison.OrdinalIgnoreCase)
                            && lo[p.Name] is JObject localizedObject && p.Value is JObject baseObject)
                        {
                            foreach (string field in new[]
                                     {
                                         "CanBeTrashed", "CanBeGivenAsGift", "ExcludeFromShippingCollection",
                                     })
                            {
                                if (baseObject[field] == null
                                    || JToken.DeepEquals(localizedObject[field], baseObject[field]))
                                    continue;
                                localizedObject[field] = baseObject[field]!.DeepClone();
                                changed = true;
                            }
                        }

                        // 作物的 RegrowDays 也是结构字段，不能只改 base（中文会整份加载 zh-CN）。
                        if (stem.Equals("Crops", StringComparison.OrdinalIgnoreCase)
                            && lo[p.Name] is JObject localizedCrop && p.Value is JObject baseCrop
                            && baseCrop["RegrowDays"] != null
                            && !JToken.DeepEquals(localizedCrop["RegrowDays"], baseCrop["RegrowDays"]))
                        {
                            localizedCrop["RegrowDays"] = baseCrop["RegrowDays"]!.DeepClone();
                            changed = true;
                        }
                    }

                    // Shops 的新增货架 item 在已有 shop 的 Items 数组里。
                    if (stem.Equals("Shops", StringComparison.OrdinalIgnoreCase))
                        changed |= MergeShopItems(new JObject(), co, lo);
                }
                else if (current is JArray ca && localized is JArray la)
                {
                    var ids = new HashSet<string>(la.OfType<JObject>()
                        .Select(x => x.Value<string>("Id") ?? "")
                        .Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
                    foreach (JObject item in ca.OfType<JObject>())
                    {
                        string? id = item.Value<string>("Id");
                        if (id != null && ids.Contains(id)) continue;
                        la.Add(item.DeepClone());
                        if (id != null) ids.Add(id);
                        changed = true;
                    }
                }

                if (changed)
                    output.StageJson(rel, localized);
            }
        }
    }

    private static bool MergeShopItems(JObject before, JObject after, JObject localized)
    {
        bool changed = false;
        foreach (JProperty shop in after.Properties())
        {
            if (before.Property(shop.Name) != null && JToken.DeepEquals(before[shop.Name], shop.Value))
                continue;
            if (shop.Value is not JObject baseShop || localized[shop.Name] is not JObject localShop)
                continue;
            if (baseShop["Items"] is not JArray baseItems || localShop["Items"] is not JArray localItems)
                continue;
            var keys = new HashSet<string>(localItems.OfType<JObject>().Select(ShopItemKey));
            foreach (JObject item in baseItems.OfType<JObject>())
            {
                string key = ShopItemKey(item);
                if (!keys.Add(key)) continue;
                localItems.Add(item.DeepClone());
                changed = true;
            }
        }
        return changed;
    }

    private static string ShopItemKey(JObject item) =>
        (item.Value<string>("ItemId") ?? "") + "\u0001" + (item.Value<string>("Id") ?? "");

    private static string Safe(string text, int limit = 80)
    {
        string s = Regex.Replace(text ?? "", @"[^A-Za-z0-9_.-]+", "_").Trim('_', '.', '-');
        if (s.Length > limit) s = s[..limit];
        if (s.Length == 0 || s.All(c => c is '_' or '-'))
            s = (s.Length == 0 ? "x" : s) + "_" + Convert.ToHexString(
                SHA1.HashData(Encoding.UTF8.GetBytes(text ?? "")))[..6].ToLowerInvariant();
        return s;
    }

    private static IReadOnlyList<JObject?> Variations(JObject model)
    {
        var list = new List<JObject?>();
        if (model["ManualVariations"] is JArray manual)
        {
            foreach (JObject v in manual.OfType<JObject>()
                         .Where(v => v.Value<int?>("Id") is >= 0)
                         .OrderBy(v => v.Value<int>("Id")))
                list.Add(v);
            if (list.Count > 0) return list;
        }
        int n = Math.Max(1, model.Value<int?>("Variations") ?? 1);
        for (int i = 0; i < n; i++) list.Add(null);
        return list;
    }

    private static string VariationLabel(JObject model, JObject? variation, string baseName, int index)
    {
        string name = variation?.Value<string>("Name") ?? $"{baseName} ({index + 1})";
        if (model["Seasons"] is JArray { Count: > 0 } seasons)
            name = string.Join(", ", seasons.Select(s => s.ToString())) + " " + name;
        return name;
    }

    /// <summary>
    /// 季节也是物品语义的一部分（春季绿篱和秋季枯篱即使只差颜色也不能互相顶掉），
    /// 所以去重分组要带上季节；没有 Seasons 的模型归到空分组。
    /// </summary>
    private static string SeasonKey(JObject model) =>
        model["Seasons"] is JArray { Count: > 0 } seasons
            ? string.Join(",", seasons.Select(s => s.ToString().ToLowerInvariant()))
            : "";

    private static IReadOnlyList<string> BaseNames(JObject model)
    {
        var names = new List<string>();
        if (model["CollectiveNames"] is JArray collective)
            names.AddRange(collective.Select(x => x.ToString()));
        if (model.Value<string>("ItemName") is { Length: > 0 } item) names.Add(item);
        return names;
    }

    /// <summary>
    /// AT 的 `ItemName` 不总是逐字等于原版条目的显示名：`Children bed` vs `Child Bed`、
    /// `Computer` vs `Farm Computer`、`Seasonal Decor Spring` vs `Seasonal Decor`。
    /// 先逐字匹配，再按「归一化词元」近似；同时拒绝匹配本工具自己生成的 `AT_…` 条目，
    /// 否则重跑会拿上一次的产物当基础。
    /// </summary>
    private static (string Id, string Value)? FindFurnitureBase(JObject furniture, IReadOnlyList<string> names)
    {
        foreach (JProperty p in furniture.Properties())
        {
            if (p.Value.Type != JTokenType.String) continue;
            string first = p.Value.Value<string>()!.Split('/')[0];
            if (names.Any(n => n.Equals(first, StringComparison.OrdinalIgnoreCase)))
                return (p.Name, p.Value.Value<string>()!);
        }

        (string Id, string Value)? best = null;
        int bestScore = 0, bestExtra = int.MaxValue;
        bool bestVanilla = false;
        bool ambiguous = false;
        foreach (JProperty p in furniture.Properties())
        {
            if (p.Value.Type != JTokenType.String) continue;
            string first = p.Value.Value<string>()!.Split('/')[0];
            if (IsGeneratedBase(p.Name, first)) continue;
            int score = BaseNameScore(names, first, out int extra);
            if (score <= 0) continue;
            bool vanilla = IsVanillaId(p.Name);
            if (score > bestScore || (score == bestScore && extra < bestExtra)
                || (score == bestScore && extra == bestExtra && vanilla && !bestVanilla))
            {
                best = (p.Name, p.Value.Value<string>()!);
                bestScore = score; bestExtra = extra; bestVanilla = vanilla; ambiguous = false;
            }
            else if (score == bestScore && extra == bestExtra && vanilla == bestVanilla)
            {
                ambiguous = true;
            }
        }
        return ambiguous ? null : best;
    }

    private static (string Id, JObject Obj)? FindBigCraftableBase(JObject big, IReadOnlyList<string> names)
    {
        (string Id, JObject Obj)? best = null;
        int bestScore = 0, bestExtra = int.MaxValue;
        bool bestVanilla = false;
        bool ambiguous = false;
        foreach (JProperty p in big.Properties())
        {
            if (p.Value is not JObject o) continue;
            string name = o.Value<string>("Name") ?? "";
            if (name.Length == 0 || IsGeneratedBase(p.Name, name)) continue;
            int score = BaseNameScore(names, name, out int extra);
            if (score <= 0) continue;
            bool vanilla = IsVanillaId(p.Name);
            if (score > bestScore || (score == bestScore && extra < bestExtra)
                || (score == bestScore && extra == bestExtra && vanilla && !bestVanilla))
            {
                best = (p.Name, o);
                bestScore = score; bestExtra = extra; bestVanilla = vanilla; ambiguous = false;
            }
            else if (score == bestScore && extra == bestExtra && vanilla == bestVanilla)
            {
                ambiguous = true;
            }
        }
        return ambiguous ? null : best;
    }

    private static bool IsGeneratedBase(string id, string name) =>
        id.StartsWith("AT_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("AT_", StringComparison.OrdinalIgnoreCase);

    private static bool IsVanillaId(string id) => id.All(char.IsDigit);

    private static readonly Dictionary<string, string> BasePlurals = new(StringComparer.Ordinal)
    {
        ["children"] = "child", ["people"] = "person", ["mice"] = "mouse", ["geese"] = "goose",
    };

    private static string[] BaseTokens(string text)
    {
        string normalized = Regex.Replace((text ?? "").ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        var tokens = new List<string>();
        foreach (string word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string w = BasePlurals.TryGetValue(word, out string? singular) ? singular : word;
            if (w.Length > 3 && w.EndsWith('s')) w = w[..^1];
            tokens.Add(w);
        }
        return tokens.ToArray();
    }

    private static int BaseNameScore(IReadOnlyList<string> names, string candidate, out int extra)
    {
        extra = 0;
        string[] ct = BaseTokens(candidate);
        if (ct.Length == 0) return 0;
        int best = 0;
        foreach (string name in names)
        {
            string[] nt = BaseTokens(name);
            if (nt.Length == 0) continue;
            var ns = new HashSet<string>(nt);
            var cs = new HashSet<string>(ct);
            if (ns.SetEquals(cs))
            {
                extra = 0;
                return 100;
            }

            if (cs.IsSubsetOf(ns) || ns.IsSubsetOf(cs))
            {
                int diff = Math.Abs(ns.Count - cs.Count);
                int score = 70 - diff;
                if (score > best) { best = score; extra = diff; }
                continue;
            }

            double jaccard = (double)ns.Intersect(cs).Count() / ns.Union(cs).Count();
            if (jaccard >= 0.6)
            {
                int score = (int)(50 * jaccard);
                if (score > best) { best = score; extra = ns.Union(cs).Count() - ns.Intersect(cs).Count(); }
            }
        }
        return best;
    }

    private const string TextureConfig = """
        {
          "Content": {
            "Extension": ".png",
            "Format": 0,
            "ContentWidth": 0,
            "ContentHeight": 0,
            "ShaderOffset": 0,
            "ShaderShell": null
          },
          "Header": {
            "Target": "Windows",
            "FormatVersion": 5,
            "CompressedFlag": "Hidef, Lzx"
          },
          "Readers": [
            {
              "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553",
              "Version": 0
            }
          ]
        }
        """;

    private static void EnsureImageConfig(OutputTree output, string assetName)
    {
        if (output.Exists(assetName + ".config")) return;
        if (AssetCreator.CopyImageConfig(output, assetName) == null)
            output.WriteText(assetName + ".config", TextureConfig);
    }

    private static string CopyAsset(OutputTree output, string source, string rel, List<string> assets)
    {
        output.StageCopy(rel, source);
        string assetName = rel[..^".png".Length];
        EnsureImageConfig(output, assetName);
        assets.Add(rel);
        return assetName;
    }

    private static bool TrySize(ImageCache images, string source, out int width, out int height)
    {
        if (images.TryReadSource(source, out _, out width, out height, out _)) return true;
        width = height = 0;
        return false;
    }

    private static List<string> SliceVertical(OutputTree output, ImageCache images, string source,
        string relPrefix, int frameHeight, int count, List<string> assets)
    {
        var rels = new List<string>();
        if (!images.TryReadSource(source, out byte[] pixels, out int width, out int height, out _))
            return rels;
        int row = width * 4;
        for (int i = 0; i < count && (i + 1) * frameHeight <= height; i++)
        {
            var slice = new byte[width * frameHeight * 4];
            Array.Copy(pixels, i * frameHeight * row, slice, 0, slice.Length);
            string rel = $"{relPrefix}_{i}.png";
            images.SetPixels(rel, slice, width, frameHeight);
            EnsureImageConfig(output, rel[..^".png".Length]);
            assets.Add(rel);
            rels.Add(rel);
        }
        return rels;
    }

    // ================================================================== AT

    private static void SeedFurnitureFingerprints(CosmeticDeduper dedup, OutputTree output,
        ImageCache images, JObject furniture)
    {
        foreach (JProperty p in furniture.Properties())
        {
            if (!p.Name.StartsWith("AT_", StringComparison.Ordinal) ||
                p.Value.Type != JTokenType.String) continue;
            string[] f = p.Value.Value<string>()!.Split('/');
            if (f.Length < 10) continue;
            string rel = f[9].Replace('\\', '/');
            string? full = output.ResolveReadablePath(rel + ".png") ?? output.ResolveReadablePath(rel);
            if (full == null || !TrySize(images, full, out int tw, out int th)) continue;
            int sprite = int.TryParse(f[8], out int si) ? si : 0;
            string[] size = f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int w = size.Length >= 1 && int.TryParse(size[0], out int sw) ? sw * 16 : tw;
            int h = size.Length >= 2 && int.TryParse(size[1], out int sh) ? sh * 16 : th;
            int x = sprite * 16 % tw;
            int y = sprite * 16 / tw * 16;
            var fp = CosmeticDeduper.Capture(images, full, x, y, w, h);
            dedup.Seed(p.Name, fp);
        }
    }

    private static void SeedTextureEntries(CosmeticDeduper dedup, OutputTree output,
        ImageCache images, JObject table, int fw, int fh, bool spriteField)
    {
        foreach (JProperty p in table.Properties())
        {
            if (!p.Name.StartsWith("AT_", StringComparison.Ordinal) ||
                p.Value is not JObject item) continue;
            string? tex = item.Value<string>("Texture");
            if (tex == null) continue;
            string? full = output.ResolveReadablePath(tex.Replace('\\', '/') + ".png");
            if (full == null || !TrySize(images, full, out int tw, out int th)) continue;
            int sprite = spriteField ? item.Value<int?>("SpriteIndex") ?? 0 : 0;
            int x = sprite * 16 % tw;
            int y = sprite * 16 / tw * 16;
            int cw = fw > 0 ? Math.Min(fw, tw - x) : tw;
            int ch = fh > 0 ? Math.Min(fh, th - y) : th;
            dedup.Seed(p.Name, CosmeticDeduper.Capture(images, full, x, y, cw, ch));
        }
    }

    private static int ProcessDecoration(OutputTree output, ImageCache images, string uid,
        TextureModel model, JArray awf, List<string> assets, List<string> notes,
        CosmeticDeduper dedup)
    {
        if (model.Pngs.Count == 0)
        {
            notes.Add($"{model.Model.Value<string>("ItemName")}: 没有 texture.png");
            return 0;
        }
        int count = Variations(model.Model).Count;
        string item = model.Model.Value<string>("ItemName") ?? "Floor";
        string wanted = $"AT_{Safe(uid)}_{Safe(item)}";
        string group = $"decoration|{item}|{SeasonKey(model.Model)}";
        CosmeticDeduper.Fingerprint? fp = null;
        if (dedup.Enabled && TrySize(images, model.Pngs[0], out int dw, out int dh))
            fp = CosmeticDeduper.Capture(images, model.Pngs[0], 0, 0, dw, dh);
        if (awf.OfType<JObject>().Any(x => x.Value<string>("Id") == wanted))
        {
            dedup.Seed(group, wanted, fp);
            return 0;
        }
        if (!dedup.TryAccept(group, wanted, fp, out string? dup))
        {
            notes.Add($"{item}: 与 {dup} 相似/纯变色，按 --dedup-cosmetics 跳过");
            return 0;
        }

        string rel = $"Mods/{Safe(uid)}/AT/{Safe(item)}_{count}.png";
        string asset = CopyAsset(output, model.Pngs[0], rel, assets);
        awf.Add(new JObject
        {
            ["Id"] = wanted,
            ["Texture"] = asset,
            ["IsFlooring"] = item.Equals("Floor", StringComparison.OrdinalIgnoreCase),
            ["Count"] = count,
        });
        return 1;
    }

    /// <summary>
    /// 从源图取一块，必要时补透明边到 16px 网格，写成一张新资产。
    /// 家具的源矩形按 16 像素网格算；床/树等 63/79 高的素材不补边会越界。
    /// </summary>
    private static (string Rel, int Width, int Height)? PaddedFrame(
        OutputTree output, ImageCache images, string source, string relBase,
        int sx, int sy, int sw, int sh, List<string> assets)
    {
        if (!images.TryReadSource(source, out byte[] pixels, out int width, out int height, out _))
            return null;
        if (sx < 0 || sy < 0 || sw <= 0 || sh <= 0 || sx + sw > width || sy + sh > height)
            return null;
        int tw = (sw + 15) / 16 * 16;
        int th = (sh + 15) / 16 * 16;
        var dst = new byte[tw * th * 4];
        for (int y = 0; y < sh; y++)
            Array.Copy(pixels, (sy + y) * width * 4 + sx * 4,
                dst, y * tw * 4, sw * 4);
        string rel = relBase + ".png";
        images.SetPixels(rel, dst, tw, th);
        EnsureImageConfig(output, rel[..^".png".Length]);
        assets.Add(rel);
        return (rel, tw, th);
    }

    private static int ProcessFurniture(OutputTree output, ImageCache images, string uid,
        TextureModel model, JObject furniture, List<string> assets, List<string> notes,
        CosmeticDeduper dedup)
    {
        IReadOnlyList<string> names = BaseNames(model.Model);
        (string Id, string Value)? baseEntry = FindFurnitureBase(furniture, names);
        if (baseEntry == null)
        {
            notes.Add($"{model.Model.Value<string>("ItemName")}: 找不到基础 Data/Furniture 条目");
            return 0;
        }

        var vars = Variations(model.Model);
        int w = model.Model.Value<int?>("TextureWidth") ?? 16;
        int h = model.Model.Value<int?>("TextureHeight") ?? 16;
        bool split = model.Pngs.Count > 1;
        int added = 0;
        string baseName = baseEntry.Value.Value.Split('/')[0];
        int actualW = 0, actualH = 0;
        bool sizeKnown = false;
        void EnsureSize()
        {
            if (sizeKnown) return;
            sizeKnown = true;
            TrySize(images, model.Pngs[0], out actualW, out actualH);
        }
        bool fastSheetKnown = false, fastSheet = false;
        bool IsFastSheet()
        {
            if (fastSheetKnown) return fastSheet;
            fastSheetKnown = true;
            if (split) return false;
            EnsureSize();
            fastSheet = w > 0 && h > 0 && w % 16 == 0 && h % 16 == 0
                        && actualW == w && actualH >= h * vars.Count;
            return fastSheet;
        }

        string group = $"furniture|{baseName}|{SeasonKey(model.Model)}";
        for (int i = 0; i < vars.Count; i++)
        {
            string label = VariationLabel(model.Model, vars[i], baseName, i);
            // 手工变体常出现**同名不同图**（Mioudew 29 条）—— 只按 Name 生成 ID 会互相覆盖。
            // 用变体自带的 Id（没有就用 1-based 序号）做稳定后缀，显示名仍保持 label。
            string varSuffix = vars[i]?.Value<int?>("Id") is int vid ? $"v{vid}" : $"v{i + 1}";
            string id = $"AT_{Safe(uid)}_{Safe(baseName)}_{Safe(label)}_{varSuffix}";

            // 观赏去重：在写资产之前先比对，命中就整条不写（开关默认关闭）。
            CosmeticDeduper.Fingerprint? fp = null;
            if (dedup.Enabled)
            {
                if (split)
                {
                    if (i < model.Pngs.Count && TrySize(images, model.Pngs[i], out int sw, out int sh))
                        fp = CosmeticDeduper.Capture(images, model.Pngs[i], 0, 0, sw, sh);
                }
                else if (IsFastSheet())
                {
                    fp = CosmeticDeduper.Capture(images, model.Pngs[0], 0, i * h, w, h);
                }
                else
                {
                    EnsureSize();
                    int frameW = actualW > 0 ? actualW : w;
                    int frameH = vars.Count > 0 && actualH > 0 && actualH % vars.Count == 0
                        ? actualH / vars.Count : h;
                    fp = CosmeticDeduper.Capture(images, model.Pngs[0], 0, i * frameH, frameW, frameH);
                }
            }

            // 已落盘的条目也要进同组，重跑时缺失变体才能跟它比较。
            if (furniture.Property(id) != null)
            {
                dedup.Seed(group, id, fp);
                continue;
            }
            if (!dedup.TryAccept(group, id, fp, out string? dupId))
            {
                // 报告里带候选 ID，便于拿全量树里的图做视觉核对。
                notes.Add($"{id}: 与 {dupId} 相似/纯变色，按 --dedup-cosmetics 跳过");
                continue;
            }

            string rel;
            int sprite;
            int fw, fh;
            if (split)
            {
                if (i >= model.Pngs.Count)
                {
                    notes.Add($"{baseName}/{label}: 只有 {model.Pngs.Count} 张分离贴图，变体不足");
                    continue;
                }
                if (!TrySize(images, model.Pngs[i], out int sw, out int sh))
                {
                    notes.Add($"{baseName}/{label}: 无法读取 {model.Pngs[i]}");
                    continue;
                }
                var padded = PaddedFrame(output, images, model.Pngs[i],
                    $"Mods/{Safe(uid)}/AT/{Safe(baseName)}_{Safe(label)}", 0, 0, sw, sh, assets);
                if (padded == null) { notes.Add($"{baseName}/{label}: 切图失败"); continue; }
                (rel, fw, fh) = padded.Value;
                sprite = 0;
            }
            else if (IsFastSheet())
            {
                rel = $"Mods/{Safe(uid)}/AT/{Safe(baseName)}_{Safe(label)}_sheet.png";
                CopyAsset(output, model.Pngs[0], rel, assets);
                fw = w;
                fh = h;
                sprite = i * (fw / 16) * (fh / 16);
            }
            else
            {
                // 单张 sheet 实际尺寸与 texture.json 不一致，或高度不是 16 的倍数
                // （实测床 64×63、圣诞树 48×79）：逐变体切一块并补透明边。
                int frameW = actualW > 0 ? actualW : w;
                int frameH = vars.Count > 0 && actualH > 0 && actualH % vars.Count == 0
                    ? actualH / vars.Count : h;
                var padded = PaddedFrame(output, images, model.Pngs[0],
                    $"Mods/{Safe(uid)}/AT/{Safe(baseName)}_{Safe(label)}_frame",
                    0, i * frameH, frameW, frameH, assets);
                if (padded == null)
                {
                    notes.Add($"{baseName}/{label}: 单张 sheet 切图失败（{actualW}×{actualH}）");
                    continue;
                }
                (rel, fw, fh) = padded.Value;
                sprite = 0;
            }

            var fields = baseEntry.Value.Value.Split('/').ToList();
            while (fields.Count < 11) fields.Add("");
            fields[0] = id;
            fields[7] = label;
            fields[8] = sprite.ToString();
            fields[9] = rel[..^".png".Length].Replace('/', '\\');
            if (string.IsNullOrEmpty(fields[10])) fields[10] = "false";
            // 补边后宽高一定是 16 的倍数；显式写 2/3，避免基础条目的 `-1` 按类型默认裁图。
            fields[2] = $"{fw / 16} {fh / 16}";
            fields[3] = fields[2];
            furniture[id] = string.Join('/', fields);
            added++;
        }
        return added;
    }

    private static int ProcessCrop(OutputTree output, ImageCache images, string uid,
        TextureModel model, JObject crops, JObject objects, JObject shops,
        List<string> assets, List<string> notes)
    {
        string? seed = model.Model.Value<string>("ItemName");
        if (seed == null || crops[seed] is not JObject baseCrop || objects[seed] is not JObject baseObj)
        {
            notes.Add($"{seed}: 找不到基础 Data/Crops / Data/Objects 条目");
            return 0;
        }

        var vars = Variations(model.Model);
        bool split = model.Pngs.Count > 1;
        int added = 0;
        for (int i = 0; i < vars.Count; i++)
        {
            string label = VariationLabel(model.Model, vars[i], seed, i);
            // 同名手工变体不能共用一个种子 ID（后写会覆盖前写）。
            string varSuffix = vars[i]?.Value<int?>("Id") is int vid ? $"v{vid}" : $"v{i + 1}";
            string newSeed = $"AT_{Safe(uid)}_{Safe(seed)}_{Safe(label)}_{varSuffix}";
            if (crops.Property(newSeed) != null || objects.Property(newSeed) != null) continue;

            string rel;
            int sprite;
            if (split)
            {
                if (i >= model.Pngs.Count)
                {
                    notes.Add($"{seed}/{label}: 只有 {model.Pngs.Count} 张分离贴图，变体不足");
                    continue;
                }
                rel = $"Mods/{Safe(uid)}/AT/{Safe(seed)}_{Safe(label)}.png";
                CopyAsset(output, model.Pngs[i], rel, assets);
                sprite = 0;
            }
            else
            {
                rel = $"Mods/{Safe(uid)}/AT/{Safe(seed)}_{Safe(label)}_crop.png";
                CopyAsset(output, model.Pngs[0], rel, assets);
                sprite = i;
                if (TrySize(images, model.Pngs[0], out _, out int ih) && sprite * 32 + 32 > ih)
                {
                    notes.Add($"{seed}/{label}: 贴图高度 {ih}，第 {sprite + 1} 行越界");
                    continue;
                }
            }

            var crop = (JObject)baseCrop.DeepClone();
            crop["Texture"] = rel[..^".png".Length];
            crop["SpriteIndex"] = sprite;
            if (model.Model["Seasons"] is JArray { Count: > 0 } seasons)
                crop["Seasons"] = new JArray(seasons.Select(s => s.ToString().ToLowerInvariant()));
            crops[newSeed] = crop;

            var obj = (JObject)baseObj.DeepClone();
            obj["Name"] = newSeed;
            string displayKey = obj.Property("DisplayName") != null ? "DisplayName"
                : obj.Property("Displayname") != null ? "Displayname" : "DisplayName";
            obj[displayKey] = label;
            objects[newSeed] = obj;

            bool sold = false;
            foreach (JObject shop in shops.Properties().Select(p => p.Value).OfType<JObject>())
            {
                if (shop["Items"] is not JArray items) continue;
                var clones = new List<JObject>();
                foreach (JObject it in items.OfType<JObject>())
                {
                    string iid = it.Value<string>("ItemId") ?? "";
                    if (iid != seed && iid != $"(O){seed}") continue;
                    var n = (JObject)it.DeepClone();
                    string prefix = iid.StartsWith("(O)", StringComparison.Ordinal) ? "(O)" : "";
                    n["Id"] = prefix + newSeed;
                    n["ItemId"] = prefix + newSeed;
                    clones.Add(n);
                }
                foreach (JObject n in clones) items.Add(n);
                if (clones.Count > 0) sold = true;
            }
            if (!sold) notes.Add($"{newSeed}: 没有找到基础种子的商店货架，需手动加获取方式");
            added++;
        }
        return added;
    }

    private static int ProcessCraftable(OutputTree output, ImageCache images, string uid,
        TextureModel model, JObject objects, JObject fences, JObject big,
        JObject recipes, JObject shops, List<string> assets, List<string> notes,
        CosmeticDeduper dedup)
    {
        IReadOnlyList<string> names = BaseNames(model.Model);
        (string? Id, JObject? Obj) baseObj = (null, null);
        foreach (JProperty p in objects.Properties())
            if (p.Value is JObject o && names.Any(n => n.Equals(o.Value<string>("Name"), StringComparison.OrdinalIgnoreCase)))
            { baseObj = (p.Name, o); break; }
        (string? Id, JObject? Obj) baseBig = (null, null);
        foreach (JProperty p in big.Properties())
            if (p.Value is JObject o && names.Any(n => n.Equals(o.Value<string>("Name"), StringComparison.OrdinalIgnoreCase)))
            { baseBig = (p.Name, o); break; }
        if (baseObj.Obj == null && baseBig.Obj == null)
        {
            // 只对 BigCraftables 做近似：普通 Object 有可能是洒水器/蟹笼这类按 ID
            // 判定行为的功能物品，复制成新 ID 会“看着像、功能没了”，宁可不写。
            if (FindBigCraftableBase(big, names) is { } fuzzy)
                baseBig = fuzzy;
        }

        if (baseObj.Obj == null && baseBig.Obj == null)
        {
            notes.Add($"{model.Model.Value<string>("ItemName")}: 找不到基础 Objects/BigCraftables 条目");
            return 0;
        }

        var vars = Variations(model.Model);
        int w = model.Model.Value<int?>("TextureWidth") ?? 16;
        int h = model.Model.Value<int?>("TextureHeight") ?? 32;
        bool split = model.Pngs.Count > 1;
        int added = 0;
        string baseName = baseObj.Obj?.Value<string>("Name") ?? baseBig.Obj!.Value<string>("Name") ?? "Craftable";
        string group = $"craftable|{baseName}|{SeasonKey(model.Model)}";
        bool isFence = baseObj.Obj != null && fences.Property(baseObj.Id!) != null;

        for (int i = 0; i < vars.Count; i++)
        {
            string label = VariationLabel(model.Model, vars[i], baseName, i);
            // 手工变体常出现**同名不同图**（Mioudew 29 条）—— 只按 Name 生成 ID 会互相覆盖。
            // 用变体自带的 Id（没有就用 1-based 序号）做稳定后缀，显示名仍保持 label。
            string varSuffix = vars[i]?.Value<int?>("Id") is int vid ? $"v{vid}" : $"v{i + 1}";
            string id = $"AT_{Safe(uid)}_{Safe(baseName)}_{Safe(label)}_{varSuffix}";
            if (!isFence && baseBig.Obj == null)
            {
                notes.Add($"{model.Model.Value<string>("ItemName")}: 基础是普通 Object，暂不处理");
                break;
            }
            bool exists = isFence ? objects.Property(id) != null : big.Property(id) != null;

            CosmeticDeduper.Fingerprint? fp = null;
            if (dedup.Enabled)
            {
                if (split)
                {
                    if (i < model.Pngs.Count && TrySize(images, model.Pngs[i], out int sw, out int sh))
                        fp = CosmeticDeduper.Capture(images, model.Pngs[i], 0, 0, sw, sh);
                }
                else
                {
                    fp = CosmeticDeduper.Capture(images, model.Pngs[0], 0, i * h, w, h);
                }
            }

            if (exists)
            {
                dedup.Seed(group, id, fp);
                continue;
            }
            if (!dedup.TryAccept(group, id, fp, out string? dupId))
            {
                notes.Add($"{baseName}/{label}: 与 {dupId} 相似/纯变色，按 --dedup-cosmetics 跳过");
                continue;
            }

            if (isFence)
            {
                string rel;
                if (split)
                {
                    if (i >= model.Pngs.Count) { notes.Add($"{id}: 分离贴图不足"); continue; }
                    rel = $"Mods/{Safe(uid)}/AT/{Safe(id)}.png";
                    CopyAsset(output, model.Pngs[i], rel, assets);
                }
                else
                {
                    var slices = SliceVertical(output, images, model.Pngs[0],
                        $"Mods/{Safe(uid)}/AT/{Safe(id)}", h, vars.Count, assets);
                    if (i >= slices.Count) { notes.Add($"{id}: 栅栏切图失败"); continue; }
                    rel = slices[i];
                }
                var obj = (JObject)baseObj.Obj.DeepClone();
                obj["Name"] = id;
                string dk = obj.Property("DisplayName") != null ? "DisplayName"
                    : obj.Property("Displayname") != null ? "Displayname" : "DisplayName";
                obj[dk] = label;
                objects[id] = obj;
                var fence = (JObject)fences[baseObj.Id!]!.DeepClone();
                fence["Texture"] = rel[..^".png".Length];
                fences[id] = fence;
                if (!CloneRecipe(recipes, baseObj.Id!, id, id))
                {
                    if (CloneShop(shops, new[] { baseObj.Id! }, id, new[] { "(O)" }))
                        notes.Add($"{id}: 没有配方，已复制基础栅栏的商店货架");
                    else notes.Add($"{id}: 没找到基础栅栏配方/商店");
                }
                notes.Add($"{id}: 栅栏贴图从 AT 变体切出，连接形状需实机核对");
                added++;
            }
            else
            {
                string rel;
                int sprite;
                if (split)
                {
                    if (i >= model.Pngs.Count) { notes.Add($"{id}: 分离贴图不足"); continue; }
                    rel = $"Mods/{Safe(uid)}/AT/{Safe(id)}.png";
                    CopyAsset(output, model.Pngs[i], rel, assets);
                    sprite = 0;
                }
                else
                {
                    rel = $"Mods/{Safe(uid)}/AT/{Safe(id)}_sheet.png";
                    CopyAsset(output, model.Pngs[0], rel, assets);
                    sprite = w == 16 ? i * 2 : 0;
                }
                var b = (JObject)baseBig.Obj.DeepClone();
                b["Name"] = id;
                string dk = b.Property("DisplayName") != null ? "DisplayName"
                    : b.Property("Displayname") != null ? "Displayname" : "DisplayName";
                b[dk] = label;
                b["Texture"] = rel[..^".png".Length];
                b["SpriteIndex"] = sprite;
                big[id] = b;
                if (!CloneRecipe(recipes, baseBig.Id!, id, id))
                {
                    if (CloneShop(shops, new[] { baseBig.Id! }, id, new[] { "(BC)" }))
                        notes.Add($"{id}: 没有配方，已复制基础大件的商店货架");
                    else notes.Add($"{id}: 没找到基础配方/商店（需手动加获取方式）");
                }
                added++;
            }
        }
        return added;
    }

    private static bool CloneRecipe(JObject recipes, string baseId, string newId, string newName)
    {
        foreach (JProperty p in recipes.Properties().ToList())
        {
            if (p.Value.Type != JTokenType.String) continue;
            var parts = p.Value.Value<string>()!.Split('/');
            if (parts.Length < 3 || parts[2].Split(' ')[0] != baseId) continue;
            parts[2] = parts[2].Replace(baseId, newId, StringComparison.Ordinal);
            recipes[newName] = string.Join('/', parts);
            return true;
        }
        return false;
    }

    private static bool CloneShop(JObject shops, IReadOnlyCollection<string> baseIds, string newId,
        IReadOnlyCollection<string> prefixes)
    {
        bool found = false;
        foreach (JObject shop in shops.Properties().Select(p => p.Value).OfType<JObject>())
        {
            if (shop["Items"] is not JArray items) continue;
            var clones = new List<JObject>();
            foreach (JObject it in items.OfType<JObject>())
            {
                string iid = it.Value<string>("ItemId") ?? "";
                string prefix = "";
                string raw = iid;
                foreach (string p in new[] { "(O)", "(BC)", "(F)" })
                    if (raw.StartsWith(p, StringComparison.Ordinal)) { prefix = p; raw = raw[p.Length..]; break; }
                if (!prefixes.Contains(prefix) || !baseIds.Contains(raw)) continue;
                var n = (JObject)it.DeepClone();
                n["Id"] = prefix + newId;
                n["ItemId"] = prefix + newId;
                clones.Add(n);
            }
            foreach (JObject n in clones) items.Add(n);
            if (clones.Count > 0) found = true;
        }
        return found;
    }

    // ================================================================== MFM

    private static int ProcessMail(PackInfo pack, JObject mail, JObject mailZh, JArray triggers,
        JObject cooking, JObject crafting, List<string> notes)
    {
        var i18n = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (string locale in new[] { "default", "zh" })
        {
            string p = System.IO.Path.Combine(pack.Path, "i18n", locale + ".json");
            if (File.Exists(p))
            {
                try { i18n[locale] = JsoncReader.ParseFile(p); } catch { }
            }
        }
        string Local(string text, string locale)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (i18n.TryGetValue(locale, out JObject? t) && t[text] != null) return t[text]!.ToString();
            if (i18n.TryGetValue("default", out JObject? d) && d[text] != null) return d[text]!.ToString();
            return text;
        }

        JToken Parsed;
        string mailPath = System.IO.Path.Combine(pack.Path, "mail.json");
        if (!File.Exists(mailPath)) { notes.Add("mail.json 不存在"); return 0; }
        try { Parsed = JsoncReader.ParseToken(File.ReadAllText(mailPath)); }
        catch (Exception ex) { notes.Add($"mail.json 读取失败：{ex.Message}"); return 0; }
        IEnumerable<JObject> items = Parsed switch
        {
            JArray a => a.OfType<JObject>(),
            JObject o => new[] { o },
            _ => Array.Empty<JObject>(),
        };

        int added = 0;
        foreach (JObject m in items)
        {
            string? mid = m.Value<string>("Id");
            if (mid == null || mail.Property(mid) != null) continue;
            var localNotes = new List<string>();
            string title = Local(m.Value<string>("Title") ?? "", "default");
            string body = Local(m.Value<string>("Text") ?? "", "default");
            foreach (JObject a in (m["Attachments"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string? tok = MailAttachment(a, localNotes);
                if (tok != null) body += tok;
            }
            if (m.Value<string>("Recipe") is { Length: > 0 } recipe)
            {
                string? tok = MailRecipe(recipe, cooking, crafting);
                if (tok != null) body += tok;
                else localNotes.Add($"Recipe={recipe} 不在原版配方表");
            }
            if (m.Value<bool?>("RandomlyChooseAttachment") == true)
                localNotes.Add("RandomlyChooseAttachment 没有静态等价（已按顺序写附件）");
            if (m.Value<string>("LetterBG") != null || m.Value<int?>("WhichBG") != null)
                localNotes.Add("LetterBG/WhichBG 自定义信纸没有原版字段");
            mail[mid] = body + (title.Length > 0 ? $"[#]{title}" : "");

            string zhTitle = Local(m.Value<string>("Title") ?? "", "zh");
            string zhBody = Local(m.Value<string>("Text") ?? "", "zh");
            if (zhTitle != title || zhBody != body)
            {
                foreach (JObject a in (m["Attachments"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    string? tok = MailAttachment(a, new List<string>());
                    if (tok != null) zhBody += tok;
                }
                if (m.Value<string>("Recipe") is { Length: > 0 } r2)
                {
                    string? tok = MailRecipe(r2, cooking, crafting);
                    if (tok != null) zhBody += tok;
                }
                mailZh[mid] = zhBody + (zhTitle.Length > 0 ? $"[#]{zhTitle}" : "");
            }

            triggers.Add(new JObject
            {
                ["Id"] = $"MFM_{Safe(pack.UniqueId)}_{Safe(mid)}",
                ["Trigger"] = "DayEnding",
                ["Condition"] = MfmCond(m, cooking, crafting, localNotes) is { Length: > 0 } cond ? cond : null,
                ["SkipPermanentlyCondition"] = null,
                ["HostOnly"] = false,
                ["Action"] = $"AddMail Current {mid}",
                ["Actions"] = null,
                ["CustomFields"] = null,
                ["MarkActionApplied"] = m.Value<bool?>("Repeatable") != true,
            });
            notes.AddRange(localNotes);
            added++;
        }
        return added;
    }

    private static string? MailAttachment(JObject a, List<string> notes)
    {
        string? idx = a.Value<string>("Index") ?? a["Index"]?.ToString();
        if (idx == null) { notes.Add("附件缺 Index"); return null; }
        int stack = a.Value<int?>("Stack") ?? 1;
        if (a.Value<int?>("Quality") is > 0) notes.Add($"附件 {idx}: Quality 没有邮件命令字段");
        string type = (a.Value<string>("Type") ?? "").ToLowerInvariant();
        string? prefix = type switch
        {
            "object" or "ring" => "(O)",
            "bigobject" or "bigcraftable" => "(BC)",
            "furniture" => "(F)",
            "weapon" => "(W)",
            "boots" => "(B)",
            "qualifieditemid" => "",
            _ => null,
        };
        if (prefix == null) { notes.Add($"附件类型 {a.Value<string>("Type")} 没有原版邮件命令"); return null; }
        string qid = prefix.Length == 0 || idx.StartsWith('(') ? idx : prefix + idx;
        return $"%item id {qid} {stack} %%";
    }

    private static string? MailRecipe(string recipe, JObject cooking, JObject crafting)
    {
        if (cooking.Property(recipe) != null) return $"%item cookingRecipe {recipe} %%";
        if (crafting.Property(recipe) != null) return $"%item craftingRecipe {recipe} %%";
        return null;
    }

    private static string MfmCond(JObject m, JObject cooking, JObject crafting, List<string> notes)
    {
        var parts = new List<string>();
        if (m.Value<string>("GameStateQuery") is { Length: > 0 } gsq) parts.Add(gsq);
        if (m.Value<string>("Date") is { Length: > 0 } date)
        {
            Match mm = Regex.Match(date, @"^(\d+)\s+(\w+)(?:\s+Y?(\d+))?$", RegexOptions.IgnoreCase);
            if (mm.Success)
            {
                parts.Add($"DAY_OF_MONTH {mm.Groups[1].Value}");
                parts.Add($"SEASON {mm.Groups[2].Value.ToLowerInvariant()}");
                if (mm.Groups[3].Success) parts.Add($"YEAR {mm.Groups[3].Value}");
            }
            else notes.Add($"Date=\"{date}\" 无法翻译");
        }
        if (m["Days"] is JArray { Count: > 0 } days)
            parts.Add("ANY " + string.Join(" ", days.Select(d => $"\"DAY_OF_MONTH {d}\"")));
        if (m["Seasons"] is JArray { Count: > 0 } seasons)
            parts.Add("ANY " + string.Join(" ", seasons.Select(s => $"\"SEASON {s.ToString().ToLowerInvariant()}\"")));
        if (m.Value<string>("Weather") is { Length: > 0 } weather &&
            weather.ToLowerInvariant() is "sunny" or "rainy")
            parts.Add($"WEATHER {weather.ToLowerInvariant()}");
        foreach (JObject f in (m["FriendshipConditions"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            string? npc = f.Value<string>("NpcName");
            int lvl = f.Value<int?>("FriendshipLevel") ?? 0;
            if (!string.IsNullOrEmpty(npc) && lvl > 0)
                parts.Add($"PLAYER_FRIENDSHIP_POINTS Current {npc} {lvl * 250}");
        }
        if (m["EventsSeen"] is JArray { Count: > 0 } ev)
            parts.Add("ANY " + string.Join(" ", ev.Select(e => $"\"PLAYER_HAS_SEEN_EVENT Current {e}\"")));
        foreach (JToken e in (m["EventsNotSeen"] as JArray) ?? new JArray())
            parts.Add($"!PLAYER_HAS_SEEN_EVENT Current {e}");
        if (m["MailReceived"] is JArray { Count: > 0 } mr)
            parts.Add("ANY " + string.Join(" ", mr.Select(x => $"\"PLAYER_HAS_MAIL Current {x} Received\"")));
        foreach (JToken x in (m["MailNotReceived"] as JArray) ?? new JArray())
            parts.Add($"!PLAYER_HAS_MAIL Current {x} Received");
        foreach (JToken r in (m["RecipeKnown"] as JArray) ?? new JArray())
            parts.Add($"{(cooking.Property(r.ToString()) != null ? "PLAYER_HAS_COOKING_RECIPE" : "PLAYER_HAS_CRAFTING_RECIPE")} Current {r}");
        foreach (JToken r in (m["RecipeNotKnown"] as JArray) ?? new JArray())
            parts.Add($"!{(cooking.Property(r.ToString()) != null ? "PLAYER_HAS_COOKING_RECIPE" : "PLAYER_HAS_CRAFTING_RECIPE")} Current {r}");
        string? ep = m.Value<string>("ExpandedPrecondition");
        if (!string.IsNullOrEmpty(ep))
        {
            Match mm = Regex.Match(ep, @"^y\s+(\d+)$", RegexOptions.IgnoreCase);
            if (mm.Success) parts.Add($"YEAR {mm.Groups[1].Value}");
            else notes.Add($"ExpandedPrecondition=\"{ep}\" 无法翻译");
        }
        else if (m["ExpandedPreconditions"] is JArray { Count: > 0 })
            notes.Add("ExpandedPreconditions 数组无法整组翻译");
        if (m.Value<int?>("HouseUpgradeLevel") is int hu) parts.Add($"PLAYER_FARMHOUSE_UPGRADE Current {hu}");
        if (m.Value<int?>("CurrentMoney") is int cm) parts.Add($"PLAYER_CURRENT_MONEY Current {cm}");
        if (m.Value<long?>("TotalMoneyEarned") is long tm) parts.Add($"PLAYER_MONEY_EARNED Current {tm}");
        if (m.Value<double?>("RandomChance") is double rc)
            parts.Add($"RANDOM {rc.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        foreach (string k in new[] { "DeepestMineLevel", "Buildings", "HasMods", "SkillConditions",
                     "StatsConditions", "CollectionConditions", "SpecialDateCondition" })
            if (m[k] != null && m[k]!.Type != JTokenType.Null)
                notes.Add($"{k} 没有原版 GSQ 等价（未写入条件）");
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }

    // ================================================================== CC

    /// <summary>
    /// 把 CC 的帧表重排成原版 Pet 的 4×4、32×32 帧表。
    /// CC 行序是 Down/Right/Up/Left；原版 <c>Pet.updateSlaveAnimation</c> 用的是
    /// Right(0)/Up(4)/Down(8)/Left(12)。同时把任意 frameW/H 的帧底部居中补进 32×32。
    /// </summary>
    private static bool TryBuildPetSheet(ImageCache images, string source, JObject companion,
        int frameW, int frameH, out byte[]? sheet)
    {
        sheet = null;
        if (frameW <= 0 || frameH <= 0) return false;
        if (!images.TryReadSource(source, out byte[] pixels, out int sw, out int sh, out _))
            return false;
        int cols = Math.Max(1, sw / frameW);
        var result = new byte[128 * 128 * 4];

        var fallback = PetFrames(companion["DownAnimation"] as JObject);
        if (fallback.Count == 0) fallback.Add(0);
        var rows = new[]
        {
            PetFrames(companion["RightAnimation"] as JObject),
            PetFrames(companion["UpAnimation"] as JObject),
            PetFrames(companion["DownAnimation"] as JObject),
            PetFrames(companion["LeftAnimation"] as JObject),
        };

        for (int row = 0; row < 4; row++)
        {
            List<int> frames = rows[row].Count > 0 ? rows[row] : fallback;
            for (int col = 0; col < 4; col++)
            {
                int frame = frames[col % frames.Count];
                int fx = (frame % cols) * frameW;
                int fy = (frame / cols) * frameH;
                if (fx < 0 || fy < 0 || fx + frameW > sw || fy + frameH > sh) continue;
                int dx = col * 32 + Math.Max(0, (32 - frameW) / 2);
                int dy = row * 32 + Math.Max(0, 32 - frameH);
                int copyW = Math.Min(frameW, sw - fx);
                int copyH = Math.Min(frameH, sh - fy);
                for (int y = 0; y < copyH && y < 32; y++)
                {
                    int si = ((fy + y) * sw + fx) * 4;
                    int di = ((dy + y) * 128 + dx) * 4;
                    int n = Math.Min(copyW, 32) * 4;
                    Array.Copy(pixels, si, result, di, n);
                }
            }
        }

        sheet = result;
        return true;
    }

    private static List<int> PetFrames(JObject? animation)
    {
        var frames = new List<int>();
        if (animation == null) return frames;
        if (animation["ManualFrames"] is JArray manual)
        {
            foreach (JObject f in manual.OfType<JObject>())
                frames.Add(f.Value<int?>("Frame") ?? 0);
            return frames;
        }

        int start = animation.Value<int?>("StartingFrame") ?? 0;
        int count = animation.Value<int?>("NumberOfFrames") ?? 1;
        for (int i = 0; i < count; i++) frames.Add(start + i);
        return frames;
    }

    private static int ProcessCompanions(OutputTree output, ImageCache images, PackInfo pack, JObject pets,
        List<string> assets, List<string> notes, HashSet<string>? vanillaCues,
        CompanionSheetState ccSheets)
    {
        var i18n = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (string locale in new[] { "default", "zh" })
        {
            string p = System.IO.Path.Combine(pack.Path, "i18n", locale + ".json");
            if (File.Exists(p))
            {
                try { i18n[locale] = JsoncReader.ParseFile(p); } catch { }
            }
        }
        string Local(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (i18n.TryGetValue("zh", out JObject? zh) && zh[text] != null) return zh[text]!.ToString();
            if (i18n.TryGetValue("default", out JObject? d) && d[text] != null) return d[text]!.ToString();
            return text;
        }

        string uid = Safe(pack.UniqueId);
        int added = 0;
        string packRoot = System.IO.Path.GetFullPath(pack.Path);
        foreach (string srcPath in Directory.EnumerateFiles(pack.Path, "companion.json",
                     SearchOption.AllDirectories))
        {
            string dir = System.IO.Path.GetDirectoryName(srcPath)!;
            if (string.Equals(dir, packRoot, StringComparison.OrdinalIgnoreCase)) continue;
            JObject src;
            try { src = JsoncReader.ParseFile(srcPath); }
            catch (Exception ex) { notes.Add($"{System.IO.Path.GetRelativePath(pack.Path, dir)}: {ex.Message}"); continue; }
            string name = src.Value<string>("Name") ?? System.IO.Path.GetFileName(dir);
            string id = $"CC_{uid}_{Safe(name)}";

            // CC 的贴图有两种：TileSheetPath（游戏资产名）或目录里的 companion.png。
            string? source = null;
            string tile = src.Value<string>("TileSheetPath") ?? "";
            if (tile.Length > 0)
            {
                string rel = tile.Replace('\\', '/').TrimStart('/');
                // TileSheetPath 是**资产名**，不带扩展名；产出树里实际是 .png/.xnb。
                source = output.ResolveReadablePath(rel + ".png")
                         ?? output.ResolveReadablePath(rel + ".xnb")
                         ?? output.ResolveReadablePath(rel);
                if (source == null)
                {
                    string localTile = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(rel) + ".png");
                    if (File.Exists(localTile)) source = localTile;
                }
            }
            if (source == null)
            {
                string local = System.IO.Path.Combine(dir, "companion.png");
                if (File.Exists(local)) source = local;
            }
            if (source == null)
            {
                notes.Add($"{name}: 找不到 TileSheetPath / companion.png");
                continue;
            }

            string relAsset = $"Mods/{uid}/CC/{Safe(name)}.png";
            string asset = CopyAsset(output, source, relAsset, assets);
            int frameW = src.Value<int?>("FrameSizeWidth") ?? 32;
            int frameH = src.Value<int?>("FrameSizeHeight") ?? 32;

            // CC 的行序是 Down/Right/Up/Left，原版 Pet 是 Right/Up/Down/Left；
            // 而且原版 Pet 的 AnimatedSprite 写死 32×32。这里按方向重排并补成
            // 128×128 的 4×4 帧表，frameW/H 不是 32 的同伴也能正确显示。
            string petRel = $"Mods/{uid}/CC/{Safe(name)}_pet32.png";
            string petAsset = petRel[..^".png".Length];
            if (ccSheets.Matches(output, petRel, source))
            {
                // 源文件与产出帧表都逐字节一致：不重排、不解码、不写盘。
                EnsureImageConfig(output, petAsset);
                assets.Add(petRel);
            }
            else if (TryBuildPetSheet(images, source, src, frameW, frameH, out byte[]? sheet))
            {
                // 与产出树里的像素逐字节相同就不标脏，省掉 PNG 重编码/写盘。
                if (!images.TryGetPixels(petRel, out byte[] current, out int cw, out int ch, out _)
                    || cw != 128 || ch != 128
                    || !current.AsSpan().SequenceEqual(sheet!))
                {
                    images.SetPixels(petRel, sheet!, 128, 128);
                }
                ccSheets.Mark(petRel, source);
                EnsureImageConfig(output, petAsset);
                assets.Add(petRel);
            }
            else
            {
                notes.Add($"{name}: 宠物帧表重排失败，退回原始贴图");
                ccSheets.Forget(petRel);
                petAsset = asset;
            }

            string? barkSound = ChooseCompanionSounds(src, vanillaCues, notes, name,
                out string? movingSound);
            var pet = new JObject
            {
                ["DisplayName"] = Local(name),
                // 空字符串会让 Pet.playContentSound 去播一个空 cue；null 才是“没有音效”。
                ["BarkSound"] = barkSound,
                ["ContentSound"] = null,
                ["MoveSpeed"] = src.Value<int?>("TravelSpeed") ?? 2,
                ["Breeds"] = new JArray(new JObject
                {
                    ["Id"] = id + "_Breed",
                    ["Texture"] = petAsset,
                    // 图标继续用原图的第一帧；不要拿重排后的 128×128 当图标。
                    ["IconTexture"] = asset,
                    ["IconSourceRect"] = new JObject { ["X"] = 0, ["Y"] = 0, ["Width"] = 16, ["Height"] = 16 },
                    ["CanBeChosenAtStart"] = true,
                    ["CanBeAdoptedFromMarnie"] = true,
                    ["AdoptionPrice"] = 0,
                }),
                ["Behaviors"] = BuildCompanionBehaviors(src, movingSound),
                ["GiftChance"] = 0,
            };
            if (pets.Property(id) == null || !SameJson(pets[id]!, pet))
            {
                pets[id] = pet;
                added++;
            }
            if (src.Value<string>("InspectionDialogue") is { Length: > 0 })
                notes.Add($"{name}: InspectionDialogue 没有原版宠物对话槽");
            if (src["Portrait"] != null)
                notes.Add($"{name}: 肖像/检视 UI 丢失");
            string idleBehavior = src.Value<string>("IdleBehavior") ?? "";
            if (idleBehavior.Length > 0
                && !idleBehavior.Equals("Nothing", StringComparison.OrdinalIgnoreCase)
                && !idleBehavior.Equals("Wander", StringComparison.OrdinalIgnoreCase))
                notes.Add($"{name}: IdleBehavior `{idleBehavior}` 没有精确原版状态机，按 Walk 近似");
        }
        return added;
    }

    /// <summary>
    /// 按 JSON 文本比较两个 JToken。
    /// <para>
    /// `JToken.DeepEquals` 会把刚构造的 `int` 与从 JSON 读出的 `long` 当成不同值；
    /// 框架烘焙第二次跑时就会把同一个宠物当成“改过”，幂等性测试因此失败。
    /// 序列化后再解析，类型统一，比较的是实际落盘内容。
    /// </para>
    /// </summary>
    private static bool SameJson(JToken a, JToken b) =>
        JToken.DeepEquals(
            JToken.Parse(a.ToString(Formatting.None)),
            JToken.Parse(b.ToString(Formatting.None)));

    /// <summary>
    /// CC 的 <c>IdleBehavior</c> → 原版 Pet 的状态机。
    /// <para>
    /// `Wander`（以及不认识的玩法）保留原来的 `Walk`；`Nothing` 明确表示站着不动，
    /// 不能仍让它一直走。`Pacing`/`Jumper`/`Hover` 没有精确等价，继续按 `Walk` 近似，
    /// 并在调用方报告。
    /// </para>
    /// </summary>
    private static JArray BuildCompanionBehaviors(JObject source, string? movingSound)
    {
        string idle = source.Value<string>("IdleBehavior") ?? "";
        // CompanionModel 的默认值就是 NOTHING：字段省略时不能当成 Wander。
        if (idle.Length == 0 || idle.Equals("Nothing", StringComparison.OrdinalIgnoreCase))
        {
            return new JArray(new JObject
            {
                ["Id"] = "Idle",
                ["WalkInDirection"] = false,
                ["Animation"] = null,
                ["LoopMode"] = "Loop",
                ["AnimationMinimumLoops"] = -1,
                ["AnimationMaximumLoops"] = -1,
            });
        }

        var walk = new JObject
        {
            ["Id"] = "Walk",
            ["WalkInDirection"] = true,
            ["RandomizeDirection"] = true,
            ["MoveSpeed"] = source.Value<int?>("TravelSpeed") ?? 2,
            // Walk 的四向帧由 Pet 自己按方向选行；不要再塞 DownAnimation 的绝对帧号。
            ["Animation"] = null,
            ["LoopMode"] = "Loop",
            ["AnimationMinimumLoops"] = -1,
            ["AnimationMaximumLoops"] = -1,
        };

        if (movingSound != null)
        {
            // CC 的 Moving 是行走时持续/周期播放；原版只有行为开始时的
            // SoundOnStart。Wander 的 Walk↔Idle 循环至少能让它隔一段时间响一次。
            walk["SoundOnStart"] = movingSound;
            walk["SoundIsVoice"] = true;
        }

        // CC 的 Pacing 在两点之间来回；原版没有路径循环，至少限制为左右方向，
        // 不再让它在四个方向乱走。
        if (idle.Equals("Pacing", StringComparison.OrdinalIgnoreCase))
            walk["IsSideBehavior"] = true;

        // CC 的大部分 Wander 同伴会按 ChanceForHalting 停一会儿；原版只有
        // Walk 会一直走。这里给 Wander 补一个 Idle 状态，按 CC 的
        // MinHaltTime/MaxHaltTime 停，再回到 Walk。
        bool isFlying = (source.Value<string>("Type") ?? "")
            .Equals("Flying", StringComparison.OrdinalIgnoreCase);
        bool canHalt = !isFlying && (source.Value<bool?>("CanHalt") ?? true);
        double haltChance = source.Value<double?>("ChanceForHalting") ?? 1.0;
        if (idle.Equals("Wander", StringComparison.OrdinalIgnoreCase) && canHalt && haltChance > 0)
        {
            int minHalt = Math.Max(250, source.Value<int?>("MinHaltTime") ?? 2000);
            int maxHalt = Math.Max(minHalt, source.Value<int?>("MaxHaltTime") ?? 10000);
            walk["MinimumDuration"] = 3000;
            walk["MaximumDuration"] = 8000;
            walk["TimeoutBehaviorChanges"] = new JArray(new JObject { ["Behavior"] = "Idle" });
            return new JArray(walk, new JObject
            {
                ["Id"] = "Idle",
                ["WalkInDirection"] = false,
                ["Animation"] = null,
                ["LoopMode"] = "Loop",
                ["AnimationMinimumLoops"] = -1,
                ["AnimationMaximumLoops"] = -1,
                ["MinimumDuration"] = minHalt,
                ["MaximumDuration"] = maxHalt,
                ["TimeoutBehaviorChanges"] = new JArray(new JObject { ["Behavior"] = "Walk" }),
            });
        }

        return new JArray(walk);
    }

    /// <summary>
    /// 原版 `Sound Bank` 的 cue 集合；读不到就返回 <c>null</c>（不因此拒写，
    /// 只跳过“cue 不存在”的校验）。
    /// </summary>
    private static HashSet<string>? LoadVanillaCueNames(OutputTree output)
    {
        string? text = output.ReadTextOrNull("XACT/Sound Bank.xsb.json");
        if (text == null) return null;
        try
        {
            var root = JObject.Parse(text);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject sound in (root["Sounds"] as JArray)?.OfType<JObject>()
                                       ?? Enumerable.Empty<JObject>())
                if (sound.Value<string>("Cue") is { Length: > 0 } cue)
                    names.Add(cue);
            return names.Count == 0 ? null : names;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// CC 的 <c>Sounds</c> → <c>BarkSound</c> + <c>Walk.SoundOnStart</c> 的近似。
    /// <para>
    /// `Idle`/`Always` → `BarkSound`（多个只保留第一个）；
    /// `Moving` → Walk 行为的 `SoundOnStart`（原版没有持续行走声，Wander 的
    /// Walk↔Idle 循环会每隔一段时间重新触发一次）；时序/概率仍丢失。
    /// </para>
    /// </summary>
    private static string? ChooseCompanionSounds(JObject source, HashSet<string>? cueNames,
        List<string> notes, string name, out string? movingSound)
    {
        movingSound = null;
        if (source["Sounds"] is not JArray sounds || sounds.Count == 0) return null;

        string? picked = null;
        int idleCount = 0, movingCount = 0, missingCue = 0;
        foreach (JObject sound in sounds.OfType<JObject>())
        {
            string cue = sound.Value<string>("SoundName") ?? "";
            string when = sound.Value<string>("WhenToPlay") ?? "";
            if (cue.Length == 0) continue;
            if (when.Equals("Moving", StringComparison.OrdinalIgnoreCase))
            {
                movingCount++;
                if (cueNames == null || cueNames.Contains(cue))
                    movingSound ??= cue;
                else
                    missingCue++;
                continue;
            }
            if (!when.Equals("Idle", StringComparison.OrdinalIgnoreCase)
                && !when.Equals("Always", StringComparison.OrdinalIgnoreCase))
                continue;

            idleCount++;
            if (cueNames != null && !cueNames.Contains(cue))
            {
                missingCue++;
                continue;
            }
            picked ??= cue;
        }

        if (idleCount > 1 && picked != null)
            notes.Add($"{name}: {idleCount} 个 Idle/Always 音效只保留第一个 `{picked}`（原版只有 BarkSound 一个槽）");
        if (missingCue > 0)
            notes.Add($"{name}: {missingCue} 个音效不在原版 Sound Bank，已丢弃");
        if (movingCount > 1 && movingSound != null)
            notes.Add($"{name}: {movingCount} 个 Moving 音效只保留第一个 `{movingSound}` 到 Walk.SoundOnStart");
        else if (movingCount > 0 && movingSound != null)
            notes.Add($"{name}: Moving 音效写到 Walk.SoundOnStart（不是持续播放）");
        return picked;
    }

    // ================================================================== CCCB

    private static int ProcessCccb(PackInfo pack, JObject bundles, string preferred, List<string> notes)
    {
        string contentPath = System.IO.Path.Combine(pack.Path, "content.json");
        if (!File.Exists(contentPath)) { notes.Add("content.json 不存在"); return 0; }
        JObject data;
        try { data = JsoncReader.ParseFile(contentPath); }
        catch (Exception ex) { notes.Add($"content.json 读取失败：{ex.Message}"); return 0; }
        JProperty? variant = data.Properties().FirstOrDefault();
        if (variant?.Value is not JObject custom) { notes.Add("没有 BundleVariant"); return 0; }
        if (!string.IsNullOrEmpty(preferred) &&
            !variant.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"变体 {variant.Name} 不是选中的 {preferred}，跳过（两套 CCCB 互斥）");
            return 0;
        }

        int replaced = 0;
        foreach (JProperty p in bundles.Properties())
        {
            if (p.Value.Type != JTokenType.String) continue;
            string value = p.Value.Value<string>()!;
            string name = value.Split('/')[0];
            JToken? wanted = custom[name];
            if (wanted == null || wanted.ToString() == value) continue;
            p.Value = wanted.ToString();
            replaced++;
        }
        // ★ **不能**新建 `CCCB_<名字>` 这种键：原版 `NetWorldState.SetBundleData`
        //   把键按 `/` 拆成 `Area/Index`（`key.Split('/')[1]`），没有斜杠的直接
        //   IndexOutOfRangeException ⇒ `CommunityCenter` 建不起来、新存档进不去。
        //   CCCB 的新捆绑包本来由它的 DLL 注册，纯 XNB 没有那层；只能保留对**已有
        //   原版键**的替换，新增项丢弃并写进报告。
        var missing = custom.Properties().Select(p => p.Name)
            .Where(n => !bundles.Properties().Any(p => p.Value.Type == JTokenType.String &&
                                                         p.Value.Value<string>()!.Split('/')[0] == n))
            .ToList();
        if (missing.Count > 0)
            notes.Add($"变体 {variant.Name}: {missing.Count} 个新捆绑包没有原版 `Area/Index` 落点，" +
                      $"纯 XNB 下跳过（例如 {string.Join(", ", missing.Take(3))}）");
        notes.Add($"变体 {variant.Name}: 替换 {replaced} 条");
        return replaced;
    }

    /// <summary>
    /// 去掉 <c>Data/Bundles</c> 里原版解析不了的条目。
    /// 原版 `NetWorldState.SetBundleData`：`key.Split('/')[1]` + `value.Split('/')[2]`，
    /// 缺任一段都是 `IndexOutOfRangeException`，`CommunityCenter` 构造直接失败。
    /// </summary>
    private static int RemoveInvalidBundles(JObject bundles)
    {
        var bad = new List<JProperty>();
        foreach (JProperty p in bundles.Properties())
        {
            if (p.Value.Type != JTokenType.String) { bad.Add(p); continue; }
            string[] key = p.Name.Split('/');
            string value = p.Value.Value<string>() ?? "";
            if (key.Length < 2 || !int.TryParse(key[1], out _)
                || value.Split('/').Length < 3)
                bad.Add(p);
        }
        foreach (JProperty p in bad) p.Remove();
        return bad.Count;
    }

    // ================================================================== SF

    private static JToken Rect(JToken? token, JToken fallback)
    {
        if (token is JObject) return token.DeepClone();
        if (token is JValue { Type: JTokenType.String } v)
        {
            string[] p = v.Value<string>()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4 &&
                int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y) &&
                int.TryParse(p[2], out int w) && int.TryParse(p[3], out int h))
                return new JObject { ["X"] = x, ["Y"] = y, ["Width"] = w, ["Height"] = h };
        }
        return fallback.DeepClone();
    }

    private static JToken Pair(JToken? token, JToken fallback)
    {
        if (token is JObject) return token.DeepClone();
        if (token is JValue { Type: JTokenType.String } v)
        {
            string[] p = v.Value<string>()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 2 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y))
                return new JObject { ["X"] = x, ["Y"] = y };
        }
        return fallback.DeepClone();
    }

    private static int ProcessSolidFoundations(OutputTree output, ImageCache images, PackInfo pack,
        JObject buildings, List<string> assets, List<string> notes)
    {
        var i18n = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (string locale in new[] { "default", "zh" })
        {
            string p = System.IO.Path.Combine(pack.Path, "i18n", locale + ".json");
            if (File.Exists(p))
            {
                try { i18n[locale] = JsoncReader.ParseFile(p); } catch { }
            }
        }
        string Local(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (i18n.TryGetValue("zh", out JObject? zh) && zh[text] != null) return zh[text]!.ToString();
            if (i18n.TryGetValue("default", out JObject? d) && d[text] != null) return d[text]!.ToString();
            return text;
        }

        string uid = Safe(pack.UniqueId);
        int added = 0;
        string packRoot = System.IO.Path.GetFullPath(pack.Path);
        foreach (string srcPath in Directory.EnumerateFiles(pack.Path, "building.json",
                     SearchOption.AllDirectories))
        {
            string dir = System.IO.Path.GetDirectoryName(srcPath)!;
            if (string.Equals(dir, packRoot, StringComparison.OrdinalIgnoreCase)) continue;
            JObject src;
            try { src = JsoncReader.ParseFile(srcPath); }
            catch (Exception ex) { notes.Add($"{System.IO.Path.GetRelativePath(pack.Path, dir)}: {ex.Message}"); continue; }

            string bid = src.Value<string>("Id") ?? System.IO.Path.GetFileName(dir);
            var skinsSrc = (src["Skins"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var skinFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string skinsDir = System.IO.Path.Combine(dir, "Skins");
            if (Directory.Exists(skinsDir))
                foreach (string f in Directory.EnumerateFiles(skinsDir, "*.png"))
                    skinFiles[System.IO.Path.GetFileNameWithoutExtension(f)] = f;
            string basePng = System.IO.Path.Combine(dir, "building.png");
            if (!File.Exists(basePng))
            {
                foreach (JObject s in skinsSrc)
                    if (skinFiles.TryGetValue(s.Value<string>("Texture") ?? "", out string? cand)) { basePng = cand; break; }
            }
            if (!File.Exists(basePng)) { notes.Add($"{bid}: 找不到 building.png / 皮肤贴图"); continue; }

            string relBase = $"Mods/{uid}/SF/{Safe(bid)}.png";
            string baseAsset = CopyAsset(output, basePng, relBase, assets);
            var layerSources = (src["DrawLayers"] as JArray)?.OfType<JObject>().ToList()
                               ?? new List<JObject>();
            bool hideBase = layerSources.Any(l => l.Value<bool?>("HideBaseTexture") == true);
            string buildingTexture = baseAsset;
            if (hideBase)
            {
                // 原版 BuildingData 没有 HideBaseTexture；把底图换成同尺寸透明图，
                // 让 DrawLayers 自己画内容。图层仍指向原始贴图，否则会一起变透明。
                if (images.TryReadSource(basePng, out _, out int bw, out int bh, out _))
                {
                    string emptyRel = $"Mods/{uid}/SF/{Safe(bid)}_empty.png";
                    images.SetPixels(emptyRel, new byte[bw * bh * 4], bw, bh);
                    EnsureImageConfig(output, emptyRel[..^".png".Length]);
                    assets.Add(emptyRel);
                    buildingTexture = emptyRel[..^".png".Length];
                }
                else
                {
                    notes.Add($"{bid}: HideBaseTexture 需要透明底图，但 building.png 读不出来");
                }
            }

            var outBuilding = new JObject
            {
                ["Name"] = Local(src.Value<string>("Name") ?? bid),
                ["Description"] = Local(src.Value<string>("Description") ?? ""),
                ["Texture"] = buildingTexture,
                ["DrawShadow"] = src.Value<bool?>("DrawShadow") ?? true,
                ["FadeWhenBehind"] = src.Value<bool?>("FadeWhenBehind") ?? true,
                ["Size"] = Pair(src["Size"], new JObject { ["X"] = 1, ["Y"] = 1 }),
                ["SourceRect"] = Rect(src["SourceRect"],
                    new JObject { ["X"] = 0, ["Y"] = 0, ["Width"] = 0, ["Height"] = 0 }),
                ["CollisionMap"] = src.Value<string>("CollisionMap"),
                ["BuildCost"] = src.Value<int?>("BuildCost") ?? 0,
                ["BuildDays"] = src.Value<int?>("BuildDays") ?? 0,
                ["Builder"] = src.Value<string>("Builder") ?? "Robin",
            };
            foreach (string k in new[] { "SeasonOffset", "DrawOffset", "SortTileOffset", "HumanDoor",
                         "AnimalDoor", "MagicalConstruction", "UpgradeSignTile", "UpgradeSignHeight",
                         "BuildMaterials", "BuildCondition", "IndoorMap", "IndoorMapType", "MaxOccupants" })
                if (src[k] != null)
                {
                    outBuilding[k] = k switch
                    {
                        "SeasonOffset" or "DrawOffset" or "HumanDoor" or "UpgradeSignTile" =>
                            Pair(src[k], new JObject { ["X"] = 0, ["Y"] = 0 }),
                        "AnimalDoor" => Rect(src[k], new JObject { ["X"] = -1, ["Y"] = -1, ["Width"] = 0, ["Height"] = 0 }),
                        _ => src[k]!.DeepClone(),
                    };
                }

            var skins = new JArray();
            foreach (JObject s in skinsSrc)
            {
                string tex = s.Value<string>("Texture") ?? "";
                // 语料里有的写 `Skin`，有的可能带 `.png`；查表统一按无扩展名。
                string texKey = System.IO.Path.GetFileNameWithoutExtension(tex);
                if (!skinFiles.TryGetValue(texKey, out string? png))
                {
                    notes.Add($"{bid}/{s.Value<string>("ID")}: 皮肤贴图 {tex} 找不到");
                    continue;
                }
                string rel = $"Mods/{uid}/SF/{Safe(bid)}_{Safe(tex)}.png";
                CopyAsset(output, png, rel, assets);
                var skin = new JObject
                {
                    ["Id"] = s.Value<string>("ID") ?? $"{bid}_Skin{skins.Count}",
                    ["Name"] = Local(s.Value<string>("Name") ?? tex),
                    ["Description"] = Local(s.Value<string>("Description") ?? ""),
                    ["Texture"] = rel[..^".png".Length],
                };
                foreach (string k in new[] { "BuildCost", "BuildDays", "BuildMaterials",
                             "ShowAsSeparateConstructionEntry", "Condition" })
                    if (s[k] != null) skin[k] = s[k]!.DeepClone();
                skins.Add(skin);
            }
            if (skins.Count > 0) outBuilding["Skins"] = skins;

            var layers = new JArray();
            int li = 0;
            foreach (JObject layer in layerSources)
            {
                var seqs = (layer["Sequences"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                var l = new JObject
                {
                    ["Id"] = $"SF_{Safe(bid)}_{li++}",
                    // 图层必须继续指向原始贴图；HideBaseTexture 时只把 building.Texture 换成透明。
                    ["Texture"] = baseAsset,
                    ["SourceRect"] = Rect(layer["SourceRect"], outBuilding["SourceRect"]!),
                    ["DrawPosition"] = Pair(layer["DrawPosition"], new JObject { ["X"] = 0, ["Y"] = 0 }),
                    ["DrawInBackground"] = layer.Value<bool?>("DrawInBackground") ?? false,
                };
                // SF 既有直接 FrameCount/FramesPerRow/FrameDuration，也有 Sequences；
                // 旧实现只读 Sequences，且把 FramesPerRow 写死 1，多行动画会错。
                int frameCount = layer.Value<int?>("FrameCount") ?? seqs.Count;
                if (frameCount > 0)
                {
                    l["FrameCount"] = frameCount;
                    l["FramesPerRow"] = layer.Value<int?>("FramesPerRow") ?? 1;
                    l["FrameDuration"] = layer.Value<int?>("FrameDuration")
                                         ?? seqs.FirstOrDefault()?.Value<int?>("Duration") ?? 90;
                }
                layers.Add(l);
            }
            if (layers.Count > 0) outBuilding["DrawLayers"] = layers;
            if (src["Lights"] is JArray { Count: > 0 } lights)
                notes.Add($"{bid}: {lights.Count} 个灯没有原版 BuildingData 字段");
            if (buildings.Property(bid) == null || !JToken.DeepEquals(buildings[bid], outBuilding))
            {
                buildings[bid] = outBuilding;
                added++;
            }
        }
        return added;
    }

    // ================================================================== ContentHashes / report

    private static void AddContentHashes(OutputTree output, IEnumerable<string> rels)
    {
        string? text = output.ReadTextOrNull("ContentHashes.json");
        if (text == null) return;
        JObject hashes;
        try { hashes = JObject.Parse(text); } catch { return; }
        // 原来每个新增 rel 都把上万个属性线性扫一遍（O(H×A)）。
        // 这里只建一次大小写不敏感的键集合，查询 O(1)；集合本身只存已有键名。
        var existing = new HashSet<string>(
            hashes.Properties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (string rel in rels.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string key = System.IO.Path.ChangeExtension(rel, ".xnb").Replace('\\', '/');
            if (!existing.Add(key)) continue;
            hashes[key] = "";
            changed = true;
        }
        if (changed) output.WriteText("ContentHashes.json", hashes.ToString(Formatting.None));
    }

    // ================================================================== 运行时框架的静态近似

    /// <summary>
    /// MoreGrass 的内容包是「每季一堆 15×20 草贴图」，原版草只从
    /// <c>TerrainFeatures/grass</c> 的 3 个横向槽位随机取（<c>Grass.setUpRandom</c> 的
    /// <c>random.Next(3)</c>）。没有 DLL 时无法扩充槽位，只能把每季最多 3 张图
    /// **覆盖进原版槽位** —— 这不是增量条目，而是全局换草；其余 10 个变体、
    /// 位置白名单、冬季生长配置都会丢。
    /// </summary>
    private static int ProcessMoreGrass(OutputTree output, ImageCache images, PackInfo pack,
        List<string> assets, List<string> notes)
    {
        const string rel = "TerrainFeatures/grass.png";
        string? full = output.ResolveReadablePath(rel);
        if (full == null ||
            !images.TryReadSource(full, out byte[] sheet, out int width, out int height, out _))
        {
            notes.Add("找不到 TerrainFeatures/grass.png，MoreGrass 只能跳过");
            return 0;
        }

        byte[] before = (byte[])sheet.Clone();
        (string Dir, int Row)[] seasons =
        {
            ("spring", 0), ("summer", 20), ("fall", 40), ("winter", 80),
        };
        int seasonsBaked = 0;
        foreach ((string dirName, int row) in seasons)
        {
            string dir = System.IO.Path.Combine(pack.Path, dirName);
            if (!Directory.Exists(dir)) continue;
            var files = Directory.EnumerateFiles(dir, "*.png")
                .OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0) continue;

            // 原版只有 3 个槽位；从 13 个变体里等距取 3 个，避免只拿到相邻的同款。
            int[] picks = files.Count >= 3
                ? new[] { 0, files.Count / 2, files.Count - 1 }
                : Enumerable.Range(0, files.Count).ToArray();
            bool rowChanged = false;
            for (int slot = 0; slot < 3 && slot < picks.Length; slot++)
            {
                int sx = slot * 15, sy = row;
                if (sx + 15 > width || sy + 20 > height) continue;
                byte[] src;
                int sw, sh;
                try { src = Png.Read(files[picks[slot]], out sw, out sh); }
                catch { continue; }
                int cw = Math.Min(15, sw), ch = Math.Min(20, sh);
                // 先清空整格，避免源图小于 15×20 时残留原版草像素。
                for (int y = 0; y < 20; y++)
                    Array.Clear(sheet, ((sy + y) * width + sx) * 4, 15 * 4);
                for (int y = 0; y < ch; y++)
                    Array.Copy(src, y * sw * 4, sheet,
                        ((sy + 20 - ch + y) * width + sx) * 4, cw * 4);
                rowChanged = true;
            }
            if (rowChanged) seasonsBaked++;
        }

        if (sheet.AsSpan().SequenceEqual(before)) return 0;
        images.SetPixels(rel, sheet, width, height);
        assets.Add(rel);
        notes.Add("MoreGrass：每季吸收最多 3 个变体到原版 TerrainFeatures/grass（全局替换；" +
                  "位置白名单/冬季生长配置/其余变体丢失）");
        return seasonsBaked;
    }

    private static int ProcessBusLocations(OutputTree output, PackInfo pack, List<string> notes)
    {
        string path = System.IO.Path.Combine(pack.Path, "content.json");
        JToken root;
        try { root = JsoncReader.ParseFile(path); }
        catch (Exception ex)
        {
            notes.Add($"content.json 读取失败：{ex.GetType().Name}: {ex.Message}");
            return 0;
        }
        IEnumerable<JObject> items = root switch
        {
            JArray a => a.OfType<JObject>(),
            JObject o => new[] { o },
            _ => Array.Empty<JObject>(),
        };
        int added = 0;
        foreach (JObject b in items)
        {
            // BusLocations 的 content.json 是全小写字段（mapname/destinationX…），
            // 框架本体靠 Newtonsoft 的大小写不敏感反序列化读；这里也按不区分大小写取。
            string map = b.GetValue("MapName", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "";
            int x = b.GetValue("DestinationX", StringComparison.OrdinalIgnoreCase)?.Value<int?>() ?? 0;
            int y = b.GetValue("DestinationY", StringComparison.OrdinalIgnoreCase)?.Value<int?>() ?? 0;
            string display = b.GetValue("DisplayName", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? map;
            if (map.Length == 0) continue;
            if (map.Equals("Desert", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"{display}: 原版巴士已能到 Desert，跳过重复站点");
                continue;
            }
            if (!MapExists(output, map))
            {
                notes.Add($"{display}: 目标地图 Maps/{map}.tbin 不在产出树里，跳过");
                continue;
            }
            string action = $"Warp {x} {y} {map}";
            if (MapHasAction(output, "Maps/BusStop", action))
            {
                notes.Add($"{display}: 巴士站已有同一 Warp 图块，跳过（幂等）");
                continue;
            }
            // ★ 售票机的**外观**与**交互**必须分层：
            //   原版 `BusStop.checkAction` 只要看到 Buildings 层是 `outdoors@1057`，就直接
            //   弹“去沙漠”的买票对话，**轮不到**图块自己的 `Action=Warp`。所以克隆机把
            //   1057 画在 Buildings2（只负责外观），Buildings 放一格**透明图块**（16）承载
            //   Action；这样点击才会落到原版 `GameLocation.checkAction` 的 Warp 分支。
            if (AddTransitPad(output, "Maps/BusStop", "Buildings", "outdoors", 1057,
                    17, 11, action, display, notes, visualLayer: "Buildings2", actionTileIndex: 16,
                    upperLayer: "Front"))
                added++;
        }
        return added;
    }

    private static int ProcessTrainStation(OutputTree output, PackInfo pack, List<string> notes)
    {
        string path = System.IO.Path.Combine(pack.Path, "TrainStops.json");
        JObject root;
        try { root = JObject.Parse(File.ReadAllText(path)); }
        catch (Exception ex)
        {
            notes.Add($"TrainStops.json 读取失败：{ex.GetType().Name}: {ex.Message}");
            return 0;
        }
        int added = 0;
        foreach (JObject stop in (root["TrainStops"] as JArray)?.OfType<JObject>()
                                   ?? Enumerable.Empty<JObject>())
        {
            string map = stop.Value<string>("TargetMapName") ?? "";
            int x = stop.Value<int?>("TargetX") ?? 0;
            int y = stop.Value<int?>("TargetY") ?? 0;
            string display = stop["LocalizedDisplayName"] is JObject loc
                ? loc.Properties().Select(p => p.Value.ToString()).FirstOrDefault() ?? map
                : stop.Value<string>("LocalizedDisplayName") ?? map;
            if (map.Length == 0) continue;
            if (!MapExists(output, map))
            {
                notes.Add($"{display}: 目标地图 Maps/{map}.tbin 不在产出树里，跳过");
                continue;
            }
            string action = $"Warp {x} {y} {map}";
            if (MapHasAction(output, "Maps/Railroad", action))
            {
                notes.Add($"{display}: 火车站已有同一 Warp 图块，跳过（幂等）");
                continue;
            }
            if (AddTransitPad(output, "Maps/Railroad", "Buildings", "untitled tile sheet", 1205,
                    46, 40, action, display, notes))
                added++;
        }
        return added;
    }

    private static bool MapExists(OutputTree output, string mapName) =>
        output.ReadBytesOrNull($"Maps/{mapName}.tbin") != null;

    /// <summary>扫地图里所有图块属性，判断同一个 Warp 是否已经落过（幂等）。</summary>
    private static bool MapHasAction(OutputTree output, string mapAsset, string action)
    {
        byte[]? bytes = output.ReadBytesOrNull(mapAsset + ".tbin");
        if (bytes == null) return false;
        TBin10 map;
        try { map = (TBin10)TBin10Reader.Create(bytes).Read(); }
        catch { return false; }
        foreach (Layer layer in map.Layers ?? new List<Layer>())
            foreach (BaseTile? tile in layer.Tiles ?? new List<BaseTile>())
                if (tile?.Properties?.Any(p => p.Key == "Action" &&
                                               string.Equals(p.Value?.ToString(), action,
                                                   StringComparison.OrdinalIgnoreCase)) == true)
                    return true;
        return false;
    }

    /// <summary>
    /// 在 <paramref name="mapAsset"/> 的空 Buildings 格上复制一个「售票机/箱子」图块，
    /// 把 Action 改成原版 <c>Warp</c>。这是纯 XNB 下唯一能保留“点一下去另一个地点”的落点；
    /// 原框架的票价、条件、选站 UI 都没有了。
    /// </summary>
    private static bool AddTransitPad(OutputTree output, string mapAsset, string layerName,
        string sheetId, int tileIndex, int anchorX, int anchorY, string action, string label,
        List<string> notes, string? visualLayer = null, int actionTileIndex = -1,
        string? upperLayer = null)
    {
        string rel = mapAsset + ".tbin";
        byte[]? bytes = output.ReadBytesOrNull(rel);
        if (bytes == null)
        {
            notes.Add($"{label}: 找不到 {rel}，无法落交通图块");
            return false;
        }
        TBin10 map;
        try { map = (TBin10)TBin10Reader.Create(bytes).Read(); }
        catch (Exception ex)
        {
            notes.Add($"{label}: {rel} 读取失败：{ex.Message}");
            return false;
        }
        Layer? buildings = map.Layers?.FirstOrDefault(l => l.Id == layerName);
        Layer? back = map.Layers?.FirstOrDefault(l => l.Id == "Back");
        if (buildings == null || back == null)
        {
            notes.Add($"{label}: {mapAsset} 缺 {layerName}/Back 图层，跳过");
            return false;
        }

        int w = buildings.LayerSize.X, h = buildings.LayerSize.Y;

        // 模板优先取**锚点上的真实图块**（BusStop 的原版售票机在 17,11），
        // 不再盲信硬编码的图集/索引 —— 否则复制出来的是别的机器（实机像灰色问号箱）。
        if (anchorX >= 0 && anchorY >= 0 && anchorY < h && anchorX < w)
        {
            int anchorIndex = anchorY * w + anchorX;
            if (buildings.Tiles != null && anchorIndex < buildings.Tiles.Count &&
                buildings.Tiles[anchorIndex] is StaticTile template)
            {
                sheetId = template.TileSheet;
                tileIndex = template.TileIndex;
            }
        }

        // 售票机是**两层**的：交互/下半身在 Buildings，显示屏/顶部在上一格的 Front
        // （原版 BusStop 的 17,11 = Buildings@1057，17,10 = Front@1032）。
        // 只复制 Buildings 会得到一台没有屏幕的半截机器。
        Layer? upper = null;
        string? upperSheet = null;
        int upperIndex = 0;
        if (upperLayer != null)
        {
            upper = map.Layers?.FirstOrDefault(l => l.Id == upperLayer);
            int anchorUpper = (anchorY - 1) * w + anchorX;
            if (upper?.Tiles != null && anchorY - 1 >= 0 && anchorUpper >= 0 &&
                anchorUpper < upper.Tiles.Count && upper.Tiles[anchorUpper] is StaticTile upperTemplate)
            {
                upperSheet = upperTemplate.TileSheet;
                upperIndex = upperTemplate.TileIndex;
            }
            else
            {
                notes.Add($"{label}: {mapAsset} 的锚点上方没有 {upperLayer} 外观图块，只复制下半身");
                upper = null;
            }
        }

        (int X, int Y)? spot = null;
        // 已占的售票机/箱子（含模板本身）——新设施至少隔 1 格，不能密铺把站台堵死。
        // 扫描起点在模板机器右边一格；模板本身在 anchorX-1，必须一起算进“已占”。
        var occupied = new List<(int X, int Y)> { (Math.Max(0, anchorX - 1), anchorY) };
        for (int i = 0; i < buildings.Tiles.Count; i++)
        {
            BaseTile? tile = buildings.Tiles[i];
            if (tile?.Properties == null) continue;
            bool warp = tile.Properties.Any(p => p.Key == "Action" &&
                p.Value?.ToString()?.StartsWith("Warp ", StringComparison.OrdinalIgnoreCase) == true);
            bool passable = tile.Properties.Any(p => p.Key == "Passable" &&
                string.Equals(p.Value?.ToString(), "T", StringComparison.OrdinalIgnoreCase));
            if (warp && passable) occupied.Add((i % w, i / w));
        }
        bool Clear(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            int i = y * w + x;
            // Back 层必须是有地板的空格，且没有自己的动作属性。
            if (back.Tiles[i] == null || (back.Tiles[i].Properties?.Count ?? 0) != 0)
                return false;
            // 不覆盖**任何**非 Back 图层（Buildings/Objects/Front/Paths…）已有图块。
            foreach (Layer layer in map.Layers)
            {
                if (layer.Id == "Back" || layer.Tiles == null) continue;
                if (i < layer.Tiles.Count && layer.Tiles[i] != null) return false;
            }
            // 上半身要放到 (x, y-1)，那里也不能压已有图块。
            if (upper != null)
            {
                if (y - 1 < 0) return false;
                int ui = (y - 1) * w + x;
                if (upper.Tiles == null || ui >= upper.Tiles.Count || upper.Tiles[ui] != null)
                    return false;
            }
            // 与已有设施保持至少 1 格空位（切比雪夫距离 ≥ 2）。
            return occupied.All(p => Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y)) >= 2);
        }

        // 先沿同一行向右/向左，再逐行上下扩；每台之间留一格，玩家能走进去。
        for (int d = 0; d < 12 && spot == null; d++)
        {
            int y = d == 0 ? anchorY
                : anchorY + ((d + 1) / 2) * ((d % 2 == 1) ? 1 : -1);
            if (y < 0 || y >= h) continue;
            for (int x = anchorX; x < Math.Min(w, anchorX + 12) && spot == null; x++)
                if (Clear(x, y)) spot = (x, y);
            for (int x = anchorX - 1; x >= Math.Max(0, anchorX - 12) && spot == null; x--)
                if (Clear(x, y)) spot = (x, y);
        }
        if (spot == null)
        {
            notes.Add($"{label}: {mapAsset} 找不到空的 Buildings 格，跳过");
            return false;
        }

        if (visualLayer != null && map.Layers?.Any(l => l.Id == visualLayer) != true)
            visualLayer = null;
        if (actionTileIndex < 0) actionTileIndex = tileIndex;

        var actionProps = new JObject { ["Action"] = action };
        // 单层设施（火车站箱子）默认有碰撞，标 Passable 不堵站台；
        // 分层的售票机保留原版“机器是实体”的碰撞，只把外观放到 Buildings2。
        if (visualLayer == null) actionProps["Passable"] = "T";

        var newTiles = new JArray();
        // 交互格：绝不能再是 `outdoors@1057`（BusStop.checkAction 会抢先弹去沙漠）。
        newTiles.Add(new JObject
        {
            ["Layer"] = layerName,
            ["Position"] = new JObject { ["X"] = spot.Value.X, ["Y"] = spot.Value.Y },
            ["SetTilesheet"] = sheetId,
            ["SetIndex"] = visualLayer == null ? tileIndex : actionTileIndex,
            ["SetProperties"] = actionProps,
        });
        if (visualLayer != null)
        {
            // 外观格：只画售票机，不挂 Action。
            newTiles.Add(new JObject
            {
                ["Layer"] = visualLayer,
                ["Position"] = new JObject { ["X"] = spot.Value.X, ["Y"] = spot.Value.Y },
                ["SetTilesheet"] = sheetId,
                ["SetIndex"] = tileIndex,
            });
        }
        if (upper != null && upperSheet != null)
        {
            // 上半身（屏幕/按钮）在上一格的 Front，和原版 17,10 的 Front@1032 同构。
            newTiles.Add(new JObject
            {
                ["Layer"] = upperLayer,
                ["Position"] = new JObject { ["X"] = spot.Value.X, ["Y"] = spot.Value.Y - 1 },
                ["SetTilesheet"] = upperSheet,
                ["SetIndex"] = upperIndex,
            });
        }

        var patch = new JObject
        {
            ["Action"] = "EditMap",
            ["MapTiles"] = newTiles,
        };
        MergeOutcome outcome = MapApplier.Apply(output, mapAsset, patch);
        if (!outcome.Success)
        {
            notes.Add($"{label}: 写 {mapAsset} 失败：{outcome.FailureReason}");
            return false;
        }
        if (!MapHasAction(output, mapAsset, action))
        {
            notes.Add($"{label}: 图块没有落进 {mapAsset}（图集 Id/图层不匹配？）");
            return false;
        }
        notes.Add($"{label}: {mapAsset} ({spot.Value.X},{spot.Value.Y}) 新增 Passable Warp 图块；" +
                  "原框架的票价/条件/选站 UI 丢失");
        return true;
    }

    private static JObject BuildReport(IReadOnlyList<PackResult> results, int deduped)
    {
        var root = new JObject
        {
            ["frameworks"] = new JArray("AlternativeTextures", "SolidFoundations",
                "CustomCompanions", "CCCB", "MailFramework", "MoreGrass",
                "BusLocations", "TrainStation", "SpaceCore"),
            ["summary"] = new JObject
            {
                ["furniture"] = results.Sum(r => r.Furniture),
                ["decoration"] = results.Sum(r => r.Decoration),
                ["crop"] = results.Sum(r => r.Crop),
                ["craftable"] = results.Sum(r => r.Craftable),
                ["mail"] = results.Sum(r => r.Mail),
                ["bundles"] = results.Sum(r => r.Bundles),
                ["buildings"] = results.Sum(r => r.Buildings),
                ["pets"] = results.Sum(r => r.Pets),
                ["grass"] = results.Sum(r => r.Grass),
                ["transit"] = results.Sum(r => r.Transit),
                ["textures"] = results.Sum(r => r.Textures),
                ["deduped"] = deduped,
            },
            ["packs"] = new JArray(results.Select(r => new JObject
            {
                ["framework"] = r.Framework,
                ["name"] = r.Name,
                ["path"] = r.Path,
                ["furniture"] = r.Furniture,
                ["decoration"] = r.Decoration,
                ["crop"] = r.Crop,
                ["craftable"] = r.Craftable,
                ["mail"] = r.Mail,
                ["bundles"] = r.Bundles,
                ["buildings"] = r.Buildings,
                ["pets"] = r.Pets,
                ["grass"] = r.Grass,
                ["transit"] = r.Transit,
                ["textures"] = r.Textures,
                ["notes"] = new JArray(r.Notes ?? Array.Empty<string>()),
            })),
        };
        return root;
    }
}
