# 2026-10-19 待实机验证清单（广度优先）

> 用法：下一次**明确下令全量**、产出装进游戏后，按这张表逐项打勾。
> 「静态已验」= 单测 / 单包定向复跑已验证，但仍需实机确认表现。

## A. 必须实机看的（上一轮修的东西）

- [ ] **农场西门 → 巴士站**：出门不再落到地图外（`FarmType` 冻结 Standard）。
- [ ] **翻转/旋转图块**：找一张原地图里带 `rotation/flip` 的图（如节日装饰），
      显示方向正确、不是原样不翻。
- [ ] **自定义图层（云雾）**：原本超过原版渲染层数的图层，现在能显示（层级名归一后）。
- [ ] **主界面 Logo / 季节图块**：中文界面下是模组的图，不是原版英文图。
- [ ] **中文 UI / 对话**：菜单、任务、对话为中文；不再出现整段英文。
- [ ] **音频**：自定义 BGM 能播；`Looped: true` 的曲目能循环。

## B. 本轮（2026-10-19 第二轮审计）修的东西

- [ ] **GiEX 的 Javi 立绘**：对话时能看到立绘（不再只有行走图）；
      结婚/雨天/年轻版立绘也齐全。
- [ ] **Hurst（Woodsman Hurst）NPC**：能遇到、能送礼；`Data/Characters` 条目存在。
- [ ] **Lavril / GoblinZic / CharlieChicken**：NPC 能出现（原本要等剧情事件才创建；
      纯 XNB 按无条件写，可能提前出现 —— 能出现即算通过）。
- [ ] **Rodney / Meredith / Nora / Aster / Cambria**：NPC 能出现、能对话。
      ⚠ 关系令牌（`{{RodneyDatability}}` 等）按**第一块的默认状态**冻结 ——
      Rodney/Nora 可能显示为不可约会（纯 XNB 没有运行时关系旗标）；能遇见即算通过。
      Meredith 的 `Meredith Portrait Style Path` 在 vanilla 配置下是上游拼写 bug
      （`Meredith Portrait  Style Path` 多了一个空格），CP 同样解析不到。
- [ ] **Hurst 的 39 个物品**：制作菜单里能看见（名字不是 `{{i18n:…}}`），
      物品图标正常（来自 `SMAPI/sprinton.hurst/assets/images/WHObjects.png`）。
- [ ] **Hurst 商店/鱼/动物**：商店货架有物品、动物贴图正常。
- [ ] **Rodney / Eli / Metalcore / Crystal Island**：同样用了
      `{{InternalAssetKey}}`，物品/NPC/动画贴图应正常。
- [ ] **Radiant 的 16 把新武器**：冒险家公会商店有货、图标正常
      （`Data/Weapons` 的 `Texture` 指向 `SMAPI/...`）。
- [ ] **鸟类观察协会的 6 个配方**：制作菜单显示“加热石浴盆”等本地化名，
      不再显示 `{{i18n:…}}`；西语/其它语言资产里也是对应语言名。
- [ ] **跨包地图编辑**：
      - `Maps/Custom_ConvenientRoad`（Lnh 社区中心出口贴图/传送点）
      - `Maps/Custom_GrampletonFields`（SVE 的不可传送区）
      - 其它「先加载包创建、后加载包编辑」的地图。
- [ ] **后加载包的 `Load` 不再冲掉先前包的 `EditData`**：
      例如 `Data/CraftingRecipes` 里同时有 AutoPetterRecipe 与鸟类观察协会的配方。
- [ ] **布尔字段按默认块**：Hurst 这类 `{{关系令牌}}` 的 NPC 存在；
      若默认是 `CanBeRomanced=false`，他不能被送花（预期如此）。

- [ ] **Rose and the Alchemist 的动物**：`AlchemistShop` 分支的 Include 能进产物；
      动物商店/畜棚里能看到 SASS 的 Cat/Crow/Fox/Toad/Honeybee，贴图来自
      `Mods/Wildflour.SASS/*`（不再是原版 14 条动物）。
- [ ] **Nora The Herpetologist**：`Data/Characters` 里有 `Nora` 条目；
      冬季外套行走图（`nora_sprites_winter_coat`）正常，不再是“有立绘、没 NPC”。
- [ ] **Nora / Juliet 的事件**：`Data/Events/Saloon` 的 Nora 8 心第二段、
      `Data/Events/CommunityCenter` 的 Juliet 14 心才艺秀能触发；
      自引用动态令牌（`{{NoraParty_SetUp}}` / `{{Juliet_SetUp}}`）不再整条丢。
- [ ] **Sunflower Acres 的觅食**（仅在把 FarmType 冻结成 `SunflowerAcres` 时）：
      Farm 与 Custom_SunflowerQuarry 每天按全局表刷新春/夏/秋/冬物品（4/3/4/5 种）。
      ⚠ 默认冻结成 Standard 时，该 FTM 包的文件级 `FarmType = SunflowerAcres` 为假，
      这 32 条判 `NotApplicable` 是**预期**。
- [ ] **Always Raining in the Valley**：Sterling / Mia / Henry 的 `Characters` 与
      `Portraits` 按 `SeasonalsLite` 配置加载（多目标条件 Load 已确定化）。
- [ ] **Juliet & Jessie 的 SVE 分支**：装了 SVE 时 `Data/Events/JojaMart` 的 Load
      不应把 SVE 版本盖上（`HasMod |contains=SVE, Nikolai` 任一命中语义已修）。

## C. 静态已验、不需要再跑的

- [x] 全量单测：**1613** 条（本轮新增：InternalAssetKey / 键名 i18n / 布尔与枚举默认块 /
      配置字段 When / DLL 配置令牌 / 条件新条目与多目标条件 Load / Include 动态令牌 /
      自引用动态令牌 / 多值 HasMod / FTM 全局回退 / 现成 tbin 地图归一）。
  `spouseRoom的小TMX` 的偶发抖动已定位到写入层：`MapCache.Flush` 被调用两次时复用同一个
  `TBin10Reader`，而它的 `Save` **不重置 `BufferWriter.BytePosition`**，第二次会把整份文档
  追加到文件末尾（实测 1564 字节、两份 tBIN10；池化缓冲里还可能混着别的文档残片）。
  现在写成功即换干净写入器并清 `Dirty`，测试也断言文件里只有一份文档。
- [x] 单包定向复跑（当前 Release）：
      GI Extra locations（立绘 + 106 个 GiEX 物品）、Lnh ×7（跨包 EditMap）、
      HxW Tilesheets（季节图集）、Seasonal Cute Characters ×4、Jonghyuk、
      鸟类观察协会（键名本地化）、Woodsman Hurst（39 物品 + SMAPI 素材 + NPC 条目）、
      Rodney O'Brien（NPC 出现 + WWWA 物品 + 172 个 SMAPI 素材）、
      Lumisteria Mt Vapius（Aster 写入，`Manner=Polite`）、Metalcore（Cambria/Orchid 写入）、
      Lavril（条件创建 NPC 写入）、Distant Lands（GoblinZic 写入）、
      SVE（CharlieChicken + 日程 + 对话 + GrapeMastery 能力 + Apples 全部写入）、
      S&S（Hector/Mateo/Cirrus + 6 个 Power 写入；残留仅上游 `LPPN1`）、
      Ridgeside（108 NPC / 783 个对话资产；残留仅上游韩文 `{{0}`）。
- [x] **确定性**：同参数连跑两次 Lavril（两趟链），7,829 个产出文件逐字节相同（差异 0）。
- [x] **地图属性 i18n**：SVE 三张图（HenchmanBackyard/MorrisHouse/TreasureCave）里
      8 处 `{{i18n:…}}` 已按中文译文写入（tbin 内 0 残留）。
- [x] **两趟报告合并**：SVE 复跑 `report.json` 带 `twoPassMerged: true`、
      `patches = Load + Edit`（4,542 条）；原始两趟留在 `report.loads.json` / `report.edits.json`。
- [x] 双扩展名资产回包：`SMAPI/sprinton.hurst/assets/images/WHObjects.png.png`
      → `WHObjects.png.xnb`（游戏请求 `…/WHObjects.png` 正好命中）。
- [x] 季节图集四变体：fix26 全树 0 处缺失（`check-map-tilesheets.py`）。
- [x] 地点 `CreateOnLoad.MapPath`：0 处找不到地图（`check-map-warps.py` 检查二）。
- [x] 全树贴图引用扫描（32,311 条字段）：**转换器真缺口 0**；18 个悬空引用全部是上游/
      可选外观/原版占位/框架 DLL（子代理逐条核对）。
- [x] **SVE 新树**（3 包链，`quick-sve4`）：残留令牌 2 处（韩文上游 `{{0}`）；
      物品精灵下标 0、图集引用 0、`CreateOnLoad.MapPath` 0；
      图块 Warp 的 2 个缺失目标均来自未选的 Grampleton Fields 包。
- [x] **当前代码新树**（East Scarp 7 包链，`quick-eastscarp2`）子代理全量审计：
      3,158 个 JSON 残留令牌 **0**；配方/商店/机器/畜产跨表 **0** 真缺口；
      89 个 NPC 的住所与 9 个婚房地图 **0** 缺失；贴图 2,551 个字段只有 1 个上游
      （`Monsters\Shadow_Brute`，原版同值）。
- [x] Ridgeside 对原版逐包自检 151 处：`spriteSheet` 多为单包跑缺依赖；全链里
      `Philip_work` 实际以 `Philip_Work` 存在（Windows 大小写不敏感，非缺口）；
      `Bryle_Beach` / `Jio_Unmask` 仍是 RSV 上游未创建。`tokenConditions` 为提示项。
- [x] East Scarp 对原版逐包自检 29 处：`tokenConditions` 是"条件键是包内令牌"的**提示**
      （解析器已处理）；`spriteSheet` 缺失来自**单包跑、依赖包不在**；
      `Jasper_Read/jasper_read` 大小写重复键实测可正常回包（XNB 解回两条都在）。
- [x] 定向复跑（2026-10-20，当前 Release）：
      Rose and the Alchemist 的 Include 动态令牌展开后，`Data/FarmAnimals` 有 5 种 SASS 动物，
      `Mods/Wildflour.SASS/` 下 21 个动物素材 + 2 张条件化的地图/立绘 Load 全部写入；
      Nora 的 `Data/Characters` 有 `Nora` 且冬季外套 Sprite 为无扩展名 SMAPI 资产；
      Nora 8 心第二段、Juliet 14 心才艺秀事件键已在产出里；
      ARV 的 Sterling/Mia/Henry 的 Characters/Portraits 全部存在（多目标条件 Load）。
      Sunflower Acres 的 FTM 全局回退生成 32 条，但 Standard 存档下被文件级
      `FarmType = SunflowerAcres` 判 `NotApplicable`（预期）。
- [x] 新建资产的 ContentHashes 键：`.tbin` 地图此前完全没补键；修复后
      Lavender 的 2 张新地图、SCC 链的新地图都能在清单里查到 `*.xnb`。
- [x] 重复语言资源：`Data/Objects.fr-FR`、`Data/FruitTrees.fr-FR`、`Data/Locations.zh-CN`
      这些与基础逐字相同的新增变体不再落盘（Lavender 三包链实跑，Locations 562 KB）。
      加 `--prune-baseline-locales` 还可清理原版里相同的语言副本（Zuzu 树实测
      131 张 PNG、26.4 MB）；其余重复 XNB 在 `pack-tree.py` 打包后硬链接去重。
- [x] 定向复跑（2026-10-20，Lavender + Lumisteria CP + FTM 三包链）：
      `RailRoad` 已归一到 `Railroad`（Forage 12 / ArtifactSpots 9，无孤立键）；
      Secret Wood Weeds 的 4 条季节物品已写入；Forest 春季 `Chance` 0.2 → 0.5；
      报告 notes 去重；`--packs` 自动带上 required 依赖，自定义地图有 `CreateOnLoad`。
- [x] 检查器误报修正：warp 地名允许 `.`/`-`、地点比较大小写不敏感；
      图集引用里 `LooseSprites/...` 这类内容根相对路径不再误报。
      图块 Warp 的 11 处假阳性降到 4 处（全是上游陈旧地名/粘字节）。
      `check-map-warps.py` 改为按 tbin 属性结构（属性名 + 类型 + 长度）取 `Warp`/`NPCWarp`，
      不再把 `Doors` 的五元组当 Warp —— Downtown Zuzu 的 4 张公寓图不再误报 `indoor`；
      重跑后只剩 1 个上游目标（CrossNorth → 未安装的 Stargrow Valley）。

## D. 已知**上游/不可转**的，不要当 bug 修

- RSV `Data/Characters` 的 `Appearance[].Portrait: Portraits/Jio_Unmask` 等：
  包内没有任何补丁创建该资产。
- SVE `Minigames/boatJourneyMap`：游戏只加载 `Minigames/<season>_boatJourneyMap`。
- `Content/farmhouse_tiles` / `farmhouse_tiles`：正确资产名是 `Maps/farmhouse_tiles`。
- `Maps/Custom_CapeBusStopBuildable`：没有任何包创建它。
- `NCarigon.BushBloomMod/**`、`Mods/Pathoschild.TractorMod/**`：别的框架/DLL 资产。
- `{{spacechase0.JsonAssets/...}}`、`{{PeacefulEnd.CustomCompanions/...}}`、
  `{{TheMightyAmondee.CustomTokens/...}}`：DLL/框架令牌，静态不可解。
- S&S `Marriage.Cirrus.Spring24` 的 `{{LPPN1}}`、Lurking 的 `{{ClosingEH53}}`：
  i18n 具名参数没给（CP 运行时同样保留字面量）。
- `Characters/Schedules` 里按 `HasSeenEvent`/天气/时间的候选：原版日程键容量封闭，
  只能冻结一种状态。
- SVE 的 `Apples` 已按「运行时状态冻结 ≠ 静态为假」写入（可能提前出现）；
  不再列为缺口。
- `Strings/schedules` 里按条件切换的同一台词键（如 Kent 的 `CommunityDay_Kent`）：
  游戏按字面键查找，条件无落点；现在取最后一个变体写成普通键，**条件丢失**但台词能显示。
  实机看到某一句季节台词即算通过。
- SVE 的 `Data/FarmAnimals.StatToIncrementOnProduce` 源文件就写 `""`（模型是数组）；
  这是 CP 语料里的既有写法/游戏序列化可接受，非转换器改动。
- SVE `Data/WorldMap` 的 `KnownCondition: "LOCATION_ACCESSIBLE Railroad"` 是合法 GSQ
  （`MapArea.cs` 消费）；落点自检把它当"未知位置"是检查器口径，不是数据错误。
- `DN.SnS_DeepDarkChest` 的贴图 `Textures/DN.SnS/DeepDark_Treasure` 原图就是 96×31
  （BigCraftable 惯例 16×32），自检报"放不下一格"是上游素材尺寸，不是转换错误。
- `Cornucopia_FlavoredYogurtSilver`：`machines-essentialcrops/yogurtjar.json` 把规则 Id 误写成
  ItemId；实际物品是 `Cornucopia_FlavoredYogurt` + `quality_silver`，上游笔误。
- Downtown Zuzu 的 `Custom_DTZ_Waterway` 里 `Action = Message "DTZ.Waterway.4"`，
  但 `content.json` 的 `StringsFromMaps` 只定义了 `.1`–`.3`（`.4` 整条缺失）。
  CP 同样取不到文本，不是转换器漏写。
- East Scarp 的 `HartResidence.22`：地图里有 `Message` 引用、i18n 里也有
  `Strings.HartResidence.22`，但 `LocationMessages.json` 只列到 `.21` —— 上游漏补丁。
- SVE 的 `Backwoods.1` / `ForestWest.11` / `ForestWest.12` 的 Message 值含
  `{{FarmName}}`，按“运行时令牌不猜”整条不写，属预期。
- SCC 完整链里 `Portraits/MichaelHart_Drawberry_Beach` 在包内没有对应源图；
  `_Beach`/`_FlowerDance` 的几条是可选外观或上游未创建，CP 同样会走
  `DoesAssetExist` 回退。
- Downtown Zuzu 的 `Custom_DTZ_CrossNorth` 有一条 warp 指向
  `Custom_StargrowValley_ZuzuDispensaryInterior`；该目标来自未安装的 Stargrow Valley，
  基准树里也不存在。
- East Scarp 的 `DeepMountains_Van.tmx` 把地点写成 `EastScarpe_Village`、
  Lavril 的 `ArkOrchard.tmx` 写成 `EastScarpe`，全语料没有任何资产叫这两个名字；
  `EastScarpeInn_seance.tmx` 的 `Custom_ESHartResidence` 同样无人创建 —— 都是上游陈旧地名。
  `Custom_ShearwaterBridge` 则来自可选依赖 SVE；只跑 East Scarp 子集时自然报缺。

## E. DLL 配置令牌（已修，待实机确认）

- `{{mistyspring.GiEXredux.code/ExcludeFishes}}` / `ExcludeAnimalProduce`
  曾导致 GiEX 的 **28 个鱼 + 31 个动物产物**定义整条不写。现在 `DllConfigTokens`
  按窄规则读代码模组的 `config.json`（白名单 + 唯一前缀），单包已产出；
  实机确认这些物品/产物在游戏里存在、商店有货即可。
