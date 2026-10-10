# 这个目录下的 NVorbis 是**反编译移植**进来的

`NVorbis/` 与 `NVorbis.Ogg/` 两个子目录不是本仓库写的代码，是 **NVorbis** 的产物。

| 项 | 说明 |
|---|---|
| 来源 | 反编译自 `MonoGame.Framework.dll`（MonoGame 把它内嵌在框架里）|
| 工具 | `ilspycmd 10.1`，项目模式（`-p`）|
| 取的版本 | `D:\Stardew.Valley.v1.6.15\Stardew Valley\MonoGame.Framework.dll` |
| 上游许可 | NVorbis 原始项目是 **MIT** |
| 为什么放进来 | 把模组的 `.ogg` 解成 PCM —— XACT 波形库只认 PCM/ADPCM/XMA/WMA，加条目得先解码；引 NuGet 会破坏本仓库的零依赖取向 |

**为什么要用游戏自己那个版本**：1.6 的游戏解 ogg 用的就是这份 NVorbis
（`AudioCueModificationManager` → `OggStreamSoundEffect`），拿同一份实现解码，
解出来的样本才和游戏里播的一致。

⚠ **这是反编译产物，不是上游源码**：变量名、语句结构由 ilspy 还原，与上游仓库的源码
不逐字相同（但当前版本读起来是正常的字段名，不是 `num`/`array` 那种）。
**改这里的代码前先意识到这一点** —— 想对照原始写法时，去查 NVorbis 上游仓库，别把
这里的形态当成"原样"。
