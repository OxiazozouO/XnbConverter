using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Merging;

/// <summary>一条补丁的合并结果。失败带可读原因，不抛异常。</summary>
public sealed record MergeOutcome
{
    public bool Success { get; init; }
    public JToken? Data { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>
    /// 成功但有保留意见时记在这里（<b>不是失败</b>）。当前唯一来源是
    /// <b>有损确定化</b>——动态令牌的条件判不了，被当作不匹配从而落到默认块，
    /// 值可能不等于运行时真值。<b>报告必须展示它</b>，不得混进「无损」。
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>成功。图片资产只落盘文件、没有 JSON 数据，<paramref name="data"/> 可为 null。</summary>
    public static MergeOutcome Ok(JToken? data = null) => new() { Success = true, Data = data };

    /// <summary>成功、且带保留意见。</summary>
    public static MergeOutcome Ok(JToken? data, IReadOnlyList<string> notes) =>
        new() { Success = true, Data = data, Notes = notes };
    public static MergeOutcome Fail(string reason) => new() { Success = false, FailureReason = reason };
}
