using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace GameSmoke;

/// <summary>
/// 装机自检：**全自动**地把「一个玩家会怎么开始玩」跑一遍，把报错写下来。
///
/// <list type="number">
/// <item><b>自己新建存档</b>：走游戏**自带**的跳过开场那条路
///   （<see cref="TitleMenu.createdNewCharacter"/> 反编译：`loadForNewGame()` →
///   `eventsSeen.Add("60367")` 跳开场 → 放到家里床上 → `Game1.NewDay(0f)` 起床 → 进游戏）；
///   ⚠ 这一步本身就覆盖了实机第一类崩溃（建号菜单枚举自定义农场时加载 `Mods/**` 图标）。</item>
/// <item><b>出门</b>：起床后逐张地点 <see cref="Game1.getLocationFromName"/> + 真传送过去
///   —— 地点建不起来（图集缺失 / 数据不对 / 地图结构坏）在这一步必现
///   （实机第二类崩溃：27 个地点因为图集引用带前导点而建不起来）。</item>
/// <item><b>连跨几天</b>：`Game1.NewDay(0f)` 若干次 —— 跨天会走
///   `NPC.dayUpdate` → `RequireLocation("Town")` 那条路，地点缺了就在这里崩
///   （实机第三类崩溃）。</item>
/// <item>最后触发装机探针 `probe_maps`（若装了 <c>AssetProbe</c>）逐张地图再验一遍。</item>
/// </list>
///
/// 用法：装上后**什么都不用做**（进游戏自动跑）；也可以在 SMAPI 控制台敲 <c>smoke</c> 重跑。
/// 结果落在本 mod 目录的 <c>smoke-result.txt</c>（边跑边写，随时可看）。
/// </summary>
public class ModEntry : Mod
{
    /// <summary>要巡的地点。vanilla 的常去地点 + 实机报过建不起来的那些（含各包自定义的）。</summary>
    private static readonly string[] Tour =
    {
        // 原版（含实机报错的那批）
        "Farm", "FarmHouse", "FarmCave", "Backwoods", "BusStop", "Town", "Mountain", "Forest",
        "Beach", "Railroad", "Desert", "Sewer", "Woods", "BugLand", "Mine", "AdventureGuild",
        "IslandSouth", "IslandWest", "IslandNorth", "IslandEast", "Caldera", "Greenhouse", "Cellar",
        // 各包自定义的（2026-10-12 实机日志里报「建不起来」的那些）
        "EastScarp_Village", "EastScarp_IntroMine", "EastScarp_JacobBarn",
        "Custom_CapeKeyMaze", "Custom_CapeKeyMaze3", "Custom_CapeKeyMaze4", "Custom_CrimsonBadlands",
        "Custom_ForestWest", "Custom_GrampletonFields", "Custom_Pantry_Barn",
        "Custom_SBV_AriMarket", "Custom_SBV_DialaCafe", "Custom_SBV_LaniHouse", "Custom_SBV_SunberryHouse",
        "Squeaks.MGC.CP_CCGreenhouse", "Wildflour.SASS_Atelier",
    };

    private enum Phase { WaitTitle, WaitWorld, Tour, WalkOut, Audio, Sleep, Seasons, Maps, WaterWatch, WaterDone, Done }

    /// <summary>
    /// 播几条**我们自己注入的** cue（`Data/AudioChanges` 那条路写进声库/波形库的）。
    /// 波形库是 MS-ADPCM 的，只有真的播一次才会走「加载 → 解码 → 播放」那条路 ——
    /// 报错会落在 SMAPI 日志里（`Could not load …`／音频异常），这也是**在没有耳朵的情况下**
    /// 能对音乐做的最强验证（好不好听得人来听，这一点它替不了）。
    /// </summary>
    private static readonly string[] Cues =
    {
        "EnD.EliTheme", "EnD.DylanTheme", "MGC.Theme", "noraTheme", "risingAbove", "WWWA.ComfyHouse",
    };

    private Phase _phase = Phase.WaitTitle;
    private int _ticks;
    private int _index;
    private int _walkIndex;
    private bool _walkWaiting;
    private bool _walkPrewarm;
    private int _walkStepTicks;
    private int _walkOk;
    private int _walkBad;
    private readonly List<string> _walkBadLines = new();
    private int _warped;
    private int _failed;
    private int _days;
    private int _played;
    private bool _finished;
    private string _result = "";
    private readonly List<string> _lines = new();

    // NPC 踩水追踪（`SDVCP_WATER_WATCH=1`）：跟住一个 NPC 的早晨行程，
    // 一旦脚下是 Water 图块就落报告并结束进程 —— 给"NPC 卡水里"留下可分析的现场。
    private readonly bool _waterWatch =
        Environment.GetEnvironmentVariable("SDVCP_WATER_WATCH") == "1";
    private readonly string _waterTarget =
        Environment.GetEnvironmentVariable("SDVCP_WATER_NPC") ?? "DTZ.DowntownZuzuCP_Kristoff";
    private readonly string _waterLocation =
        Environment.GetEnvironmentVariable("SDVCP_WATER_LOC") ?? "Custom_DTZ_JojaCEO";
    private int _waterStartTime =
        int.TryParse(Environment.GetEnvironmentVariable("SDVCP_WATER_TIME"), out int waterTime)
            ? waterTime
            : 830;
    private NPC? _watched;
    private string _waterReport = "";
    private string _waterTraceFile = "";
    private readonly List<string> _waterTrace = new();
    private int _waterLastWarp;
    private bool _waterHit;
    private Vector2 _lastTracePos = new(-1, -1);
    private readonly bool _waterForceSchedule =
        Environment.GetEnvironmentVariable("SDVCP_WATER_FORCE_SCHEDULE") == "1";
    private int _waterLastTime;
    private bool _waterDayReset;
    private bool _loggedScheduleMethods;
    private readonly bool _waterForcePath =
        Environment.GetEnvironmentVariable("SDVCP_WATER_FORCE_PATH") == "1";
    private readonly bool _waterReloadSchedule =
        Environment.GetEnvironmentVariable("SDVCP_WATER_RELOAD_SCHEDULE") == "1";
    private bool _waterPathForced;
    private bool _waterScheduleReloaded;

    public override void Entry(IModHelper helper)
    {
        _result = Path.Combine(helper.DirectoryPath, "smoke-result.txt");
        _waterReport = Path.Combine(helper.DirectoryPath, "water-report.txt");
        _waterTraceFile = Path.Combine(helper.DirectoryPath, "water-trace.txt");
        Note($"# GameSmoke 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss}（mod 目录 {helper.DirectoryPath}）");
        if (_waterWatch)
            Note($"# 踩水追踪：目标 `{_waterTarget}`，地点 `{_waterLocation}`，起始时间 {_waterStartTime}");
        helper.Events.GameLoop.UpdateTicked += OnTick;
        helper.ConsoleCommands.Add("smoke", "重跑一次自检（新建存档→出门→跨天）", (_, _) => Reset());
        helper.ConsoleCommands.Add("season", "切换季节：season spring|summer|fall|winter",
            (_, args) => SwitchSeason(args.FirstOrDefault()));
    }

    private void Reset()
    {
        _phase = Phase.WaitTitle;
        _ticks = 0;
        _index = 0;
        Note("# 手动重跑");
    }

    /// <summary>边跑边写盘 —— 崩了也能看到已经跑到哪儿。</summary>
    private void Note(string line)
    {
        _lines.Add(line);
        try { File.WriteAllLines(_result, _lines); } catch { /* 写不进去也不该影响自检 */ }
        Monitor.Log(line, LogLevel.Info);
    }

    private bool _skippedDialogue;
    private bool _skippedEvent;

    /// <summary>
    /// 自动跳过挡路的对话/事件。模组在开场、跨天或传送后弹的对话会把
    /// `createdNewCharacter` / 地点巡游卡住 —— 自检不替玩家做选择，直接跳过。
    /// </summary>
    private void DismissBlockingUi()
    {
        try
        {
            if (Game1.dialogueUp || Game1.activeClickableMenu is DialogueBox)
            {
                Game1.exitActiveMenu();
                Game1.dialogueUp = false;
                Game1.currentSpeaker = null;
                if (!_skippedDialogue)
                {
                    _skippedDialogue = true;
                    Note("# 跳过阻塞的对话");
                }
            }

            // 事件在开局与巡图阶段跳；农场出口阶段不跳 —— 那里的事件可能正是"到新地点"的过场，
            // 跳掉会把玩家送回农场，造成假阴性（实测 Tunnel 出口 2–13 全失败）。
            if (_phase is Phase.WaitTitle or Phase.WaitWorld or Phase.Tour
                or Phase.WaterWatch or Phase.WaterDone
                && Game1.eventUp && Game1.CurrentEvent != null)
            {
                Game1.CurrentEvent.skipEvent();
                if (!_skippedEvent)
                {
                    _skippedEvent = true;
                    Note("# 跳过阻塞的事件");
                }
            }
        }
        catch (Exception ex)
        {
            Note($"# 跳过对话/事件失败：{ex.GetType().Name}: {FirstLine(ex.Message)}");
        }
    }

    private void OnTick(object? sender, UpdateTickedEventArgs e)
    {
        _ticks++;
        try
        {
            DismissBlockingUi();
            switch (_phase)
            {
                case Phase.WaitTitle: TryStartNewGame(); break;
                case Phase.WaitWorld:
                    if (Context.IsWorldReady)
                    {
                        if (_waterWatch)
                        {
                            _phase = Phase.WaterWatch;
                            _ticks = 0;
                            BeginWaterWatch();
                        }
                        // 有 `probe-only.txt` 就只跑地图探针（单图迭代用，一分钟一轮）
                        else if (File.Exists(Path.Combine(Helper.DirectoryPath, "probe-only.txt")))
                        {
                            _phase = Phase.Maps;
                            _ticks = 0;
                            BeginMapProbe();
                        }
                        else BeginTour();
                    }
                    break;
                // 每个地点留 300 tick（约 5 秒）：传送后的过场/对话常在这几秒里弹出来，
                // 期间由 DismissBlockingUi 跳掉，再传下一个 —— 原来是每 10 tick 硬传，
                // 对话还没出现就被下一次传送打断（实机表现为巡图中途卡住）。
                case Phase.Tour: if (_ticks % 300 == 0) WarpNext(); break;
                // 传送要走淡入淡出 ⇒ 每 tick 推进，由 WalkOut 自己等 `isWarping/fadeToBlack`
                // 落定后再发下一条（固定等 2 s 偶发不够，实测 1/13 假阴性）。
                case Phase.WalkOut: WalkOut(); break;
                case Phase.Audio: if (_ticks % 120 == 0) PlayNextCue(); break;
                case Phase.Sleep: if (_ticks % 400 == 0) Sleep(); break;
                case Phase.Seasons: if (_ticks % 180 == 0) NextSeason(); break;
                case Phase.Maps: if (_ticks % 4 == 0) ProbeNextMap(); break;
                case Phase.WaterWatch: WaterWatchTick(); break;
                case Phase.WaterDone:
                    if (_ticks >= 30) Environment.Exit(0);
                    break;
                case Phase.Done:
                    // 跨天是异步的（要等淡入淡出走完）⇒ 进 Done 之后再宽限 400 tick 才收尾。
                    if (_ticks >= 400 && !_finished) Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            _failed++;
            Note($"✗ 自检自身抛异常（{_phase}）：{ex.GetType().Name}: {ex.Message}");
            _phase = Phase.Done;
        }
    }

    /// <summary>① 新建存档：走游戏自带的跳过开场路径。</summary>
    private void TryStartNewGame()
    {
        if (Game1.activeClickableMenu is not TitleMenu menu) return;
        if (Game1.gameMode != 0) return;             // 0 = 标题画面

        // 自检会连续播放注入的音乐 cue；测试时强制静音，避免占用用户正在听的歌。
        try
        {
            Game1.options.musicVolumeLevel = 0f;
            Game1.options.ambientVolumeLevel = 0f;
            Game1.options.soundVolumeLevel = 0f;
            Game1.musicCategory?.SetVolume(0f);
            Game1.ambientCategory?.SetVolume(0f);
            Game1.soundCategory?.SetVolume(0f);
        }
        catch { /* 音量设不上不影响自检 */ }

        Game1.whichFarm = 0;                          // Standard 农场（别落到某个 mod 的自定义农场上）
        Game1.whichModFarm = null;
        Note("# 调用 TitleMenu.createdNewCharacter(skipIntro: true) 新建存档");
        menu.createdNewCharacter(true);
        _phase = Phase.WaitWorld;
    }

    private void BeginTour()
    {
        Note($"# 进游戏了：第 {Game1.Date.TotalDays + 1} 天，玩家在 `{Game1.currentLocation?.Name}`");
        // 出口探针放在巡图**之前**：巡图会踩到各 mod 地图的开场事件，事件收尾常把玩家送回
        // Farm 并留下 `eventUp/eventOver` —— 之后再调 `warpFarmer` 会被事件状态吞掉，
        // 实测 13 条出口全判「到不了」。刚进存档时还没有这些事件，先量完再巡图。
        _phase = Phase.WalkOut;
        _ticks = 0;
        _walkIndex = 0;
        _walkWaiting = false;
        _walkPrewarm = true;
    }

    /// <summary>
    /// ②′ **农场出口探针**（2026-10-17，业主报「连农场地图都走不出去，一直都没有传送点」）。
    /// <para>
    /// 离线只能证明"地图里 <b>有</b> `Warp` 且格式合法"（已经验过：装机里那份解回来 15 条、0 畸形，
    /// 游戏源码 `GameLocation.updateWarps`（`:17141`）的解析口径一致）—— 但玩家关心的是**能不能用**。
    /// 这里在游戏里量：把玩家放到每条 `Warp` 的**入口格**上，看**游戏自己**会不会把他传出去
    ///（`Warp` 是踩到就触发的，不需要按键）。
    /// </para>
    /// </summary>
    private void WalkOut()
    {
        GameLocation? farm = null;
        try { farm = Game1.getLocationFromName("Farm"); }
        catch (Exception ex) { Note($"# ✗ 拿农场地点抛异常：{ex.Message}"); }
        if (farm == null)
        {
            Note("# ✗ 农场地点拿不到（`Maps/Farm` 或 Data/Locations 有问题）");
            StartTour();
            return;
        }

        if (_walkPrewarm)
        {
            if (!_walkWaiting)
            {
                if (Game1.isWarping || Game1.fadeToBlack) return;
                Note("# 农场出口探针：先把玩家送到 `Farm`");
                Game1.warpFarmer("Farm", 64, 15, false);
                _walkWaiting = true;
                _walkStepTicks = 0;
                return;
            }
            if (!WalkSettled()) return;

            _walkPrewarm = false;
            _walkWaiting = false;
            Note($"# 农场出口探针起点：`{Game1.currentLocation?.Name ?? "(无)"}`");
            if (Game1.currentLocation?.Name != "Farm")
            {
                Note("# ✗ 到不了 `Farm`，跳过出口探针");
                StartTour();
                return;
            }
        }

        if (_walkIndex >= farm.warps.Count)
        {
            Note($"# **农场出口探针结束**：共 {farm.warps.Count} 条 `Warp`，按目标能传到 **{_walkOk}** 条，传不到 **{_walkBad}** 条");
            foreach (string line in _walkBadLines) Note("   ✗ " + line);
            StartTour();
            return;
        }

        Warp w = farm.warps[_walkIndex];
        string label = $"({w.X},{w.Y})→{w.TargetName} {w.TargetX},{w.TargetY}";

        if (!_walkWaiting)
        {
            // ⚠ **不能**"站上去看会不会触发"：地图 Warp 的触发在 `Farmer.MovePositionImpl`（`:8583`），
            //   只在**往那一格移动**时判（`nextPosition(direction)` + `isCollidingWithWarp`）——
            //   瞬移站上去**不会**触发（实测 15/15 假阴性）。这里改验**目标能不能到**：
            //   调的也是游戏自己的 `Game1.warpFarmer`，失败会留在原地。
            if (Game1.isWarping || Game1.fadeToBlack) return;   // 上一条的淡入淡出还没走完
            try { Game1.warpFarmer(w.TargetName, w.TargetX, w.TargetY, false); }
            catch (Exception ex) { _walkBad++; _walkBadLines.Add($"{label} —— warpFarmer 抛异常：{ex.Message}"); _walkIndex++; return; }
            _walkWaiting = true;
            _walkStepTicks = 0;
            return;
        }

        if (!WalkSettled()) return;

        string now = Game1.currentLocation?.NameOrUniqueName ?? "(无)";
        string nowName = Game1.currentLocation?.Name ?? "(无)";
        bool ok = string.Equals(now, w.TargetName, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(nowName, w.TargetName, StringComparison.OrdinalIgnoreCase);
        if (ok) _walkOk++;
        else
        {
            _walkBad++;
            string state = $"eventUp={Game1.eventUp} eventOver={Game1.eventOver}"
                           + $" isWarping={Game1.isWarping}"
                           + $" menu={(Game1.activeClickableMenu?.GetType().Name ?? "无")}";
            _walkBadLines.Add($"{label} —— 传过去后人还在 `{now}`（目标地点到不了；{state}）");
        }
        Note($"# 农场出口 {(_walkOk + _walkBad)}/{farm.warps.Count}：{label} ⇒ {(ok ? "到了" : "**到不了**（人在 " + now + "）")}");
        // 不必回 Farm：下一条直接从当前地点传，少一次淡入淡出，也不会和上一条抢 `locationRequest`。
        _walkIndex++;
        _walkWaiting = false;
    }

    /// <summary>等上一次传送的淡入淡出真正走完（先给 15 tick，最多等 5 秒）。</summary>
    private bool WalkSettled()
    {
        _walkStepTicks++;
        if (_walkStepTicks < 15) return false;
        if ((Game1.isWarping || Game1.fadeToBlack) && _walkStepTicks < 300) return false;
        return true;
    }

    private void StartTour()
    {
        Note($"# 开始巡 {Tour.Length} 个地点（每个地点停约 5 秒，期间跳过挡路对话/事件）");
        _phase = Phase.Tour;
        _ticks = 0;
        _index = 0;
    }

    /// <summary>② 出门：拿到地点（拿不到 = 建不起来）+ 真传送过去。</summary>
    private void WarpNext()
    {
        if (_index >= Tour.Length)
        {
            Note($"# 巡图结束：传送成功 {_warped} / 建不起来 {_failed}");
            _phase = Phase.Audio;
            _ticks = 0;
            _index = 0;
            Note($"# 开始播 {Cues.Length} 条注入的 cue（波形库是 MS-ADPCM，播一次才走加载/解码那条路）");
            return;
        }

        string name = Tour[_index++];
        GameLocation? loc;
        try
        {
            loc = Game1.getLocationFromName(name);
        }
        catch (Exception ex)
        {
            _failed++;
            Note($"✗ `{name}`：建不起来 —— {ex.GetType().Name}: {FirstLine(ex.Message)}");
            return;
        }

        if (loc == null)
        {
            _failed++;
            Note($"✗ `{name}`：不在 Game1.locations 里（地点数据/地图缺失）");
            return;
        }

        if (loc.Map == null)
        {
            _failed++;
            Note($"✗ `{name}`：地点在，但 Map 为 null");
            return;
        }

        try
        {
            int x = Math.Max(1, loc.Map.Layers[0].LayerWidth / 2);
            int y = Math.Max(1, loc.Map.Layers[0].LayerHeight / 2);
            Game1.warpFarmer(name, x, y, false);
            _warped++;
        }
        catch (Exception ex)
        {
            _failed++;
            Note($"✗ `{name}`：传送时抛 —— {ex.GetType().Name}: {FirstLine(ex.Message)}");
        }
    }

    // ────────────────────────── NPC 踩水追踪 ──────────────────────────

    /// <summary>
    /// 跟住一个 NPC 的早晨行程：把玩家送到目标地点、把时间拨到早晨，
    /// 之后只要 NPC 脚下出现 Water 图块就写现场报告并结束游戏。
    /// 配置来自 `SDVCP_WATER_*` 环境变量（见 <see cref="Entry"/> 上方的说明）。
    /// </summary>
    private void BeginWaterWatch()
    {
        // 先找 NPC：他在哪张图，就跟到哪张图。配置里的地点只是"没找到人时"的兜底。
        _watched = FindWatched();
        GameLocation? loc = _watched?.currentLocation;
        if (loc == null)
        {
            try { loc = Game1.getLocationFromName(_waterLocation); }
            catch (Exception ex) { Note($"✗ 踩水追踪：地点 `{_waterLocation}` 建不起来 —— {Describe(ex)}"); }
        }

        if (loc?.Map == null)
        {
            Note($"✗ 踩水追踪：`{_waterLocation}` 没有 Map，目标也在别处");
            _phase = Phase.WaterDone;
            _ticks = 0;
            return;
        }

        int x = Math.Max(1, loc.Map.Layers[0].LayerWidth / 2);
        int y = Math.Max(1, loc.Map.Layers[0].LayerHeight / 2);
        try { Game1.warpFarmer(loc.Name, x, y, false); }
        catch (Exception ex) { Note($"✗ 踩水追踪：传送到 `{loc.Name}` 失败 —— {Describe(ex)}"); }

        try { Game1.timeOfDay = _waterStartTime; }
        catch (Exception ex) { Note($"# 踩水追踪：设置时间失败 —— {FirstLine(ex.Message)}"); }

        _waterLastWarp = _ticks;
        Note($"# 踩水追踪已就位：`{loc.Name}` ({x},{y})，时间 {Game1.timeOfDay}，"
             + $"目标 `{_waterTarget}` {(_watched == null ? "尚未出现" : $"在 `{_watched.currentLocation?.Name ?? "?"}` tile={_watched.TilePoint}")}");
        if (_watched != null)
        {
            try
            {
                if (_watched.Schedule != null)
                    foreach (var entry in _watched.Schedule)
                        Note($"   schedule {entry.Key} -> {entry.Value} | {DescribeSchedule(entry.Value)}");
            }
            catch { /* 日程读不出来就算了，现场报告里还会再试 */ }
        }
        WriteWaterTrace("就位");
    }

    private NPC? FindWatched()
    {
        if (_waterTarget.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            NPC? npc = Game1.getCharacterFromName(_waterTarget, true);
            if (npc != null) return npc;
        }
        catch { /* 名字不存在/数据坏了：下面再扫地点 */ }

        foreach (GameLocation loc in Game1.locations)
        {
            if (loc?.characters == null) continue;
            foreach (NPC npc in loc.characters)
                if (string.Equals(npc.Name, _waterTarget, StringComparison.OrdinalIgnoreCase))
                    return npc;
        }
        return null;
    }

    /// <summary>
    /// 诊断用：游戏里 <c>checkSchedule</c> 不是公开 API，靠反射调；失败只记一行，不影响追踪。
    /// </summary>
    private void ForceSchedule(NPC npc)
    {
        try
        {
            if (_waterReloadSchedule && !_waterScheduleReloaded)
            {
                _waterScheduleReloaded = true;
                string key = npc.ScheduleKey ?? "Mon";
                try
                {
                    bool ok = npc.TryLoadSchedule(key);
                    Note($"# 踩水追踪：TryLoadSchedule(\"{key}\") → {ok}，"
                         + $"当前 {npc.currentLocation?.Name} ({npc.TilePoint.X},{npc.TilePoint.Y})");
                }
                catch (Exception ex)
                {
                    Note($"# 踩水追踪：TryLoadSchedule 抛 —— {FirstLine(ex.GetBaseException().Message)}");
                }
            }

            if (!_loggedScheduleMethods)
            {
                _loggedScheduleMethods = true;
                foreach (var m in npc.GetType()
                             .GetMethods(System.Reflection.BindingFlags.Instance
                                         | System.Reflection.BindingFlags.Public
                                         | System.Reflection.BindingFlags.NonPublic)
                             .Where(m => m.Name.Contains("Schedule", StringComparison.OrdinalIgnoreCase)))
                    Note($"   method {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})"
                         + (m.Name == "pathfindToNextScheduleLocation"
                             ? $" params={string.Join(",", m.GetParameters().Select(p => p.Name))}"
                             : ""));
            }

            var ignore = npc.GetType().GetField("ignoreScheduleToday",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (ignore?.GetValue(npc) is true)
            {
                ignore.SetValue(npc, false);
                Note("# 踩水追踪：ignoreScheduleToday=true，诊断模式已置 false");
            }

            var methods = npc.GetType()
                .GetMethods(System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic)
                .Where(m => m.Name == "checkSchedule"
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType == typeof(int))
                .ToList();
            if (methods.Count == 0)
            {
                Note("# 踩水追踪：找不到 NPC.checkSchedule(int)（反射）");
                return;
            }
            foreach (var method in methods)
            {
                try
                {
                    object? result = method.Invoke(npc, new object[] { Game1.timeOfDay });
                    Note($"# 踩水追踪：强制 checkSchedule({Game1.timeOfDay}) → {result ?? "(void)"}");
                }
                catch (Exception ex)
                {
                    Note($"# 踩水追踪：强制 checkSchedule 抛 —— {FirstLine(ex.GetBaseException().Message)}");
                }
            }

            if (!_waterDayReset && Game1.timeOfDay >= 900 && GetPrivate(npc, "controller") == null)
            {
                _waterDayReset = true;
                try
                {
                    npc.dayUpdate(Game1.dayOfMonth);
                    npc.OnDayStarted();
                    Note($"# 踩水追踪：controller 仍为 null，已调 dayUpdate/OnDayStarted（scheduleKey={npc.ScheduleKey}）");
                    foreach (var method in methods)
                    {
                        try { method.Invoke(npc, new object[] { Game1.timeOfDay }); }
                        catch { /* 已记录过 */ }
                    }
                }
                catch (Exception ex)
                {
                    Note($"# 踩水追踪：dayUpdate/OnDayStarted 抛 —— {FirstLine(ex.GetBaseException().Message)}");
                }
            }

            if (_waterForcePath && !_waterPathForced)
                TryForcePath(npc);
        }
        catch (Exception ex)
        {
            Note($"# 踩水追踪：反射 checkSchedule 失败 —— {FirstLine(ex.Message)}");
        }
    }

    /// <summary>
    /// 诊断兜底：checkSchedule 不落地时，直接调游戏自己的 pathfindToNextScheduleLocation，
    /// 让 NPC 按日程目标真的走起来（参数按方法签名里的名字填）。
    /// </summary>
    private void TryForcePath(NPC npc)
    {
        try
        {
            var upcoming = npc.Schedule?
                .OrderBy(kv => kv.Key)
                .FirstOrDefault(kv => kv.Key >= Game1.timeOfDay);
            if (upcoming?.Value == null) return;
            object desc = upcoming.Value.Value;
            object? tileObj = GetMember(desc, "targetTile");
            if (tileObj is not Point target)
            {
                Note("# 踩水追踪：日程目标没有 targetTile，无法强制寻路");
                return;
            }

            string targetLoc = GetMember(desc, "targetLocation") as string ?? "";
            if (targetLoc.Length == 0) targetLoc = npc.currentLocation?.Name ?? "";
            int direction = GetMember(desc, "direction") is int d ? d : 0;
            string? endBehavior = GetMember(desc, "endBehavior") as string;
            string? script = GetMember(desc, "script") as string;

            var method = npc.GetType().GetMethod("pathfindToNextScheduleLocation",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (method == null)
            {
                Note("# 踩水追踪：找不到 pathfindToNextScheduleLocation");
                return;
            }

            var ps = method.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                args[i] = (ps[i].Name ?? "").ToLowerInvariant() switch
                {
                    "schedulekey" => upcoming.Value.Key.ToString(),
                    "startinglocation" or "startlocation" or "sourcelocation" => npc.currentLocation?.Name ?? "",
                    "startingx" or "startx" or "sourcex" => npc.TilePoint.X,
                    "startingy" or "starty" or "sourcey" => npc.TilePoint.Y,
                    "endinglocation" or "endlocation" or "targetlocation" => targetLoc,
                    "endingx" or "endx" or "targetx" => target.X,
                    "endingy" or "endy" or "targety" => target.Y,
                    "finalfacingdirection" or "direction" => direction,
                    "endbehavior" => endBehavior,
                    "endmessage" or "script" => script,
                    _ => ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null,
                };
            }

            _waterPathForced = true;
            method.Invoke(npc, args);
            Note($"# 踩水追踪：强制 pathfind {upcoming.Value.Key} → `{targetLoc}` ({target.X},{target.Y})，"
                 + $"controller={GetPrivate(npc, "controller")?.GetType().Name ?? "null"}");
        }
        catch (Exception ex)
        {
            Note($"# 踩水追踪：强制 pathfind 抛 —— {FirstLine(ex.GetBaseException().Message)}");
        }
    }

    private static object? GetMember(object target, string name)
    {
        try
        {
            Type type = target.GetType();
            return type.GetField(name,
                       System.Reflection.BindingFlags.Instance
                       | System.Reflection.BindingFlags.Public
                       | System.Reflection.BindingFlags.NonPublic)?.GetValue(target)
                   ?? type.GetProperty(name,
                       System.Reflection.BindingFlags.Instance
                       | System.Reflection.BindingFlags.Public
                       | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
        }
        catch { return null; }
    }

    /// <summary>每 tick 调一次：跟地图、查每个候选 NPC 的脚下图块。</summary>
    private void WaterWatchTick()
    {
        if (_waterHit) return;

        if (_ticks % 60 == 0)
        {
            _watched = FindWatched();
            FollowWatchedLocation();
            WriteWaterTrace(_ticks % 600 == 0 ? "轨迹" : null);
        }

        // 诊断开关：人工拨时间/传送进来的存档里，NPC 不一定会自己触发日程。
        // 这里显式调一次游戏自己的 checkSchedule，让路径/踩水行为能跑起来。
        if (_waterForceSchedule && _watched != null && Game1.timeOfDay != _waterLastTime)
        {
            _waterLastTime = Game1.timeOfDay;
            ForceSchedule(_watched);
        }

        // 目标找到了就只盯目标（避免把别处合法游泳的 NPC 算进来）；
        // 没找到时盯当前地点的所有 NPC —— 至少能留下现场。
        IEnumerable<(GameLocation Loc, NPC Npc)> candidates;
        if (_watched?.currentLocation != null)
            candidates = new[] { (_watched.currentLocation, _watched) };
        else if (Game1.currentLocation?.characters != null)
            candidates = Game1.currentLocation.characters.Select(n => (Game1.currentLocation, n));
        else
            candidates = Array.Empty<(GameLocation, NPC)>();

        foreach ((GameLocation loc, NPC npc) in candidates)
        {
            if (IsStandingOnWater(loc, npc, out int tx, out int ty))
            {
                _waterHit = true;
                var report = BuildWaterReport(loc, npc, tx, ty);
                try { File.WriteAllLines(_waterReport, report); } catch { }
                foreach (string line in report) _waterTrace.Add(line);
                WriteWaterTrace("踩水");
                Note($"# 💧 发现踩水：`{npc.Name}` @ `{loc.Name}` ({tx},{ty})，时间 {Game1.timeOfDay}");
                _phase = Phase.WaterDone;
                _ticks = 0;
                return;
            }
        }

        // 早上到中午还没踩到就收工（别让自检一直挂着）。
        if (Game1.timeOfDay >= 1200 && _ticks > 1200)
        {
            var end = new List<string>
            {
                $"# 踩水追踪结束：到 {Game1.timeOfDay} 仍未检测到 `{_waterTarget}` 踩水",
                $"playerLocation={Game1.currentLocation?.NameOrUniqueName}",
                $"watched={_watched?.Name ?? "(null)"}",
                $"watchedLocation={_watched?.currentLocation?.NameOrUniqueName ?? "(null)"}",
                $"watchedTile={_watched?.TilePoint.ToString() ?? "(null)"}",
                $"watchedPosition={_watched?.Position.ToString() ?? "(null)"}",
            };
            try { File.WriteAllLines(_waterReport, end); } catch { }
            foreach (string line in end) _waterTrace.Add(line);
            WriteWaterTrace("结束");
            Note($"# 踩水追踪结束：到 {Game1.timeOfDay} 没踩到水");
            _phase = Phase.WaterDone;
            _ticks = 0;
        }
    }

    /// <summary>NPC 换地图后跟过去；否则只在当前地点更新，目标会站着不动。</summary>
    private void FollowWatchedLocation()
    {
        if (_watched?.currentLocation == null) return;
        GameLocation target = _watched.currentLocation;
        if (Game1.currentLocation == target) return;
        if (_ticks - _waterLastWarp < 120) return;
        if (target.Map == null) return;

        int x = Math.Max(1, target.Map.Layers[0].LayerWidth / 2);
        int y = Math.Max(1, target.Map.Layers[0].LayerHeight / 2);
        try
        {
            Game1.warpFarmer(target.Name, x, y, false);
            _waterLastWarp = _ticks;
            Note($"# 踩水追踪：跟到 `{target.Name}`（目标已经换地图）");
        }
        catch (Exception ex)
        {
            Note($"# 踩水追踪：跟随到 `{target.Name}` 失败 —— {FirstLine(ex.Message)}");
        }
    }

    private static bool IsStandingOnWater(GameLocation loc, NPC npc, out int tx, out int ty)
    {
        tx = npc.TilePoint.X;
        ty = npc.TilePoint.Y;
        try
        {
            if (loc.isWaterTile(tx, ty)) return true;

            // 站立像素在大体型 NPC 上更接近"脚底"，两个都查（只用于判定，不改变日志里的 tile）。
            Point stand = npc.StandingPixel;
            int sx = stand.X / 64, sy = stand.Y / 64;
            if (sx != tx || sy != ty)
            {
                if (loc.isWaterTile(sx, sy)) { tx = sx; ty = sy; return true; }
            }
        }
        catch { /* 坐标越界/地图正在换：这一 tick 不算 */ }
        return false;
    }

    private List<string> BuildWaterReport(GameLocation loc, NPC npc, int tx, int ty)
    {
        var lines = new List<string>
        {
            $"# NPC 踩水现场 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"npc={npc.Name}",
            $"displayName={npc.displayName}",
            $"location={loc.NameOrUniqueName}",
            $"time={Game1.timeOfDay} season={Game1.currentSeason} day={Game1.dayOfMonth}",
            $"tilePoint={npc.TilePoint.X},{npc.TilePoint.Y}",
            $"standingPixel={npc.StandingPixel.X},{npc.StandingPixel.Y} (tile {npc.StandingPixel.X / 64},{npc.StandingPixel.Y / 64})",
            $"reportedTile={tx},{ty}",
            $"isWaterTile={SafeWater(loc, tx, ty)}",
            $"isMoving={SafeMoving(npc)}",
            $"scheduleKey={npc.ScheduleKey}",
        };

        lines.Add("mapWater3x3:");
        for (int dy = -1; dy <= 1; dy++)
        {
            var row = new List<string>();
            for (int dx = -1; dx <= 1; dx++)
                row.Add(SafeWater(loc, tx + dx, ty + dy) ? "W" : ".");
            lines.Add($"  {ty + dy,3}: {string.Join(" ", row)}");
        }

        lines.Add("schedule:");
        try
        {
            if (npc.Schedule != null)
                foreach (var entry in npc.Schedule)
                    lines.Add($"  {entry.Key} -> {entry.Value}");
            else
                lines.Add("  (null)");
        }
        catch (Exception ex) { lines.Add($"  (读日程失败：{ex.GetType().Name}: {FirstLine(ex.Message)})"); }

        lines.Add("tileProperties:");
        foreach (string layer in new[] { "Back", "Back 2", "Buildings", "Buildings1", "Buildings2", "Front", "AlwaysFront" })
        {
            foreach (string key in new[] { "Water", "Passable", "Action", "TouchAction", "Warp", "NPCWarp", "NoSpawn" })
            {
                string? value = SafeTileProperty(loc, tx, ty, key, layer);
                if (value != null) lines.Add($"  {layer}.{key}={value}");
            }
        }

        return lines;
    }

    /// <summary>把 SchedulePathDescription 的目标/路线摘要成一行，判断"日程没触发"还是"无路可走"。</summary>
    private static string DescribeSchedule(object? value)
    {
        if (value == null) return "null";
        var parts = new List<string>();
        Type type = value.GetType();
        foreach (string name in new[] { "targetLocation", "targetTile", "route", "direction", "endBehavior", "script" })
        {
            try
            {
                object? v = type.GetField(name,
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic)?.GetValue(value)
                    ?? type.GetProperty(name,
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic)?.GetValue(value);
                if (v == null) { parts.Add($"{name}=null"); continue; }
                if (v is System.Collections.IEnumerable en and not string)
                {
                    int n = 0;
                    foreach (object? _ in en) n++;
                    parts.Add($"{name}=<{v.GetType().Name} n={n}>");
                }
                else parts.Add($"{name}={v}");
            }
            catch { parts.Add($"{name}=?"); }
        }
        return string.Join(", ", parts);
    }

    private static object? GetPrivate(object target, string name)
    {
        try
        {
            return target.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
        }
        catch { return null; }
    }

    /// <summary>追踪用的少量私有字段快照：没路径时看是"没触发日程"还是"寻路失败"。</summary>
    private static string ReflectNpc(NPC npc)
    {
        var parts = new List<string>();
        foreach (string name in new[]
                 {
                     "ignoreScheduleToday", "controller", "tileController",
                     "lastCrossMap", "doingEndOfRouteBehavior", "currentSchedule",
                 })
        {
            try
            {
                var field = npc.GetType().GetField(name,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (field == null) continue;
                object? value = field.GetValue(npc);
                parts.Add($"{name}={(value == null ? "null" : value is bool b ? b.ToString() : value.GetType().Name)}");
            }
            catch { /* 反射失败不影响判定 */ }
        }
        return string.Join(",", parts);
    }

    private static bool SafeWater(GameLocation loc, int x, int y)
    {
        try { return loc.isWaterTile(x, y); }
        catch { return false; }
    }

    private static bool SafeMoving(NPC npc)
    {
        try { return npc.isMoving(); }
        catch { return false; }
    }

    private static string? SafeTileProperty(GameLocation loc, int x, int y, string key, string layer)
    {
        try { return loc.doesTileHaveProperty(x, y, key, layer); }
        catch { return null; }
    }

    private void WriteWaterTrace(string? tag)
    {
        if (_waterTraceFile.Length == 0) return;
        if (tag != null)
            _waterTrace.Add($"--- {tag} t={Game1.timeOfDay} ticks={_ticks} at={DateTime.Now:HH:mm:ss} ---");

        // 目标写 ALL 时盯整张地图：每个 NPC 都记一行，踩水时才知道是谁、站在哪。
        if (_watched == null)
        {
            if (tag == null && _ticks % 300 != 0) return;
            GameLocation? here = Game1.currentLocation;
            _waterTrace.Add($"t={Game1.timeOfDay} player={here?.Name ?? "?"} npcs={here?.characters?.Count ?? 0}");
            if (here?.characters != null)
            {
                foreach (NPC c in here.characters)
                {
                    bool wet = SafeWater(here, c.TilePoint.X, c.TilePoint.Y);
                    _waterTrace.Add($"  {c.Name} tile={c.TilePoint} moving={SafeMoving(c)} water={(wet ? "W" : ".")}");
                }
            }
            try { File.WriteAllLines(_waterTraceFile, _waterTrace); } catch { }
            return;
        }

        bool changed = _watched.Position != _lastTracePos;
        if (tag == null && !changed && _ticks % 300 != 0) return;
        if (_watched != null) _lastTracePos = _watched.Position;

        bool inChars = _watched?.currentLocation?.characters?.Contains(_watched) ?? false;
        string npc = _watched == null
            ? "target=(null)"
            : $"target={_watched.Name} loc={_watched.currentLocation?.Name ?? "?"} "
              + $"inChars={inChars} charCount={_watched.currentLocation?.characters?.Count ?? -1} "
              + $"tile={_watched.TilePoint} pos={_watched.Position} moving={SafeMoving(_watched)} "
              + $"water={(_watched.currentLocation != null && SafeWater(_watched.currentLocation, _watched.TilePoint.X, _watched.TilePoint.Y) ? "W" : ".")} "
              + $"scheduleKey={_watched.ScheduleKey} fields={ReflectNpc(_watched)}";
        _waterTrace.Add($"t={Game1.timeOfDay} player={Game1.currentLocation?.Name ?? "?"} "
                        + $"eventUp={Game1.eventUp} dialogueUp={Game1.dialogueUp} "
                        + $"menu={Game1.activeClickableMenu?.GetType().Name ?? "null"} {npc}");
        try { File.WriteAllLines(_waterTraceFile, _waterTrace); } catch { }
    }

    /// <summary>②b 播注入的 cue（音色好不好听替不了人听，但加载/解码出问题会在这里冒出来）。</summary>
    private void PlayNextCue()
    {
        if (_index >= Cues.Length)
        {
            Note("# 音乐播完（有没有报错看 SMAPI 日志里的 Could not load / 音频异常）");
            _phase = Phase.Sleep;
            _ticks = 0;
            return;
        }

        string cue = Cues[_index++];
        try
        {
            Game1.changeMusicTrack(cue);
            _played++;
            Note($"# 播了 `{cue}`");
        }
        catch (Exception ex)
        {
            _failed++;
            Note($"✗ 播 `{cue}` 时抛 —— {ex.GetType().Name}: {FirstLine(ex.Message)}");
        }
    }

    /// <summary>③ 跨天（走 `NPC.dayUpdate` → `RequireLocation` 那条路）。</summary>
    private void Sleep()
    {
        if (_days >= 3)
        {
            // 季节检查放在**引用 Daisy 图集的地图**上（Cellar 用了 `*_daisyextras`）——
            // 站在标准农场上切季只能看到原版图块，看不出模组图集有没有进来。
            try
            {
                GameLocation? cellar = Game1.getLocationFromName("Cellar");
                if (cellar?.Map != null)
                {
                    int x = Math.Max(1, cellar.Map.Layers[0].LayerWidth / 2);
                    int y = Math.Max(1, cellar.Map.Layers[0].LayerHeight / 2);
                    Game1.warpFarmer("Cellar", x, y, false);
                    Note("# 季节检查：已传送到 `Cellar`（引用 Daisy 图集的地图）");
                }
            }
            catch (Exception ex)
            {
                Note($"# 季节检查前传送到 Cellar 失败：{ex.GetType().Name}: {FirstLine(ex.Message)}");
            }

            _phase = Phase.Seasons;
            _ticks = 0;
            _seasonIndex = 0;
            return;
        }

        _days++;
        Note($"# 跨天第 {_days} 次（Game1.NewDay）");
        Game1.NewDay(0f);
    }

    private static readonly string[] Seasons = { "summer", "fall", "winter" };
    private int _seasonIndex;

    /// <summary>依次切到夏/秋/冬，并让当前地图按季重载 —— 检查四季图集是不是真的能换。</summary>
    private void NextSeason()
    {
        if (_seasonIndex >= Seasons.Length)
        {
            _phase = Phase.Maps;
            _ticks = 0;
            BeginMapProbe();
            return;
        }

        SwitchSeason(Seasons[_seasonIndex++]);
    }

    private void SwitchSeason(string? season)
    {
        if (string.IsNullOrWhiteSpace(season)) return;
        string value = season.Trim().ToLowerInvariant();
        if (value is not ("spring" or "summer" or "fall" or "winter"))
        {
            Note($"✗ 未知季节：{season}");
            return;
        }

        try
        {
            Game1.currentSeason = value;
            Game1.dayOfMonth = 1;
            GameLocation? here = Game1.currentLocation;
            here?.reloadMap();
            Note($"# 切换季节：{value}（当前地图 `{here?.Name}` 已按季重载）");
        }
        catch (Exception ex)
        {
            Note($"✗ 切换季节 {value} 抛 —— {ex.GetType().Name}: {FirstLine(ex.Message)}");
        }
    }

    // ────────────────────────── 逐张地图探针 ──────────────────────────

    private string[] _maps = Array.Empty<string>();
    private int _mapIndex;
    private int _mapPass, _mapSkip, _mapFail;

    private int _seasonFail;

    /// <summary>
    /// ★ **四季图集族**：游戏换季换的是**文件名前缀**（<c>GameLocation.GetSeasonalTilesheetName</c>
    /// 只对以 `spring_`/`summer_`/`fall_`/`winter_` 开头的名字做替换），所以只要四季四份**都在**
    /// （且在资产清单里 —— 游戏判"存在"看的是 `ContentHashes.json`，不看文件系统）就能取到。
    /// <para>
    /// 这里用**游戏自己的两个 API** 核一遍，比"盘上有文件"更接近真实加载：
    /// <c>GetSeasonalTilesheetName</c>（算这一季该去取谁）+ <c>DoesAssetExist</c>（查清单）。
    /// 名单来自 mod 目录的 <c>season-sheets.txt</c>，每行一个**完整的资产名**（含季前缀），
    /// 例如 <c>Maps/spring_Spring_z_extras</c> —— ⚠ 必须是**游戏会去请求的那个完整名字**：
    /// 地图的图集引用是相对地图自己那层目录解析的，`Maps/` 顶层的地图取的就是 `Maps/&lt;名&gt;`
    /// （漏掉 `Maps/` 前缀会得到**假阴性** —— 实测踩过，盘上有文件也报缺）。
    /// </para>
    /// </summary>
    private void CheckSeasonalFamilies()
    {
        string list = Path.Combine(Helper.DirectoryPath, "season-sheets.txt");
        if (!File.Exists(list)) return;

        foreach (string raw in File.ReadAllLines(list))
        {
            string name = raw.Trim().Replace('\\', '/');
            if (name.Length == 0 || name.StartsWith("#", StringComparison.Ordinal)) continue;

            var missing = new List<string>();
            foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            {
                // 游戏**自己**的换季规则：只认文件名开头的 `<季>_`，换掉前缀后去取那一份。
                string asset = GameLocation.GetSeasonalTilesheetName(name, season);
                if (!Game1.content.DoesAssetExist<Microsoft.Xna.Framework.Graphics.Texture2D>(asset))
                    missing.Add($"{season} → `{asset}`");
            }

            if (missing.Count == 0)
                Note($"# 四季图集族 `{name}`：四季四份都在（游戏按季取得到）");
            else
            {
                _seasonFail++;
                Note($"✗ 四季图集族 `{name}` 缺：{string.Join("、", missing)}");
            }
        }
    }

    /// <summary>扫 `Content/Maps/**/*.xnb` 当清单（与 AssetProbe 同一口径）。</summary>
    private void BeginMapProbe()
    {
        CheckSeasonalFamilies();

        // 只探清单里的（`probe-only.txt`，每行一个资产名；`#` 开头当注释）—— 单图迭代用
        string only = Path.Combine(Helper.DirectoryPath, "probe-only.txt");
        if (File.Exists(only))
        {
            _maps = File.ReadAllLines(only)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal))
                .ToArray();
            Note($"# 只探清单里的 {_maps.Length} 个资产（probe-only.txt）");
            return;
        }

        string mapsDir = Path.Combine(Constants.ContentPath, "Maps");
        if (!Directory.Exists(mapsDir))
        {
            Note($"# 找不到 {mapsDir}，跳过逐张地图探针");
            _phase = Phase.Done;
            _ticks = 0;
            return;
        }

        _maps = Directory.GetFiles(mapsDir, "*.xnb", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Constants.ContentPath, p)
                .Replace(Path.DirectorySeparatorChar, '/')[..^4])
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Note($"# 逐张地图探针：{_maps.Length} 张（`Maps/` 下不全是地图，贴图按 Texture2D 再试一次，记「跳过」）");
    }

    /// <summary>一次一张：先按地图建，建不起来再按贴图试（贴图不算失败）。</summary>
    private void ProbeNextMap()
    {
        if (_mapIndex >= _maps.Length)
        {
            Note($"# 地图探针结束：PASS {_mapPass} / 贴图跳过 {_mapSkip} / **FAIL {_mapFail}**（共 {_maps.Length}）");
            _phase = Phase.Done;
            _ticks = 0;
            return;
        }

        string asset = _maps[_mapIndex++];
        try
        {
            // ⚠ 照 `AssetProbe` 的做法：**真建一个 GameLocation 并碰 `.Map`** —— 1.6 里地图走的是
            //   `Game1.game1.xTileContent`，用 `Game1.content.Load<Map>` 会得到**假失败**。
            //   ⚠ 名字用固定值 `Probe`：构造里按名字判 isFarm/isStructure（`name.Contains("Farm")` 之类），
            //   资产名里带 Farm 的会被当成农场走另一条路，探针要的是中性构造。
            var location = new GameLocation(asset, "Probe");
            if (location.Map == null) throw new InvalidOperationException("`.Map` 读出来是 null");
            _mapPass++;
        }
        catch (Exception mapError)
        {
            try
            {
                Game1.content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>(asset);
                _mapSkip++;
            }
            catch (Exception)
            {
                _mapFail++;
                // ⚠ 只印外层消息看不出来缺哪个文件（`ContentLoadException` 的外层就一句
                //   "The content file was not found."，真正的路径在内层）⇒ 把异常链整条印出来。
                Note($"✗ `{asset}` 建不起来 —— {Describe(mapError)}");
            }
        }
    }

    /// <summary>④ 收尾：写总结。只做一次。</summary>
    private void Finish()
    {
        _finished = true;
        // `Data/Characters` 装的是**每个 NPC 的定义** —— 它要是没进装机（打包器打不出那份资产时
        // 覆盖式装机就退回原版），mod 新增的 NPC 会**静静地不存在**（不崩、也没人报）。
        // 实测 2026-10-13：那棵树里有 269 条（原版 48 条），而装机里一直是原版那份。
        int npcData = Game1.characterData?.Count ?? 0;
        Note($"# `Data/Characters` 里有 {npcData} 个 NPC 定义（原版 48 个；明显偏少 = 那份资产没进装机）");

        // 原版作物是**运行时数据**：Data/Crops 里少一条，对应的种子就种不出来。
        // 实测 2026-10-24：纯代码框架 CustomBush 的删除补丁把葡萄/啤酒花/咖啡/蓝莓/仙人掌删了。
        int cropData = 0;
        var missingCrops = new List<string>();
        try
        {
            var crops = Game1.content.Load<Dictionary<string, StardewValley.GameData.Crops.CropData>>("Data/Crops");
            cropData = crops.Count;
            foreach (string id in new[] { "301", "302", "433", "481", "802" })
                if (!crops.ContainsKey(id)) missingCrops.Add(id);
        }
        catch (Exception ex)
        {
            Note($"# ✗ 读 `Data/Crops` 失败：{FirstLine(ex.Message)}");
        }
        Note($"# `Data/Crops` 里有 {cropData} 条；原版葡萄/啤酒花/咖啡/蓝莓/仙人掌 "
             + (missingCrops.Count == 0 ? "齐全" : "**缺 " + string.Join(",", missingCrops) + "**"));

        Note($"# 总结：传送成功 {_warped} / 地点建不起来 {_failed} / 播出 {_played} 条音乐 / 跨天 {_days} 次 / "
             + $"地图探针 PASS {_mapPass} · 贴图跳过 {_mapSkip} · **FAIL {_mapFail}**"
             + $" · NPC 定义 {npcData}"
             + (_seasonFail == 0 ? "" : $" · 四季图集族缺 {_seasonFail}") + " —— "
             + (_failed == 0 && _mapFail == 0 && _seasonFail == 0 && missingCrops.Count == 0
                 ? "**没有建不起来的** ✓" : "**有建不起来的** ✗"));
        Note("# 逐张地图那一层另跑 `probe_maps`（AssetProbe）—— 这里不代它触发，SMAPI 没给跨 mod 触发命令的口子。");
        Monitor.Log($"GameSmoke 完成：成功 {_warped}，失败 {_failed}", LogLevel.Alert);
    }

    /// <summary>异常链（外层 ← 内层 …）：探针失败时"缺哪个文件"只有内层说得清。</summary>
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e != null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {FirstLine(e.Message)}");
        return string.Join(" ← ", parts);
    }

    private static string FirstLine(string text)
    {
        int at = text.IndexOf('\n');
        return at < 0 ? text : text[..at];
    }
}
