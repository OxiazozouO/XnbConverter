# 合并新语料（Mods.zip）后的全量：122 包 / 21185 条

日期：2026-10-11 晚 → 12 日凌晨
起因：用户把 `D:\Downloads\Mods.zip`（617 MB、156 个顶层文件夹、198 个 mod）并进游戏 `Mods`，
要求**拿它当新语料测**。

> ⚠ **本文的第一版是在"不完整语料"上写的**（85 包 / 10963 条）—— 当时还没发现 §2 那个
> `manifest.json` 严格解析的 bug（**约一万条补丁被静默丢掉**）。下面 §3 起是修好后的**完整语料**数字。

## 0. 语料怎么合的（**不覆盖已装**）

- 目标目录：**105 → 276 个 manifest**，**0 个重复 UID**（体检过 ✓）；
- 已装过 UID 的 **35 个顶层文件夹整份跳过**（其中 13 个是**旧版**：`ContentPatcher 2.3.0→2.9.1`、
  `FarmTypeManager 1.23→1.26.1`、`SVE Code 1.15.8→1.15.11`、`ItemExtensions 1.9.1→1.16.0`…）——
  **保守：不动已装的那份**（要升级得单独说）；
- 新装 **135 个顶层文件夹**；同名不同 UID 的（`ConsoleCommands`/`SaveBackup`）UID 已装 ⇒ 归到跳过。
- ⚠ 合并脚本第一版用严格的 `json.load` 读 manifest ⇒ 带注释/尾逗号的 manifest 解析失败被当成
  "这夹里没有 manifest"而整份跳过 —— `Ridgeside Village`、`SpaceCore`、`GenericModConfigMenu` 等
  **28 个顶层文件夹**因此漏了；改用仓库自带的 `tools/jsonc.py` 补齐 ✓（并对已复制的那批做了 UID 重复体检：0 ✓）。

## 1. 成本（回答"是不是性能退化"）

每包 **平均 2.4–3.1 s**，末尾仍是 1–2 s/包，**没有随树增长的趋势**；122 包整轮约 8 分钟。
（"约一小时"是我拿 `run-packs.py` 的每包 20–40 s 外推的 —— 那段时间几乎全花在**每包重拷 600 MB 原版树**上。）

## 2. ★ 修掉"约一万条补丁被静默丢掉"：**manifest 的严格解析**

`tools/cppacks.py` 与 `tools/convert-mods.py` 都用严格 `json.load` 读 `manifest.json`，
解析失败直接 `continue` = **整包静默消失**。实测：223 个 manifest 严格可读、**53 个只有容错解析能读**
（带注释/尾逗号），其中约 **30 个 CP 包 + 5 个 FTM 包**从没被转过 ——
`Ridgeside Village CP`、`RSV 季节性服装`、`Sunberry Village`、`Grandpa's Farm`、`East Scarp Core/NPCs`、
`[CP] Lunna`、`Cape Stardew`、`Lumisteria Tilesheets`、`Rodney O'Brien`、`Integrated Minecarts`…
**而且"没转清单"里也没有它们**（发现阶段就被滤掉了 —— 连"不藏东西"那道保险都一起失效了）。

修法：manifest 一律走 `jsonc.load` ✓；新增 `cppacks.unparseable_manifests()` —— **两边都读不出来**的
会列进 `_not-converted.txt`（"读不出来就是没转"，不许不说）。
修后：CP 包 **74 → 104**、FTM **13 → 18**；整轮从 **85 包 / 10963 条**变成 **122 包 / 21185 条**：

```
written 6063 → 12018（+5955）｜ Converted+ConvertedNoCondition 5698 → 11003（+5305）
```

⚠ 同一批还修了个"整轮跑崩"：包名里可能有 Windows 非法字符（实测 `East Scarp: Locations` 带**冒号**）
⇒ `makedirs` 抛 `NotADirectoryError`，把整轮崩在**第 71 个包**上。现在目录名统一净化。

## 3. 完整语料的判定分布（`prof52`，122 包 / 21185 条）

```
ConvertedNoCondition  6565 ｜ Converted  4438 ｜ NotApplicable 2455 ｜ NoLandingPoint 2377
NeedsReview           2061 ｜ LocalEdit  1194 ｜ CreatesNewAsset 1040 ｜ TargetMissing  515
Emitted                455 ｜ TargetContainsTokens 54 ｜ NonTranslatableCondition 16 ｜ NeedsRuntime 15
attemptedWrite 13007 / written 12018 / **writeFailed 199**
```

### 3.1 先修掉的那一类：**内容包没有 i18n 不等于失败**（229 条）

对着 CP/SMAPI 核过：`TranslationValueProvider.GetValues` 走 SMAPI 的 `TranslationHelper.Get(key)`，
**没有任何译文**时给的是占位符 **`(no translation:key)`**（SMAPI `Translation.PlaceholderText`），
CP **照常把资产写出去**。我们原先判整条失败 ⇒ **写少了**。
去掉那道 `Fail`、交给既有的缺键优雅降级路径（`translations?.GetValue` ⇒ `|defaultKeys=` ⇒ `|default=`
⇒ 占位符）后：`writeFailed 369 → 137`、`written +232`、**判定分布逐项相同**。

### 3.2 剩下 199 条的构成

| 类 | 条数 | 性质 |
|---|---|---|
| 外观组未发射：`X` 不在 `Data/Characters` 里 | 92 | 见 §4（**候选**：这些包的变体名由**别的包**注册）|
| 烘焙求值失败：命名空间令牌取不到（mod 的 `config.json` 里没有那个键 / 没有 config.json）| 34 | 该拒：运行时才知道，**不猜** |
| 外观组未发射：`When` 里的 `IsOutdoors` 不是布尔 | 24 | 该拒：`Appearance[]` 只吃 `true/false` |
| 烘焙求值失败：`Relationship` 等**不是可烘焙令牌** | 17 | 该拒：通道 C 只管 HasMod/ModId/HasFile/配置令牌/命名空间令牌 |
| 来源 tmx 只支持内嵌单个 `<image>` 的 tileset | 16 | **能力缺口**（见 §5）|
| 对话值内联 GSQ 失败（取不到旧值 / 值含 `|`）| 4 | 老面孔 |
| 其余 | ~12 | — |

## 4. 那 92 条"外观组"的真相（**未确证的部分要说清**）

例：`FadeHeim's East Scarp Seasonal Anime Portraits`（包目录名居然叫 `1.3.4`）：

```json
{ "For": { "Season": "{{range: SeasonList}}" },
  "Action": "EditImage", "Target": "Portraits/Aideen_{{Season}}",
  "Priority": "Late", "When": { "HasFile:assets/Aideen/Aideen_{{Season}}.png": true, "AnimeAideen": "True" } }
```

- 我们的令牌枚举把 `{{Season}}` 展开成 4 条 ✓，目标成了 `Portraits/Aideen_spring` 这类**变体资产名** ✓；
- 这些名字**不是 NPC 名**（NPC 是 `Aideen`）⇒ 外观发射器判"NPC 不在 `Data/Characters` 里" ⇒ 整组不发射；
- 而**真正的消费方是别的包**：East Scarp 的 NPC 数据在 `Data/Characters[Aideen].Appearance[]` 里引用
  `Portraits/Aideen_{{Season}}`（核过：修好 manifest 后 `Data/Characters[Aideen]`/`[Lunna]` **已进产出树** ✓）。
  ⇒ 我们该做的是**把这些图按新资产写出去**（游戏只在注册的条件成立时读它 ⇒ 多写不算错），
  **不该**自己再往 `Appearance[]` 里注册一遍。
- ⚠ 但那条补丁用了 **`For`** 字段，而**反编译的 CP 2.9.1 里没有这个字段**（`PatchConfig` 全字段逐个数过），
  本机网络受限、查不了官方文档 ⇒ **`For` 的确切语义与引入版本：未确证**。所以这一档
  **只记账、先不改**（改法清楚：给发射器加一种"只写图、不注册"的模式）。

## 5. 能力缺口（候选，未做）

**tmx 的外部 `.tsx` / 图集集合**（16 条）：现在的 tmx→tbin 只吃「内嵌单个 `<image>` 的 tileset」。
`Crystal Island` 这类把 tileset 拆成外部 `.tsx` 或集合 ⇒ 整条地图不写。修法清楚（读 tsx 再拼）。

## 6. 仍然"不是我们的问题"（复核过）

- `spriteSheet.outOfBounds 1`：`DN.SnS_DeepDarkChest` 的**源 PNG 就是 96×31**（BigCraftable 要 16×32 的
  整数倍）⇒ 逐字节照搬，CP 那边同样错位；
- `gameData.shapeMismatch 12`：SVE 的 3 只动物（Camel/Goose/Bear）× 4 个语言变体（空串当 `List`，已确证无害）；
- `keyCase` 0 冲突、`unexpandedTokens` 0 有害。

## 7. 待办 / 未确证

- `For` 的语义（§4）—— 网络受限，**未确证**；
- 那 13 个**旧版框架**没升级（保守选择）；
- 全树回包（打包器）这一轮**没跑**（上一轮结论是整树 6516/0；树现在大了一倍，要重跑才算数）；
- 实机探针（`probe_maps`）仍未跑（游戏那份 MonoGame 是 x86）。
---

## 8. 三版合并之后的**新基准**（`prof60` / `prof61`，232 包 / 32771 条）

三个来源合成一份（详见 `../verification/2026-10-12-three-way-mods-merge-and-game-check.md`）之后重跑：

```
包 232（CP 206 + FTM 26）｜ 补丁 32771 ｜ written 15043 / writeFailed 234
NotApplicable 9790 ｜ ConvertedNoCondition 8301 ｜ Converted 5633 ｜ NoLandingPoint 2949
NeedsReview 2464 ｜ LocalEdit 1348 ｜ CreatesNewAsset 1148 ｜ TargetMissing 583 ｜ Emitted 452 …
```

写入失败 234 条的构成（与上一版同族，量翻倍）：
外观组 114（80「NPC 不在 `Data/Characters`」+ 24「`When` 里的 `IsOutdoors` 不是布尔」+ 10「条件翻不成 GSQ」）、
烘焙求值失败 44、tmx 外部 `.tsx` 16、对话值内联 10+ …

### 8.1 ★ `For` —— **我们没实现的 CP 补丁字段**（本语料 94 条卡在这里，未确证其语义）

`206_FadeHeim's East Scarp Seas`（包目录名居然是 `1.3.4`）的立绘补丁：

```json
{ "For": { "Season": "{{range: SeasonList}}" },
  "Action": "EditImage", "Target": "Portraits/Aideen_{{Season}}",
  "Priority": "Late", "When": { "HasFile:assets/Aideen/Aideen_{{Season}}.png": true, "AnimeAideen": "True" } }
```

- **字段守卫一上线就把根因说出来了**（`解析警告：…补丁里有**我们不认识的字段** `For` —— 它没被处理…要么是我们漏读了 CP 新加的字段`）——
  这正是那个守卫存在的意义：**不认识的字段必须吵出来**，否则就会像这次一样，问题被误读成"NPC 不在 `Data/Characters` 里"。
- 现状：**目标名被我们拆成了四季**（`Portraits/Aideen_spring` 等，说明令牌枚举那条路认到了它 ✓），
  但 **`When` 里的同一个令牌没人换** ⇒ `HasFile` 的"文件在不在"判不了 ⇒ 整条退回 `NeedsReview`，
  理由还写着「需提供 `--mods` 才能求值」（而 `--mods` 明明给了）。
- ⚠ **`For` 的确切语义与引入版本：未确证** —— 反编译的 CP **2.9.1** 的 `PatchConfig` 里逐字段数过、**没有** `For`；
  本机网络受限（拉不了 CP 文档）⇒ **不猜着实现**。
- 本轮试过并**保留**的两处（都**语料零影响**、都有测试）：
  ① 枚举拆分时对**整条补丁**做深度令牌替换（含 `When` 的键与值）；② 枚举的候选探测也扫 `When`。
  —— 它们是对的（同一块的值理应贯穿全条），但**不是这条的解法**（这条卡在 `For` 上）。
- 若将来能核实 `For`：修法是「按 `For` 的每个值复制补丁 + 把该令牌贯穿全条」，
  与 `TokenEnumerationExpander` 现有的拆分逻辑同构 —— 等证据。
