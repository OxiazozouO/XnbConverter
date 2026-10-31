# SpaceCore 纹理覆盖与 AT 基础名解析：两块硬骨头

日期：2026-10-27

## 1. SpaceCore `TextureOverrides`：90 条实际全部被跳过

### 根因

`FrameworkBaker.ProcessTextureOverrides` 把目标资产名去掉 `.png` 后交给 `ImageCache`：

```csharp
targetKey = targetRel;                 // "Maps/springobjects"
images.TryGetPixels(targetKey, ...);   // 缓存按 "Maps/springobjects.png" 查找 → 永远失败
```

于是 4 个包的 90 条覆盖全部报“产出树里没有这张图”，实际上 PNG 就在树里。
修复：缓存的键始终是带 `.png` 的文件名。

### 结果

| 包 | 应用条数 |
|---|---:|
| Bear Family - Custom NPCs | 1 |
| Crystal Island | 1 |
| 动态鱼（Animated Fish） | 86 |
| Eli & Dylan | 2 |
| **合计** | **90** |

改到的目标贴图：

```
Maps/springobjects.png
TileSheets/Objects_2.png
TileSheets/furniture_2.png
LooseSprites/parrots.png
Mods/MadDog.HashtagBearFam/MadDogFurniture.png
Mods/EnD/Objects.png
```

### 重跑不能叠加：`_texture-overrides.json`

`TextureOverrides` 是 **alpha 混合**，重复应用会让半透明帧越叠越暗（实测 MadDog 家具第二次会变色）。
现在把“应用后像素的 SHA-256”记在产出树外的 `_texture-overrides.json`：

- 当前目标图的像素哈希与记录一致 ⇒ 整张目标跳过；
- 目标图被重建/被别的补丁改过 ⇒ 哈希不一致，自动重新应用；
- 删掉状态文件 = 强制重新应用。

实测同一命令第二次运行：纹理覆盖 0，所有目标图字节不变。

### XNB 回包

90 条改动落在 PNG 后，用 `pack` 单独重打 6 个目标资产的 XNB 并安装。逐像素对比：
`furniture_2` / `Objects_2` 完全一致；其余差异只来自 **alpha 预乘取整** 和“全透明像素 RGB=0 vs 255”，
没有内容丢失。

### 本地化语言变体

同一张贴图如果还有 `springobjects.es-ES.png`、`.fr-FR` 等语言变体，覆盖会**逐个写进每个变体**：
游戏在非中文语言下直接加载变体，不会回落到基础图。变体由目标文件同目录的同名语言后缀自动发现，
不需要在 SpaceCore 数据里额外声明。

实测动态鱼 82 条覆盖：`springobjects` 基础图 + 6 个语言变体的像素哈希在首次运行后全部一致；
第二次运行纹理覆盖为 **0**（每条目标图的像素哈希都已记录）。幂等状态按“每个变体”单独记录，
所以新增语言变体或某个变体被重建后，只会重烤那一张。

回包并逐像素核对（6 个变体）：尺寸一致，**完全不透明像素 0 差异**；
半透明边缘因 alpha 预乘取整有 1337 个像素差（最大 98），全透明像素的 RGB 差 1894 个，
与基础图回包时的既有口径一致。6 个本地化 XNB 已安装到游戏 `Content/Maps`。

---

## 2. AT 基础条目：从逐字匹配到归一化词元

### 现状

`ProcessFurniture` / `ProcessCraftable` 只按**显示名逐字相等**找基础条目，导致：

```
Children bed  ≠ Child Bed
Computer      ≠ Farm Computer
Jukebox       ≠ Mini-Jukebox
Hay Hopper    ≠ Hopper
Seasonal Decor Spring ≠ Seasonal Decor
```

### 算法

1. 先保留逐字匹配；
2. 归一化：小写、标点/下划线转空格、常见不规则复数（children→child 等）、词尾 s；
3. 词元集合相同 = 100；一方是另一方子集 = 70−差值；Jaccard ≥ 0.6 = 50×Jaccard；
4. 优先原版数字 ID，其次多出的词元更少；**同分同长度直接判歧义并放弃**（Junimos/Mushroom 就是这样）；
5. 排除本工具生成的 `AT_…` 条目，避免重跑拿上次产物当基础；
6. 近似只对 **BigCraftables** 开放；普通 Object（洒水器/蟹笼）按 ID 判定行为，复制成新 ID 会“有外观、没功能”，继续不写。

### 结果

- 新增 1 件家具：`Children bed → Child Bed`
- 新增 7 个大件：
  - `Computer → Farm Computer`
  - `Jukebox → Mini-Jukebox`
  - `Hay Hopper → Hopper`
  - `Seasonal Decor Fall/Spring/Summer/Winter → Seasonal Decor`
  - `Mushroom → Mushroom Box`
- 仍放弃：`Junimos`（Stone Junimo / Junimo Chest 同分）、普通 Object 基础。

新增条目写进 `Data/Furniture` / `Data/BigCraftables`，贴图写到 `Mods/<uid>/AT/`，配方/商店能克隆就克隆。

### 回包注意

`Data/BigCraftables`、`Data/Shops` 的 XNB 写入依赖打包器目录下的
`.config/custom dll/StardewValley.GameData.dll`。本轮 CLI 的 `bin` 目录只有 MonoGame shim，
缺这三个数据 DLL，导致 BigCraftables/Shops 报 `NotImplementedException`；
补齐 `StardewValley.dll` / `StardewValley.GameData.dll` / `xTile.dll` 后 9 个文件全部打包成功。
以后跑全量 pack 必须确认这个目录完整。

## 3. Custom Companions：216 张帧表重排成原版 Pet 布局

原版 `Pet` 的 `AnimatedSprite` 写死 **32×32**，方向行序是
**Right(0–3) / Up(4–7) / Down(8–11) / Left(12–15)**（`Pet.updateSlaveAnimation`）。
CC 的 `companion.json` 行序是 **Down / Right / Up / Left**，而且帧尺寸可以是 16×32、16×16 等；
旧实现只把 DownAnimation 的绝对帧号塞进 `Behaviors.Animation`，其余方向完全没处理。

现在的算法：

1. 读源图、`FrameSizeWidth/Height`，按 4 列网格切帧；
2. 从 `Right/Up/Down/LeftAnimation` 各取最多 4 帧（少于 4 帧循环，`ManualFrames` 也支持）；
3. 按原版行序写进 **128×128** 四向帧表，每格 32×32；
4. 小于 32 的帧底部居中补透明（16×32 → x=8,y=0；16×16 → x=8,y=16）；
5. `Texture` 指向新帧表；`IconTexture` 仍指原图，`IconSourceRect` 保持 0,0,16,16；
6. `Behaviors.Walk.Animation=null` —— Walk 的四向动画由 Pet 按方向选行，不再使用源绝对帧号。

同时修了“已存在就跳过”的早退：旧 `Data/Pets` 条目现在会被更新，而不是永远保留旧贴图。

结果：216 张宠物帧表全部生成并回包（`Data/Pets.xnb` + 216 个 `_pet32.xnb`）。
随机抽查四向帧哈希不同，行内容非空；解包 `Data/Pets.xnb` 能看到
`Texture=..._pet32`、`IconTexture=...`、`Animation=null`。

仍丢失：CC 的跟随 AI、多只召唤、检视对话/肖像、自定义音效；这些原版 PetData 没有槽位。

## 4. Solid Foundations：HideBaseTexture 与多行帧

SF 的 `DrawLayers` 有三件事原版 BuildingData 不直接支持：

1. `HideBaseTexture`：底图不该画；
2. 图层自己的 `FrameCount/FramesPerRow/FrameDuration`；
3. 图层贴图与底图是同一张时，不能让底图透明把图层一起弄没。

处理：

- 对 `HideBaseTexture=true` 的建筑，生成一张同尺寸**全透明底图**作为 `Building.Texture`；
- `DrawLayers[].Texture` 继续指向原始 `building.png`；
- 直接读取 `FrameCount/FramesPerRow/FrameDuration`（旧实现只读 `Sequences`，且把 `FramesPerRow` 写死 1）；
- 已有建筑条目不再早退，按新结构比较后更新。

10 个建筑（3 个灯串 + 7 个喷泉）已更新并回包；解包核对：
`Texture=..._empty`、图层仍指原图、喷泉 `FrameCount=24/FramesPerRow=6`、灯串 `FrameCount=4`。

灯光本身（`Lights`）仍没有原版 BuildingData 字段，继续报丢失。

## 5. 本地化 Data：框架新增行必须同步到每个语言变体

`LocalizedContentManager.Load` 在中文下加载 **`Data/Furniture.zh-CN`**，不是 base；
之前框架烘焙只往 `Data/Furniture.json` 加 AT 行，中文客户端实际看不到这 1088 件家具，
BigCraftables（118）、Buildings（21）、Objects/Crops/CraftingRecipes/Shops 的新增行同理。

新增 `MirrorLocalizedData`：

- 对每个 `Data/<asset>.<locale>.json`，把 base 有、本地化没有的**键**补进去；
- `Data/Buildings` 已有条目保留本地化 `Name/Description`，只更新 `Texture/DrawLayers` 等结构字段；
- `Data/Shops` 不只补新 shop 键，还合并已有 shop 的 `Items`（按 `ItemId`+`Id` 去重）；
- 数组资产（`AdditionalWallpaperFlooring` / `TriggerActions`）按 `Id` 补缺。

同步后行数（zh-CN）：

```
Furniture       2686 / 2686
BigCraftables    473 / 473
Objects         2860 / 2860
Crops            320 / 320
Buildings         60 / 60
Shops            201 / 201
```

`CraftingRecipes.zh-CN` 原本还有 6 条 base 没有的旧本地化配方（389 vs 383），保留不动。
同步是幂等的，重复跑不会重复插入。`Data/Shops.zh-CN` 的 201 个 shop 与 base 的
`Items` 逐条比对后 **0 缺失**。

同步后把整个 `tree/Data` 重打成 XNB：**1579 / 0 失败**，1579 个 XNB 已覆盖安装到游戏，
并把 `ContentHashes.json` 与产物合并（新增 `_pet32`、`_empty` 等键）；否则本地化 XNB 虽然
内容对了，`DoesAssetExist` 仍可能看不到新增资产。

## 6. 报告汇总

`FrameworkBaker` 的“本轮是否写了东西”的判断漏了 `Textures`，只烤纹理时不会覆盖旧报告。
已把 `Textures` 计入 total；合并后的完整报告：

```
家具 1089 / 装饰 4 / 作物 20 / 可制作 157 / 邮件 3 / 捆绑包 27 /
建筑 21 / 宠物 216 / 草地 4 / 交通 2 / 纹理覆盖 90
```

## 7. 本轮改动与验证

- `FrameworkBaker.cs`
  - TextureOverrides 目标键保留 `.png`；
  - TextureOverrides 同步写入目标图的本地化语言变体；
  - 像素哈希幂等状态（按变体记录）；
  - 报告 total 计入 Textures；
  - AT 基础名归一化近似匹配；
  - Custom Companions 四向帧表重排（含已存在条目更新）；
  - Solid Foundations 透明底图 + 多行帧字段；
  - 框架行同步到全部本地化 Data（Buildings 保留译文、Shops 合并 Items）。
- 90 条纹理覆盖已应用并重打 XNB 安装；6 个目标资产逐像素核对。
- 动态鱼 82 条覆盖的 `springobjects` 基础图 + 6 个语言变体已同步；重跑 0 覆盖；
  6 个本地化 XNB 已打包安装，可见像素 0 差异。
- 8 条 AT 新增条目已写入树、重打 9 个 XNB 并安装；解包复核条目存在。
- 216 张宠物帧表已生成，`Data/Pets.xnb` + 216 个 XNB 打包安装成功；解包复核。
- 10 个 SF 建筑已更新，`Data/Buildings.xnb` + 10 张透明底图 XNB 打包安装成功；解包复核。
- 本地化 Data 同步后整份 `Data` 回包 **1579 / 0** 并安装；`ContentHashes.json` 合并新增资产键。
- 完整单测：**1651 通过，0 失败**。
- 备份：`D:\tmp\textureoverride-backup`、`D:\tmp\xnb-before-textureoverride`、
  `D:\tmp\at-fuzzy-backup`、`D:\tmp\at-xnb-backup`、`D:\tmp\pets-before-frame-repack.json`、
  `D:\tmp\pet-xnb-backup`、`D:\tmp\buildings-before-sf-layers.json`、`D:\tmp\sf-xnb-backup`。
