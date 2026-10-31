# 快速夹具（开发期回归）

全量 RSV 端到端要处理 **1664 条补丁 + 7594 个文件**，约 **12 分钟**。
本夹具压到 **16 条补丁 + 7 个资产**，约 **7 秒**（快约 110 倍）。

## 用法

```bash
python tests/fixture/build.py --sdv-tree D:\sdv_tree --run
```

`--sdv-tree` 默认 `D:\sdv_tree`（星露谷 Content 的解包树）。
生成物落在 `tests/fixture/run/`，**已在 .gitignore 里**。

## 为什么原版资产不入库

夹具需要若干**原版游戏资产**（`Data/Festivals/spring13.json` 等）来充当目标。
它们由 `build.py` **在本地从解包树复制**，不随仓库分发——避免把游戏资源提交进仓库。
仓库里只有我们自己的 `pack/` 与这个脚本（共约 8 KB）。

## 它同时是一张回归覆盖表

每条补丁覆盖一个机制，**含两条负例**。跑完对着下表逐行核：

| # | LogName | 覆盖 | 预期判定 |
|---|---|---|---|
| 01 | 无条件 EditData | 基线 | `ConvertedNoCondition` |
| 02 | GSQ 落点 | `Data/Locations` 的 `Condition` 字段 | `Converted` + `Gsq` |
| 03 | 恒假 HasMod | 常量折叠 | `NotApplicable`（跳过，非失败）|
| 04a | 配置开关 true | 命名空间令牌烘焙 | `ConvertedNoCondition` |
| 04b | 配置开关 false | 同上 | `NotApplicable` |
| 05 | 配偶键改写 | `<NPC>_spouse` | `Converted` + `NativeKey` |
| 06 | 键即条件蕴含 | `Data/mail:spring_13` | `Converted` + `NativeKey` |
| 07a | 基础对白（无条件）| 给 07b 提供假分支原值 | `ConvertedNoCondition` |
| 07b | `$query` 对话 | 对话内联 GSQ | `Converted` + `Gsq`，产物形如 `$query <GSQ>#新\|旧` |
| 08 | Load 新建数据资产 | 新建资产 | `Converted` |
| 09 | Load 新建图片资产 | 新建图片 + `.config` | `Converted` |
| 09b | EditImage 合成 | `Overlay`，目标是 09 刚 Load 出来的图 | `ConvertedNoCondition`（写入，重编码 PNG）|
| 10 | i18n 展开 | 语言后缀产出 | `ConvertedNoCondition` |
| 11 | TextOperations | 文本操作 | `ConvertedNoCondition` |
| **12** | **负例** `{{Random}}` | 不可静态解析的 `FromFile` | **`TargetMissing`（必须拒绝）** |
| **13** | **负例** `PlayerName` | 无 GSQ 等价物 | **`NonTranslatableCondition`（必须拒绝）** |

**基准值**：`total=16 convertible=14 written=12 writeFailed=0`，
`Converted 6 / ConvertedNoCondition 6 / NotApplicable 2 / NonTranslatableCondition 1 / TargetMissing 1`。

## 两条负例是故意的

回归测试最容易的失败模式，是**把该拒绝的也接受了**。
12 号必须判失败（否则我们会烘死一个随机外观），13 号必须判不可翻译（否则会写出语义错误的产物）。

## 已知的夹具依赖

- 07b 依赖 07a 提供「假分支原值」——这是 `$query` 规则（同键恰一条条件补丁 + 至少一条无条件补丁）的要求，
  **删掉 07a 会让 07b 退化为 `NeedsReview`**，那是正确行为。
- 09 依赖树里有带 `Texture2DReader` 的 `.config`（由 `Characters/Grandpa.png` 提供）。
- 09b 依赖 09 先把 `LooseSprites/FixtureIcon.png` Load 出来 —— 它验证的正是
  「同包 Load 的产物对随后的 `EditImage` 可写可见」（写入分两阶段：先 Load，登记新资产、重判，再写编辑补丁）。
