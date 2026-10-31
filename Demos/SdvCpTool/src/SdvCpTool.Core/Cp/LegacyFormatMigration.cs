namespace SdvCpTool.Core.Cp;

/// <summary>
/// 声明 **`Format` 早于 <c>2.0.0</c>** 的内容包，CP 会拿 <c>Migration_2_0</c> 把它们的补丁**迁移到 1.6 的
/// 资产名与数据形状**（`ContentPatcher.Framework.Migrations/Migration_2_0.cs`）。我们**没实现迁移器**
/// ⇒ 这类补丁的判定**不可信**，只能如实说"这儿有件我们没做的事"，而不是照字面写出去。
///
/// <para>
/// <b>CP 的规则</b>（`AggregateMigration.cs:36`）：<c>Migrations.Where(m =&gt; m.Version.IsNewerThan(packFormat))</c>
/// —— 比包的 `Format` **新**的迁移统统会跑，所以 `Format: 1.29.0` 的包会吃下 `1.30` + **`2.0`** + `2.1`…`2.3`。
/// </para>
///
/// <para>
/// <b>迁移器的两张表</b>（`Migration_2_0.cs`，逐条对了源码）：
/// <list type="table">
/// <item><b>改了名字</b>（`RedirectTarget` 返回新名）：`Data/BigCraftablesInformation`→`Data/BigCraftables`、
///   `Data/Blueprints`→`Data/Buildings`、`Data/NPCDispositions`→`Data/Characters`、
///   `Data/ObjectContextTags`→`Data/Objects`、`Data/ObjectInformation`→`Data/Objects`；</item>
/// <item><b>名字不变、形状变</b>（`RedirectTarget` 返回 <c>null</c>，但条目要按 1.6 的模型重写）：
///   `Data/Boots` / `Data/Crops` / `Data/Furniture` / `Data/Locations` / `Data/Weapons`。</item>
/// </list>
/// 后者那些**名字在 1.6 里还在**，所以**不能**一律拦 —— 现代格式的包写它们是对的；
/// 只有"包声明是旧格式 + 目标是这些名"才说明作者写的是**旧形状**。
/// </para>
///
/// <para>
/// <b>为什么只报不修</b>：迁移器把「斜杠分隔的字符串」重写成 1.6 的对象模型
/// （`MergeIntoNewFormat`），逐字段规则上千行；**本语料里 0 条命中**
/// （4 个旧格式包 03/16/19/26 的目标全是对话/日程/地图/自定义资产，没有一条碰这些名）。
/// 实现一整套迁移器换不来任何东西，但**必须让这类补丁在报告里看得见** —— 否则就是"静默写错"。
/// </para>
/// </summary>
public static class LegacyFormatMigration
{
    /// <summary>迁移器管的资产名 → 1.6 里的名字（<c>null</c> = 名字没变，只有形状变）。</summary>
    private static readonly (string Name, string? NewName)[] MigratedAssets =
    {
        ("Data/BigCraftablesInformation", "Data/BigCraftables"),
        ("Data/Blueprints", "Data/Buildings"),
        ("Data/NPCDispositions", "Data/Characters"),
        ("Data/ObjectContextTags", "Data/Objects"),
        ("Data/ObjectInformation", "Data/Objects"),
        ("Data/Boots", null),
        ("Data/Crops", null),
        ("Data/Furniture", null),
        ("Data/Locations", null),
        ("Data/Weapons", null),
    };

    /// <summary>`Migration_2_0` 的版本 —— 包的 `Format` 早于它才会挨上这套迁移。</summary>
    private static readonly Version Migration2_0 = new(2, 0);

    /// <summary>
    /// **令牌只在某个版本之后才有**（各 <c>Migration_*.cs</c> 里的 <c>AddedTokens</c>）。
    /// CP 的 `BaseMigration.TryMigrate(ref ILexToken)`（`BaseMigration.cs:32-41`）**直接报错** ——
    /// 也就是说：声明旧 `Format` 的包**用了它那个版本还没有的令牌**，CP 会把**整份 content.json 拒掉**
    /// （`ContentConfig` 迁移失败 ⇒ 该文件不生效）。
    /// <para>
    /// ⚠ 这条**正面验证过**：全语料 `{{ModId}}` 的使用者**全部**声明 `Format ≥ 2.0`，
    /// 而 `ModId` 正是 `Migration_2_0` 的新增令牌（语料里 **0 条**命中"用早了"）。
    /// </para>
    /// </summary>
    private static readonly (string Version, string[] Tokens)[] AddedTokens =
    {
        ("1.4", new[] { "DayEvent", "HasFlag", "HasSeenEvent", "Hearts", "Relationship", "Spouse" }),
        ("1.5", new[] { "FarmCave", "FarmhouseUpgrade", "FarmName", "HasFile", "HasProfession", "PlayerGender", "PlayerName", "PreferredPet", "Year" }),
        ("1.6", new[] { "HasWalletItem", "SkillLevel" }),
        ("1.7", new[] { "HasReadLetter", "HasValue", "IsCommunityCenterComplete", "IsMainPlayer" }),
        ("1.8", new[] { "IsOutdoors", "LocationName", "Target", "TargetWithoutPath" }),
        ("1.10", new[] { "HasDialogueAnswer", "HavingChild", "IsJojaMartComplete", "Pregnant", "Random" }),
        ("1.11", new[] { "Lowercase", "Uppercase" }),
        ("1.13", new[] { "Query" }),
        ("1.14", new[] { "FromFile", "Round" }),
        ("1.15", new[] { "HasConversationTopic" }),
        ("1.17", new[] { "TargetPathOnly" }),
        ("1.18", new[] { "I18n" }),
        ("1.19", new[] { "Time" }),
        ("1.20", new[] { "LocationContext", "LocationUniqueName" }),
        ("1.22", new[] { "FirstValidFile", "HasActiveQuest" }),
        ("1.23", new[] { "ChildGenders", "ChildNames", "Count", "HasCaughtFish" }),
        ("1.24", new[] { "HasCookingRecipe", "HasCraftingRecipe", "LocationOwnerId", "Merge", "Roommate", "PathPart" }),
        ("1.25", new[] { "AbsoluteFilePath", "FormatAssetName", "InternalAssetKey" }),
        ("2.0", new[] { "ModId" }),
        ("2.2", new[] { "FarmMapAsset", "HasVisitedLocation" }),
    };

    /// <summary>
    /// 这份 `content.json`（或它的 Include 文件）里用了**这个包格式还没有的令牌**吗？有就报出来。
    /// <para>
    /// 与 <see cref="NeedsMigration"/> 不同，这条是**包级**的 —— CP 的失败是整个文件的
    /// （<c>TryMigrateMainContent</c> 返回 false ⇒ 该 content.json 被忽略），所以这里也只写警告，
    /// 不去改任何补丁的判定。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> FindForbiddenTokens(Newtonsoft.Json.Linq.JToken? root, Version? packFormat)
    {
        var found = new List<string>();
        if (root == null || packFormat == null) return found;

        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var since = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string version, string[] tokens) in AddedTokens)
        {
            if (!Version.TryParse(version, out Version? v) || packFormat >= v) continue;
            foreach (string t in tokens)
            {
                forbidden.Add(t);
                since[t] = version;
            }
        }
        if (forbidden.Count == 0) return found;

        foreach (string name in TokenNames(root))
            if (forbidden.Contains(name) && since.TryGetValue(name, out string? ver))
                found.Add($"`{{{{{name}}}}}`（CP {ver} 才有，本包声明 `Format {packFormat}`）");

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>把 JSON 里所有 `{{名字}}` 的**名字**抠出来（只认第一层：`{{X|a=b}}` / `{{X:arg}}` 都算 X）。</summary>
    private static IEnumerable<string> TokenNames(Newtonsoft.Json.Linq.JToken node)
    {
        var texts = new List<string>();
        Collect(node, texts, 0);
        foreach (string text in texts)
        {
            int i = 0;
            while ((i = text.IndexOf("{{", i, StringComparison.Ordinal)) >= 0)
            {
                int end = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                string inner = text.Substring(i + 2, end - i - 2);
                int cut = inner.Length;
                foreach (char c in new[] { ':', '|', ' ' })
                {
                    int p = inner.IndexOf(c);
                    if (p >= 0 && p < cut) cut = p;
                }
                string name = inner[..cut].Trim();
                if (name.Length > 0) yield return name;
                i = end + 2;
            }
        }
    }

    private static void Collect(Newtonsoft.Json.Linq.JToken node, List<string> texts, int depth)
    {
        if (depth > 24) return;
        switch (node)
        {
            case Newtonsoft.Json.Linq.JValue { Value: string s }:
                texts.Add(s);
                break;
            case Newtonsoft.Json.Linq.JObject o:
                foreach (Newtonsoft.Json.Linq.JProperty p in o.Properties())
                {
                    texts.Add(p.Name);
                    Collect(p.Value, texts, depth + 1);
                }
                break;
            case Newtonsoft.Json.Linq.JArray a:
                foreach (Newtonsoft.Json.Linq.JToken child in a) Collect(child, texts, depth + 1);
                break;
        }
    }

    /// <summary>
    /// 这条补丁**要不要如实报"CP 会迁移它、我们没做"**？是则给出理由（含迁移目标与依据）。
    /// <para>
    /// <paramref name="packFormat"/> 为 <c>null</c>（<c>content.json</c> 没写 `Format`，或压根不是 CP 包
    /// —— 比如 FTM 包，它们的补丁是我们自己合成的）时**什么都不说**：不知道就不猜。
    /// </para>
    /// </summary>
    public static bool NeedsMigration(string? target, Version? packFormat, out string? reason)
    {
        reason = null;
        if (packFormat == null || packFormat >= Migration2_0) return false;

        string t = (target ?? "").Replace('\\', '/').Trim('/');
        if (t.Length == 0) return false;

        foreach ((string name, string? newName) in MigratedAssets)
        {
            if (!t.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

            reason = newName != null
                ? $"本包声明 `Format {packFormat}`（早于 2.0.0）⇒ CP 会把这条的目标 `{name}` **改名**到 "
                  + $"`{newName}` 并按 1.6 的模型重写条目（`Migration_2_0`）；我们**没实现迁移器**，"
                  + "照字面写会造出一个游戏根本不读的资产 ⇒ 不写"
                : $"本包声明 `Format {packFormat}`（早于 2.0.0）⇒ CP 会把这条 `{name}` 的条目当成**1.6 之前的老形状**"
                  + "重写成 1.6 的模型（`Migration_2_0`）；我们**没实现迁移器**，照字面写形状就是错的 ⇒ 不写";
            return true;
        }
        return false;
    }
}
