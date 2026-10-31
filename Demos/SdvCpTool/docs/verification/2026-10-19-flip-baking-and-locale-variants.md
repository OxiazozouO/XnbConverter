# 翻转/旋转烘焙 + 语言变体（主界面、季节图块、中文字符串）

日期：2026-10-19
起因：业主实机反馈两件事：

1. 原版地图里对图块做了**旋转/翻转**，转成 tbin 后完全不对；
2. 有些图片没有正常替换（**主界面、季节图块**），并且界面语言是中文时，
   对话/界面提示仍然全是英文。

## 1. 翻转/旋转：为什么 SMAPI 行、纯 XNB 不行

- Tiled 把翻转/旋转编码在 GID 的高 3 位（H/V/D）。
- **xTile 自己完全不认**：tbin 每个图块只存 `TileIndex` + `BlendMode`，渲染时直接按索引取格子
  （`xTile.dll` 反编译里 `Flip`/`Rotation` 0 次出现）。
- **SMAPI 能正确显示**，是因为它用 `SDisplayDevice` 替换了渲染器，读取 TMXTile 在转换 TMX 时
  写下的 `@Rotation` / `@Flip` 图块属性，按角度 + `SpriteEffects` 画
  （`StardewModdingAPI.Framework.Rendering.SDisplayDevice`）。
- 纯 XNB 没有 SMAPI ⇒ 只能把变换后的像素**烘进图集**。

语料规模：**667 个 TMX 含翻转，共 50,687 个翻转图块**；实际参与转换约 **35,864 个**。

## 2. 修法：把变换烘成图集里的新格

- `TmxToTbin` 不再丢弃翻转位，按 TMXTile 的公式换算成 `(rotation, effect)`，写在图块的
  临时属性 `@SdvCpFlip` 上（动画块挂在 `AnimatedTile` 上）。
- 新增 `FlipBaker`，在**地图落盘前**逐块处理：
  - 取原格的像素，按 SMAPI 相同语义做旋转/翻转（`rotation` 绕格中心 + `SpriteEffects`）；
  - 把结果**追加到图集末尾**，得到新 `TileIndex`；图片不够高就扩行；
  - 地图图块改指新索引；
  - 逐块属性（`@TileIndex@旧@Passable` 等）复制到新索引；
  - **季节变体**：`spring_/summer_/fall_/winter_` 同一组图在**相同位次**各追加一格
    （各用自己那季的像素变换）；
  - **语言变体**：`<图集>.zh-CN.png` 等也要追加同一个索引（中文存档加载的是它）；
  - 动画块逐帧处理；最后删除临时标记。
- `MapCache` 在并行写图之前**串行**做烘焙（要改图集像素，不能并发）。

## 3. 主界面/季节图块没替换：语言变体

- 游戏的 `LocalizedContentManager` 对**所有资产**都是"先要 `asset.<语言>`、没有再回落基础资产"。
  中文存档加载的是 `Minigames/TitleButtons.zh-CN`、`Maps/spring_outdoorsTileSheet.zh-CN`。
- 我们以前只改基础资产 ⇒ 中文玩家看到的还是原版图。
- 修法：
  - `ImageApplier`（`Load`）与 `EditImageApplier`（`EditImage`）把同一条操作**镜像到已存在的
    所有语言变体**；
  - `FlipBaker` 的追加位次也覆盖语言变体；
  - 新增 `Workspace/LocaleVariants.cs` 统一语言后缀表。

## 4. 界面还是英文：三个独立原因

### 4.1 `Language` 令牌没确定化

- 界面包用 `When: { "Language |contains=zh": true }` 分开中英文主界面图；
  判不了 ⇒ 中文变体永远不生效。
- 修法：`Language` 与 `Season`/`FarmType` 同一条**确定化**政策，冻结到 **`zh`**
  （启动配置 `languageCode=zh`）。
- `Target`/`FromFile` 里的 `{{Language}}` 也代成 `zh`
  （实测 `[CP]猫露谷物语(Interface)` 的 `FromFile: assets/{{Target}}_{{language}}.png`
  以前判 `TargetMissing`，中文图整批不写）。

### 4.2 语言变体被英文基础资产整份覆盖

- `TranslationApplier` 以前只要有 `{{i18n}}`，就用**英文基础资产整份**重建语言变体。
  Cape Stardew / Crystal Island 等包只加一个 `{{i18n}}` 条目，就让
  `Strings/StringsFromCSFiles.zh-CN` 从 **1650 条中文**变成 **15 条中文**（其余全英文）。
- 修法：已有本地化文件时，变体以它为底稿，**只覆盖本补丁 i18n 点所在的顶层键**。

### 4.3 图片语言变体没镜像（见 §3）

### 4.4 模组对话/事件在中文下不出现：数据语言变体没镜像

- `Data/Events/*.zh-CN`、`Strings/*.zh-CN`、`Characters/Dialogue/*.zh-CN` 都是**独立资产**；
  中文存档加载它们，不会回落基础资产。
- 我们以前只改基础资产 ⇒ 模组加的事件/对话（没有 `{{i18n}}` 的那些）在中文下**根本不出现**。
- 修法：每条 `EditData` 补丁把**自己改到的顶层键**镜像到已存在的 `<asset>.zh-CN`；
  键从**补丁结构**（`Entries`/`Fields`/`MoveEntries`/`TargetField`/`TextOperations`）取，
  **不依赖补丁前快照** —— 原地改（`takeOwnership`）会把暂存里的对象一起改掉，快照读到的
  已经是补丁后的内容（实测 Cape Stardew 的 `14635122/SawEvent` 因此没进中文事件表）。
- 条件落点会**改写键**（`146351222` → `146351222/G …`）：按「原始键」或「原始键 + `/`」前缀
  解析成最终键，否则会把改写后的键当成删除。
- 已知边界：`Entries` 里**删除**条目的意图不会同步到语言变体（宁可留旧条目，不误删）。

## 5. 验证

- 单测：**1542/1542**。
- 定向转换（界面包 + CapeStardew）：
  - `Strings/StringsFromCSFiles.zh-CN`：**1651 条中文**（修前 15）；
  - `Minigames/TitleButtons.png` 与 `TitleButtons.zh-CN.png` 字节一致（中文主界面图已落）；
  - 树上**无 `@SdvCpFlip` 残留**；**170 张图集**被扩展（含各自的 `.zh-CN` 变体）。
- 全量 A/B（fix23 → fix24）：见下节。

## 6. 全量 A/B（fix23 → fix26）

> 同参数、全语料 232 包。中间 fix24/fix25 是调试轮（fix24 还暴露并修掉了一个
> `MoveEntries.From/To` 为数组时的 `JArray → JToken` 强转异常，80 个包整条写入失败；
> fix25 又补了「本包没有中文 i18n 时也要把新增键写进 zh 变体」）。

- 产出树：**26501 → 26501 个文件**，新增 **0**、消失 **0**、内容变化 **1764**。
- 写入异常：**0**（fix24 曾有 80 个包）；补丁总数/写入失败 **32762 / 630**（与 fix23 相同）。
- 判定分布：`TargetMissing` **269 → 202**（`{{Language}}` 代掉后目标可解析）、
  `ConvertedNoCondition` **9049 → 9114**（`Language |contains=zh` 等条件确定化）、
  `NotApplicable` **10875 → 10893**（其它语言变体不再误写）。
- 翻转：1251 张地图里 **0 个 `@SdvCpFlip` 残留**；**249 张图集**被扩展
  （其中 **184 张是语言变体**，如 `spring_outdoorsTileSheet.zh-CN`）。
- 本地化：
  - `Strings/StringsFromCSFiles.zh-CN`：**1695 / 1718 条中文**（fix23 只有 15 条中文）；
    仅缺的 2 条是**原版中文包自己就缺**的键（`OptionsPage.cs.11239/11275.console`）。
  - `Data/Events/Farm.zh-CN` 与基础资产**键集合一致**（含模组新事件）；
    原版事件保留中文，模组新增事件按默认语言写入。
  - `Minigames/TitleButtons.zh-CN` 已替换为界面包的中文主界面图（与基础资产字节一致）。
- 越界传送审计不变：`checked=4465 out-of-bounds=46`（与 fix23 相同，本次未触碰地图传送）。
