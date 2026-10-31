# 为什么注入的 cue 不循环，以及要修得动哪里

日期：2026-10-10

**背景**：`Data/AudioChanges` 已经改成「ogg 并进 Wave Bank + cue 加进 Sound Bank」
（见 `docs/verification/2026-10-10-xact-music-injection.md`），音频**能播**，
但**播一遍就停**。SVE 那 37 条里 `Looped: true` 有 **30** 条受影响。

> ✅ **已修**（同日）：改走「简单 Cue → 带 Clip 的 Sound → LoopCount=255」这条路
> （也就是原版音乐本身的形态），不再用「复杂 Cue + 变体类型 0」。做法、证据与
> 未确证项见 `docs/verification/2026-10-10-xact-looped-cues.md`。
> 本文留作「当时为什么卡住」的记录。

## 症结：我们写的是「变体类型 0」，那条路没有 loopCount

新 cue 的形态是「复杂 Cue + 变体类型 0」。MonoGame 读到它时走的是

```csharp
// XactSoundBankSound(SoundBank soundBank, int waveBankIndex, int trackIndex)
complexSound = false;
```

⇒ **没有 `XactClip`**，而循环信息就存在 `XactClip` 的 `PlayWaveEvent` 里
（`XactClip` 的读法：`clipEvents[i]` 那个 `case 1u` 分支里有个 `loopCount` 字节，
播放侧 `while (!sound.IsDisposed && (sound.LoopCount >= 255 || num++ < sound.LoopCount))`
—— **255 = 无限循环**）。

⚠ 另一条**走不通**的路：波形库条目元数据里本来有 `LoopStart`/`LoopLength`，
但 MonoGame 的 `SoundEffect`（波形库走的那个 internal 重载）把这两个参数透传给
`PlatformInitializePcm`，而**那个方法根本没用它们**。所以「在 Wave Bank 里设循环区」
对 MonoGame 无效。

## 原版是怎么做的

原版 **56 个复杂 Cue** 里，**36 条是变体类型 1**（指向 sound 记录）、**20 条内联 Sound**，
**一条类型 0 都没有**（类型 1 的例子 `stoneCrack`/`cluck`/`swordswipe` 全是音效）。
**音乐走的是简单 Cue** → `SoundOffset` → 一条完整的 sound 记录 → `XactClip` → `loopCount`。

也就是说：**要循环就得写出「sound 记录 + XactClip」那一层**。

## 修在哪里

落在 **XnbConverter 的 `SoundBankReader`**（`BuildAppendingCues` 那条追加路径）：

1. **追加路径现在只支持「复杂 Cue + 变体类型 0」**，要再加一种形态：
   「简单 Cue + 一条新 sound 记录（`complexSound = true`，内含一个 `XactClip`，
   `loopCount` 按 `Looped` 填 255 或 0）」。

2. **难点在 `Sounds` 段的位置**。现在追加路径之所以敢「已有字节一个不动」，是因为
   新内容（变体段、复杂 Cue 表、名字表）全都能塞到**文件末尾**；但 `Sounds` 段在文件
   **中段**，加一条会移动它后面的所有段 ⇒ 那些段里的**绝对偏移**（各 Cue 的
   `VariationTableOffset`、`TransitionTableOffset`、变体类型 1/3 里的 `SoundOffset`）
   **全都要重新算**。

   ⇒ 可行的做法是**整体平移 + 一处集中修正**：把 `Sounds` 之后的所有段平移到新偏移，
   并遍历所有 offset 字段加上 delta。⚠ 这一步**没有现成的正确性参照**，
   写错了是「游戏加载声库失败」，比「没声音」严重 —— 所以当时没顺手做。

3. `AudioChangesMerger` 侧只要按 `Looped` 选形态即可（现在它把
   `Looped: true` 的条数如实写进 `writeNote`，改成真修之后就删掉那句标注）。

## 现在的过渡状态

- `AudioChangesMerger` 数出 `Looped: true` 的条数，写进 `writeNote`：
  「⚠ 其中 N 条在 CP 里是 `Looped: true`，但当前写法表达不了循环，游戏里会播一遍就停」。
- `docs/research/2026-10-10-known-losses.md` 第 4 条记着这条残留损失。

## 一条查过的岔路：游戏**不会**自动重播（追完了）

`Game1` 的音乐更新里有个 `else if` 链的最末一支，看着像是会重播：

```csharp
else if (currentSong != null && !currentSong.IsPlaying && !currentSong.IsStopped)
{
    currentSong = soundBank.GetCue(currentSong.Name);
    currentSong.Play();
}
```

如果它能走到，我们的 cue 播完后游戏就会重播（只是每遍之间有间隙）。**追完发现走不到**：

1. `Cue.IsStopped` 优先看 `_soundEffect`：`if (_soundEffect != null) return _soundEffect.State == Stopped;`
   （只有 `_soundEffect == null` 时才退到 `!IsPrepared` 那一支）。
2. 我们的 cue 是「复杂 Cue + 变体类型 0」⇒ `complexSound = false` ⇒
   `Cue.Play()` 里 `GetSimpleSoundInstance()` **返回非 null** ⇒ 调 `PlaySoundInstance(...)`，
   而那里第一件事就是 `_soundEffect = sound_instance;`（`Cue.cs:301-336`）。
   ⇒ **`_soundEffect` 一定非 null**，走的是第一支。
3. 播完 ⇒ `IsPlaying` 为 false（`_time` 被置 -1），同时 `_soundEffect.State` 变成 `Stopped`
   （XNA 里播完就是这个状态）⇒ `IsStopped` 为 **true** ⇒ 外层那个 `!IsStopped` **不成立**。

⇒ **不会重播**，"播一遍就停"这个说法成立。⚠ 第 3 步里"播完 ⇒ `State == Stopped`"是照 XNA
语义推的、没实机听——但这一步不成立的话结论只会**更好**（会重播），所以按现在这样写不吃亏。

## 支持的音频格式（边界，记清楚免得以后问）

`AudioChangesMerger` **不检查扩展名**，把 `FilePaths` 指的文件原样复制进产出树；
解码是打包时 `WaveBankWriter` 按扩展名分派的：

| 扩展名 | 会怎样 |
|---|---|
| `.ogg` | 走移植进来的 NVorbis 解码 ✅ |
| `.wav` | 走原有的 wav 解析 ✅ |
| 其它（`.mp3` 等） | **打包时报错**（XACT 波形库也不支持这些格式）❌ |

**量过**：语料里 `Data/AudioChanges` 的 `FilePaths` **37 条全是 `.ogg`**，
所以这个边界目前没有暴露面 —— 也就没为它加扩展名预检（加了属于推测性处理）。
⚠ 真遇到非 ogg/wav 时，症状是**打包阶段报错**而不是静默产出坏文件。
