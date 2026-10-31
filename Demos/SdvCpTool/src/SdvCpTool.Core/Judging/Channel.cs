namespace SdvCpTool.Core.Judging;

/// <summary>把（条件, 改动）组合落到原生形式的通道。</summary>
public enum Channel
{
    /// <summary>没有可用通道。</summary>
    None,

    /// <summary>GSQ 条件 + 数据资产的条件落点。</summary>
    Gsq,

    /// <summary>语言后缀资产（&lt;name&gt;.&lt;长码&gt;.xnb）。</summary>
    LocaleSuffix,

    /// <summary>季节 tilesheet 命名（spring_ 等）。</summary>
    SeasonalTilesheet,

    /// <summary>昼夜瓦片（DayTiles / NightTiles）。</summary>
    DayNightTiles,

    /// <summary>昼夜地图环境光（AmbientLight / AmbientNightLight）。</summary>
    DayNightAmbient,

    /// <summary>读 Mods/ 与包内文件离线烘焙。</summary>
    Bake,

    /// <summary>
    /// 原生键名：条件维度由资产本来就支持的专用键表达
    /// （如节日对话的 <c>&lt;NPC&gt;_spouse</c> —— 游戏在该 NPC 是玩家配偶时自动读取）。
    /// </summary>
    NativeKey
}
