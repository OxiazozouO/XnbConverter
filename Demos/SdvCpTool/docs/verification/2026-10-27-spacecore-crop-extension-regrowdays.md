# SpaceCore 作物扩展：RegrowDays 子集

日期：2026-10-27

## 1. 能映射的部分

`spacechase0.SpaceCore/CropExtensionData` 的 `YieldOverrides.<harvest>.RegrowDays` 与原版
`Data/Crops.RegrowDays` 语义相同，但原版只有**一个全局值**。

规则：

- 所有被覆盖的收获序号给出**同一个** `RegrowDays` ⇒ 写回 `Data/Crops[key].RegrowDays`；
- 出现多个不同值 ⇒ 报歧义，不猜、不写；
- 顶层直接的 `RegrowDays` 也接受，并与 `YieldOverrides` 的值一起比较。

## 2. 仍不映射

- `YieldOverrides.*.Drops` / `IgnoreDefaultDrops`
- `ExperienceGained`（原版作物没有这个字段）
- 其它逐次收获的差异化行为

这些继续作为损失报告。

## 3. 实现与本地化

`SpaceCoreCropExtensions` 只解析包 JSON、算出要改的 `Data/Crops` 字段，落盘仍走
`FrameworkBaker.StageIfChanged`。

`Data/Crops.<locale>` 里的 `RegrowDays` 也同步 base 值；作物名称/描述等文本保留本地化。

## 4. 测试

- 唯一值：多个 yield 覆盖都是 7 ⇒ 写入 7；
- 不同值 3/7 ⇒ 不写并报“多个值”；
- `ExperienceGained` / `YieldOverrides` 其它字段 ⇒ 报未映射；
- 框架烘焙集成：base 与 `Data/Crops.zh-CN` 都得到 5；
- 完整单测：**1665 通过，0 失败**。

没有启动游戏。
