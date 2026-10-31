namespace SdvCpTool.Core.Normalizing;

/// <summary>
/// 变体按什么<b>维度</b>分叉 —— 即「同一份内容因什么而不同」。
/// <para>
/// 这是编译器 IR 的一个轴。维度从补丁的 <c>When</c> 令牌名判定（见 <see cref="VariantNormalizer"/> 的词表），
/// <b>不从 Target 猜</b>。
/// </para>
/// </summary>
public enum VariantDimension
{
    Season,
    Marriage,
    Year,
    SeenEvent,
    Hearts,
}

/// <summary>
/// 补丁实现变体选择所用的<b>载体</b> —— 即「变体被写在哪一层」。
/// <para>
/// 载体决定查表键的另一个轴，也解释了同一个意图为什么会有 <b>5–7 种写法</b>（数据熵的度量对象）。
/// </para>
/// </summary>
public enum VariantCarrier
{
    /// <summary>图集里的格子：变体是同张图上的不同矩形（<c>EditImage</c>）。</summary>
    Sprite,

    /// <summary>资产名本身：为每个变体各 <c>Load</c> 一个新资产，靠资产名区分。</summary>
    AssetName,

    /// <summary>
    /// 数据条目：变体写在<b>条目这一层</b> —— 可能是键名，也可能是值/字段。
    /// <para>
    /// <b>为什么键名与值不再分成两档</b>：同一个资产两种都用得上 ——
    /// <c>Data/Festivals</c> 既用键名（<c>&lt;NPC&gt;_spouse</c>）又用值（<c>$query</c>）；
    /// <c>Strings/schedules</c> 的值是 <c>$query</c> 宿主而键不是。
    /// <b>一个载体装不下两种机制</b>，而「用哪种」本来就由<b>资产类型 × 维度</b>决定 ——
    /// 硬拆成两档只会让同一条补丁落进不同的桶（实测：224 条因此被判「未定」）。
    /// </para>
    /// </summary>
    Entry,

    /// <summary>地图：变体是地图里的不同 tile / 图层。</summary>
    Map,

    /// <summary>载体认不出来 —— <b>不计入任何一档，如实报未知</b>。</summary>
    Unknown,
}

/// <summary>
/// IR：一条<b>变体意图</b>。
/// <para>
/// 「同一 <see cref="VariantCarrier"/> 上、同一 <see cref="Base"/>、按同一 <see cref="Dimension"/> 分叉的多条补丁」
/// 归一化成<b>一个</b>节点。归一化器要做的就是把它们发射成同一个原生形式。
/// </para>
/// </summary>
public sealed class VariantIntent
{
    /// <summary>被作用的对象：资产名（含条目路径）。</summary>
    public string Base = "";

    public VariantDimension Dimension;

    public VariantCarrier Carrier;

    /// <summary>该维度出现过的取值（季节名 / 配偶名 / 年份 / 事件 ID / …），去重且保序。</summary>
    public List<string> Values = new();

    /// <summary>参与本意图的补丁数 —— 也就是「本节点原本的实现数」。</summary>
    public int PatchCount;

    /// <summary>值集是否覆盖该维度的<b>全体</b>取值。只对封闭维度（季节）可判定；开放维度恒为 <c>null</c>。</summary>
    public bool? CoversWholeDimension;

    /// <summary>
    /// 桶内补丁的条件里，<b>除本维度外</b>还出现过的条件令牌名（基名，去重）。
    /// <para>
    /// <b>这是「近似」的判据。</b>实测：SVE 的季节灌木 208 条**全部**还带 <c>IsOutdoors</c>，
    /// 而原版的季节机制不区分室内外 —— 归一到最简形式就会<b>丢掉这一维</b>。
    /// 非空即表示<b>收敛后有语义损失，只能标「近似」</b>。
    /// </para>
    /// </summary>
    public HashSet<string> OtherConditions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>收敛会丢语义（条件里带了本维度之外的东西）。<b>不得把这种算成满分转换。</b></summary>
    public bool IsApproximate => OtherConditions.Count > 0;

    /// <summary>
    /// ★ <b>本意图真的被发射了</b>：由哪个机制压的。空串 = 没发射。
    /// <para>
    /// <b>为什么必须与发射表的推断分开记</b>：<see cref="NormalizationReport.CollapsedPatches"/>
    /// 是<b>按发射表推的</b> —— 它回答「这一格原版有没有机制」，不回答「本次真的压了没有」。
    /// 实测：`(Sprite, Season, Mods/*)` 这一格表上判 Native，但 52 个组是因为
    /// 「消费方恰好唯一 + SpriteIndex=0」等前提才发射得出去，另有 60 组被拒。
    /// <b>把推的当成的报，就是虚报。</b>
    /// </para>
    /// </summary>
    public string EmittedAs = "";

    public override string ToString() =>
        $"{Carrier}/{Dimension} @ {Base} ×{PatchCount}";
}
