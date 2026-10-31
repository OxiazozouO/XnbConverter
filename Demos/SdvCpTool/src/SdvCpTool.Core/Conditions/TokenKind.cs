namespace SdvCpTool.Core.Conditions;

public enum TokenKind
{
    /// <summary>有 GSQ 等价关键字，可机械翻译。</summary>
    GsqTranslatable,

    /// <summary>能生成 GSQ，但语义与 CP 有偏差，须人工确认。</summary>
    GsqApproximate,

    /// <summary>确认 GSQ 原理上表达不了（需要返回集合或遍历实体）。</summary>
    NoGsqEquivalent,

    /// <summary>依赖已装模组或包内文件 —— 读对应目录即可离线求值（通道 C 烘焙）。</summary>
    Bakeable,

    /// <summary>当前游戏语言 —— 把内容写进对应语言后缀的资产即可表达（通道 B1）。</summary>
    Locale,

    /// <summary>纯计算或取值，字面量参数可离线求值。</summary>
    PureComputation,

    /// <summary>CP 内容包之外的 mod 通过 C# 注册的令牌。</summary>
    CustomModToken,

    /// <summary>不认识的键名。</summary>
    Unknown
}
