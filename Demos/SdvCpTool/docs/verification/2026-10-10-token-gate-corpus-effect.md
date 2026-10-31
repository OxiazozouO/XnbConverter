# 「逐条取舍」修复的全语料效果 + 一套自检入口

日期：2026-10-10

修复本身见 [`research/2026-10-10-token-gate-is-per-entry-not-per-patch.md`](../research/2026-10-10-token-gate-is-per-entry-not-per-patch.md)
（一条条目带 `{{FarmName}}` 就让整条补丁不写 ⇒ 地图上的字符串显示成裸键）。

## 一、改前 / 改后逐字节对比（`tools/diff-trees.py`，同参数：全量 32 包、同一顺序）

| | 改前 `chain3` | 改后 `chain5` |
|---|---|---|
| 文件数 | 13,335 | **13,471** |

```
只有旧树有：0        只有新树有：136        内容不同：122
```

- **只有旧树有 0** —— 只多不少；
- 只有新树有 **136** 个 = 之前**整个资产都不存在**、现在建出来的
  （`Characters/Dialogue/Alissa`、`Bert`… 这些 NPC 的对话资产以前一条都写不进去）；
- 内容不同 **122** 个 = 既有资产**补回了被整条丢掉的条目**。

## 二、一套自检入口：`tools/verify-tree.py`

以前每次要手工跑七八个脚本，现在一条命令：

```
python tools/verify-tree.py <产出树> <基准树>
```

在**改后那棵全量树**上的结果：

| 检查项 | 产出相对基准的「新增问题数」 |
|---|---|
| 每条 `Warp` 的目标地点 | **0** |
| 地点的 `CreateOnLoad.MapPath` | **0** |
| 每张地图引用的图集图片 | **0** |
| 季节性图集变体 | **0** |
| 语料 tmx 的 tileset 图片 | 4（已知，见下）|
| **地图 `Message` 的文本** | **2**（改前 **557**）|
| `LocalizedText` 引用 | 1（已知，见下）|

### 地图 Message：557 → 2

这就是用户截图那个症状的**全语料规模**，一次修复从 557 种掉到 2 种。
剩下 2 种（`Town.2`、`SeedShop.11`）**SVE 自己就没有这两个键**（它的 `Strings.json` 里查不到）。

### 另外那 4 + 1 处，都不是我们的

- **语料 tmx 图集 4 处**：`spirit_sewer`（用它那个 tmx 的补丁带 `When` 被拒 ⇒ 树里没有地图引用它）
  与 `spring_daisyextras`（由**没装**的 `DaisyNiko.SVR3` 提供）；
- **`LocalizedText` 1 处**：`Strings/1_6_Strings:HeavyFurnace_pieces` ——
  原版那份也只有 `HeavyFurnace_coal`/`_ores`、**没有 `_pieces`**，我们那份与原版逐键相同
  ⇒ 是**内容包自己写错了键**。

## 三、单元测试

**1202 通过 / 0 失败**（这一轮修复新增 4 条：`EditDataMerger` 的两条逐条取舍 +
`WritePolicy` 的"全坏仍要拦" + 令牌闸那条改造）。

## 四、回包回归检查：**零回归**

"逐条取舍"多写出来的 68 个新资产，打包器能不能吃 —— 拿改前/改后各打一次比：

| | 成功 | 失败 |
|---|---|---|
| 改前（`content3`）| 6404 | 16 |
| 改后（`content5`）| **6472** | **16** |

```
失败清单逐条对比：新增失败 0 种，不再失败 0 种   （两边完全一样）
```

- **+68 = 新增的那 68 个资产全部打出来了** ✓；
- 失败的还是原来那 16 个（`Data/{Buildings,FarmAnimals,Fences,Machines,Pets}` 及其语言变体）。
  ⚠ **这一条已于 2026-10-10 晚订正：那 16 个今天打得出**（5 个资产单独打 5/0、
  原版 `Data` 851/0、产出树 `Data` 982/0、整树定点回包 3486/0）——
  见 `../research/2026-10-09-five-data-assets-cannot-be-packed.md` 顶部的订正。

⇒ 这次修复**只加不坏**：树里多出来的东西全都落了地，失败面一点没扩大。

## 五、最终实机：**零报错**

把 `content5` 覆盖式装进游戏副本（原版 Content + 我们的 6477 个 xnb）再跑探针：

```
======== 地图创建：成功 445 / 失败 459 ========
```

- 安装里 `Maps/` 共 904 个资产 = **445 张真地图 + 459 张图片** ⇒
  **成功 445 = 真地图 445、失败 459 = 图片 459**，一一对应，**没有一张真地图建不出来**；
- 整份日志的**非 TRACE 告警只剩一条 Steam 凭据噪声**（`Galaxy auth failure`）——
  **没有任何 `ERROR game`**（上一轮那条 `Cornucopia_SeaSalt` 纹理告警**这次也没出现**，
  与"它是瞬时问题、不是产出缺东西"的判断一致）。

⇒ 当前代码的全量产出，在实机上的状态是：**地图全建得起来 + 零报错**。

## 六、报告里**内置审计**那两处（共 6 条）也逐条看过了 —— 都是**包侧**

`report.json` 里除了判定，还带内置审计段。最终树上：

| 段 | 结果 |
|---|---|
| `unexpandedTokens` | 0 有害 |
| `keyCase` | 82 个资产，0 冲突 |
| `conditionPlacement` | 2763 个资产，0 错位 |
| `outputShape` | 2763 个资产，0 数组变对象 |
| `gameData` | 91 个资产，**3 处形状不符** |
| `spriteSheet` | 3 个资产 / 1758 条，**1 处格宽 + 2 处引用不存在的贴图** |

> **订正（2026-10-11 晚）**：上面 `conditionPlacement` / `outputShape` 那两行的 **0 是"没比"** ——
> 链式跑法 `--tree <同一棵> --out <同一棵> --append` 让这两项自检拿**产出跟它自己比**，
> 形状当然一样。逐包对原版树跑时，SVE 那份实际报 **1 处**
> （`Data/WorldMap.json` 的 `MapAreas[]/KnownCondition`，SVE 自己写错的位置）。
> 现在链式跑法会置 `skippedSameTree: true` 并在报告里写"跳过"。
> 见 `2026-10-09-self-audit-findings-reviewed.md` §6.3。
> 其余四行（`unexpandedTokens` / `keyCase` / `gameData` / `spriteSheet`）**不跟输入树对照**，
> 那些结论仍然成立。

### `StatToIncrementOnProduce`（3 处，Camel/Goose/Bear）

SVE 的 `code/Other/FarmAnimals.json` **自己写的就是空字符串**：

```json
"StatToIncrementOnProduce": ""
```

而 1.6 的类型是**列表**（原版长这样：`[{"Id":"ChickenEggsLayed",…}]`）。
**我们逐字照抄** ⇒ 形状不符来自**包**，不是我们改的。

### `GuntherSilvian_Beach`（2 处）

SVE 的季节性服装包对 `Portraits/GuntherSilvian_Beach, Characters/GuntherSilvian_Beach`
只做 **`EditImage`**，**全包搜不到任何 `Load`**（`grep -rl` 全 SVE 目录只命中那一个文件）。
⇒ **它改了一个自己从不创建的资产**；CP 在运行时会记同样的错。
（`Portraits/GuntherSilvian.png` 本身在产出里 ✓，缺的只是 `_Beach` 变体。）

### `Birch_Water` 的格宽（1 处）

SVE 那张贴图是 **15×16**，不是 16 的整数倍 —— 我的 `check-object-sprites.py` 第一版
把它误判成越界，改成**向上取整**后不再报；内置审计仍会记一条（它的判据更严）。
**游戏里它是正常显示的**（SVE 大量在用），所以这条是**提示**不是错误。

⇒ **6 条全在包侧，没有一条是我们的产出写错。** 另外：内置的 `spriteSheet` 审计比
我那个脚本**更深一层**（它会进 `Appearance[].Sprite` 这种嵌套字段），两者互补。
