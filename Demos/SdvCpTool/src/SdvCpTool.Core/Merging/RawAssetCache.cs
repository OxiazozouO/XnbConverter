using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 运行内资产的<b>原始</b>数据（保留 <c>{{i18n:...}}</c> 令牌，未翻译）。
/// <para>
/// 同一资产常被多条补丁（Load 打底 + 多条 EditData）写入。基础 <c>.json</c> 落盘时已按
/// <c>default.json</c> 翻成英文，若后一条补丁直接读回这个英文文件继续合并，语言变体就只能继承英文，
/// 丢掉先前补丁的译文。所以运行期以本缓存的原文为准合并，写盘时再整体翻译成各语言。
/// </para>
/// </summary>
public sealed class RawAssetCache
{
    private readonly Dictionary<string, JToken> _raw = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 取某资产的原文；没有则返回 <c>null</c>。
    /// <para>
    /// ⚠ **返回的就是缓存里那份，只许读**。要改请自己克隆。
    /// 原来这里 <c>DeepClone</c> 一次，而调用方 <c>EditDataMerger.Apply</c> 内部**本来就克隆**
    /// （它要在失败时保持 <c>target</c> 不动）—— 同一条链上两次深拷贝，实测 1 MB 级资产
    /// 光这一步就 3.5 秒 CPU。实测调用方只读它（另一处 <c>PriorString</c> 也是纯读）。
    /// </para>
    /// </summary>
    public JToken? Get(string relativePath) =>
        _raw.TryGetValue(relativePath, out JToken? data) ? data : null;

    /// <summary>
    /// 记下某资产的原文。
    /// <para>
    /// ⚠ **不做深拷贝** —— 调用方把这份数据的**所有权**交过来（约定：调完 <c>Set</c> 就不再改它）。
    /// 实测深拷贝是最贵的一步：SVE 的 <c>EditData</c> 阶段每条补丁两次 <c>DeepClone</c> 共 6.7 秒 CPU，
    /// 而这里那次纯属多余（唯一会继续改它的是 <c>WriteLocalized</c> 里的结构标量归一，
    /// 那步在 <c>Set</c> **之前**就做完了）。
    /// </para>
    /// </summary>
    public void Set(string relativePath, JToken data) => _raw[relativePath] = data;
}
