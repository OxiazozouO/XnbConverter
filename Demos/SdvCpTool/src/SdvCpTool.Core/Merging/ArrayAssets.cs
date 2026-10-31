using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 目标资产是不是<b>数组型</b>（XNB 反序列化目标是 <c>List&lt;T&gt;</c>，顶层是 JSON 数组）。
/// <para>
/// <b>为什么必须知道</b>：新建一个资产时，顶层产出对象还是数组决定了回包成不成 ——
/// 形状错了是**硬失败**（实测：<c>List&lt;CharacterAppearanceData&gt;</c> 位置写了对象，
/// 打包器直接报 <c>Cannot deserialize the current JSON object … because the type requires a JSON array</c>）。
/// </para>
/// <para>
/// ★ 这里原先是一份**手抄 13 个名字的白名单**，出处同样写着 <c>DataLoader.cs</c> ——
/// 与 <see cref="GameDataSchema"/> 是同一份事实的**两个副本**，会各自漂移。
/// 现在只读生成出来的 <see cref="GameDataSchema.AssetRootKind"/>；
/// 那份手抄名单**降级为测试**（<c>ArrayAssetsTests</c>）继续盯着生成器 ——
/// 它当年正是靠 <c>Data/MakeoverOutfits</c> 抓出了生成器漏掉
/// <c>content.Load&lt;T&gt;("…")</c> 这种调用形态。
/// </para>
/// <para>
/// 名单外的资产一律按对象处理（<c>false</c>）—— <b>不猜</b>。
/// </para>
/// </summary>
public static class ArrayAssets
{
    /// <summary>目标资产是否为数组型（<c>List&lt;T&gt;</c>）。</summary>
    public static bool IsArrayAsset(string assetName)
    {
        string name = (assetName ?? "").Replace('\\', '/').Trim('/');
        return GameDataSchema.AssetRootKind.TryGetValue(name, out FieldKind kind)
               && kind == FieldKind.Array;
    }
}
