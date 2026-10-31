# 第一次实机验证：把产出 XNB 装进游戏，启动看报错

日期：2026-10-09

**为什么要做**：仓库里所有验证（回包、确定性、自检）都只到「打包器/反序列化器认不认」这一层。
`2026-10-06-grand-total.md` §8 一直挂着这句：「**「能被游戏读到」这一层没有实机验证**」。
这是第一次把产出真的塞进游戏。

## 做法（**没碰任何受约束目录**）

| 步骤 | 具体 |
|---|---|
| 副本 | `D:\Stardew.Valley.v1.6.15\Stardew Valley\`（773 MB）**整份复制**到 `D:\tmp\gv\`，改的是副本 |
| 启动器 | **原版 `Stardew Valley.exe`**，**不走 SMAPI** —— 走 SMAPI 的话那 32 个 CP 包会重新施加补丁，就验证不到我们的 XNB 了 |
| 装进去的东西 | 「女巫沼泽扩展」产出树里**变更过的 54 个文件**（20 个改过的原版资产 + 17 个新资产）打包成 XNB，**37 个** |

⚠ 打包这一步本身踩了坑（打包器**静默跳过**没有 `.config` 边车的文件，
摘要却显示「失败: 0」）—— 详见 `research/2026-10-09-packer-silent-skips.md`。

## 结果

```
Stardew Valley.exe   PID 4968   稳定在 828 MB
%APPDATA%\StardewValley\ErrorLogs\   与启动前快照逐文件相同 —— 没有任何新增
```

**启动到主菜单，零报错。**（作为对照：该目录里还留着用户 2025-07 的一份日志，
内容是 `Could not load Maps\panorama asset!` —— 游戏**确实会**把加载失败写在这里，
所以「没有新文件」是有意义的负结果，不是「没检查」。）

## 这次**覆盖到了**什么

主菜单会加载的那批：

- `LooseSprites/*` —— 含我们改过的 `LooseSprites/AquariumFish.xnb`
- `Characters/*` —— 含新写的 `Characters/Aimon111.WitchSwampOverhaulCP_GoblinZic*`
- `Portraits/*`
- 部分 `Data/*`

⇒ 「我们写出来的 XNB，游戏的加载器认」这一条，**图片类与新建资产类**已经有了实机证据。

## 这次**没覆盖到**什么（**不要**当成已验证）

进游戏才加载的那批，全部**没验证**：

- `Data/Locations`、`Data/Objects`、`Data/Crops`、`Data/Fish` … 这些 `EditData` 的合并结果
- `Data/Events/{WitchSwamp,WitchHut,Blacksmith}` —— **这次改动的主要产出**（带原生 GSQ 前置条件的 12 个事件键）
- `Maps/*.tbin`（`Maps/WitchSwamp`、`Maps/Railroad`、`Maps/WitchWarpCave`）
- `Strings/StringsFromMaps`

**为什么没覆盖**：主菜单不碰它们，而**自动进游戏做不到** ——
模拟按键（`SendKeys` 一类）对 MonoGame/SDL 的全屏窗口无效（它读原始输入），
游戏本身也**没有**命令行参数可以直接加载存档
（`StardewValley/Program.cs` 的 `Main(string[] args)` 根本不使用 `args`，逐行看过）。

⇒ 要覆盖那批，只能在窗口里**手动**点进游戏。

## 状态

游戏副本**还开着**（PID 4968，主菜单），可以直接点进去把剩下那批也跑到。
副本在 `D:\tmp\gv\`（约 800 MB），验证完整棵删掉即可。
