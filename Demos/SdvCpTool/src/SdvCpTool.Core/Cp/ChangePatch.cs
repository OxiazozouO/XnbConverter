using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>一条 CP 修改记录。</summary>
public sealed class ChangePatch
{
    public string SourceFile = "";
    public int Index;
    public string Action = "";
    public string? LogName;
    public string? Target;
    public string? FromFile;
    public string? Update;
    public JObject? When;
    public JObject Raw = new();

    /// <summary>
    /// CP 的应用优先级，已解析成整数。<c>Load</c> 用 <c>AssetLoadPriority</c>（缺省 <c>Exclusive</c> = <see cref="int.MaxValue"/>），
    /// 其余 <c>Edit*</c> 用 <c>AssetEditPriority</c>（缺省 <c>Default</c> = 0）。
    /// </summary>
    public int Priority;

    public bool IsLoad => Action.Equals("Load", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => LogName ?? Target ?? $"{Path.GetFileName(SourceFile)}#{Index}";
}
