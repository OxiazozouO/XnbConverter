# `bake-frameworks` 全树崩溃：`Entries` 里的 `Target` 数组

日期：2026-10-28

## 现象

对全量产出树跑：

```
SdvCpTool.Cli bake-frameworks --tree D:\tmp\full-v11\tree --mods <Mods>
```

直接失败：

```
InvalidCastException: Cannot cast Newtonsoft.Json.Linq.JArray to Newtonsoft.Json.Linq.JToken.
  at SpaceCoreObjectExtensions.Apply(...)
```

## 原因

三个 SpaceCore/纹理扫描器都会用 `Descendants(root)` **递归遍历整份 JSON**，
然后对每个 `JObject` 直接：

```csharp
change.Value<string>("Target")
```

递归会进入 `Entries` 里的**条目值**。条目值本身也可能有 `Target` 字段，而且可以是
数组（例如地图 `TextOperations` 的 `Target: [ "MapProperties", "Message" ]`）。
`JToken.Value<string>` 遇到 `JArray` 会抛 `InvalidCastException`，整条框架烘焙中止。

这不是某个包的坏数据 —— 是扫描器**把条目值误当成补丁对象**。

## 修法

三个入口统一先判断类型，只认字符串 `Target`：

- `SpaceCoreObjectExtensions.Apply`
- `SpaceCoreCropExtensions.Apply`
- `FrameworkBaker.ProcessTextureOverrides`

```csharp
if (change["Target"]?.Type != JTokenType.String
    || !string.Equals(change["Target"]!.Value<string>(), Target,
        StringComparison.OrdinalIgnoreCase))
    continue;
```

## 验证

- 新增 2 条回归测试：`Entries` 里带 `Target` 数组时只报“目标对象不存在/跳过”，
  不抛 `InvalidCastException`。
- 完整单测：**1688 通过，0 失败**（含本轮新增 2 条，原有测试全绿）。
- 全量树重新跑 `bake-frameworks`：正常结束，0 新增（幂等），旧报告保留。
- 未启动游戏。
