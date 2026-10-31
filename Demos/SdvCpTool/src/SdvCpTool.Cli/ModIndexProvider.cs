using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Cli;

/// <summary>
/// 已装模组索引的**一进程一份**取用口。
/// <para>
/// 递归扫 Mods 树实测约 <b>0.6 s/次</b>，而一次 <c>convert</c> 原来要建 **2~4 次**
/// （主流程、<c>BuildTokenResolver</c>、Include 求值、烘焙各建一次）—— 同一份输入没必要扫第二遍
/// （<see cref="InstalledModIndex"/> 只读、无副作用）。232 包链式跑批 ⇒ 省约 1~2 分钟。
/// </para>
/// <para>
/// ⚠ 键是**目录**：同目录复用、换目录重建 ⇒ 就算将来一进程跑多个包也不会串。</para>
/// </summary>
internal static class ModIndexProvider
{
    private static readonly Dictionary<string, InstalledModIndex> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static InstalledModIndex Get(string modsDirectory, string? cachePath)
    {
        if (!Cache.TryGetValue(modsDirectory, out InstalledModIndex? hit))
            Cache[modsDirectory] = hit = new InstalledModIndex(modsDirectory, cachePath);
        return hit;
    }
}
