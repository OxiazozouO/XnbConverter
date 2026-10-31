# Cape 宝珠 `CapeOrbTouchAction` 事件桥

日期：2026-10-27

## 1. 原动作语义（反编译确证）

`CapeStardewCode.dll` 的 `OrbRuneWarpManager.TryHandleTouchAction`：

1. 玩家物品栏里任意位置有 `(O)OrbOfTheTides` 才继续；
2. 动作参数是 `CapeOrbTouchAction <x> <y>`（当前地图）或
   `CapeOrbTouchAction <map> <x> <y>`（跨图）；
3. 有宝珠时 `WarpWithOrbEffect(map, x, y)`；没有宝珠什么都不做。

纯 XNB 没有这个 Harmony 处理器，图块是死的。

## 2. 桥接方案

用原版 TouchAction `PlayEvent` + 事件前置条件 `HasItem`：

```
地图图块：PlayEvent <eventId> true false
事件键：  <eventId>/HasItem (O)OrbOfTheTides
事件脚本：playSound wand/warp farmer <x> <y>/end
```

- `true false` = 检查前置条件、**不检查是否看过**（宝珠传送必须可重复）；
- 没有宝珠时事件不启动，`PlayEvent` 返回 false，与原 DLL 的“静默不做”一致；
- 跨图脚本为 `playSound wand/changeLocation <map>/warp farmer <x> <y>/end`；
- 事件写在当前地图的 `Data/Events/<当前地图>`，同时镜像到已存在的语言变体。

## 3. 结构：先计划、再动资源

`CapeOrbBridge.BuildPlan` 是纯函数：只扫描 `TouchAction` 并返回
`eventId`、事件脚本和目标 `Propertie`，不写任何资源。

`RewriteAndGenerate`：

1. 先为 `MapCache` 本轮弄脏的地图生成计划；
2. 写 `Data/Events`，**写成功后才**改地图属性；
3. 新建事件资产时必须找到同目录 `.config` 模板，否则不改地图（不留下半成品）。

`RewriteRemaining` 补扫**已经落盘、但本轮没被弄脏**的地图：链式转换里前面的包写下的
`CapeOrbTouchAction` 不会进入本包的 `MapCache`。先做字节搜索，只解码真的含该动作的地图。
只有本包确实带了 Cape 动作才做全树补扫，避免每个包都读一遍所有地图。

后面的包可能整份 `Load` 掉 `Data/Events`，把已生成的事件键冲掉。补扫也会识别地图上已经
存在的 `PlayEvent CapeOrb_…`，从 id 反推脚本并补回**缺失的事件键**；此时地图不再算一次改写。

## 4. 实测

定向转换 `Cape Stardew`（语料树 `D:\tmp\full-v11\tree`）：

```
地图写回：46 张
Cape 宝珠事件桥：14 个图块动作
Cape 宝珠事件桥（既有地图补扫）：8 个图块动作
```

最终产出树：

- `CapeOrbTouchAction` 残留 **0**；
- `PlayEvent CapeOrb_` **22 条**，分布在 7 张地图；
- 7 个 `Data/Events/<map>.json`，共 **22 个事件键**；
- 动作数量最多的 `Custom_CapeMossy` 6 条，其余地图 2–4 条。

例如：

```
Custom_CapeMossy.json
  CapeOrb_Custom_CapeMossy_12_68/HasItem (O)OrbOfTheTides
  = playSound wand/warp farmer 12 68/end
```

重复转换时，`Custom_CapeKeyMaze` 的地图没有被本包弄脏，但它的 `Data/Events` 被整份 `Load`
覆盖，两个事件键一度丢失。补扫识别地图上的 `PlayEvent` 并从 id 反推脚本，把两个键补回；
最终仍是 **22 条动作 / 22 个事件键**，没有重复键。

## 5. 回包核对

把 7 个事件 JSON + 7 张地图单独打包：

```
成功: 14  失败: 0
```

再解包：7 个事件 JSON 的 `CapeOrb_` 键共 22 个，7 张 `.tbin` 的
`PlayEvent CapeOrb_` 共 22 条，数量与内容一一对应。

新增 `Data/Events/<map>.config` 均从同目录模板复制，回包不需要额外补类型。

## 6. 单测

`CapeOrbBridgeTests` 覆盖：

- 当前图与跨图参数解析；
- 同一事件 id 的多个图块都被改写；
- 事件数据写成功后才改地图，并镜像 `.zh-CN`；
- 没有事件资产时新建 `.json` + `.config`；
- 参数不合法保持原动作；
- `RewriteRemaining` 补扫落盘地图且第二次调用为 0。

完整单测：**1657 通过，0 失败**。

## 7. 未实机确证

只做了静态产出与 XNB 回包/解包核对，**没有启动游戏**。
“踩着图块、带宝珠真的传过去”“没宝珠真的不触发”仍需要游戏内探针确认。

## 8. Maps 回包

先试过串行分批，单批 75 张约 6 分钟，太慢；后来按“过期地图”拆成 4 个 mini 树并行回包：
1,020 张地图、每批 255 张，**每批 76 秒、255 成功 / 0 失败**，合并后
`packed/Maps` 覆盖 **1,252 / 1,252** 张 `.tbin`，没有缺失、没有过期文件。
详见 [`2026-10-27-maps-repack-parallel.md`](2026-10-27-maps-repack-parallel.md)。
上面 14/0 的结论仍只针对 7 张 Cape 地图和 7 个新事件 JSON。
