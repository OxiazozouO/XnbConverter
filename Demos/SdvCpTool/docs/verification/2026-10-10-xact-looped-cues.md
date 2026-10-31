# 注入的 cue 终于会循环了：照原版音乐的形态写

日期：2026-10-10（接 `docs/research/2026-10-10-why-injected-cues-dont-loop.md`）

**背景**：`Data/AudioChanges` 注入的音频**能播但播一遍就停**。SVE 那 37 条里 `Looped: true` 有 **30** 条受影响。

## 症结（研究文档已查明）与这次的解法

「会不会循环」由 MonoGame 的 `XactClip` 决定：只有 sound 记录里带 clips
（`complexSound = true`）才会建出 `XactClip`，而 `loopCount` 就存在它的播放事件里（255 = 无限）。
我们原先写的是「**复杂 Cue + 变体类型 0**」（直接引用波形）—— 那条路没有 clip，也就没有循环。

**关键发现**：不用去动 `Sounds` 段（研究文档担心的「往后所有绝对偏移都要重算」那一步）。
**把新 sound 记录追加到文件末尾**、让新 cue 的 `SoundOffset` 指过去就行 ——
`Sounds` 段头部的那个 `SoundsOffset` MonoGame 根本不用（它按 cue 里的偏移去取）。
于是能保住「已有字节一个不动」这条底线。

## 改了哪些地方

**XnbConverter（只碰 XACT）**

| 文件 | 改动 |
|---|---|
| `XactClipReader.Write` | 原先 `throw NotImplementedException`。现在写「类型 1（单条播放）」的事件 —— 原版音乐就是这一种 |
| `XactSoundReader.Write` | `FileOffset == 0`（新增）时**整条**写出来：`Flags=0x01` + `NumClips` + clips |
| `SoundBankReader.BuildAppendingCues` | 支持追加**简单 Cue**：搬一张新的简单 Cue 表到末尾、名字插在复杂名**之前**（「第 j 条简单 Cue ↔ 名字表第 j 个」是位置约定） |
| `SoundBankReader.ApplyInPlace` | 跳过 `FileOffset == 0` 的 Sound / SoundEntry —— 漏了这句，新增的那条会被当成「就地修补」写在写入游标处，把原文件中间的字节糊掉（实测踩过） |
| `SoundBankView.LoopedCues` | 新增字段：要循环的那些 Cue 名。空列表 = 不循环（老行为一字不变） |
| `SoundBank.SoundEntry.Name` | 补上名字：追加路径要按名字去 `_sounds` 里取 Sound 记录 |

**SdvCpTool**：`AudioChangesMerger` 按 CP 的 `Looped` 登记 —— `true` 就进 `LoopedCues`。
原先那句「⚠ 当前写法表达不了循环，游戏里会播一遍就停」如实删掉。

## 证据

1. **照原版做参照物**：原版 `spring_day_ambient`（循环音乐）实测结构是
   「**简单** Cue（`SoundEntry.Flags=0x04`）→ Sound `Flags=0x01`、`NumClips=1`
   → 类型 1 事件、`EventInfo=0x20000001`、`EventFlags=0x0C`、`LoopCount=255`」。
   新写法逐项照抄这些常量。

2. **Clip 字节级往返**：把原版 cue 的 Clip 用新写的 `XactClipReader.Write` 写回原位，
   与原文件**逐字节相同** ✓（`--clip-roundtrip`，`spring_day_ambient` / `christmasTheme` /
   `Upper_Ambient` 三条都过）。布局里那几位看不懂的（事件前 1 个未知字节、角度/随机量的定标）
   就是靠这个钉死的。

3. **端到端**：SVE 单跑 `convert` → 视图里 **37 条新增、其中 30 条登记循环**（与 CP 的
   `Looped: true` 条数一致）→ 打包器打出 `.xsb` → 读回来：
   - `…_CrimsonSands`（CategoryId=2 音乐）：简单 Cue ✓ NumClips=1 ✓ 类型 1 ✓ **LoopCount=255** ✓
   - `…_DistantTrain`（不循环）：仍是复杂 Cue + 变体类型 0 ✓（**没**进简单 Cue 表 ✓）—— 两条路都对

4. 既有检验全绿：XnbConverter 的 `--addcue` / 结构往返（`Sound Bank.xsb` 同尺寸往返
   逐字节相同）✓；SdvCpTool 测试 1273 条 ✓。

## 未确证（如实记）

- **进游戏听**：本机跑不了那条验证。游戏的 `MonoGame.Framework.dll` 是 **x86** 程序集
  （PE machine `0x014c`，TargetFramework `.NETCoreApp,Version=v6.0`），而本机只装了 x64 运行时，
  所以 `--check-cue-loop` 载不进它（工具如实报「跳过」而不是「不通过」）。
  ⇒ 剩下的靠人：把产出树按 `docs/how-to-build-an-install.md` 装进去，进游戏听音乐是不是**连着放**、
  **循环点自不自然**。参考实现 `tools/check-cue-loop.ps1`（需 x86 宿主）。
- 循环点是否自然（`LoopStart`/`LoopLength` 那层 MonoGame 不用，研究文档已确证）——
  按现在这条路径，循环就是整段重播，接缝取决于音频本身。
