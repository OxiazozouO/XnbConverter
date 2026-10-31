# 受控验证：`Data/Objects` 从「打不出包」到打得出（整数值写成小数记号）

日期：2026-10-12
背景与判据：[`../research/2026-10-08-cp-xnb-value-dialect.md`](../research/2026-10-08-cp-xnb-value-dialect.md) §8
改动：`Validating/ValueDialectCoercion`（原 `StructScalarCoercion`，加了第二条规则）
　　　`tools/gen-game-data-schema.py` + 重新生成的 `GameDataSchema.IntegerFields`（56 个类）

## 一句话

`Data/Objects` 连 5 个语言变体**打不出 XNB**，因为模组给 `int` 字段写了 `0.0`。
`0.0` 与 `0` 是同一个数 ⇒ 按**声明类型**换成整数写法，**不改取值**。改完整树回包的失败
从 25 条降到 **19 条**，且**没有新增**失败。

## 1. 定位（从整树回包的 25 条失败里分出来）

25 条失败分三类，只有第一类是**我们这边能按声明类型解决**的方言问题：

| 类 | 条数 | 原因 | 处置 |
|---|---|---|---|
| `Data/Objects` × 6 | 6 | `Buffs[].IconSpriteIndex` 是 `0.0`，字段声明为 `int` | ✅ 本次修 |
| `Data/Weapons` × 4 | 4 | `Precision: 0.3/0.5`，字段也是 `int` 但**值带小数** | ⛔ 改写法会改取值 ⇒ 只记 |
| `Data/Characters` × 8 | 8 | `EndSlideShow: true`，1.6 里是枚举 | ⛔ `true` 映射到哪个成员无依据 ⇒ 只记 |
| `Mods/**` × 2 ＋ 其余 5 | 7 | 上游/别的类别（缺 `.png`、`ReplaceDelimited` 等） | ⛔ 与本条无关 |

`int` 是**声明事实**：`D:\sdv_gd\StardewValley.GameData.Objects\ObjectBuffData.cs:37`
写着 `public int IconSpriteIndex { get; set; }`；`WeaponData.cs:34` 也是 `public int Precision`。

## 2. A/B（先把「改的是什么」钉死）

只把这几个文件交给打包器（`D:\tmp\packer`，`cwd` = 打包器自己的目录）：

| 输入 | 结果 |
|---|---|
| 改前（`prof79` 的 `Data/Objects{,.zh-CN,.ko-KR}.json` ＋ 边车） | `3 × Input string '0.0' is not a valid integer`，**一个 XNB 都没产出** |
| 改后（同一份数据过一遍转换器） | **无 ERROR，`Objects.xnb` / `Objects.zh-CN.xnb` 产出** ✅ |

⇒ 失败的确是这一个值造成的，不是"顺带的"。

## 3. 全语料受控对比（同参数）

`python tools/convert-mods.py D:\tmp\prof79` → `…\prof80`（232 包，同一份 Release 产物）：

| | 结果 |
|---|---|
| **报告侧**（`tools/compare-runs.py`） | **231 包逐项零差异**；补丁级 变好 0 / 变坏 0 / 新增 0 / 消失 0 ⇒ 这一改**不动任何判定**（只改写出去的写法） |
| **树侧**（`tools/compare-trees-by-hash.py`） | 3.15 万文件里**只有 6 个变了**，全是 `Data/Objects{,.zh-CN,.ko-KR,.ja-JP,.fr-FR,.es-ES}.json` —— 正是那 6 个打不出包的 |
| 那 6 个文件里到底改了什么 | token 级逐条比：**每个文件恰好 35 处 `0.0` → `0`**（共 210 处），**没有别的差异** |

## 4. 改动前后的粒度

那 6 个文件里 `IconSpriteIndex` 共 240 处，其中 **35 处**原来是 `0.0`、改后是 `0` ✓
（其余 205 处本来就是整数写法，一个也没动）。

## 5. 回包（同参数、同一时刻）

只打 `Data/` 这棵子树，改前改后各一遍（`D:\tmp\packer`，`cwd` = 打包器自己的目录）：

| | 成功 | 失败 |
|---|---|---|
| 改前（`prof79` 的 `Data/`） | 1576 | **19** |
| 改后（`prof80` 的 `Data/`） | 1582 | **13** |

- **修好的正是那 6 个**：`Data/Objects{,.es-ES,.fr-FR,.ja-JP,.ko-KR,.zh-CN}.config`；
- **仍失败的 13 个**恰好是 §8.4 记的两类：`Data/Characters*` 9 个（布尔当枚举）＋
  `Data/Weapons*` 4 个（小数写进整数字段）；
- **改后新增失败 0 条** ✓。

**整树闸门这一轮没有单独复测**（`pack-tree.py` 那一趟的日志在后来清缓存时被误删 ✗）。
能确证的是上面那两栏：**受影响的 `Data/` 子树实测 19 → 13**（同一时刻、同一打包器、同参数），
而整树那 25 条失败里**恰好有 6 条就是这 6 个文件**（`prof80` 的整树日志逐条列过）。
★ **后来补测到了**：`prof81` 的整树回包 = **成功 15381 / 失败 19**，与推算一致 ✓。下一段的整树回包会给出 `prof81` 的实测数（见
[`2026-10-12-in-game-first-playable-crash-hunt.md`](2026-10-12-in-game-first-playable-crash-hunt.md)）。

⚠ 一个**踩过的坑**（免得下一个人再踩）：打包器的日志是 **GBK**。按 utf-8 读，
中文正则（`\[ERROR\] 文件名：`）会**一个都匹配不上**，于是脚本报「失败文件 0 个」——
**假绿**。（`pack-check.py`/`pack-tree.py` 里都是按 gbk 读的。）
