# 反编译依据：`D:\sdv_src` 这些路径是什么、怎么重建

`docs/research/` 下有多篇文档（约 80 处）拿下面这些路径当**源码依据**，有的还带行号
（如 `sdv_src/StardewValley/GameStateQuery.cs:247`）：

```
D:\sdv_src        星露谷 1.6 主程序反编译（逻辑调用点的权威）
D:\sdv_gd         StardewValley.GameData.* 数据模型反编译（模型类**不在**主程序里）
D:\rsv_src        Ridgeside Village 反编译
D:\tmp\cp_decompile   Content Patcher 2.9.1 反编译
```

⚠ 这几个目录**一度被当临时产物清掉过，现已按上面的命令重建并保留**
（`D:\tmp\cp_decompile` 已加进 `tools/clean-cache.js` 的 KEEP）。
行号引用保留着才有可追溯性，所以不逐条去改文档。

| 目录 | 重建后 | 位置 |
|---|---|---|
| `D:\sdv_src` | 948 个 `.cs`（14 MB）| D 盘根，不在清理范围 |
| `D:\sdv_gd` | 168 个 `.cs` | 同上 |
| `D:\tmp\cp_decompile` | 278 个 `.cs` | **在清理范围内 ⇒ 已登记进 KEEP** |
| `D:\rsv_src` | 未重建 —— 本机 Mods 下没找到 Ridgeside Village（**未确证**它是哪个目录）| — |

## 怎么重建

用的是 `ilspycmd`（`dotnet tool install -g ilspycmd`，本机 10.1 版在
`C:\Users\jiang\.dotnet\tools\ilspycmd.exe`）。

```bat
:: ① 星露谷主程序 —— ⚠ 文件名**带空格**，是 "Stardew Valley.dll" 不是 "StardewValley.dll"
ilspycmd -p -o D:\sdv_src "D:\Stardew.Valley.v1.6.15\Stardew Valley\Stardew Valley.dll"

:: ② GameData 数据模型（独立的一个程序集）
ilspycmd -p -o D:\sdv_gd "D:\Stardew.Valley.v1.6.15\Stardew Valley\StardewValley.GameData.dll"

:: ③ Content Patcher（模组目录名带装饰，认 ContentPatcher.dll 就行）
ilspycmd -p -o D:\tmp\cp_decompile ^
  "D:\Steam\steamapps\common\Stardew Valley\Mods\前置-------内容修补器-----------------------1915\ContentPatcher.dll"

:: ④ Ridgeside Village
ilspycmd -t RidgesideVillage "<RSV 模组目录>\RidgesideVillage.dll" > D:\rsv_src\RidgesideVillage.decompiled.cs
```

`-p` 是项目模式（每个类型一个 `.cs`，带目录结构）；单个类型用 `-t`。
反编译 MonoGame 时注意：**`MonoGame.Framework.dll` 里内嵌了 NVorbis**
（游戏解 ogg 用的就是它，已移植进 `XnbConverter.Core/Audio/`，见那边的 `Vorbis.NOTICE.md`）。

## 那些行号还准吗 —— 抽查过，准

2026-10-10 重建之后抽了三条验（同一个 DLL ⇒ 行号应当复现）：

| 文档引用 | 实际那一行 |
|---|---|
| `GameLocation.cs:17293` | `if (!Game1.eventUp && HasMapPropertyWithValue(GetSeason().ToString() + "_Objects"))` —— 正是「按季节刷物件」|
| `GameLocation.cs:10566` | `hasCompletedCommunityCenter() && !mailReceived.Contains("JojaMember")` —— 与 `_Billed`/`_NotBilled` 那条邮件条件吻合 |
| `Farmer.cs:5908` | `mail + "_" + (Billed/NotBilled) + "_" + Male/Female` —— 正是「晕倒邮件变体」|

⇒ 文档里带行号的引用可以当依据用。⚠ 但**换 DLL 版本**（游戏更新）行号就会飘，
那时以**文件名 + 方法名**为准去搜，别死认行号。

## 一处容易踩的坑

按 `StardewValley.dll`（无空格）去找**找不到** —— 实际文件名是 `Stardew Valley.dll`。
2026-10-10 清理时我差点因此误判"主 DLL 不在了"。
