using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Cp.Lexing;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>一段数据的翻译结果。失败时带可读原因与逐处失败清单。</summary>
public sealed record TranslationOutcome
{
    public bool Success { get; init; }
    public JToken? Data { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    /// <summary>成功但有保留意见（当前唯一来源是<b>有损确定化</b>）。<b>必须展示，不得混进「无损」。</b></summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public static TranslationOutcome Ok(JToken data, IReadOnlyList<string>? notes = null) =>
        new() { Success = true, Data = data, Notes = (IReadOnlyList<string>?)notes ?? Array.Empty<string>() };
    public static TranslationOutcome Fail(IReadOnlyList<string> failures) =>
        new() { Success = false, Failures = failures, FailureReason = Summarize(failures) };

    private static string Summarize(IReadOnlyList<string> failures) =>
        failures.Count <= 1
            ? failures[0]
            : $"{failures.Count} 处：" + string.Join("；", failures.Take(3)) +
              (failures.Count > 3 ? " …" : "");
}

/// <summary>
/// 把一段资产数据里的 <c>{{i18n:key}}</c> 换成某语言的译文。
/// <para>
/// 只动 <c>{{i18n:...}}</c>，其它令牌（<c>{{Random}}</c>/<c>{{Target}}</c>/包内自定义令牌）原样保留。
/// 键含内嵌令牌（动态键）时，先按 <see cref="DynamicTokenResolver"/> 展开包内动态令牌、
/// 再展开 <c>{{Target}}</c>/<c>{{TargetWithoutPath}}</c>/<c>{{TargetPathOnly}}</c> 三个字段引用令牌；
/// 展开完仍是动态的、或带 <c>|参数</c> 的、或键查不到的一律<b>判失败</b> —— 绝不留字面量。
/// </para>
/// </summary>
public static class TranslationApplier
{
    private const string TokenStart = "{{i18n:";
    private const string TokenEnd = "}}";

    /// <summary>
    /// 数据里是否含任何 <c>{{i18n:...}}</c> 令牌（**键名也算**）。
    /// <para>
    /// 键名会算是因为 CP 把 JSON 键也当令牌串解析：实测 `[CP] 鸟类观察协会` 的
    /// `Data/CraftingRecipes` 把配方名写在**键**上（<c>"{{i18n:big-craftable…name}}": "…"</c>），
    /// 只查值会让这类资产走"无 i18n"的快速路径，键原样落进产出 —— 制作菜单直接显示
    /// <c>{{i18n:…}}</c>，且配方在中文/其它语言下不是本地化名。
    /// </para>
    /// </summary>
    public static bool ContainsToken(JToken data)
    {
        // 热路径：每写一条补丁都要扫整份目标文档（大资产几万个节点）。
        // 手写循环省掉 LINQ 的枚举器/委托分配；找到第一个就返回，行为与递归版逐字一致。
        switch (data)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties())
                    if (p.Name.Contains(TokenStart, StringComparison.OrdinalIgnoreCase)
                        || ContainsToken(p.Value))
                        return true;
                return false;
            case JArray arr:
                for (int i = 0; i < arr.Count; i++)
                    if (ContainsToken(arr[i]))
                        return true;
                return false;
            case JValue { Type: JTokenType.String } value:
                return value.Value<string>()?.Contains(TokenStart, StringComparison.OrdinalIgnoreCase)
                       ?? false;
            default:
                return false;
        }
    }

    /// <summary>
    /// 把资产写进产出树；含 <c>{{i18n:...}}</c> 时按语言展开成多个后缀资产
    /// （<c>default</c> → 基础资产，其余语言 → <c>&lt;name&gt;.&lt;长码&gt;.json</c>）。
    /// 任何一处翻译失败都<b>不落任何文件</b>。
    /// <paramref name="tokens"/> 用于展开 i18n 键里的内嵌动态令牌（<paramref name="assetName"/> 即 Target，
    /// 供 <c>{{TargetWithoutPath}}</c> 等字段引用令牌使用）。
    /// </summary>
    public static MergeOutcome WriteLocalized(OutputTree output, string assetName, JToken data,
        PackTranslations? translations, bool isNew, DynamicTokenResolver? tokens = null,
        IReadOnlyCollection<string>? changedKeys = null, bool takeOwnership = false)
    {
        // ★ **值方言统一在落盘前这一处做**。CP 的 content.json 允许把 `Vector2`/`Rectangle` 写成
        //   `"0, -32"`（游戏自己的转换器认这种糖），而 XNB 打包器只认 `{"X":0,"Y":-32}` ——
        //   写出去**直接打不了包**。这里在写盘前就地换掉，于是基础资产与各语言变体拿到的是
        //   同一份已换好的数据（`WriteLocalized` 是 EditData / Load 两条写入路径的唯一漏斗）。
        //   依据与实测代价见 `ValueDialectCoercion`。
        // 与 `PatchApplier` 同一套：高精度计时器（CPU 时间的粒度太粗，见 `Prof` 的说明）
        long _p0 = Diagnostics.Prof.Now();
        void Lap(string k)
        {
            if (!Diagnostics.Prof.On) return;
            Diagnostics.Prof.AddSince(k, _p0);
            _p0 = Diagnostics.Prof.Now();
        }

        string rel = AssetCreator.RelativePath(assetName, ".json");

        // ★ **补丁前的基线**：语言变体要按"本补丁改了哪些顶层键"来覆盖已有本地化资产
        //   （CP 是把补丁应用在**被请求的那个语言资产**上）。
        //   ⚠ 只在**调用方没给 changedKeys** 时读：原地改（`takeOwnership`）会把暂存里的对象
        //   一起改掉，此时"补丁前基线"读出来已经是**补丁后**的内容 —— `PatchApplier` 改为
        //   从补丁结构直接给出改到的顶层键，不依赖快照。
        string? beforeBaseText = changedKeys == null ? output.ReadTextOrNull(rel) : null;

        // ★ DLL 专有的事件前置条件 → 原版 GSQ（只动 `Data/Events*` 的 key；依据见 `EventPreconditions`）。
        //   未知前置条件在原版是「false 且 `!` 不生效」⇒ 带这类 key 的事件永远不触发，比命令跳过严重。
        EventPreconditions.Rewrite(assetName, data);
        Lap("Write.事件前置");

        // ★ DLL 专有的物品查询 → fallback 物品（依据见 `ItemQueryFixes`）。
        //   未知查询会生成**玩家可见的 Error Item**，不是空值。
        ItemQueryFixes.Rewrite(assetName, data);
        Lap("Write.物品查询");

        // ★ 没有原版等价、但有视觉/惩罚近似的命令（`addColouredSlime` / `SVE_BadlandsDeath`）。
        EventCommandFixes.RewriteMismatchedCommands(assetName, data);

        // ★ MistyCore 的 `append <fragmentId>` → 转换期静态内联（依据见 `EventCommandFixes`）。
        //   未知命令在原版只是被跳过，但被跳过的是一整段剧情。
        EventCommandFixes.InlineAppend(assetName, data);

        // ★ MistyCore 的 `if <GSQ>##真##假` → 原版 `fork <mail> <wrapper>`（依据见上面）。
        //   分支里的 `append` 也在这里展开。
        EventCommandFixes.RewriteMailIfs(assetName, data);
        Lap("Write.事件命令");

        // ★ 其它 DLL 专有数据字段（订单板 / 机器 OutputMethod；依据见 `DataFixes`）。
        DataFixes.Normalize(assetName, data);
        Lap("Write.数据修复");

        bool containsToken = ContainsToken(data);
        Lap("Write.检测i18n");
        if (!containsToken)
        {
            List<string> coerceNotes = Validating.ValueDialectCoercion.CoerceAsset(assetName, data);
            Lap("Write.CoerceAsset");
            MergeOutcome baseWrite = WriteBase(output, rel, data, assetName, isNew);
            Lap("Write.基础落盘");
            if (!baseWrite.Success) return baseWrite;
            // ★ 没有 `{{i18n}}` 也**不代表本地化资产不用改**：CP 会把同一条补丁应用到
            //   **游戏实际请求的语言资产**上（中文存档请求 `Data/Events/Farm.zh-CN`）。
            //   只改基础资产 ⇒ 模组加的对话/事件在中文下**根本不出现**。
            //   这里把本补丁的顶层键增量镜像到已存在的 `<asset>.zh-CN`。
            List<string>? mirrorNotes = MirrorDeltaToFrozenLocale(output, assetName, beforeBaseText, data, changedKeys);
            Lap("Write.镜像本地化");
            if (mirrorNotes != null) coerceNotes.AddRange(mirrorNotes);
            // ⚠ 归一里**删过字段**时要报出来（那是取舍：那个字段的意图没了）—— 归零风险地静默做不行。
            return coerceNotes.Count == 0 ? MergeOutcome.Ok(data) : MergeOutcome.Ok(data, coerceNotes);
        }

        // ★ **内容包压根没有 i18n 文件时，不许判失败** —— 与"缺键"同一条规矩（见下）：
        //   CP 走 SMAPI 的 `TranslationHelper.Get(key)`，没有译文就是**占位符**
        //   `(no translation:key)`（SMAPI `Translation.PlaceholderText`，反编译核对过），
        //   它照常把资产写出去。我们先前直接 `Fail` ⇒ 实测新语料里 **229 条**补丁整个没写
        //   （`Sword & Sorcery` 之类的对话/立绘包：内容里有 `{{i18n:…}}`、包里却没有 `i18n/`）。
        //   ⚠ 占位符是 SMAPI 的**原样文本**，别自作主张换成"键名"或空串 —— 游戏里显示的就是它。
        bool hasTranslations = translations != null && translations.HasAny;

        // ★ **每个语言各克隆一份整文档**是这里最贵的一步：实测本语料有一包（聚宝盆制作）
        //   改的是 `Data/Objects`（三万多个节点），9 个语言各 `DeepClone` 一次 ——
        //   单条补丁 400 ms，43 条就是 17 秒。
        //   这里改成「克隆一次、各语言只改那几个含 i18n 的字符串节点」：
        //   节点位置在**克隆体**上收集一次（`data` 本身仍保持原文，缓存还要用），
        //   之后每个语言只把原值重放一遍。产出的文档结构完全一致，
        //   序列化出来的字节与逐语言克隆没有任何区别。
        // 合批的最后一组：批内文档是私有副本，且该资产不会再有 later patch，
        // 所以不必为了「原文缓存」再克隆一份；翻译直接在工作副本上做。
        JToken working = takeOwnership ? data : data.DeepClone();
        Lap("Write.克隆");
        // 归一放在**克隆体**上先做（与原来的次序一致：先归一、再翻译、再收集 i18n 点）；
        // 调用方手里那份 `data` 等翻译全部成功之后再补上 —— 失败时它一个字节都没动。
        // 说明（"删掉了装不进去的字段"）在**下面那次**统一收：同一份资产、同一批删除，结果一样。
        _ = Validating.ValueDialectCoercion.CoerceAsset(assetName, working);
        Lap("Write.CoerceAsset");
        List<(JValue Node, string Original)> spots = CollectI18nSpots(working);
        // ★ **键名也含 i18n**（`Data/CraftingRecipes` 的配方名）：值的那套 spot 机制只管 JValue，
        //   键名要单独收集、按语言逐个改，否则 `{{i18n:…}}` 原样成为配方键。
        List<(JProperty Prop, string Original)> keySpots = CollectKeySpots(working);
        string[] keyOriginals = keySpots.Select(s => s.Original).ToArray();
        Lap("Write.收集i18n点");

        // 有损确定化会一路带到产物的每一份语言变体上，收集起来报给调用方。
        var notes = new List<string>();

        // 各语言就地改一遍、把**译文字符串**抄下来：文档本身共用同一份工作副本，
        // 序列化推迟到落盘（见下）。
        // ⚠ `notes` 的**顺序**是产出的一部分（报告里那几条有损确定化的次序）：先各语言变体、
        //   后基础资产 —— 与改之前逐条一致，别顺手调换。
        var variants = new List<(string Locale, string[] Values, string[] KeyNames)>();
        Diagnostics.Prof.Add("i18n资产[" + rel + "]", 0);
        foreach (string locale in hasTranslations ? translations!.VariantLocales : Array.Empty<string>())
        {
            TranslationOutcome outcome = TranslateInPlace(spots, translations, locale, tokens, assetName);
            if (!outcome.Success) return MergeOutcome.Fail($"i18n 解析失败（{locale}）：{outcome.FailureReason}");
            notes.AddRange(outcome.Notes);

            // 键名也要逐语言算好（失败在落盘前就返回，保持"要么全写、要么不写"）。
            TranslationOutcome keyOut = TranslateKeysInPlace(keySpots, translations, locale, tokens,
                assetName, out string[] keyNames);
            if (!keyOut.Success)
                return MergeOutcome.Fail($"i18n 键名解析失败（{locale}）：{keyOut.FailureReason}");
            notes.AddRange(keyOut.Notes);
            ApplyKeyNames(keySpots, keyOriginals);   // 还原，给下一个语言用

            variants.Add((locale, SnapshotValues(spots), keyNames));
            Lap("Write.变体纯翻译");
        }

        Lap("Write.各语言变体翻译");

        // ★ **基础资产放到最后**：它不再序列化成字符串，而是把 token 直接交给 `StageJson`
        //   —— 序列化推迟到落盘。同一份资产被几十条补丁反复改时，只有最后那次才真的序列化。
        //   （放在最前也行，但那样每一条补丁都要序列化一遍整文档。）
        //   改回 default 之后再交出去：交出去以后就没有人会再动 `working` 了。
        TranslationOutcome baseOut2 = TranslateInPlace(spots, translations,
            PackTranslations.DefaultLocale, tokens, assetName);
        if (!baseOut2.Success) return MergeOutcome.Fail($"i18n 解析失败：{baseOut2.FailureReason}");
        notes.AddRange(baseOut2.Notes);

        // 基础语言的键名（也留在 `working` 上，基础资产直接用它序列化）。
        TranslationOutcome baseKeyOut = TranslateKeysInPlace(keySpots, translations,
            PackTranslations.DefaultLocale, tokens, assetName, out string[] keyDefaults);
        if (!baseKeyOut.Success) return MergeOutcome.Fail($"i18n 键名解析失败：{baseKeyOut.FailureReason}");
        notes.AddRange(baseKeyOut.Notes);

        // 基础语言的那组值：语言变体的生成函数跑完要**还原**成它
        //（共用同一份文档，不还原的话基础资产会被最后跑的那个语言覆盖）。
        string[] defaults = SnapshotValues(spots);

        // 到这儿为止**不会再有失败**了：补上调用方那份的归一（i18n 令牌不是结构标量，
        // 这一步对它是空操作，与原来的次序等价）。放在这里是为了「要么原样失败、要么改完成功」。
        if (!ReferenceEquals(working, data))
            notes.AddRange(Validating.ValueDialectCoercion.CoerceAsset(assetName, data));
        Lap("Write.基础翻译");

        // 全部翻译成功后才落盘，避免留下半套资产
        MergeOutcome baseWritten = WriteBase(output, rel, working, assetName, isNew);
        Lap("Write.基础落盘");
        if (!baseWritten.Success) return baseWritten;

        // ★ **语言变体不能拿英文基础资产整份覆盖已有的本地化资产** ——
        //   实测（2026-10-19，业主报"界面全是英文"）：Cape Stardew / Crystal Island 等包
        //   只往 `Strings/StringsFromCSFiles` 里加**一个** `{{i18n}}` 条目，却让整份变体
        //   从英文基础资产重建 ⇒ 原版 `StringsFromCSFiles.zh-CN` 的 1650 条中文被换成英文
        //   （只剩 15 条是中文）。CP 的做法是**在请求的那个语言资产上应用补丁**；
        //   等价到静态产出：已有本地化文件时，只覆盖**本补丁改到的顶层键**，其余原样保留。
        // 键名被翻译过后，补丁里的原始键（`{{i18n:…}}`）在 `working` 里已经不存在了 ——
        // 先把原始键映射到**基础语言**的最终键名，再走原来的"找顶层键"逻辑。
        IReadOnlyCollection<string>? effectiveChangedKeys = changedKeys;
        if (changedKeys != null && keySpots.Count > 0)
        {
            var rawToBase = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < keySpots.Count; i++) rawToBase[keyOriginals[i]] = keyDefaults[i];
            effectiveChangedKeys = changedKeys
                .Select(k => rawToBase.TryGetValue(k, out string? mapped) ? mapped : k)
                .ToList();
        }
        HashSet<string>? overlayKeys = effectiveChangedKeys != null
            ? ResolveChangedKeys(working, effectiveChangedKeys)
            : TopLevelDelta(beforeBaseText, working);
        Lap("Write.计算覆盖键");
        if (overlayKeys is { Count: > 0 })
            notes.Add("语言变体在**已有的本地化资产**上只覆盖本补丁改到的顶层键"
                      + "（避免用英文基础资产盖掉原版译文）");

        string? baseConfig = output.ReadTextOrNull(AssetCreator.RelativePath(assetName, ".config"));
        int skippedIdenticalVariants = 0;
        foreach ((string locale, string[] values, string[] keyNames) in variants)
        {
            string variantName = assetName + "." + locale;
            string variantRel = AssetCreator.RelativePath(variantName, ".json");
            string variantConfigRel = AssetCreator.RelativePath(variantName, ".config");
            bool variantIsNew = !output.Exists(variantRel);

            // 已有本地化资产（原版/前面包写的）⇒ 读出它作为底稿；读不出/形状不符再退回整份重建。
            JObject? existingLocalized = null;
            if (!variantIsNew && overlayKeys != null)
            {
                // 直接拿内存对象：上一包刚 stage 的变体还没序列化，走 ReadTextOrNull
                // 会把整份大文档先 ToString 再 Parse 一遍（聚宝盆 制作实测 38 s）。
                if (output.ReadJsonOrNull(variantRel) is JObject obj) existingLocalized = obj;
            }

            // ★ 这个语言的译文与本补丁的基础值**逐字相同**（常见的“包里有语言文件但该键没翻”
            //   或“资产里根本没有可翻译点”）⇒ 不写变体，让游戏按默认语言回退。
            //   实测 Lavender：`Data/Objects.fr-FR` 730 KB、`Locations.zh-CN` 562 KB 与基础完全一致。
            if (variantIsNew
                && values.Length == defaults.Length && values.AsSpan().SequenceEqual(defaults)
                && keyNames.Length == keyDefaults.Length && keyNames.AsSpan().SequenceEqual(keyDefaults))
            {
                skippedIdenticalVariants++;
                continue;
            }

            // ★ **变体也攒到最后再序列化**：整文档 `ToString` 是这里最贵的一步
            //   （实测 `聚宝盆 制作` 一包 720 次里 89% 白做 —— 同一个资产被 39 条补丁反复改，
            //   只有最后一次落盘）。生成函数自己负责还原成基础语言的值。
            // 基础语言键名 → 本语言键名（只对含 i18n 的键有映射，其余同名）。
            Dictionary<string, string>? keyLocaleName = null;
            if (keySpots.Count > 0)
            {
                keyLocaleName = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < keySpots.Count; i++) keyLocaleName[keyDefaults[i]] = keyNames[i];
            }

            Func<JToken> produce = () =>
            {
                for (int i = 0; i < spots.Count; i++) spots[i].Node.Value = values[i];
                JToken result;
                if (existingLocalized != null && overlayKeys != null && working is JObject merged)
                {
                    // ⚠ 这里 `working` 的键名还是**基础语言**的（改名在下面那条分支做）——
                    //   按基础名取属性，写进已有本地化资产时换成该语言的键名。
                    // `existingLocalized` 就是本变体上一版的内存文档（磁盘读来的或上一包
                    // 留下的 pending），这一包不会再有人读旧版 —— 直接原地叠，别整份克隆；
                    // 否则 9 个语言 × 每条补丁都要克隆一次几万节点的大文档。
                    JObject doc = existingLocalized;
                    foreach (string key in overlayKeys)
                    {
                        string localizedKey = keyLocaleName != null
                                              && keyLocaleName.TryGetValue(key, out string? mapped) ? mapped : key;
                        JProperty? prop = merged.Property(key);
                        if (prop == null) doc.Remove(localizedKey);
                        else doc[localizedKey] = prop.Value.DeepClone();
                    }
                    result = doc;
                }
                else
                {
                    ApplyKeyNames(keySpots, keyNames);
                    // 序列化要等到 Flush；这里必须把「本语言的键名 + 本语言的译文」固定下来，
                    // 否则下面还原默认值后，落盘写出去的会是默认语言。
                    result = working.DeepClone();
                    ApplyKeyNames(keySpots, keyDefaults);
                }
                for (int i = 0; i < spots.Count; i++) spots[i].Node.Value = defaults[i];
                return result;
            };

            if (!output.TryStageLazyJson(variantRel, produce, out string? variantError))
                return MergeOutcome.Fail($"写入语言变体 `{variantName}` 失败：{variantError}");
            // 覆盖树里已有的本地化文件时不算新建（它的 ContentHashes key 本就在），只记真正新增的
            if (variantIsNew) output.CreatedFiles.Add(variantRel);

            // 语言变体也要能回包成 xnb：配套 .config 从基础资产复制（与 ContentHashes 的 key 缺一不可）
            if (baseConfig != null)
            {
                bool configIsNew = !output.Exists(variantConfigRel);
                if (!output.TryWriteText(variantConfigRel, baseConfig, out string? configError))
                    return MergeOutcome.Fail($"写入语言变体 `{variantName}` 的 .config 失败：{configError}");
                if (configIsNew) output.CreatedFiles.Add(variantConfigRel);
            }
        }

        Lap("Write.变体落盘");
        if (skippedIdenticalVariants > 0)
            notes.Add($"语言变体与基础资产逐字相同：省略 {skippedIdenticalVariants} 个"
                      + "（游戏按默认语言回退，不再写重复资源）");

        // ★ 本包**没有中文 i18n** 时，语言变体循环不会产出 `.zh-CN`；但这条补丁在 CP 下
        //   仍会作用到中文存档请求的 `asset.zh-CN`（值就是默认语言的文本/占位符）。
        //   不镜像的话，模组新增的键在中文下**整条不存在**（实测 `Dwarf_EngagedRoommate`）。
        bool frozenProduced = variants.Any(v =>
            v.Locale.Equals(LocaleVariants.FrozenSuffix.TrimStart('.'), StringComparison.OrdinalIgnoreCase));
        if (!frozenProduced)
        {
            // ⚠ 键名含 i18n 时，`changedKeys` 里是**原始**键（`{{i18n:…}}`），而 `working`
            //   上的键已翻成基础语言名 —— 必须用映射后的 `effectiveChangedKeys`，否则镜像
            //   找不到属性、会把新增键当成"本补丁删掉了它"而从 zh-CN 里移除。
            List<string>? mirrorNotes = MirrorDeltaToFrozenLocale(
                output, assetName, beforeBaseText, working, effectiveChangedKeys);
            Lap("Write.镜像本地化");
            if (mirrorNotes != null) notes.AddRange(mirrorNotes);
        }

        return notes.Count == 0
            ? MergeOutcome.Ok(data)
            : MergeOutcome.Ok(data, notes.Distinct(StringComparer.Ordinal).ToList());
    }

    private static MergeOutcome WriteBase(OutputTree output, string rel, JToken data, string assetName, bool isNew)
    {
        // 坏路径（非法字符 / 保留设备名 / 超长）在这里降级为该条补丁写入失败，不抛异常、不终止整次运行。
        // 走 `StageJson`：**落盘时才序列化**。同一份大资产被几十条补丁反复改时，
        // 每条都 `ToString` 一遍是纯浪费（实测 EditData 的主要开销之一）。
        if (!output.TryStageJson(rel, data, out string? error))
            return MergeOutcome.Fail($"写入目标资产 `{assetName}` 失败：{error}");

        if (!isNew) return MergeOutcome.Ok();

        output.CreatedFiles.Add(rel);

        // 变体的 .config 要复制，所以基础 .config 必须先就位
        AssetCreator.CopyConfig(output, assetName, data);
        return MergeOutcome.Ok();
    }

    /// <summary>
    /// 把"补丁里写的键"解析成**合并后文档里的最终顶层键**。
    /// <para>
    /// 例如事件键 `<c>146351222</c>` 经条件落点改写后变成
    /// `<c>146351222/G PLAYER_HAS_SEEN_EVENT …</c>` —— 只按原始键找会**找不到**，
    /// 进而把这条从语言变体里删掉（实测就是中文事件表少了这条）。
    /// 前缀匹配（原始键 + <c>/</c>）把改写后的键也收进来。
    /// </para>
    /// </summary>
    private static HashSet<string>? ResolveChangedKeys(JToken after, IReadOnlyCollection<string>? raw)
    {
        if (raw == null) return null;
        if (after is not JObject obj) return null;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in raw)
        {
            if (obj.Property(key) != null) keys.Add(key);
            foreach (JProperty prop in obj.Properties())
                if (prop.Name.StartsWith(key + "/", StringComparison.Ordinal))
                    keys.Add(prop.Name);
        }
        return keys;
    }

    /// <summary>
    /// **本补丁改了哪些顶层键**（新增/值变化/删除）。根不是对象或基线读不出时返回 <c>null</c>
    /// （调用方退回"整份重建"/放弃镜像）。
    /// <para>
    /// 为什么按顶层键：CP 的 `EditData` 是按条目粒度改的；把补丁应用到语言资产时，
    /// 只有被点名的条目会变，其余条目必须保留原语言资产里的内容（否则整份被英文覆盖）。
    /// </para>
    /// </summary>
    private static HashSet<string>? TopLevelDelta(string? beforeText, JToken after)
    {
        if (beforeText == null || after is not JObject afterObj) return null;

        JToken before;
        try { before = JToken.Parse(beforeText); }
        catch (JsonException) { return null; }
        if (before is not JObject beforeObj) return null;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (JProperty prop in afterObj.Properties())
        {
            JProperty? old = beforeObj.Property(prop.Name);
            if (old == null || !JToken.DeepEquals(old.Value, prop.Value))
                keys.Add(prop.Name);
        }
        foreach (JProperty prop in beforeObj.Properties())
            if (afterObj.Property(prop.Name) == null) keys.Add(prop.Name);
        return keys;
    }

    /// <summary>
    /// 把本补丁的顶层键增量应用到已存在的 <c>&lt;asset&gt;.zh-CN</c>（没有就什么都不做）。
    /// 返回说明（只有失败/异常时才有，避免每条补丁都刷一条报告）。
    /// </summary>
    private static List<string>? MirrorDeltaToFrozenLocale(
        OutputTree output, string assetName, string? beforeText, JToken after,
        IReadOnlyCollection<string>? changedKeys)
    {
        if (after is not JObject src) return null;
        HashSet<string>? keys = changedKeys != null
            ? ResolveChangedKeys(after, changedKeys)
            : beforeText != null ? TopLevelDelta(beforeText, after) : null;
        if (keys is not { Count: > 0 }) return null;

        string variantName = assetName + LocaleVariants.FrozenSuffix;
        string rel = AssetCreator.RelativePath(variantName, ".json");
        // 直接拿内存对象（上一包镜像留下的 pending 也在里面）；不要 ToString→Parse 往返。
        JToken? existing = output.ReadJsonOrNull(rel);
        if (existing == null)
        {
            // 区分「文件不存在」与「不是合法 JSON」：后者要保持一条报告说明。
            string? raw = output.ReadTextOrNull(rel);
            return raw == null
                ? null
                : new List<string> { $"`{variantName}` 不是 JSON 对象，未镜像本补丁的改动" };
        }
        if (existing is not JObject doc)
            return new List<string> { $"`{variantName}` 不是 JSON 对象，未镜像本补丁的改动" };

        // 这份变体这一包不会再被读取旧版 —— 原地叠，避免每条补丁克隆 + 序列化整份大文档。
        foreach (string key in keys)
        {
            JProperty? prop = src.Property(key);
            if (prop == null) doc.Remove(key);
            else doc[key] = prop.Value.DeepClone();
        }

        return output.TryStageLazyJson(rel, () => doc, out string? error)
            ? null
            : new List<string> { $"镜像本补丁到 `{variantName}` 失败：{error}" };
    }

    /// <summary>把这批节点当前的值抄成一份数组（一趟翻译改过之后立刻抄，别等下一趟把它盖掉）。</summary>
    private static string[] SnapshotValues(List<(JValue Node, string Original)> spots)
    {
        var values = new string[spots.Count];
        for (int i = 0; i < spots.Count; i++) values[i] = spots[i].Node.Value<string>() ?? "";
        return values;
    }

    /// <summary>
    /// 收集文档里所有含 <c>{{i18n:...}}</c> 的字符串节点及其**原值**。
    /// 与 <see cref="Walk"/> 遍历同一个形状，只是不改、只记 —— 之后每个语言都按这批节点重放。
    /// </summary>
    private static List<(JValue Node, string Original)> CollectI18nSpots(JToken token)
    {
        var spots = new List<(JValue, string)>();
        void Visit(JToken t)
        {
            switch (t)
            {
                case JObject obj:
                    foreach (JProperty prop in obj.Properties()) Visit(prop.Value);
                    break;
                case JArray arr:
                    for (int i = 0; i < arr.Count; i++) Visit(arr[i]);
                    break;
                case JValue { Type: JTokenType.String } value:
                    string text = value.Value<string>() ?? "";
                    if (text.Contains(TokenStart, StringComparison.OrdinalIgnoreCase))
                        spots.Add((value, text));
                    break;
            }
        }
        Visit(token);
        return spots;
    }

    /// <summary>
    /// 收集**键名**里含 <c>{{i18n:...}}</c> 的属性（CP 的 JSON 键也是令牌串）。
    /// 目前语料只出现在 `Data/CraftingRecipes` 的配方名上，但语义上是通用的。
    /// </summary>
    private static List<(JProperty Prop, string Original)> CollectKeySpots(JToken token)
    {
        var spots = new List<(JProperty, string)>();
        void Visit(JToken t)
        {
            switch (t)
            {
                case JObject obj:
                    foreach (JProperty prop in obj.Properties().ToList())
                    {
                        if (prop.Name.Contains(TokenStart, StringComparison.OrdinalIgnoreCase))
                            spots.Add((prop, prop.Name));
                        Visit(prop.Value);
                    }
                    break;
                case JArray arr:
                    for (int i = 0; i < arr.Count; i++) Visit(arr[i]);
                    break;
            }
        }
        Visit(token);
        return spots;
    }

    /// <summary>
    /// 把键名换成 <paramref name="locale"/> 的译文；返回每个属性**当前的名字**（按 spots 顺序）。
    /// 撞名（两个键翻成同一个名字，或撞上同对象里别的键）一律判失败 —— 静默覆盖会丢数据。
    /// </summary>
    private static TranslationOutcome TranslateKeysInPlace(List<(JProperty Prop, string Original)> spots,
        PackTranslations translations, string locale, DynamicTokenResolver? tokens, string? tokenTarget,
        out string[] names)
    {
        names = new string[spots.Count];
        if (spots.Count == 0) return new TranslationOutcome { Success = true };

        var failures = new List<string>();
        var notes = new List<string>();
        for (int i = 0; i < spots.Count; i++)
            names[i] = Replace(spots[i].Original, translations, locale, failures, tokens, tokenTarget, notes);

        // 每个父对象内查重：新名字不能撞上**不在本次改名范围内**的键，也不能两个都改成同名。
        string[] computed = names;
        foreach (var group in spots.Select((spot, i) => (spot, i)).GroupBy(x => x.spot.Prop.Parent))
        {
            if (group.Key is not JObject parent) continue;
            var inside = group.ToDictionary(x => x.spot.Prop, x => computed[x.i]);
            // 游戏/回包对键大小写不敏感：`Name` 与 `name` 撞车同样会丢一条。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JProperty prop in parent.Properties())
            {
                string final = inside.TryGetValue(prop, out string? renamed) ? renamed : prop.Name;
                if (!seen.Add(final))
                {
                    failures.Add($"i18n 键名翻译后撞车：`{final}`（{locale}）");
                    break;
                }
            }
        }

        if (failures.Count > 0)
            return TranslationOutcome.Fail(failures);

        ApplyKeyNames(spots, names);
        return new TranslationOutcome { Success = true, Notes = notes };
    }

    /// <summary>
    /// 把属性名改成 <paramref name="names"/>；`JProperty.Name` 是只读的，必须整体替换属性。
    /// <para>
    /// ⚠ 必须**保住 value 的对象同一性**：值那批 spot 指着 `prop.Value` 里的 JValue 节点，
    /// 如果 `new JProperty(name, prop.Value)` 让 Newtonsoft 克隆整棵子树，翻译就写进孤儿节点、
    /// 产出仍是原文（Bird 包正是键和值都含 i18n）。做法：先占位替换（保持属性在父对象里的位置），
    /// 再把旧属性的 Value 摘下来交给新属性 —— 此时它没有父节点，不会触发克隆。
    /// </para>
    /// </summary>
    private static void ApplyKeyNames(List<(JProperty Prop, string Original)> spots, string[] names)
    {
        for (int i = 0; i < spots.Count; i++)
        {
            JProperty prop = spots[i].Prop;
            if (prop.Name == names[i]) continue;

            var replacement = new JProperty(names[i]);
            prop.Replace(replacement);        // 位置不变
            JToken oldValue = prop.Value;
            prop.Value = null!;               // 摘下来（Parent 变 null）
            replacement.Value = oldValue;     // 直接挂上去，不克隆
            spots[i] = (replacement, spots[i].Original);
        }
    }

    /// <summary>
    /// 就地把这批节点换成 <paramref name="locale"/> 的译文（每次都从**原值**重放，
    /// 所以同一份文档可以在多个语言之间反复用）。
    /// </summary>
    private static TranslationOutcome TranslateInPlace(List<(JValue Node, string Original)> spots,
        PackTranslations translations, string locale, DynamicTokenResolver? tokens, string? tokenTarget)
    {
        var failures = new List<string>();
        var notes = new List<string>();

        foreach ((JValue node, string original) in spots)
            node.Value = Replace(original, translations, locale, failures, tokens, tokenTarget, notes);

        // 就地翻译没有「结果文档」可交（数据已经在调用方手里那份上改好了），Data 给 null。
        return failures.Count == 0
            ? new TranslationOutcome { Success = true, Data = null, Notes = notes }
            : TranslationOutcome.Fail(failures);
    }

    private static string Replace(string text, PackTranslations translations, string locale,
        List<string> failures, DynamicTokenResolver? tokens, string? tokenTarget, List<string> notes,
        int depth = 0)
    {
        if (depth > 8)
        {
            failures.Add("`{{i18n:...}}` 的具名参数嵌套过深（>8）");
            return text;
        }
        var sb = new StringBuilder(text.Length);
        int i = 0;

        while (true)
        {
            int start = text.IndexOf(TokenStart, i, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            sb.Append(text, i, start - i);
            int contentStart = start + TokenStart.Length;
            // 必须找「配对」的 }}：键里可含内嵌令牌（{{GenderNeutralRSV}}），
            // 它的 }} 不是 i18n 令牌的结尾 —— 用第一个 }} 会截断内嵌令牌。
            int end = TokenScan.FindTokenEnd(text, contentStart);

            if (end < 0)
            {
                failures.Add("`{{i18n:...}}` 未闭合");
                sb.Append(text, start, text.Length - start);
                break;
            }

            string inner = text.Substring(contentStart, end - contentStart);

            // 顶层 `|` 之后是具名参数。
            // ⚠ **切分交给词法器**（`SplitLexically` 切不动令牌内部）：RSV 真实的
            //   `{{i18n:GuestDialogue.{{Random: 1,2,…|key=RSVTouristDialogue}}}}`、
            //   SVE 的 `{{i18n:A |default={{i18n:B |default={{i18n:C}}}}}}`、
            //   `{{HasSeenEvent|contains=1234}}` —— 那个 `|` 都在**令牌内部**。
            //   裸 `Split('|')` 会把它们切成碎片，切出一个叫 `contains` 的**假占位符**，
            //   并据此把整条补丁判失败（实测 SVE Claire 婚姻对话 4 处）。
            var defaultKeys = new List<string>();
            string? defaultValue = null;
            // 具名参数里非 `defaultKeys`/`default` 的那些是 **SMAPI 的 `{{name}}` 占位符**
            //（CP 的 `AllowAnyNamedArguments = true`）。
            var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> segments = CpLexer.Instance
                .SplitLexically(inner, "|", ignoreEmpty: false).ToList();
            if (segments.Count > 1)
            {
                if (!TryReadParams(segments.Skip(1), defaultKeys, out defaultValue, placeholders, out string? paramError))
                {
                    failures.Add(paramError!);
                    sb.Append(text, start, end + TokenEnd.Length - start);
                    i = end + TokenEnd.Length;
                    continue;
                }

                // ★ 参数值本身可能就是 `{{i18n:…}}`（实测 Bear Family：
                //   `{{i18n:KEY{{BearFamGlobalToken}}}}` 展开后参数是
                //   `|LewisOrLouise={{i18n:LewisOrLouise.false}}`）。这些参数会被代入译文里
                //   同名的 `{{LewisOrLouise}}`，不先翻掉就会把**字面 i18n 令牌**写进台词。
                foreach (string name in placeholders.Keys.ToList())
                {
                    string raw = placeholders[name];
                    if (raw.Contains(TokenStart, StringComparison.OrdinalIgnoreCase))
                        placeholders[name] = Replace(raw, translations, locale, failures, tokens,
                            tokenTarget, notes, depth + 1);
                }

                // ★ 具名参数的**值**里不能有代不掉的令牌 —— 它会被代入译文，成为字面花括号。
                //   实测（谢恩 / SVE，语料 13 处）：源文件写
                //   `speak Jas "{{i18n:…event59443124.0 |JasAuncleToken={{JasAuncleToken}}}}"`，
                //   产出的 `Data/Events/Farm.json` 里就是字面的 `早上好，@{{JasAuncleToken}} ！`。
                //   依据：CP 的 `TranslationValueProvider` 把实参先经**令牌解析**（`Stringify(p.Value)`），
                //   我们代不了 ⇒ 写出去就是**与 CP 产物不一致**的字面花括号。与「值里带令牌就整条不写」同一规矩。
                //   ⚠ 只查**参数值**，不查代入后的整串：译文里**没有对应实参**的 `{{unknown}}`
                //   CP 也原样保留（SMAPI `Translation.FormatText` 认不出就留着），那一条我们照抄才对。
                var unresolvedArgs = placeholders
                    .Where(x => TokenScan.HasUnresolved(x.Value))
                    .Select(x => x.Key)
                    .ToList();
                if (unresolvedArgs.Count > 0)
                {
                    failures.Add(
                        $"i18n 具名参数的值里含无法静态解析的令牌：`{string.Join("`、`", unresolvedArgs)}`");
                    sb.Append(text, start, end + TokenEnd.Length - start);
                    i = end + TokenEnd.Length;
                    continue;
                }

                inner = segments[0].Trim();
            }

            // 键里的内嵌令牌先展开：包内 DynamicTokens，再是 Target 系列字段引用令牌。
            // 展开不了的（Random 等）会原样留下，由下面的残留检查判失败 —— 绝不写进产物。
            string key = inner;
            if (tokens != null)
            {
                DynamicTokenResolver.Expansion expansion = tokens.Expand(key);
                key = expansion.Text;
                // 有损确定化：令牌解析成功了，但值是「判不了 → 当不匹配 → 落默认块」猜出来的。
                foreach (string name in expansion.Lossy)
                    notes.Add($"有损确定化：动态令牌 `{name}` 的条件判不了，取了无条件默认块（运行时真值可能不同）");
            }
            if (tokenTarget != null) key = FieldReferenceTokens.Expand(key, fromFile: null, tokenTarget) ?? key;
            key = key.Trim();

            if (key.Contains("{{", StringComparison.Ordinal))
            {
                failures.Add($"键含未支持的内嵌令牌：`{key}`");
                sb.Append(text, start, end + TokenEnd.Length - start);
            }
            else
            {
                // ⚠ **缺键不等于失败** —— CP 的 `TranslationValueProvider.GetValues` 是优雅降级：
                // 先试 `|defaultKeys=`，再试 `|default=`，都没有才给占位符。
                // 我们先前直接判整条补丁失败，代价实测：SVE 一个 `{{i18n:Garden}}` 缺键，
                // 让**装着 57 个地点**的那条补丁整个没写。
                string? value = translations?.GetValue(key, locale);
                if (value == null)
                {
                    foreach (string alt in defaultKeys)
                    {
                        value = translations?.GetValue(alt, locale);
                        if (value != null) break;
                    }
                }
                string resolved = value ?? defaultValue ?? $"(no translation:{key})";
                resolved = ApplyPlaceholders(resolved, placeholders);
                // 译文**自身**也可能含 `{{i18n:…}}`（实测 Bear Family 的
                // `"LewisCTGunnartheLost": "…{{LewisOrLouise}}…"`、`{{i18n:BearFam.Profanity.1.…}}`）。
                // CP 的 TranslationValueProvider 返回的是 token string，嵌套令牌会继续展开；
                // 不展开就会把字面 `{{i18n:…}}` 显示在游戏里。
                if (resolved.Contains(TokenStart, StringComparison.OrdinalIgnoreCase))
                    resolved = Replace(resolved, translations, locale, failures, tokens, tokenTarget, notes,
                        depth + 1);
                sb.Append(resolved);
            }

            i = end + TokenEnd.Length;
        }

        return sb.ToString();
    }



    /// <summary>
    /// 把 SMAPI 的 <c>{{name}}</c> 占位符替换成实参值。
    /// <para>
    /// 依据 SMAPI <c>Translation.FormatText</c>：正则 <c>{{([ \w\.\-]+)}}</c>、**大小写不敏感**、
    /// <b>认不出的占位符原样保留</b>（不是清空、更不是报错）。
    /// </para>
    /// </summary>
    private static string ApplyPlaceholders(string text, Dictionary<string, string> placeholders)
    {
        if (placeholders.Count == 0 || !text.Contains("{{", StringComparison.Ordinal)) return text;

        return PlaceholderPattern.Replace(text, match =>
        {
            string name = match.Groups[1].Value.Trim();
            return placeholders.TryGetValue(name, out string? value) ? value : match.Value;
        });
    }

    /// <summary>SMAPI 的占位符正则（<c>Translation.FormatText</c>）。</summary>
    private static readonly System.Text.RegularExpressions.Regex PlaceholderPattern =
        new(@"\{\{([ \w\.\-]+)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 读 i18n 的具名参数。每一段形如 <c>defaultKeys=a,b</c> / <c>name=张三</c>，
    /// 按**第一个** <c>=</c> 切名/值。
    /// <para>
    /// ⚠ 段是**调用方**用 <see cref="CpLexer.SplitLexically"/> 切好的 ——
    /// 这里**不得**自己 <c>Split('|')</c>：令牌输入里的 <c>|</c> 不是参数分隔符
    /// （见 <c>CpLexer.SplitLexically</c> 的说明）。</para>
    /// <list type="bullet">
    /// <item><c>defaultKeys=a,b</c> —— 取不到时的候选键（CP <c>TranslationValueProvider</c>）</item>
    /// <item><c>default=x</c> —— 取不到时的兜底文案</item>
    /// <item><b>其余一律是 SMAPI 的 <c>{{name}}</c> 占位符</b>（CP 的 <c>AllowAnyNamedArguments = true</c>），
    /// 收集起来由 <see cref="ApplyPlaceholders"/> 在译文中替换 —— **不再判失败**。</item>
    /// </list>
    /// </summary>
    private static bool TryReadParams(IEnumerable<string> segments, List<string> defaultKeys,
        out string? defaultValue, Dictionary<string, string> placeholders, out string? error)
    {
        defaultValue = null;
        error = null;

        foreach (string segment in segments)
        {
            int eq = segment.IndexOf('=');
            if (eq < 0)
            {
                error = $"i18n 的具名参数 `{segment.Trim()}` 不是 `名=值` 形式";
                return false;
            }

            string name = segment[..eq].Trim();
            string value = segment[(eq + 1)..].Trim();

            if (name.Equals("defaultKeys", StringComparison.OrdinalIgnoreCase))
            {
                defaultKeys.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                defaultValue = value;
            }
            else
            {
                // SMAPI 的 `{{name}}` 占位符（CP 允许任意具名参数）。
                placeholders[name] = value;
            }
        }
        return true;
    }
}

