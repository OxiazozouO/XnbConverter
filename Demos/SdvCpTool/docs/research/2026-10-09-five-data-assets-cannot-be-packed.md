# 已知限制：5 个 `Data/*` 资产**打不出 XNB**（根因在打包器，不是我们）

日期：2026-10-09

> ## ⛔ 订正（2026-10-10 晚）：**今天复现不了，那 5 个资产能打包**
>
> 用**同一份打包器**（`D:\XnbConverter\out_publish\XnbConverter.Cli.exe`，文件日期 2026-10-05）、
> 在同一棵原版树上重跑：
>
> | 打什么 | 结果 |
> |---|---|
> | 那 5 个资产单独打（`Data/{Buildings,FarmAnimals,Fences,Machines,Pets}.{json,config}`）| **成功 5 / 失败 0** |
> | **原版** `D:\sdv_tree\Data` 整份（1702 个文件）| **成功 851 / 失败 0** |
> | **产出树** `D:\tmp\prof10\tree\Data` 整份（1964 个文件，含 Machines 的 11 个语言变体）| **成功 982 / 失败 0** |
>
> 而且那 5 份 `.config` 的 md5 **与本文记录的一致**
> （`fa30e486` / `4062f0f8` / `d32d7244` / `defb9ba2` / `e6cc0232`），
> 数据形状也确实是 `{"X":0.0,"Y":-32.0}` 对象（原版 `Data/Machines` 里 23 处 `PositionOffset` 就是）。
>
> **⇒ 下面的结论（"5 个资产在产出里根本没有"、"这 7 个包对它们的改动拿不到"、"必须覆盖式安装"）
> 今天不再成立。** 那 5 个资产（连语言变体）都能打出 XNB。
>
> **一个真实前提**（跑法，不是数据）：**必须在打包器自己的目录下跑** ——
> 它按**相对路径** `.\.config\custom dll\` 找模型程序集（`StardewValley.GameData.dll`）。
> 从别处跑时这些资产会失败，报的是「获取读取器时发生错误 / 请检查程序集
> `StardewValley.GameData.…` 对应的 dll 文件路径是否存在」+ `XNB.Encode` 的
> `NotImplementedException` —— **和本文记的那条 Newtonsoft Vector2 消息不是同一条**。
> `tools/pack-check.py` 的 `cwd=os.path.dirname(args.packer)` 就是为这个。
>
> ⚠ **本文 10-09 那次的调用条件无法还原**（同一份二进制、同样的 `.config`、同样的原版数据，
> 今天却打得出），所以「当时为什么失败」**仍未确证**；能确证的只是
> **现在能打、且必须从打包器目录下跑**。
>
> **整树也复验过**：`tools/pack-check.py` 打**全部 6461 个改过/新增的文件**
> （小树 7009 个，含 `.config` 边车）—— **成功 3486 / 失败 0**。

## 现象

全量产出（`chain3` 32 包）回包：

```
成功: 6404
失败: 16
```

失败的 16 个全是这几份 `.config`：

```
Data/Buildings.config          Data/FarmAnimals.config     Data/Fences.config
Data/Machines.config           Data/Pets.config
（外加 Machines 的 11 个语言变体 .de-DE/.es-ES/…）
```

报错一律是：

```
无效的JSON文件格式: Cannot deserialize the current JSON object (e.g. {"name":"value"})
into type 'Microsoft.Xna.Framework.Vector2' because the type requires a JSON string value
```

⇒ **`Data/Buildings.xnb`、`Data/Machines.xnb`、`Data/Pets.xnb`、`Data/Fences.xnb`、
`Data/FarmAnimals.xnb` 这 5 个资产在产出里根本没有**。

## 是打包器的锅，不是我们的 —— 三条证据

1. **拿原版树直接打**（`D:\sdv_tree\Data`，一个字节都没被我们碰过）：
   `成功 846 / 失败 5` —— **同样那 5 个失败**。
2. 产出树里这 5 份 `.config` 与原版**逐字节一致**（md5 全同）：
   `Buildings fa30e486…`、`FarmAnimals 4062f0f8…`、`Fences d32d7244…`、
   `Machines defb9ba2…`、`Pets e6cc0232…`。
3. 报错的对象是 `.config` —— 那是**解包器自己写出来的**类型描述文件。
   ⇒ 打包器**读不了它自己写的 `.config`**，是 XnbConverter 内部的往返缺陷。

## 影响面（实测）

**7 个包**改这 5 个资产：SVE、聚宝盆（作物/制作）、鸟会、食谱技能、蘑菇相关扩展、
Elle 的可爱的谷仓/鸡舍动物。它们在**产出树里是对的**（改动都在 `Data/*.json` 里），
只是在打成 XNB 时**掉队**。

## 操作后果（给装机的人）

**这份产出必须"覆盖到已有的游戏 `Content` 上"，不能"只放我们的 Content"。**
覆盖式安装时这 5 个资产自然回落到**原版**的 XNB，游戏照跑；
只放我们的产出则会缺这 5 个资产。

⚠ 也就是说：**这 7 个包对那 5 个资产的改动，在纯 XNB 安装里拿不到**
——这是本方案目前的一条**硬边界**，且**不在 SdvCpTool 这边**。

## 没做的

- 没去改 `D:\XnbConverter`（约束里写明不改；而且这是它的**往返缺陷**，
  要动得先弄清它给 `Vector2` 生成 `.config` 的那段逻辑）。
- 没试"绕过打包器手写这 5 个 XNB" —— 那等于在 SdvCpTool 里重写一遍 XNB 编码，
  与本项目"产出树交给打包器"的分工冲突。

⇒ 记在这里，**别以为"成功 6404 / 失败 16"是我们写错了 16 个资产**。

---

## 追加：根因查到了（2026-10-10）

之前这里写的是"打包器读不了它自己写的 `.config`"——**不够准**。真正卡住的是**数据**这一侧：

1. 这 5 个资产的 JSON 里，有一批字段是 **`{X, Y}` 对象**形状：

   | 资产 | 形如 `{X,Y}` 的字段（各出现次数）|
   |---|---|
   | `Data/Buildings` | `Size` 40、`UpgradeSignTile`/`SeasonOffset`/`DrawOffset`/`BuildMenuDrawOffset`/`HumanDoor` 各 25 |
   | `Data/FarmAnimals` | `EmoteOffset`/`SwimOffset`/`UpDownPetHitboxTileSize`/… 各 14 |
   | `Data/Machines` | `PositionOffset` 23 |
   | `Data/Fences` | `HeldObjectDrawOffset` 5 |
   | `Data/Pets` | `EmoteOffset`/`EventOffset`/`Motion` 各 3 |

2. 游戏 1.6 的 schema 把这些字段的类型定成 **`Vector2`**；
3. 而 Newtonsoft 默认只认 **`"X, Y"` 字符串**形式 ⇒ **`Cannot deserialize the current JSON object into type Vector2`**。

   ★ 打包器的报错里**逐字给出了路径**，与上表对得上：

   ```
   Path '['Junimo Hut'].UpgradeSignTile.X', line 10, position 10.
   ```

   —— `Buildings` 的 `UpgradeSignTile` 正是上表里那个 25 处的字段之一
   （连 `['Junimo Hut']` 这个条目名都指出来了）。

⇒ 也就是说：**解包器把 `Vector2` 写成了对象，打包器只吃字符串** —— XnbConverter 内部这一对不对称。

### 为什么不"在产出树里把它们改写成字符串"绕过

那样做**技术上可行**（打包器过了，XNB 里仍是两个 float，游戏读的是 XNB 不是 JSON），**但没做**：

- 得**照抄 XnbConverter 的模型字段类型**（哪些字段是 `Vector2`），它一改我们就静默失配；
- 这是**绕别人的 bug**，会把"打包器读不了"伪装成"我们能打包"，正是本仓库反复点名的坑。

⇒ 维护者要修就修 XnbConverter 那一侧（`XnbConverter.Entity.Mono` 里 `Vector2` 的 JSON 绑定）；
我们这侧只保留**精确的复现条件**（上面那张表）与**装机必须覆盖式**这条要求。
