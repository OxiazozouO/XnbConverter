# 三版语料合并成一份 + 进游戏实测（SMAPI 4.5.2 / Stardew 1.6.15）

日期：2026-10-12 凌晨
要求（用户原话）：「把三个版本的语料都给我合并到一起去，游戏里的 Mods 最旧、
`D:\Downloads\第二十七版星露谷物语终极版.zip` 中等、刚才那个最新…… 合并之后看看游戏会不会报错先」
+「期间注意不要把 D 盘给搞满了」。

## 0. 三份来源与合并口径

| 来源 | 形态 | mod 数（可解析） |
|---|---|---|
| **已装**（`D:\Steam\...\Stardew Valley\Mods`）| 我们一直在用的语料（旧 ∪ 昨天并进去的最新一部分）| 420 |
| **中版**（`第二十七版星露谷物语终极版.zip`，2.4 GB）| **整包发行版**（8.3 万条目，含安装器/运行库）—— 只抽 `…/3第3步：安装游戏mod/Mods/` 子树（18649 个文件）| 293 |
| **最新**（`Mods.zip`，617 MB）| 字面 Mods 目录 | 196 |

**口径**：同一 `UniqueID` **取版本最高**（版本号按数字段比；平手时优先"已经在 Mods 里"的那份，
避免把 staging 的同一版本再拷一遍）；**已被更新的旧版挪到 `D:\tmp\mods_replaced`**（同盘移动 ⇒ 不额外占空间 ⇒ 可回滚）。
合并后：**493 个 manifest / 491 个唯一 UID / 0 重复 / 0 读不出**。

### 途中修掉的三个真问题（都会让游戏报错）

1. **中版有 3 个 GBK 编码的 manifest**（`[CP] Ace Buildings Expanded Tilesheets`、
   `[FTM] Lnh's Special Farm Cave Forage`、`配偶对阵亡玩家的反应~`）—— SMAPI 只按 UTF-8 读 ⇒ 必报错。
   已重写成 UTF-8（这 3 个包里**只有 manifest 不是 UTF-8**，其余文件都正常 ⇒ 修完就能加载）。
2. **中版把 SMAPI 自带的 `存档备份`/`控制台命令` 又带了一份** ⇒ SMAPI 报「you have multiple copies of this mod installed」。
   已删副本（SMAPI 自己提供这两个 ✓）。
3. **中版整合包在"父目录"上留了 `__folder_managed_by_vortex` 标记**（mod 本体在下一层）
   ⇒ SMAPI 判「it's an empty Vortex folder」**25 条错**。已清掉这些标记（并在收尾时把 2 个只剩壳的父目录也理掉）。

## 1. 进游戏实测（**唯一权威判据**）

```bat
"D:\Stardew.Valley.v1.6.15\Stardew Valley\StardewModdingAPI.exe" ^
    --mods-path "D:\Steam\steamapps\common\Stardew Valley\Mods"
```
（带游戏 exe 的那份才起得来；那份自己的 `Mods` 只有 14 个框架 mod，**没动**。
Steam 那份是"只有 SMAPI + Content + Mods"的便携布局：没有游戏 exe，且自包含运行时目录缺失 ⇒
直接跑它报 `hostpolicy.dll not found`，那是**既有环境问题**。）

| | 第一次跑 | 修完 |
|---|---|---|
| **Loaded** | 213 | **215 mods** |
| **Skipped（ERROR）** | **38** | **8** |

**剩下 8 条全是上游问题**（三份来源里都只有这个版本 ⇒ 不是合并造成的）：
- 4 个 1.5 时代 DLL mod（`宠物增强 0.2.2`、`食谱技能 1.7.5`、`完美锻造 0.0.4`、`智能建筑 1.8.11`，
  manifest 里 `MinimumApiVersion: 3.0.0` ⇒ SMAPI 4 拒收「no longer compatible」）；
- 其中 2 个连带的 CP 半（`it needs the … mod, which could not be loaded`）；
- `资源数值背包 1.1.0`：`initialization failed`（与新版 `ResourceStorage` 的版本组合冲突）；
- `自动抓取松果 淘宝橘子正版修改器 1.1.0`：`its DLL couldn't be loaded`。

其余只有 **8 行 WARN**（Nexus 更新检查：中版那几个包 manifest 里的 `UpdateKeys` 是坏的 `???`）——不影响加载。

## 2. 对我们这边的影响（**语料变了**）

- `Mods` 从 276 → **493 个 manifest / 491 UID**，而且**目录结构变了**（中版把 mod 套在父目录里、
  又经这次合并提到顶层）⇒ **之前的 `prof*` 基准全部作废**（换语料期间的跑批已停）。
- 下次语料轮的基准要**重跑**；`_not-converted.txt` 的"读不出 manifest"那一段现在会报 0（修好的旁证）。

## 3. 跑批期间别动 Mods（又一次验证）

`prof54` 那一轮就是在合并 Mods 期间跑的 ⇒ 输入前后不是一棵树 ⇒ **整轮作废**（已删）。
这条教训 `tools/README.md` 里已写，这次是活例。
