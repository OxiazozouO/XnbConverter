# 框架烘焙性能：重复扫 Mods 树 → 单次发现

日期：2026-10-28

## 1. 原来的复杂度

`FrameworkBaker.Bake` 里每个框架各调一次 `FindPacks`，SpaceCore 又调三次 `AllPacks`；
每次都会：

```csharp
Directory.EnumerateFiles(modsRoot, "manifest.json", SearchOption.AllDirectories)
```

也就是同一棵十万级文件项的 `Mods` 树被完整扫 **11 遍**（8 个框架分支 + 3 次 SpaceCore），
然后每个包再被 `PacksFor` 过滤多次。

## 2. 改法

- `LoadPacks` 只扫一次，解析每个 `manifest.json` 一次；
- `PackInfo` 不再保留完整 `JObject Manifest`，只保留
  `Name / Path / ContentPackForUid / UniqueId` 四个字段；
- 按 `ContentPackForUid` 一次分组，8 个框架分支取自己的小列表；
- `SpaceCore` 三次遍历共用同一个 `allPacks`；
- `Data/Pets` 的原版 cue 集合 `LoadVanillaCueNames` 也只在整轮开头读一次；
- `AddContentHashes` 从每个新增资产扫一遍全部已有键（`O(H×A)`）改成
  一次建 `HashSet`（`O(H+A)`）；
- `MirrorLocalizedData` 只处理**本轮 base 真改过**的资产；base 没变的语言变体
  不再逐个解析。去掉每个语言文档的 `DeepClone` 基线副本，`changed` 只在实际
  写入/替换时置真。

## 3. 实测

同一台机器，对 `D:\tmp\full-v11\tree` 跑 `bake-frameworks`：

| | 墙钟 |
|---|---:|
| 改前（单次基线）| **13.164 s** |
| 第一刀后 | 8.751 / 8.987 / 9.372 s |
| 第二刀（本地化镜像只处理改动 base）| **6.430 / 6.472 / 7.329 s** |
| 第三刀（扫描合并 + 流式扫描 + 落盘/AT 去重）| **2.932 / 3.002 / 3.034 / 3.059 / 3.090 s** |
| 最终（CP-only + 哈希基线 + 纹理/CC 缓存 + AT 预读）| **2.351 / 2.374 / 2.407 / 2.508 / 2.537 s** |

最终版本连续五次中位数 **2.407 s**，相对 13.164 s 基线约 **−82%**。
其中第一次在 `dotnet test` 刚结束后跑出 **21.1 s**，随后马上回到 2.3–2.5 s；
这次离群值来自冷 IO/后台扫描，不计入稳态中位数，但不能当作不存在。

> §3.7–§3.12 的逐项收益是在机器被后台进程干扰时用同机交替 A/B 的分阶段
> 数据验证的；最终整套优化在负载回落后重测，稳定五次中位数为 2.407 s。

### 3.1 SpaceCore 三段扫描合并

改前 `ProcessTextureOverrides`、`SpaceCoreObjectExtensions.Apply`、
`SpaceCoreCropExtensions.Apply` 各自对每个内容包：

1. `Directory.EnumerateFiles(pack.Path, "*.json", AllDirectories)`；
2. `File.ReadAllText` 整份 JSON 只做一次 `Contains` 预筛；
3. 命中目标后 `JsoncReader.ParseFile` 再读一遍并解析。

同一棵 Mods 树因此被读 **3 遍**；实测全量语料 6,243 个 JSON、约 121 MB，
而真正含三类目标的只有 30 个文件（963,499 字节）。

新增 `SpaceCoreJsonCache`：每个包只枚举/读取一次，记录文件命中的目标类型，
只解析含 `ObjectExtensionData` / `CropExtensionData` / `TextureOverrides` 的文件；
三个扫描器共用同一份解析结果，并按目标类型过滤。索引生命周期限于单次 `Bake`，
不跨运行保留；内容包之间并行预读（并行度 4），每包内部顺序不变。

复杂度从“每个包读 3 遍 JSON”降为“读 1 遍 + 只遍历命中文件”：

```
改前：3 × Θ(所有 JSON 文件)
改后：Θ(所有 JSON 文件) + Θ(命中文件)
```

并行只降低墙钟，不改变上面的渐进复杂度。

### 3.2 阶段计时

同一棵全量树，`SDVCP_PROFILE=1`：

| 阶段 | 改前 | 改后 |
|---|---:|---:|
| 数据加载 + 变更基线 | 510 ms | 400 ms（加载 353 + 哈希 47）|
| frameworks（合计）| 806 ms | 675 ms |
| SpaceCore JSON 扫描（新增阶段）| — | 309 ms（CP-only）|
| SpaceCore 纹理处理 | 1,638 ms | 255 ms（文件哈希缓存命中）|
| SpaceCore 对象 | 1,257 ms | 25 ms |
| SpaceCore 作物+捆绑 | 1,250 ms | 6 ms |
| localized-mirror | 32 ms | 31 ms |
| flush | 214 ms | 182 ms |

SpaceCore 三段合计从 **4,145 ms** 降到 **595 ms**（扫描 309 + 纹理 255 +
对象 25 + 作物 6，约 −86%）。

框架内部最终细分（同一轮）：

```
AT 222（含 preload 74）/ SF 145 / CC 264 /
CCCB 2 / MFM 2 / MoreGrass 4 / Bus 28 / Train 8 ms
```

AT（1,439 个变体）和 CC（216 个同伴）现在以图像读写为主，不再是重复 JSON 扫描。

### 3.3 落盘比较去重与 JSON 快路径

- 原来每份 Data 文档先由 `StageIfChanged` 做一次 `DeepEquals`，再由
  `AddLocalized` 对同一对 token 做第二次 `DeepEquals`；现在合并成一个 `Stage`，
  一次比较同时决定是否暂存、是否镜像本地化。`localized-mirror` 从 27 ms 降到 14 ms，
  未改文档少遍历一遍完整 JSON。
- `JsoncReader.Parse` 先直接 `JObject.Parse(text)`；只有合法 JSON 解析失败时才走
  `Strip`（去注释/尾逗号）再解析。合法文件因此少两遍全文扫描和两份大字符串。
  注释、块注释、尾逗号、字符串里的 `//` 都由 `JsoncReaderTests` 覆盖。
  这项在本轮全量墙钟上落在噪声内，但降低了所有 JSON 读取路径的临时分配。

### 3.4 AT 家具尺寸惰性读取

`ProcessFurniture` 原来在遍历变体**之前**就 `TrySize` 解码第一张 PNG，
即使这个模型的全部变体都已经在 `Data/Furniture` 里（全量重跑的常态）。
现在只有真正遇到缺失变体、需要判断 `fastSheet` / 切图框时才读取尺寸；
去重开启时仍在模型开头读取。AT 段从约 461 ms 降到 385 ms，
`fw-at` 结束时的内存从 106 MB 降到 96 MB。首次烘焙（存在缺失变体）行为不变，
仍需读尺寸。

同时把 AT/SF/CC 的模型发现从：

```csharp
foreach (dir in Directory.EnumerateDirectories(pack, "*", AllDirectories))
    if (File.Exists(Path.Combine(dir, "texture.json"))) ...
```

改成直接枚举目标文件（`texture.json` / `building.json` / `companion.json`），
去掉每个目录一次 `File.Exists` 的元数据查询。目录集合与文件集合一一对应，
并显式跳过包根的模型文件以保持旧语义；测试与全量报告均未变化。

### 3.5 SpaceCore 扫描改流式字节查找

文字搜索不再 `File.ReadAllText` 把每个 JSON 解成字符串，而是用 64 KiB
`ArrayPool` 缓冲区流式扫描 UTF-8 字节：

- 三个 ASCII 目标名直接 `Span<byte>.IndexOf`，不做 UTF-8 解码；
- 跨块保留 `max(marker.Length)-1` 字节重叠，目标字符串横跨 64 KiB 边界也能命中；
- 首块检测到 UTF-16/32 BOM 时退回 `File.ReadAllText`；
- 每个内容包复用一块扫描缓冲区，并行度仍为 4。

扫描阶段从 557 ms 降到 393–404 ms；阶段性内存从约 226 MB 降到 212–214 MB，
避免 `File.ReadAllBytes` 一次持有多个大文件时冲到约 290 MB。
新增 2 条测试：跨 64 KiB 边界命中、UTF-16 BOM 退回文本扫描。

### 3.6 Data 文档加载也走 JSON 快路径

`LoadObject` / `LoadArray` / 本地化镜像 / MFM 邮件解析原来直接：

```csharp
JToken.Parse(JsoncReader.Strip(text))
```

绕过了 `JsoncReader.Parse` 的快路径，仍会为每份合法 JSON 构造 `Strip` 的两份全文副本。
现在统一走 `JsoncReader.ParseToken`：先直接 `JToken.Parse`，解析失败才 Strip。

- 数据加载阶段内存从 78 MB 降到 59 MB，克隆后从 114 MB 降到 95 MB；
- 墙钟在 318–326 ms 左右波动，未观察到显著下降；
- 释放的 19 MB 是 `Strip` 两份大字符串的临时分配，对后续峰值与 GC 有实际收益。

### 3.7 已存在的宠物帧表不重复编码/写盘

`ProcessCompanions` 每次重跑都会把 216 张同伴贴图重排成 128×128 帧表，
再无条件 `SetPixels`，最后统一 PNG 重编码写盘。即使产出树里已经是同一张图，
也会重复编码/写入。

现在生成帧表后先按**像素逐字节**与产出树当前内容比较：

```csharp
if (!images.TryGetPixels(petRel, out var current, out var w, out var h, out _)
    || w != 128 || h != 128
    || !current.AsSpan().SequenceEqual(sheet))
{
    images.SetPixels(petRel, sheet, 128, 128);
}
```

只有完全一致才跳过标脏/写盘；内容不同、文件缺失或解码失败都照旧重写。
`ImageCache.Flush` 也改为只对真正要写（dirty / 整文件来源）的条目建目录、
计算输出路径，不再为只读进缓存的图片做无意义的目录操作。

同一台机器交替 A/B（6 次，两个只差这一补丁的 Release 产物）：

| | 不跳过 | 跳过 |
|---|---:|---:|
| `flush` | 578 / 434 / 392 ms | 260 / 267 / 253 ms |
| 进程 CPU | 7.08 / 7.98 / 7.03 s | 6.63 / 7.06 / 6.31 s |

216 张 `*_pet32.png` 的 SHA-256 在两种二进制跑完后完全一致；
全量报告仍是 0 新增。墙钟在当时的磁盘负载下被其它进程压到同一水平，
但 CPU 与写盘量都下降，属于无损优化。

### 3.8 SpaceCore 只扫描 CP 包

原先 SpaceCore 三个扫描器遍历 `allPacks`（491 个 manifest 包），
包括 AlternativeTextures、FTM、DLL 模组等根本不会经过 Content Patcher 的 JSON。
但 `ObjectExtensionData` / `CropExtensionData` / `TextureOverrides` 是 CP 的
`EditData` 目标，只有 `ContentPackFor: Pathoschild.ContentPatcher` 的包会写入。

现在只把 `packsByFramework[CpUid]` 交给 SpaceCore 索引和三个扫描器：

| | 原 `allPacks` | 只扫 CP |
|---|---:|---:|
| manifest 包 | 491 | 206 |
| JSON 文件 | 6,243 | 3,975 |
| JSON 体积 | 约 121 MB | 约 82 MB |

全量语料里 30 个含目标字符串的 JSON 全部位于 CP 包内，因此报告和产出完全不变。
同机交替 A/B（只差这个过滤）：

| | allPacks | CP-only |
|---|---:|---:|
| `spacecore-scan` | 946 / 1062 / 965 ms | 581 / 676 / 700 ms |
| 进程 CPU | 9.17 / 10.08 / 10.58 s | 7.72 / 8.41 / 7.78 s |

语义边界：只有 CP 能把 `EditData` 应用到 SpaceCore 自定义资产；
非 CP 框架包里的同名 JSON 即使存在也不会生效，所以不扫它没有内容损失。

### 3.9 用 JSON 哈希替代 15 份 DeepClone 基线

原来每份 Data 文档在烘焙前 `DeepClone` 一份完整 `JObject` 作为基线，
结束时再用 `JToken.DeepEquals` 判断是否变化。实测 15 份文档：

| | 时间 |
|---|---:|
| 15 份 `DeepClone` | 200–305 ms |
| 15 份 `DeepEquals` | 20–27 ms |
| 15 份序列化 + SHA-256（前后各一次） | 37–54 ms |

现在改为 `HashJson`：

```csharp
var beforeFurniture = HashJson(furniture);
// ... 框架处理 ...
if (beforeHash.AsSpan().SequenceEqual(HashJson(current))) return;
```

- 固定 `Formatting.None` 写入内存流后取 SHA-256，每份文档只留 32 字节基线；
- 变化检测仍精确到 token 内容（注释不进入 token，实际写盘也不含注释）；
  序列化相同的 `int`/`long` 不再触发无意义的写盘，输出字节不变；
- 15 份文档的基线阶段从 `clone-data` 的 129–305 ms 降到 `hash-data` 的
  47 ms（前后两组哈希都用 4 路并行）；阶段内存从 95 MB 降到 76 MB；
- 完整单测通过，全量重跑仍 0 新增，报告与宠物帧表 SHA 不变。

### 3.10 TextureOverrides 文件级哈希缓存

纹理覆盖的幂等判断原来每次都：

1. `images.TryGetPixels` 解码目标 PNG；
2. 对整张 RGBA 重新算 `HashPixels`；
3. 与 `_texture-overrides.json` 里的像素哈希比较。

90 张目标图在重跑时仍要逐张解码/哈希。现在新增
`_texture-overrides-files.json`，记录**目标 PNG 文件字节**的 SHA-256：

- 先比较文件哈希；命中说明磁盘上就是上次写出的完整文件，直接跳过，
  不解码、不算像素哈希；
- 未命中才回退到原来的像素哈希校验；
- 应用过或通过像素校验的目标，在 `images.Flush()`/`output.Flush()` 之后
  重新计算文件哈希并写回缓存（首轮 1.3 KB）。
- 像素级状态文件也改为整轮只加载一次、最后写一次；原来每个 CP 包都重新
  读取 `_texture-overrides.json`，206 个包合计多出一次不必要的 I/O。

实测同一棵全量树：

| | 无文件缓存 | 有文件缓存 |
|---|---:|---:|
| `spacecore-texture`（首轮）| 570 ms | 726 ms（建缓存）|
| `spacecore-texture`（后续）| 570 ms | 226–255 ms |
| 纹理阶段后内存 | 221 MB | 178 MB |
| `flush` | 314 ms | 214 ms |

全树 19,368 张 PNG 在启用前后 SHA-256 全部相同；新增测试
`SpaceCore纹理覆盖重复跑不叠加并写文件哈希缓存` 会删除像素级状态、
只保留文件哈希跑第二遍，确认半透明帧不会被叠两次。
若确实要强制重涂，需要同时删除 `_texture-overrides.json` 和
`_texture-overrides-files.json`。

### 3.11 AT 模型并行预读

AT 的 `TextureModels` 要遍历 17 个包的目录树、找并解析 381 个 `texture.json`
（共 1,439 个变体）。原来是在处理循环里逐个惰性枚举，模型扫描/解析约 300 ms，
且全在串行路径上。

现在先按包并行预读模型列表（并行度 4），再按原来的包顺序、模型顺序处理：

```csharp
Parallel.For(0, atPacks.Count, ..., i =>
    atModels[i] = TextureModels(atPacks[i].Path).ToList());
```

- 只并行“只读的文件枚举 + JSON 解析”，共享文档仍由单线程按原顺序改；
- 某包预读异常时按原顺序串行重跑，不改变异常行为；
- `frameworks` 里的 AT 段从 454–557 ms 降到 180–237 ms
  （其中 `at-preload` 94–134 ms）；
- `fw-at` 阶段内存从 82 MB 到 84 MB，几乎不变；全树 PNG 与报告 SHA 不变。

### 3.12 CC 帧表源图/产出哈希缓存

CC 每次重跑都会对 216 个同伴重新做：解码源贴图 → 重排 4×4 帧表 →
解码产出帧表做像素比较。现在新增 `_cc-sheets.json`，每项记录：

- 源贴图文件 SHA-256；
- `*_pet32.png` 产出文件 SHA-256。

两者都匹配时直接跳过源图解码、帧表重排和产出解码，只保留宠物 JSON 的构造
（JSON 仍按原逻辑比较/更新）。生成逻辑一旦变化必须提升 `StateVersion`，
旧缓存会被整体丢弃。源图变化、产出缺失或被改动都会回退重建。

| | 改前 | 改后 |
|---|---:|---:|
| `fw-cc` | 473 ms | **264–337 ms** |
| CC 阶段后内存 | 170 MB | **140 MB** |
| `flush` | 224 ms | 202–239 ms |
| 状态文件 | — | 50 KB |

新增测试：第一次烘焙后改写 `Animals/Test.png`，第二次必须重建帧表，
第三次才恢复稳定；全树 19,368 张 PNG 和框架报告 SHA 均不变。

输出仍是：

```
合计：... 宠物 0 ...（全量树已是幂等状态）
```

运行都 0 新增，未写坏产出树；`_frameworks-report.json` 与改动前逐字节相同。
非幂等的本地化镜像路径由 `FrameworkBakerTests` 的 Objects/Crops 中文变体测试覆盖。

## 4. 空间

- 不再把每个 manifest 的完整 `JObject` 留在内存里，只保留四个字符串字段；
- 框架分支不再重复创建扫描枚举/过滤结果，按 UID 分桶复用；
- cue 集合从每个 CC 包解析一次降为整轮一次；
- `MirrorLocalizedData` 不再为每个语言变体 `DeepClone` 一份完整文档做末尾比较，
  只保留当前解析结果；
- `AddContentHashes` 额外增加一个键名 HashSet（约已有 ContentHashes 键数量），
  换掉 `O(H×A)` 的线性扫描。
- SpaceCore 索引只保留**命中目标**的 30 个文件（963,499 字节）的解析树；
  不再让对象/作物扫描器各读一遍 121 MB 全量 JSON。预读并行度限制为 4，
  扫描用每包一块 64 KiB 池化缓冲区，不再把整份大 JSON 读成字符串/字节数组；
  阶段采样峰值从改前约 300 MB 降到约 **186 MB**。
  索引随单次 `Bake` 结束释放，不跨运行累积。
- Data 文档加载不再为每份合法 JSON 保留 `Strip` 的两份大字符串，
  加载后内存从 78 MB 降到 59 MB。
- 15 份 Data 文档不再各自 `DeepClone` 一份完整基线，只保留 32 字节 SHA-256；
  基线阶段内存从 95 MB 降到 76 MB；纹理/CC 文件哈希缓存后阶段采样峰值约 186 MB。

## 5. 验证

- 完整单测：**1692 通过，0 失败**（含流式扫描、纹理覆盖文件哈希、CC 源图变更失效）
- 全量 `bake-frameworks`：0 新增、幂等、报告保留；
  `_frameworks-report.json` 与改动前 SHA-256 相同
- 216 张 `*_pet32.png` 在“跳过完全相同的帧表”前后 SHA-256 全部一致
- 全树 19,368 张 PNG 在纹理文件哈希缓存启用前后 SHA-256 全部一致
- 全量语料 30 个含 SpaceCore 目标的 JSON 全部位于 CP 包内；
  `allPacks` 扫描与 CP-only 扫描的目标文件集合一致
- 未启动游戏

## 6. 清空编译缓存后的整机测试

按“换掉基础材料”的方式，删除了 `SdvCpTool.Core` / `SdvCpTool.Cli` /
`SdvCpTool.Core.Tests` 的 `bin`、`obj`（共约 39 MB），然后从零构建：

| 步骤 | 墙钟 |
|---|---:|
| 冷构建 CLI | **21.746 s** |
| 冷构建并跑完整单测（1692 通过）| **22.248 s** |
| 冷构建后第一次全量烘焙 | **3.404 s** |
| 第二次 | 2.734 s |
| 第三次 | 2.707 s |
| 缓存恢复后的增量构建 | 2.492 s |
| 增量构建 + 完整单测 | 12.184 s |

三次全量烘焙都是 0 新增，`_frameworks-report.json` 仍为：

```
d25732c236ac179f0175103636dedbb5f2855dc2eee3449228da7498f1a57b11
```

结论：清空编译缓存主要增加**构建**成本（约 22 s），不改变稳态烘焙路径；
冷构建后的第一次运行仍有约 0.7 s 的额外 IO 波动，第二次起回到 2.7 s 左右。

## 7. 全量冷转换基线（删除中间产物）

在全新输出根上从 `D:\sdv_tree` 重跑 232 个包的 Load/Edit，再跑 `bake-flips`
和 `bake-frameworks`（不读取旧产出树或旧状态缓存）：

| 步骤 | 结果 |
|---|---:|
| 产出树 | 2.1 GB |
| CP 转换（232 包 ×2 趟）| 约 2 小时以上 |
| `bake-flips` | **1445 s（约 24 分钟）**，1,252 张地图 |
| 冷树首次 `bake-frameworks` | **27.6 s** |
| 家具 / 装饰 / 作物 / 可制作 / 宠物 / 纹理覆盖 | 1089 / 4 / 20 / 157 / 216 / 90 |

结论：稳态的 2.4 s 只代表“已有产出 + 状态缓存命中”；真正冷启动的大头是
CP 转换和 `bake-flips`，不是 `bake-frameworks` 的扫描路径。

## 8. 硬画资源修复的单线程剪枝

按要求先不动多线程。`HardDrawnResourceRepair` 保持单线程，只改算法：

- 合成对象图时记录每个 16px 格子是否有不透明像素，并建二维前缀和；
  形状匹配的某个位置若 footprint 内一个占用格都没有，`score` 必然为 0，
  直接跳过，不再逐像素扫整块；
- 单格 object 哈希按 `(TileSheet, TileIndex)` 缓存，Back 层同一个 tile 不重复做
  256 像素的 FNV 哈希。

56 张含硬画实例的受控地图树（同一份输入、单线程）：

| | 墙钟 |
|---|---:|
| 无剪枝 | **389 s** |
| 占用前缀和 + 哈希缓存 | **254 s** |

输出地图 SHA-256 完全一致，都是 12 个实例 / 10 张地图。
剪枝只跳过“必然 score=0”的候选位置，不改变匹配结果；没有引入线程。

### 8.1 像素位图 + 不透明数下界（2026-10-29）

254 s 仍然不可接受。真正的热点不是“空地图”，而是密集地图上每个参考块
（48×96 的树、32×48 的灌木）对每个 16px 位置逐像素比较。改成两层**精确**加速：

1. 合成对象图的不透明像素做成位图（每行 `ulong` 序列）。候选位置用
   **64 位 AND + popcount** 一次算出交集，不再逐像素读 4608 个 RGBA；
2. 合成图的不透明像素二维前缀和给出 footprint 内的 `B`。设参考块不透明数为 `A`，
   则 `inter ≤ min(A,B)`、`uni ≥ max(A,B)` ⇒ `B < 0.9A` 时 score 必小于 0.9，
   直接跳过。这是数学下界，不是采样启发式；
3. 树的棕色比例检查原先对每个候选都扫整块，现在只在 `score ≥ 0.9` 后做。

受控 56 张地图：

| | 墙钟 | 输出 |
|---|---:|---|
| 占用前缀和 + 哈希缓存 | 254 s | 12 实例 / 10 张地图 |
| 像素位图 + 不透明数下界 | **4.77 s** | SHA-256 与上表逐字节一致 |

全量 1,252 张地图（同一棵冷产出树）：

| | 墙钟 | 结果 |
|---|---:|---:|
| 旧实现（只跑到 256 张，按同分布外推） | 约 225 s / 256 张 | — |
| 新实现 | **41.37 s / 1,252 张** | 1,111 实例 / 109 张地图 |

同一次全量烘焙里 `FlipBakePass` 本身（322 张带标记）是 **17.1 s**，现在
`bake-flips` 的总量级从约 24 分钟降到 **1 分钟以内**。仍然单线程、输出无损。

## 9. 单线程下的全局后处理降为 dirty 集

单个包在已缓存审计的树上跑 `convert`，原先约 **10.29 s**，其中：

| 阶段 | 改前 |
|---|---:|
| 本地化镜像 | 7.234 s |
| 婚姻修复 | 0.843 s |
| NPC 资产物化 | 0.468 s |
| 地点类型清理 | 0.671 s |
| 四道审计 | 约 0.6–0.9 s |

这些步骤每包都把整棵 17,349 文件的树重新枚举/读取，但上一包已经处理过的结果不会因为这一包而变。

现在统一按 `OutputTree.PlannedWrites`（这一包真正写过的文件）取 dirty 集：

- 本地化镜像只处理本轮写过的本地化文件，以及本轮写过的 base 对应的语言变体；
- 婚姻修复只在本轮碰过 `Data/Characters` / `Weddings` / `NPCGiftTastes` 时运行；
- NPC 物化只处理本轮写过的 `Data/Characters*`；
- 地点类型清理只处理本轮写过的 `Data/Locations*`。

同一个包（`[CP] Beach Crab`，同一棵冷树、同一审计缓存）复测：

| 阶段 | 改前 | 改后 |
|---|---:|---:|
| 本地化镜像 | 7.234 s | **0.015 s** |
| 婚姻修复 | 0.843 s | **0** |
| NPC 资产物化 | 0.468 s | **0** |
| 地点类型清理 | 0.671 s | **0** |
| 单包总墙钟 | **10.293 s** | **2.171 s** |

这是单线程下降，不依赖并行。前提是链式转换中前一个包已经处理过自己的写入；
因此每一包必须保留自己的 `PlannedWrites` 语义，不能把整棵树当成“本轮都变过”。

### 9.1 同进程共享产出树索引

链式转换里每个包都新建 `OutputTree` 与 `ResourceTree`，各自递归枚举 17k 份文件
并建字典。`convert-batch` 现在打开 `SDVCP_SHARED_TREE_INDEX=1`：
首次建树/续写扫描一次，之后每次 `OutputTree.Flush` 把新路径追加进共享索引；
两个类直接复用同一份排序文件表与资产名表。普通单包 `convert` 不打开，行为不变。

小批量（3 包 ×2 趟，同一棵冷树）A/B：

| | 墙钟 |
|---|---:|
| 不共享索引 | 41.16 s |
| 共享索引 | 38.41 s |

产出树 `diff -rq` 为 0。全量 464 次调用摊下来约省 **3 分钟**。

### 9.2 图集尺寸只读 PNG 头

`ImageCache.TryComposeTaller` 原先**无条件** `Png.Read` 新图集（整张解码成 RGBA），
再判断是否需要“保留下半部分”；`DimensionsDiffer` 同样整张解码只为拿宽高。
Downtown Zuzu 一包 85 张地图实测 `LocalMapTilesheets.Copy` **148 s**。

改为先读 PNG IHDR 的宽高，只有确实需要合成时才解码：

| | `LocalMapTilesheets.Copy` | 整包 Load |
|---|---:|---:|
| 整图解码 | 148.1 s | 148.7 s |
| 只读 PNG 头 | **4.2 s** | **4.8 s** |

合成条件与像素级结果不变（尺寸相同时本来就不合成）；`PngHeader` 读的就是
`Png.Read` 会用的同一份 IHDR。

### 9.3 语言变体/冻结本地化不再反复序列化

两条写路径原来都在**每条补丁**上把整份大文档 `ToString` 再 `Parse`：

- 语言变体：下一包要读上一包暂存的变体，`ReadTextOrNull` 会触发懒生成函数，
  于是 9 个语言 × 每条补丁都序列化一次几万节点的文档；
- `.zh-CN` 镜像：`MirrorDeltaToFrozenLocale` 每次都读出字符串、解析、克隆、再写字符串。

现在 `OutputTree` 增加 `TryStageLazyJson` / `ReadJsonOrNull`：变体暂存的是 `JToken`，
同一次运行里下一包直接拿对象；已有本地化变体**原地叠**，不再整份克隆。
镜像也走同一对 API，落盘时才序列化一次。

| 包 | 阶段 | 改前 | 改后 |
|---|---|---:|---:|
| 聚宝盆 制作（i18n 变体） | 阶段 2 | 49.9 s | **5.36 s** |
| 聚宝盆 作物（`.zh-CN` 镜像） | 阶段 2 | 10.9 s | **4.14 s** |
| SVE（混合） | 阶段 2 | 25.0 s | **11.2 s** |
| SVE | 整包 edits | 67.9 s | **34.7 s** |

产出内容仍按同一份终态序列化（`Formatting.Indented`），只是把中间态留在内存里。

## 10. C# 同进程批量转换

新增 `SdvCpTool.Cli convert-batch`：在 C# 里发现/排序 CP+FTM 包，
同一进程先循环所有 Load，再循环所有 Edit，共享 Mods 索引和报告根；
产出树独立在 `<out>/tree`，避免报告目录让 `OutputTree` 误判 append。

全量 232 包 ×2 趟：

- 总墙钟 **1214.3 s（约 20.2 分钟）**；
- 464 次单包调用中位 **734.5 ms**，最慢 **68.9 s**；
- 包序已与 Python `convert-mods.py` 的 `order()` 逐项核对一致；
- 仍有少量包返回 1（未落地/写入失败，不等同进程崩溃）。

相对 Python 每包起进程的 2 小时级，已经是约 6 倍，但仍达不到 5 分钟：
主要成本变成每包仍创建一次 `OutputTree`/`ResourceTree` 并跑审计；
真正 5 分钟需要把这两者也跨包共享，并把审计移到整批末尾。

### 10.1 2026-10-29 全量冷启动复测

同一台机器、同一棵 `D:\sdv_tree`、全新输出根，命令：

```
convert-batch --mods <Mods> --tree D:\sdv_tree --out <新根> \
  --content-hashes ContentHashes.json --no-bake-flips --no-audit
bake-flips --tree <新根>\tree
bake-frameworks --tree <新根>\tree --mods <Mods>
```

| 步骤 | 改前 | 改后 |
|---|---:|---:|
| CP 两趟（232 包 ×2） | 1214.3 s | **729.2 s** |
| ├ Loads | — | 129.2 s |
| └ Edits | — | 598.9 s |
| `bake-flips` | 1445 s | **54.6 s** |
| `bake-frameworks` | 27.6 s | **10.5 s** |
| 合计 | 约 44.8 分钟 | **约 13.2 分钟** |

输出树与旧冷树逐 JSON 解析比对：内容一致；只有 3 个文件因包序修正产生差异，
其余差异是 JSON 键顺序（`HashSet` 跨进程迭代顺序，语义不变）。

单线程仍达不到 5 分钟。Edits 的 599 s 里：

- 91 个包 >3 s，合计 370 s；
- 最慢：SVE 47.7 s、Downtown Zuzu 44.5 s、Sword & Sorcery 35.1 s、Ridgeside 31.4 s；
- 其余 141 个包合计 229 s（中位 0.6 s/包，主要是落盘与每包固定成本）。

这些已经是**冷盘首次写出 2.1 GB 产出**的实测值。要继续压到 5 分钟，
要么允许独立包并行（顺序依赖之外），要么引入跨运行的持久缓存（但这就不再是冷启动）。
单线程 + 全量冷启动 + 完整审计/产出三者同时存在时，当前机器上的下限就在 12–13 分钟量级。

### 10.2 .NET 诊断工具结果（16 包冷链样本）

工具：`dotnet-trace`（`dotnet-sampled-thread-time` + `gc-verbose`）、
`dotnet-counters`、`dotnet-gcdump`、`dotnet-dump`（`dumpheap -stat` / `gcroot`）。

**CPU 采样（221 s 管理线程 CPU，按 `CPU_TIME` 的父函数归并）：**

| 函数 | CPU |
|---|---:|
| `System.Buffers.Utilities.GetMemoryPressure()`（ArrayPool Gen2 回调） | **60.9 s** |
| `XnbConverter.Xact...MsAdpcm.Encode` | **44.9 s** |
| `ConditionalWeakTable.Container.Finalize`（终结器线程） | **38.8 s** |
| `GC.GetGCMemoryInfo`（被内存压力查询重复调用） | **18.3 s** |
| NVorbis OGG 解码（ToPcm16/Decode/Mdct） | 约 21 s |
| Newtonsoft JSON 读取 | 4.3 s |

**内存 / GC：** 峰值工作集 1.6–2.0 GB，GC 堆 1.6–1.8 GB，LOH 0.98–1.35 GB，
碎片率峰值约 50%，GC 暂停峰值 0.5–0.7 s/s（均值约 6%）。
`dumpheap -stat`：`Byte[]` 558 MB（199 个数组，单个多有 16–33 MB）、
`Int16[]` 71 MB（3 个音频 PCM）、Newtonsoft `JToken` 家族约 100 MB。
`gcroot` 显示 33 MB 数组挂在 `OutputTree` 的 `Dictionary<string, byte[]>`（待落盘字节）；
现在 `OutputTree.Flush` 后清掉 `_pendingBytes/_pendingCopy`，不再把这些大数组留到自检阶段。
文本/JSON 暂存仍保留，因为 `CapeOrbBridge` 的既有契约允许 Flush 后读到本轮文本。

**资源 / IO：** 采样里 72% 是线程池空闲等待、21% 是 IO 完成端口等待；
顺序写盘实测 1.8 GB/s，瓶颈不是磁盘吞吐，而是小文件创建/复制、GC 停顿和终结器。
`DOTNET_gcServer`、强制 LOH 压缩、`GCConserveMemory`、关闭 ArrayPool Trim
做了配对测试：差异在 ±3% 噪声内，不能算稳定收益，均未保留。

**下一步真正值得做的：**

1. `MsAdpcm.Encode`：每块 `new int[channels]` ×7 次测量，可改为跨块复用缓冲区；
   测量轮不写目标字节；这是无损的，预计能从 45 s CPU 里拿回一截。
2. NVorbis → ADPCM：现在是整首 OGG 解成 `short[]`（3 个 71 MB 级别）再编码；
   改成分块流式解码/编码，能直接砍掉峰值 LOH。
3. `GetMemoryPressure`/终结器 116 s CPU 是 Gen2/LOH 压力的下游症状；
   减少大数组的长期存活（已清 pending bytes）比调 GC 开关更有效。
4. 继续追 5 分钟仍需要包级并行；单线程在这些工具数据下已经确认是 GC + 编码 + 小文件 IO 的组合下限。

### 10.3 池化落地（2026-10-29）

**`ByteBufferPool`**：按**精确长度**分桶的 byte 池，而不是 `ArrayPool<byte>.Shared` 的
2 的幂桶 —— 图片路径里有 `pixels.Length` 的直接拷贝，超长数组会读进旧尾部像素；
精确长度保持所有既有 `.Length` 语义。池上限 128 MB，超过就丢弃；`Rent` 命中时清零，
保持 `new byte[]` 的零初始化语义。

接入点：

- `EditImageApplier` 的合成结果；
- `ImageCache.TryComposeTaller` 的合并图；
- `MapApplier` 扩展图集；
- `SeasonalSpriteEmitter` 的四季横排图；
- `LoadMapTbin` 读取 `.tbin` 的载荷（`FileInfo.Length` 精确长度，`ReadExactly` 读入）。

`ImageCache.Release()` 在 `OutputTree.Flush` 之后、自检之前把像素缓冲归还；
`bake-frameworks` 在写完状态文件后同样归还。旧 `Entry` 被覆盖时也立即归还。
`OutputTree` 对 `_pendingBytes` 里来自池的数组负责：覆盖/删除时归还，Flush 写完统一归还。
载荷被图集归一化替换时（`SwapPayload`）旧池数组也立即归还。

受控 16 包 A/B（同一棵输入、同一输出根分别新建，输出 `diff -rq` 为 0）：

| 轮次 | 关闭池 | 开启池 |
|---|---:|---:|
| 1 | 159.5 s | **146.6 s** |
| 2 | 115.5 s | 116.4 s |

墙钟受机器噪声影响，不能宣称稳定百分比。`dotnet-counters` 的 16 包样本里，
分配速率峰值从 **1.13 GB/s 降到 0.87 GB/s**（约 −23%），平均分配率基本不变；
峰值 LOH/工作集仍由“本包还没 Flush 的活跃图片/地图”决定，池化不会降低这部分峰值。
所以结论要说清楚：**池化降低的是跨包的重复申请/释放和分配尖峰，不是活跃产出的峰值**。
池上限设为 128 MB，避免长期保留大块 LOH 反而加重碎片。

ADPCM 的 `EncodeBlock` scratch 也改为跨块复用（每块 7 个预测器 ×4 个
`int[channels]` 不再重复申请），编码字节不变；单测 1692 全过。

### 10.4 大于 2 MB 的待落盘字节溢写（2026-10-30）

地图 `.tbin` 载荷是 16–33 MB 级，`StageBytes` 原来把它们在内存里挂到 `Flush`。
现在 `content.Length >= 64 MB` 时（早期 2 MB 阈值会让 1,252 张地图多写一遍临时文件，
全量加载反而变慢，见 10.12）：

1. 立即写到产出自树同卷的 `<out>.stage` 临时文件；
2. 池里的源数组立刻 `Return`；
3. `Flush` 时 `File.Move(temp, final)`，不再从内存整块写；
4. `ReadBytesOrNull` / `ResolveReadablePath` / `Exists` 都认这条暂存。

延迟写语义不变，临时目录在 Flush 后删除。16 包样本 `dotnet-counters`：

| | 溢写前 | 溢写后 |
|---|---:|---:|
| LOH 峰值 | 1.15 GB | **1.08 GB** |
| LOH 均值 | 505 MB | **451 MB** |
| 碎片率峰值 | 74.4% | **36.9%** |
| 墙钟 | 113–116 s | 122 s（波动/额外临时写） |

地图树经 `bake-flips` 解析通过。结论：溢写降低 LOH 峰值/碎片，但墙钟没有稳定收益；
它是内存侧优化，不是速度优化。

### 10.5 JSON 落盘改为流式序列化（2026-10-30）

`OutputTree.Flush` 的 `_pendingJson` 原来对每份文档执行
`JToken.ToString(Formatting.Indented)`，先生成整份字符串，再由 `File.WriteAllText`
做 UTF-8 编码。大文档（`Data/Objects` 一类）这一步本身就会产生几十 MB 的临时字符串/字符数组。

现在没有被中途读过的 JSON 直接：

```
FileStream -> StreamWriter(UTF8 no BOM) -> JsonTextWriter(Formatting.Indented)
     -> JToken.WriteTo
```

只有中途被 `ReadTextOrNull` 读过、已有缓存字符串的才继续走 `File.WriteAllText`。
同一套 `JsonFile.Write` 还接到：

- 每包 `report.json`（`ReportWriter`）；
- `bake-frameworks` 的报告与状态文件；
- `CapeOrbBridge` 的事件 JSON（改为 `StageJson`，Flush 时流式写）；
- `AudioChangesMerger` 的 XACT 清单（改为 `StageJson`）。

新增单测 `流式JSON落盘与ToString逐字节一致`，覆盖中文、数字、嵌套对象/数组/null；
全量单测 **1693 通过，0 失败**。

### 10.6 其它单线程缓存/生命周期收尾（2026-10-30）

- `LoadApplier.FindMapConfig`：地图 `.config` 模板按 `LoadApplier` 缓存，并在同一
  `InputRoot` 上跨包复用（任何一份 TideReader 配置都等价），一张地图不再扫一遍 17k `InputFiles`；
- `OutputTree.ImageConfigUnder`：同一目录前缀的第一份图片 `.config` 缓存，大图集几十张图
  不再反复扫候选；
- `ImageCache.Release()` / `MapCache.Release()`：在 `OutputTree.Flush()` 后释放像素、地图对象图
  和源缓存，自检/下一包只从磁盘读；
- `CapeOrbBridge` 的事件 JSON、`AudioChangesMerger` 的 XACT 清单改用 `StageJson`，
  Flush 时走流式 JSON，不再先拼完整字符串；
- `bake-frameworks` 的报告/状态文件也走 `JsonFile.Write`。

以上都不改变产物内容；`bake-flips` 解析、单测均通过。

### 10.7 OGG 直接解成 `short[]`，少一份 PCM 中转（2026-10-30）

`AdpcmMusic` 原来：

```
NVorbis -> OggVorbis.ToPcm16 -> byte[] PCM16
        -> TryEncodeWav(byte[]) -> short[] PCM -> MsAdpcm.Encode
```

大音乐在转换期间同时活着 `byte[] PCM16` 和 `short[] PCM` 两份。
现在 `.ogg` 路径直接：

```
NVorbis.VorbisReader.ReadSamples(float[])
        -> short[]（同一转换公式 `(short)(x * 32767f)`）
        -> MsAdpcm.Encode
```

新增 `TryEncodeWav(short[], channels, rate, ...)` 重载；旧的 `byte[]` 重载保留给测试/调用方，
内部转成 `short[]` 后走同一条路。`.wav` 源仍走原有校验路径。

再进一步：`.ogg` 现在不保留整首 `short[]`，而是 **128 采样一块**直接编码：

```
NVorbis.ReadSamples(float[]) -> 一个 short[128*channels]
     -> MsAdpcm.EncodeOneBlock -> MemoryStream<ADPCM>
```

`MsAdpcm.EncodeOneBlock` 与整首 `Encode` 用同一套逐块预测器选择逻辑；
单测 `OGG分块编码与整首编码逐字节一致`（需要 `SDVCP_OGG_FIXTURE` 指向真实 ogg）验证
**wav 字节与旧路径完全相等**。最终 wav 头/块数/末块补零/`EntryShape` 均不变。

`.wav` 源也改成逐块从已读入的 PCM 字节编码，不再整份转 `short[]`；
单测 `WAV分块编码与整首编码逐字节一致` 验证 wav→ADPCM 输出完全一致。
单测总数 **1695**（OGG 夹具存在时额外做逐字节比对）。

### 10.8 ADPCM 立体声/单声道热循环展开（2026-10-30）

新 trace（3 包：SVE / Downtown Zuzu / East Scarp）显示 `MsAdpcm.EncodeBlock`
仍占 **89.5 s CPU**，是单线程剩余最大的纯计算项。原实现对任意声道数走
`Span` 索引 + `Math.Clamp` + 每帧两声道循环；音乐绝大多数是 1/2 声道。

现在 `EncodeBlock` 对 `channels == 1/2` 分别进入展开版本：

- 立体声：两个声道的旧/新采样、delta、预测都在局部变量里，热循环无索引；
- 单声道：同样展开；
- 两版都保持原字节布局：块头偏移、nibble 高/低半字节、末块补零、误差累计顺序不变。

受控 3 包运行（同一输入、两条路径各跑一次）：

| | 墙钟 |
|---|---:|
| 通用路径 | 139.8 s |
| 展开路径 | 139.2 s |

墙钟几乎无差（这两包不是纯 CPU 瓶颈），但整棵产出树 SHA-256 比较 **0 差异**；
trace 的 CPU 归属说明它仍值得保留。剩余 CPU 大头是 NVorbis `PacketReadByte`（21.5 s）
与 JSON 读取（6 s），以及 GC/IO 等待。

### 10.9 音频 WAV 拼接不再 ToArray 复制（2026-10-30）

分块音频用 `MemoryStream` 累积 ADPCM 后，原来先 `ToArray()` 再 `BuildWav`
复制一次；现在 `BuildWav` 接受 `(buffer, length)`，直接用 `GetBuffer()` 的有效长度，
只做最终 wav 那一次复制。对长音乐少一份 `payload` 大小的 LOH/拷贝。
OGG/WAV 分块与整首编码的逐字节测试仍通过。

`JsonFile.Write` 的 FileStream/StreamWriter 缓冲从 64 KB 降到 4 KB：
报告/变体通常是几 KB，64 KB 缓冲在每包几十份文件时反复分配/清零（trace 的
`AllocateBuffer` 占 10 s）；大文件仍按 4 KB 分块写，输出字节不变。
单测 **1695** 全过。

### 10.10 ADPCM 预测器试算早停（2026-10-30）

每个块要试 7 个预测器，原来每个预测器都把 128 个采样完整算完。
现在测量轮带一个 `cutoff = 当前最小误差`：累计平方误差一旦**严格大于**当前最优，
后续误差只增不减，必然赢不了 ⇒ 立刻返回 `long.MaxValue`。
这是精确剪枝，不改变被选中的预测器、块字节或误差总计。
立体声/单声道/通用三条路径都接入了早停。

单线程 trace 里 `MsAdpcm.EncodeBlock` 仍是最大的纯 CPU 项（64.7 s / 3 包）；
早停只减少“注定失败”的预测器试算，不引入线程。

### 10.11 重新采样后关闭 SharedArrayPool Trim（2026-10-30）

最新 3 包 trace（SVE / Downtown Zuzu / East Scarp）里，最大的异常项变成
`ConditionalWeakTable` 枚举（22.0 + 19.8 s）和 `GC.GetGCMemoryInfo`（33.3 s），
调用栈全部是：

```
Finalizer Thread -> Gen2GcCallback.Finalize
  -> SharedArrayPool<byte/char/int>.Trim
  -> ConditionalWeakTable.Enumerator.MoveNext
```

这是运行时 ArrayPool 的 Gen2 回调，不是业务代码。把
`System.Buffers.ArrayPool.EnableSharedArrayPoolTrimming` 在 CLI **最前面**关掉，
并在同一输入上配对：

| | 墙钟 |
|---|---:|
| 默认 Trim | 185.6 s |
| 关闭 Trim | **169.8 s** |

**但重新 trace 后结论反转**：关闭 Trim 后 `GetMemoryPressure` + `GC.GetGCMemoryInfo`
反而升到 **112 + 109 s CPU**（CWT 消失，但 GC 压力变大），trace 总 CPU 从 197 s 升到 430 s。
因此该开关**没有保留**，CLI 恢复运行时默认行为；关闭 Trim 的 9% 墙钟差异只是配对噪声。
ADPCM 早停、流式 OGG/WAV、JSON 流式仍保留。

### 10.12 全量复测与 2 MB 溢写阈值回退（2026-10-30）

在关闭 Trim 的版本上全量复测（232 包，`--no-audit`）：

| 步骤 | 时间 |
|---|---:|
| Loads | 207.4 s |
| Edits | 825.7 s |
| CP 合计 | **1039.4 s** |

比早期 729.2 s 更慢。2 MB 溢写让 1,252 张地图每张多写一次临时文件再 Move，
加载从 129 s 升到 207 s；但把阈值提到 64 MB 再测时，SVE 编辑从 57 s 反弹到 **158 s**、
DTZ 从 68 s 到 86 s —— 活跃地图字节留在 LOH 的 GC 代价比临时写更大。
因此最终保留 **2 MB 阈值**：Loads 付临时写，Edits 避免 LOH 爆炸，整轮更稳。
全量 729 s 的旧数据没有计入后来的溢写/编码改动，仍只能作为历史基线。

### 10.13 2026-10-30 复测：729 s 基线其实不是冷启动；ADPCM 再省一次整块编码

#### (1) 先纠正口径：729 s / 1033 s 那两次跑的树里已经有音频

同一天两轮全量（同样 232 包、`--no-bake-flips --no-audit`）对 DTZ 单包对比：

| | DTZ edits | 说明 |
|---|---:|---|
| `full-hybrid` | 32.0 s | 35 条音频 cue **已在声库里**，`AudioChangesMerger` 全部走「同名只留一条」跳过 |
| `full-stream2` / 冷启动 | 123–133 s | 35 条 ogg 真编成 ADPCM，单包音频编码约 86–148 s |

`32 s < 单包音频编码时间`，只可能是编码被跳过（报告里没留下音频注记，这是从耗时反推的）。判据在
`AudioChangesMerger` 的 `existingCues`：它读的是**输入树里**的 `Sounds`/`Cues`，
输出树若是上一轮留下的，音频条目就全算「已存在」。
⇒ 10.1 的 729.2 s、10.12 之前的 1033 s 都不能当作冷启动数字；冷启动必须从空输出根起。

#### (2) ADPCM：候选块直接写 scratch，省掉「选中后重新整块编码」

原来每块先试 7 个预测器（只量误差、不写字节），选中后再完整编码一遍。
现在试跑时把候选字节写进一块 `scratch`，在误差更小的瞬间 `Buffer.BlockCopy`
进产出块 —— 少一次整块编码，且早停照旧。输出逐字节不变（同一套整数算术）。

DTZ 单包（35 条 ogg，103 MB）：

| | 音频编码 |
|---|---:|
| 改前 | 148.5 s |
| 改后 | **86.1 s**（另一次 87.8 / 93.8 s） |

全量产出树里 **682 个 `.wav` 与改前逐字节相同**；`Adpcm`/`Audio` 27 个单测通过。

试过但**放弃**的方案：把 7 个预测器的误差交错在一个循环里算（数组存 7 份状态）。
JIT 不展开 7 次内层循环，状态全落在栈上，DTZ 音频从 86 s 反弹到 **183 s**，已回退。

#### (3) 本地化镜像：base 只解析一次

`LocalizedDataMirror` 原来对每个 `<asset>.<locale>.json` 都重新
`ReadTextOrNull(base)` + `Parse`；同一份 base 有 7 个语言变体就解析 7 次。
现在按 base 分组、用 `ReadJsonOrNull` 直接拿内存对象（pending JSON 不再
「序列化成文本再解析回来」），各变体复用。DTZ 产出树 8206 个文件逐字节不变。

#### (4) 冷启动全量（空输出根，最新代码）

| 步骤 | 时间 |
|---|---:|
| Loads（不溢写） | 114.0 s |
| Edits（≥2 MB 溢写） | 688.2 s |
| CP 合计 | **802.6 s**（CPU 798.1 s） |
| 文件数 / 体积 | 32,494 / 2.01 GB（与旧树一致） |

同一台机器上墙钟噪声仍能到 ±50%（10.12 的 1039 s 与 10.1 的 729 s 之间就夹着这个量级），
所以 802.6 s 只能和**同日、同机、同命令**的 1028.5 s（`full-hybrid`）对照着看，不能直接减出「省了 226 s」。

#### (5) 还剩下的硬成本

`SDVCP_PROFILE=1` 全量（`full-prof2`，1062.6 s / CPU 859.3 s）的 CPU 分解：

| 项 | CPU |
|---|---:|
| `Write.逐目标`（含音频） | 448.9 s |
| ├ `音频.ADPCM编码`（219 条） | **344.9 s** |
| `本地化镜像` | 120.6 s |
| `EditData.写(含翻译/序列化)` | 62.5 s |
| `输出 Flush` + `图片Flush` | 48.7 s |
| `EditData.检测令牌` | 33.8 s |

音频编码占了编辑期 CPU 的四成。单线程 5 分钟在这份语料上到不了：
仅音频就要 ~345 s，其余编辑 ~400 s。要再往下走只有两条路 ——
更快的编码器实现（SIMD/多线程）或减少要编的音频；后者会改变产出。

### 10.14 2026-10-30 音频编码按文件并行（第一个真正并行的环节）

单线程的账已经算死：219 条音频 345 s，超过 5 分钟预算本身；AVX2 试过两种布局都更慢
（见 10.13）。于是改成**按文件并行**——每个 ogg 的 ADPCM 只取决于它自己的 PCM，
并行不改变任何字节，只有清单条目的**组装**仍留在串行循环里。

实现（`AudioChangesMerger.Apply`）：

- 先扫一遍本轮要编的 `(cue, 源文件)`，按路径去重，**大的先编**（LPT，编码耗时基本与文件大小成正比）；
- `Parallel.ForEach` 编完放进 `ConcurrentDictionary`，默认 `min(逻辑核数, 8)`，
  `SDVCP_AUDIO_THREADS` 可覆盖（设 1 退回单线程，便于对照）；
- 串行循环按原顺序取结果、`FreeName`、写波形清单与声库 —— 顺序与单线程完全一致。

配对实测（DTZ，35 条 ogg，同一台机器连续两次）：

| | 墙钟 | CPU |
|---|---:|---:|
| `SDVCP_AUDIO_THREADS=1` | 159.4 s | 155.9 s |
| `=8` | **66.2 s** | 284.2 s |

音频编码本身 122.3 s → 38.5 s，全量产出树（8205 个文件）逐字节相同。
12 线程时 DTZ 音频再降到 28.9 s，但全量在笔记本功耗墙下反而更慢（CPU 秒数翻倍），默认取 8。

全量冷启动（`--no-bake-flips --no-audit`，232 包）：

| | 墙钟 | 说明 |
|---|---:|---|
| 单线程（10.13） | 786.4 s | 安静时段 |
| 8 线程音频 | 671.2 s / 770.5 s | 两次同代码、不同机器负载 |

产出与单线程冷启动 **32,494 个文件逐字节相同**。这台机器的墙钟噪声仍有 ±30–50%
（同一份二进制在负载高的时段跑到 952 s），所以全量数字只能看配对，不能看单次。

剩下的墙钟几乎全在非音频环节（每包串行的补丁应用、翻译克隆、地图/图片写回、GC）。
再往下要动的是**按目标资产并行化补丁应用**，需要给 `OutputTree` / `ImageCache` / `MapCache`
加并发保护并按资产分组；那是下一阶段的事。

### 10.15 2026-10-30 阶段 2 按目标资产分 lane 并行

音频之外最贵的是每包串行的补丁应用。阶段 2 现在按**目标资产**分 lane：

- 可并行的：`EditData`、单一目标、非 `NativeKey`（原生键改写可能换目标）；
- 同一资产的多条补丁永远在同一条 lane 里按原顺序执行；
- 其余（`EditImage`/`EditMap`/多目标/原生键改写）留在串行道；遇到串行补丁时，
  先把已攒的并行组跑完再执行它 —— 与串行执行时看到的前序写入完全一致；
- 默认 `min(核数/2, 6)` 条 lane，`SDVCP_LANES` 可覆盖（设 1 退回串行）。

配套改动：`OutputTree` 的暂存字典加锁（`LockScope`，Monitor 可重入）、
懒物化（`MaterializeLazyText/Json`）与读盘放在锁外、`CreatedFiles` 换成带锁的集合、
`Prof` 计数加锁、`written/failed` 用 `Interlocked`。

验证：

| 输入 | 串行 vs 并行 |
|---|---:|
| Bear Family 三包 | 8539 个文件，**0 差异** |
| SVE + Ridgeside + DTZ | 14511 个文件，**0 差异** |
| Bear Family 并行跑两次 | 0 差异（确定性） |

耗时（同机连续）：

| | 串行 lane | 6 lane |
|---|---:|---:|
| Bear Family 三包 | 63.1 s | **55.1 s** |
| SVE+RSV+DTZ | 188.0 s | 187.0 s（音轨占大头，非音频部分不大） |

CPU 会涨 5–18%（并行的固有代价）。全量对比没做成：取样时段机器被其他进程占满（系统 CPU 95%），
墙钟数字没有意义，等空闲时段再补。
