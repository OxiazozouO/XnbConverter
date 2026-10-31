# 非原版 tile action 全量普查

日期：2026-10-06
依据：`D:\sdv_src` 三源合一构造原版 action 全集（`performAction` 119 + `performTouchAction` 15 + 子类 override）
＋ **原版 259 个 `.tbin` 实物解析** ＋ 两语料全量扫描 ＋ **96+13 个 DLL 的字符串堆二进制核实**

---

## 0. 一句话（**推翻了我此前的判断**）

> **「其实原生就能做」的占比：两语料合计 688 / 876 ≈ 79%；
> 剔除「本安装下根本没人注册、等于没在跑」的 136 条后，是 688 / 740 ≈ 93%。**

我此前断言「mod 用 C# 注册的自定义动作，纯 XNB 表达不了」——**错了 93%**。

---

## 1. 分档统计

| | 实例总数 | (a) 无损 | (b) 可近似 | (c) 烘焙 | (d) 做不到 | ⑤ 空操作 |
|---|---|---|---|---|---|---|
| **语料 A（RSV）** | 177 | 30 | 90 | 3 | 34 | 20 |
| **语料 B（96 项）** | 699 | 360 | 208 | 0 | 15 | 116 |
| **合计** | **876** | 390 | 298 | 3 | 49 | 136 |

**⑤「空操作」必须与 (d) 分开**：`FestivalDialogue` 80 条依赖未安装的 `MouseyPounds.ShadowFestival`，
图根本不加载。把它算进「做不到」会把可转换率**估低 11 个百分点**。

---

## 2. 可直接抄的替换清单

```
RSVTeleport <loc> <x> <y>   →  Warp <x> <y> <loc>                        (b) 少音效/淡出/贴图
LoadMap <loc> <x> <y>       →  Warp <loc> <x> <y>   (TouchAction)        (a) 参数顺序完全一致
RSVShop <shopId>            →  OpenShop <shopId> […]                     (a) RSV 官方补丁就这么干
RSVKitchen                  →  kitchen                                    (a) 原值疑似笔误
RSV.TrashCan <n>            →  Garbage <Data/GarbageCans 里的 id>         (b)
RSVQuestBoard <board>       →  Billboard                                  (b) 需把日常任务搬进 Data/Quests
RSVSpecialOrderBoard        →  SpecialOrders                              (b) 需把 SO 搬进 Data/SpecialOrders
RatesCounter/RSVHistoryScroll/MyLetter  →  Letter <key>                   (b) 文本搬进 Strings\StringsFromMaps
RSVOpenDaiaBook             →  PlayEvent 75160259（+ Message）            (b)
SVE_PlaySound <cue>         →  playSound <cue>       (Action)            (b) 踩上→点击
```

**最硬的证据是 `RSVShop`**：RSV 的 DLL 里**没有** `RSVShop`——
**它自己已经在 CP 补丁里把 23 条改写成原生 `OpenShop`**（`"Action": "OpenShop RSVJericShop none 600 2600 …"`），
与 tmx 里遗留的 `RSVShop RSVJericShop` **逐一对应**。**作者给出的迁移路径就是答案。**

---

## 3. 真做不到的（49 条，全部读过方法体）

共同特征：**自定义 `IClickableMenu` 自绘 / `createQuestionDialogue` + 写死效果 / 跨全图改状态**。

| 类 | 例子 | 为什么 |
|---|---|---|
| 自绘任意贴图菜单 | `ShowImage`(9)、`ShowMap`(2) | 原版 119 个 case 里**没有任何一个**是把数据指定的贴图丢进菜单 |
| 选项菜单 + 写死效果 | `HotelCounter`、`IanCounter`、`EventHallCounter`、`BlissBook`、`RSVMaiveLoan` | 原版有 yes/no，但效果写死 |
| 运行时改图 + 持久旗标 | `RSVCorruptedFire` | `removeTile`+`setMapTile`+`modData`，数据表达不了 |
| 第三方 mod 私有渲染 | `VF.SpawnBasket` | 只往 Visible Fish 自己的水面覆盖层塞东西 |
| SVE 的神龛/锁 | `DarkShrine*`(6)、`SVE_Lock`(5) | 跨全图改状态 / 按手持物品匹配 |

**判定没有一条是靠「名字不像原版」得出的——全部读到了方法体。**

---

## 4. 三个「差点又误判」（诚实项）

1. **`WarpNetwork` 不是 RSV 的**——RSV 只是用 `"When": {"HasMod":"tlitookilakin.warpnetwork"}` 包着它，
   本安装下**这条补丁根本不展开**。两个语料任何 DLL 里都没有这个串。
2. **`RSVShop` 也差点被算成「RSV 的自定义 action」**——它的「实现」其实是 **RSV 自己把它改写成了原生 `OpenShop`**。
3. **`FestivalDialogue`（80 条，语料 B 第三大）不是「做不到」，是「没在跑」**——
   依赖未安装的 ShadowFestival，图不加载。

---

## 5. 方法学（可复用）

- **原版 action 全集要三源合一**：`performAction` 的 119 个 case ＋ `performTouchAction`（**不是字符串 switch，
  是 `value.Length`+`value[0]` 两层**）＋ **子类 override** ＋ **原版 259 个 tbin 实物解析**。
  `TownMailbox`/`BrokenBeachBridge`/`WitchCaveBlock` 在 `.cs` 里**根本搜不到**，只在实物里出现。
- **「谁注册的」必须做二进制核实**：扫全部 DLL 的字符串堆（.NET 字面量在 `#US` 堆是 UTF-16LE，
  用 utf-16-le + ascii 双编码匹配），再用 `ilspycmd -t <类>` 确认。
  **上次把 `WarpNetwork` 记错成 RSV，就是没做这一步。**
- **`TileIndexProperties` 上的 `Action` 不扩充动作集**——它只参与 `doesTileHaveProperty` 取值，
  最终仍回到同一个 switch。
