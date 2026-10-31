# 支持外部 `.tsx` 的 tmx → tbin（新语料 17 条地图不写 → 写了）

日期：2026-10-12
起因：三版合并后的新语料（232 包 / 32771 条）里，**来源地图 tmx 转换失败：只支持内嵌单个 `<image>`
的 tileset（不支持外部 tsx / 图集集合）** 这一档有 16 条（`Crystal Island` 一张图里就 15 条）⇒
**整张地图不写**。这是"能力缺口"而不是"语义落不了" ⇒ 本轮补上。

## 判据（Tiled 的规矩）

```xml
<map …>
  <tileset firstgid="1" source="sheet.tsx"/>   <!-- 定义在另一个文件里，相对 **tmx 所在目录** 解析 -->
  …
</map>
```
- `firstgid` 在**引用处**，其余（`name` / `tilewidth` / `image` / `tile` 属性与动画）在 `.tsx` 里；
- 目标 tbin 的 `TileSheet.Id` 取 **tileset 的 `name`**、`Image` 取 **image 的 `source`**（与内嵌那条同一套映射 ✓）。

## 改法

`Maps/TmxToTbin.ParseTilesets` 增一个 `tmxDir` 参数：遇到带 `source` 的 `<tileset>` 就把那个
`.tsx` 读进来（找不到 / 根元素不对 ⇒ 明确报错），把引用处的 `firstgid` 借过来，再走原来那条内嵌路径。
**图集集合**（`<tile><image …/></tile>`，每格一张图）**仍然拒** —— tbin 的图集只有**一张 sheet**，
集合表示不了；但理由改成说清是"集合"（原来笼统说"不支持外部 tsx / 图集集合"，读的人分不出是哪种）。

## 同参数受控对比（`prof61` → `prof62`，232 包 / 32771 条）

```
报告：230 包逐项相同；**2 包**变了 —— 12_Crystal Island（16 条）、162_GI Extra locations（1 条）
written 15043 → 15060（+17）｜ writeFailed 234 → 217（−17）
产出树：只在 B 有 2 个文件（新增的地图：Maps/GiEX_Aquarium.tbin + .config）；
        其余 15 条是**改既有地图**（文件已存在，属于"内容变化"而不是"新增文件"）——逐字节比见下
单测：新增 `外部tsx也能转`（读 tsx / firstgid 借用 / Id+Image 映射）与 `图集集合该拒`（理由要说"集合"）
```

## 留档：仍然拒的

- **图集集合**（Tiled 的 "collection of images"）：tbin 体系里一张 tileset = 一张 sheet ⇒ 表示不了
  ⇒ 整条拒（理由已说清）✓；
- 外部 `.tsx` **里再套**外部引用（Tiled 不允许）✓ 不存在这种情况。
