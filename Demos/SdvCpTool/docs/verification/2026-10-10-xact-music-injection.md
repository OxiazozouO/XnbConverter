# XACT 音乐注入：端到端验证（用游戏自己的 MonoGame 加载器）

日期：2026-10-10

**为什么要做**：已知损失清单里挂着「**地图没有音乐**」（SVE 的 `Data/AudioChanges`
37 条值里是 `{{AbsoluteFilePath: assets/Music/*.ogg}}`，绝对路径没法静态化，全被拒）。
⚠ 那个数字后来订正过：**精确说是 13 张地图**（按 tbin 的 `Music` 属性精确解析，
早先的「19 张」是拿短名做**子串**匹配数出来的，把长名也算进去了）。

原以为「音频数据必须以裸文件形式躺在 `Content/` 下」—— 那条依赖装机流程，不干净。
后来发现 `AudioCueModificationManager` 只是**运行时**从文件建 `SoundEffect`，
而 1.6 **确实还在走 XACT**（`SoundBankWrapper`  deference 到 MonoGame 的 `SoundBank`），
所以真正干净的路子是：**把音频塞进 `Wave Bank.xwb`，再往 `Sound Bank.xsb` 加一条 cue**，
全程在 xnb 体系里。

## 结论：**这条路走通了**（底层能力已在 XnbConverter 落地并验证）

| 改动 | 提交 | 验证 |
|---|---|---|
| 波形库支持追加条目 | `XnbConverter e26f8e3` | `Wave Bank.xwb` 437→438 条，已有载荷逐字节不变 |
| 声音库支持追加 Cue | `XnbConverter 641add4` | `Sound Bank.xsb` 复杂 Cue 56→57、总 Cue 433→434 |
| 视图层新增入口 | `XnbConverter f4c6278` | 走 `SoundBankView` 也加得进去 |
| 注入工具 | `XnbConverter ddd5767` | `--inject-music` 全链路 |

## ⚠ 代价：波形库从 439 MB 涨到 1.02 GB（2026-10-10 晚量）

注入进去的音乐在波形库里是 **PCM**，而原版库几乎全是 **ADPCM** —— 于是体积按 **1:10.5**
（ogg→PCM）膨胀：

| | 字节 |
|---|---|
| 原版 `Content/XACT/Wave Bank.xwb` | **460,453,688**（439 MB）|
| 注入 35 条 ogg（60.7 MB）后打出来的 | **1,095,519,576**（1.02 GB）|
| 净增 | **+635 MB** |

编解码分布（`Wave Bank.xwb.config` 的 `Format & 3`，474 条）：**ADPCM 431 条 / PCM 43 条** ——
那 43 条里 **37 条是我们注入的**（`DecodeOggEntries` 写死 `Format` 的 PCM 位、
`Length` 取解码后的 PCM 长度），原版只有 6 条 PCM。

**⇒ 装机后 `XACT/Wave Bank.xwb` 是原版的 2.4 倍。** 这是音频注入的**真实代价**，
别只看「13 张地图有音乐了」。

**能不能压回去**：能，把注入的那几条也编成 MS-ADPCM（约 4:1，635 MB → ~160 MB）。
`XnbConverter.Core/Xact/WaveBank/Codec/MsAdpcm.cs` 与 `WavePayload.Build` **已经支持**
ADPCM 编码（「等长重编码」那条路在用），缺的是让 `DecodeOggEntries` 那条路按目标编解码走一遍。
**没做**，理由如实记：**验证不了** —— 本机载不进游戏的 MonoGame（x86），
「游戏会不会认我们新追加的 ADPCM 条目」只能靠实机；而现在这条 PCM 路子已经端到端验过
（字节级 + 读回）。⇒ 未确证的事不开工。

**另一个坑（操作层面）**：`D:\XnbConverter\out_publish\XnbConverter.Cli.exe` 是 **2026-10-05**
的发布版，**早于**「波形库直接吃 .ogg」那几次提交 —— 拿它回包会报
`无效的 WAV 格式：OggS，应为 RIFF`，`Wave Bank.xwb` 根本不会生成。
要用**重新构建的**打包器（`Demos/XnbConverter.Cli/bin/Release/net8.0/`，
且要在它自己的目录下跑，因为 `.config\custom dll` 是相对路径）。

## 怎么验的（**这层以前没有**）

**关键认识**：`%APPDATA%\StardewValley\ErrorLogs\` 里「零新增」**不能**用来证明声库写对了 ——
把 `Sound Bank.xsb` 的头部魔数改成 `XXXX` 之后，游戏**照样起、日志照样干净**。
⇒ **主菜单根本不加载 sound bank**（加载发生在进游戏之后），
所以「原版启动到主菜单、零报错」这种验证在这个资产上**完全无效**。

**改用的办法**：写一个探针，**用「游戏自己那份 `MonoGame.Framework.dll`」**
（反射加载，不引编译期依赖）去 `new AudioEngine(...)` → `new WaveBank(...)` →
`new SoundBank(...)` → `Exists(cue)` → `GetCue(cue)`。

⚠ 坑：MonoGame 的 `TitleContainer` 只收**相对路径**，基准是**程序集所在目录**（不是 cwd）
—— 得把 `FarmerSounds.xgs` / `Wave Bank.xwb` / `Sound Bank.xsb` 放进 bin 目录再跑。

### 基线（原版文件，先证明探针本身有效）

```
AudioEngine 建起来了（FarmerSounds.xgs）
WaveBank 加载成功：Wave Bank.xwb（460453688 字节）
SoundBank 加载成功：Sound Bank.xsb（32062 字节）
  SoundBank.Exists("doorClose") = True
  GetCue("doorClose") → Cue  ✓ 波形也建出来了
```

### 我们的注入版

注入内容：`DistantTrain.ogg`（SVE，78 KB）→ ffmpeg 转 **2ch/44100Hz/16bit PCM**（载荷 1380668 字节）
→ 追加进 `Wave Bank.xwb`（第 438 条）→ 在 `Sound Bank.xsb` 加 cue `zzz_injected_music` 指向 track 437。

```
AudioEngine 建起来了（FarmerSounds.xgs）
WaveBank 加载成功：Wave Bank.xwb（461834380 字节）      ← 我们改的那份
SoundBank 加载成功：Sound Bank.xsb（37752 字节）        ← 我们改的那份
  SoundBank.Exists("zzz_injected_music") = True          ← 新 cue 认到了
  SoundBank.Exists("doorClose")（原版对照）= True         ← 没破坏已有的
  GetCue("zzz_injected_music") → Cue  ✓ 波形也建出来了
```

`GetCue` 这一步是重点：它会走
`new SoundEffect(codec, buffer, channels, rate, alignment, LoopStart, LoopLength)`，
**能把波形解出来**就说明 `Format` 与载荷长度都吃得下。

⚠ **订正（同日复核）**：我原先在这句后面写过「循环区游戏吃得下」——**那句话是错的**。
反编译 `MonoGame.Framework.dll` 的 `SoundEffect` 看到，波形库走的是
`internal SoundEffect(MiniFormatTag, …)` 这个重载，它把 `loopStart`/`loopLength` 透传给
`PlatformInitializePcm`，而**那个方法根本没用这两个参数**（只拿 buffer/采样率/声道去建
`RtapSpring`）——**Wave Bank 条目元数据里的循环区，MonoGame 是丢弃的**。
（`loopLength == 0 ⇒ 整段循环` 那条规则只存在于**公开**的 `SoundEffect(byte[], …)`
重载里，波形库不走那条。）

## 为什么这条路原本走不通、怎么绕开的

- `WaveBankWriter` 原本是「**同尺寸原地替换**」（`Prefix` 原样回写、载荷按记录 offset 落位）
  ⇒ 加不了条目。现在清单条目多于头部记录数时**重建头部**（改 `EntryCount` 与 5 段表、
  补元数据记录、新波形按 `Alignment` 追加），已有条目只整体平移。
- `SoundBankReader.Build` 原本是「以读入字节为模板、按记录原 `FileOffset` 就地修补」
  ⇒ 加不了 Cue。现在多一条**追加路径**：新 Cue 写成「复杂 Cue + 变体类型 0」
  （`(trackIndex, waveBankIndex)` 内联在变体表里，**不需要 sound 记录**），
  新内容一律追加到文件末尾，已有字节一个不动。
- **cue 名哈希表不用管**：MonoGame 把 Cue 装进 `Dictionary<string, CueDefinition>` 按名字查，
  文件里的 `CueNameHashTable` / `CueNameHashVals` **它根本不读**
  （反编译 `MonoGame.Framework.dll` 的 `SoundBank` 构造函数确认）。

## 这次**没**覆盖到的（别当成已验证）

- ~~⚠ **新 cue 不会循环**~~ —— ✅ **同日已修**：改走「简单 Cue → 带 Clip 的 Sound →
  `LoopCount=255`」，见 `2026-10-10-xact-looped-cues.md`
  （⚠ 那条路仍缺「进游戏听」的实机证据）。下面这段留作**当时的症结记录**：
  
  ⚠ **新 cue 不会循环** —— 这一条最要紧，别被"音乐救回来了"这种说法盖过去。
  我们写的是「复杂 Cue + 变体类型 0」，MonoGame 走
  `XactSoundBankSound(soundBank, waveBankIndex, trackIndex)`，它把 `complexSound` 置 false，
  **没有 `XactClip` 也就没有 `loopCount`**，播放时拿到的 `SoundEffectInstance` 是默认不循环的。

  ⇒ **实测影响**：SVE 那 37 条里 `Looped: true` 有 **30** 条、`false` **7** 条。
  `false` 那 7 条碰巧是对的；**`true` 那 30 条会播一遍就停**。

  ⇒ 原版音乐是靠**简单 Cue → sound 记录 → `XactClip` 事件里的 `loopCount`
  （255 = 无限）**循环的。要让新 cue 也循环，得写成那种形态，
  工作落在 XnbConverter 的 `SoundBankReader`（要能写 sound 记录与 `XactClip`），
  这次没做。

- **没有真的进游戏听到声音**。验的是「游戏的加载器认、波形解得出来」这一层；
  实际播放（尤其循环点是否自然）要人进游戏听。
- 只验了 **PCM** 新条目。原版 437 条里 431 条是 MS-ADPCM，往混合库里加 PCM 条目
  游戏是接受的（上面的 `Exists`/`GetCue` 就是在这个混合库上过的），但没验过新加 ADPCM。
- `Data/AudioChanges` 那条通路**没有**用上 —— 本方案里地图的 `Music` 直接引用新 cue 名，
  不需要 `AudioChanges`。SdvCpTool 侧怎么把两者串起来还没做。

## 下一步（还没做）

1. SdvCpTool 侧把 `Data/AudioChanges` 的补丁翻成这组改动：
   ogg 转 PCM wav 放进产出树的 `XACT/Wave Bank/Music/`、在 `.xwb.config` 的 `Entries`
   里追加一条（`Offset: -1`）、在 `.xsb.json` 里追加 `Sounds`/`Variations`
   —— 这三步**正好都是打包器的输入**，不需要改装机流程。
2. ⚠ 需要一个**外部依赖**：ogg→PCM 转码（本机有 `ffmpeg`，但它不该被写死进工具）。
