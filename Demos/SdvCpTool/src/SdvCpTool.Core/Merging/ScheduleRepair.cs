using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 修复日程里悬空的 <c>GOTO &lt;key&gt;</c>。
/// <para>
/// 典型来源：某个季节日程的值含运行期令牌（`{{Random: {{Pool}} …}}`），转换时整条
/// 季节键被省掉，但 `Mon` 等键仍写 `GOTO spring` ⇒ 游戏报
/// <c>GOTO references schedule 'spring' which doesn't exist</c> 并退回，NPC 当天没有日程。
/// 这里只把**确定不存在的目标**改成同文件里存在的 <c>rain</c>（没有就取第一个非 GOTO 键），
/// 不猜作者原本的季节语义。
/// </para>
/// </summary>
public static class ScheduleRepair
{
    public static int Repair(OutputTree output)
    {
        var files = new List<string>();
        string dir = Path.Combine(output.OutputRoot, "Characters", "schedules");
        if (Directory.Exists(dir))
            files.AddRange(Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(output.OutputRoot, p).Replace('\\', '/')));
        foreach (string rel in output.InputFiles)
            if (rel.StartsWith("Characters/schedules/", StringComparison.OrdinalIgnoreCase) &&
                rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !files.Contains(rel, StringComparer.OrdinalIgnoreCase))
                files.Add(rel);

        int changedFiles = 0;
        foreach (string rel in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? text = output.ReadTextOrNull(rel);
            if (text == null) continue;
            JObject schedule;
            try { schedule = JObject.Parse(text); }
            catch { continue; }

            string? fallback = schedule.Property("rain") != null ? "rain"
                : schedule.Properties().Select(p => p.Name)
                    .FirstOrDefault(n => !n.Equals("GOTO", StringComparison.OrdinalIgnoreCase));
            if (fallback == null) continue;

            bool dirty = false;
            foreach (JProperty entry in schedule.Properties())
            {
                if (entry.Value.Type != JTokenType.String) continue;
                string value = entry.Value.Value<string>() ?? "";
                if (!value.StartsWith("GOTO ", StringComparison.OrdinalIgnoreCase)) continue;
                string target = value[5..].Trim();
                if (target.Length == 0 || schedule.Property(target) != null) continue;
                entry.Value = "GOTO " + fallback;
                dirty = true;
            }

            if (dirty)
            {
                output.StageJson(rel, schedule);
                changedFiles++;
            }
        }
        return changedFiles;
    }
}
