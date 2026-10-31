# SpaceCore 对象扩展：可映射字段子集

日期：2026-10-27

## 1. 能精确落到原版 `ObjectData` 的字段

`spacechase0.SpaceCore/ObjectExtensionData` 的条目本质上是对 `Data/Objects` 条目的扩展。
当前只映射原版 `ObjectData` 确实读取、语义一致的四个字段：

| SpaceCore 字段 | 原版 `Data/Objects` 字段 | 规则 |
|---|---|---|
| `CanBeTrashed` | `CanBeTrashed` | 直接复制布尔值 |
| `CanBeGivenAsGift` | `CanBeGivenAsGift` | 直接复制布尔值 |
| `CanBeShipped` | `ExcludeFromShippingCollection` | `false` ⇒ `true`；`true` ⇒ `false` |
| `HideFromShippingCollection` | `ExcludeFromShippingCollection` | 直接复制布尔值 |

来源：原版 `ObjectData` 的 `CanBeTrashed`/`CanBeGivenAsGift` 读取点与
`ExcludeFromShippingCollection` 的出货判断（`Object.cs` / `ObjectData` 消费点）。

## 2. 仍不映射的字段

以下字段原版 `ObjectData` 没有对应槽位，继续作为损失报告，不猜：

- `CategoryTextOverride` / `CategoryColorOverride`（分类名/色是代码常量）
- `MaxStackSizeOverride`
- `TotemWarp`
- `EatenHealthRestoredOverride` / `EatenStaminaRestoredOverride`
  （原版只有 `Edibility`，不能同时表达两个独立覆盖）
- 逐 NPC 礼物白名单、`UseForTriggerAction` / `ConsumeForTriggerAction`

## 3. 实现

新增 `SpaceCoreObjectExtensions`：

1. 扫描包的 `*.json`，只处理 `Target = spacechase0.SpaceCore/ObjectExtensionData`；
2. 把 `{{ModID}}` 解析成包的 `UniqueID`；仍有其它未展开令牌就不猜；
3. 目标不在 `Data/Objects` 时只记原因，不新建对象；
4. 带 `When` 条件的扩展补丁**不无条件落盘**，只记原因（原版对象字段没有条目级条件槽位）；
5. 只改上面的四个字段，其余字段写进报告 notes；
5. 中文等本地化 `Data/Objects.<locale>` 里同一条目也同步这三个**结构字段**，
   `Name/DisplayName/Description` 保留本地化文本。

这一步仍然在语法层算完，最终由 `FrameworkBaker` 的 `StageIfChanged` 统一落盘。

## 4. 验证

- 纯函数用例：字段映射、`CanBeShipped` 取反、未知目标、未展开令牌；
- 框架烘焙集成用例：`Bake()` 后 base 与 `Data/Objects.zh-CN` 的结构字段都改到，
  中文 `Name` 保持不变；
- 完整单测：**1665 通过，0 失败**（对象扩展 4 个用例 + 作物 RegrowDays 用例）。

没有启动游戏；没有把 `TotemWarp` 等无原版槽位的字段硬塞进 `Data/Objects`。
