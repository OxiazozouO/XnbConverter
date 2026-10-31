# 图片块：`EditImage` 与图片合成

日期：2026-10-06
依据：CP 2.9.1 反编译、SMAPI 反编译（`StardewModdingAPI.dll`）、`XnbConverter.Core` 只读、两份语料实测

---

## 0. 一句话

**`EditImage` 是唯一「现在一条都写不出」的大类**（RSV 43 条 `written` 全空），
而它**不缺能力**——`XnbConverter.Core` 的 PNG 编解码够用且不依赖游戏。缺的只是**一个 applier**。

---

## 1. CP `EditImage` 的完整字段

定义在 `ConfigModels\PatchConfig.cs`（一个类承载所有 Action 的字段）：

| 字段 | 说明 |
|---|---|
| `Target` | 目标资产名，**支持逗号列表**（见 §4）|
| `FromFile` | 源图片路径，**也支持逗号列表** |
| `FromArea` | 源矩形 `{X,Y,Width,Height}` |
| `ToArea` | 目标矩形，同上 |
| `PatchMode` | `Replace`（默认）/ `Overlay` |
| `When` / `Update` / `Priority` / `Enabled` / `LogName` / `LocalTokens` | 基字段 |

> ⚠️ **`SetOpacity` / `MovePosition` 在 CP 2.9.1 里根本不存在**（全量 grep 零命中）。
> 本项目计划早期版本提过它们，是错的。

---

## 2. `PatchMode` 的精确语义

枚举在 `Conditions\PatchImageMode.cs:3-8`：`Replace` / `Overlay` / `Mask`。

- **`Mask` 已被 2.9 禁用**（`Migrations\Migration_2_9.cs:31` 直接报错），**不需要实现**。
- **`Replace` 是默认值**（`PatchLoader.cs:475`）。

真正的绘实在 **SMAPI 侧**（`AssetDataForImage.PatchImageImpl`，CP 只是转发）：

### `Replace`
整块直拷，**含 alpha**。目标区域被完全覆盖（哪怕源像素全透明）。

### `Overlay`（**必须逐字复刻**）

```
1. 在源区域内找首行/末行 alpha >= 5 的像素
   （MinOpacity = 5 —— 因为 Linux/macOS 上「全透明」像素的 alpha 可能到 4）
2. 若源区域整块全透明 → 直接返回，什么都不做
3. 把 targetArea 重定位到 [首行, 末行] 这个行区间
4. 逐像素：
   源 A < 5                    → 保留目标像素
   源 A == 255 或 目标 A < 5    → 目标 := 源
   否则（半透明混合）：
       k = 1 - src.A/255
       R = src.R + dst.R*k      （G/B 同理）
       A = max(src.A, dst.A)
```

> ⚠️ **`A = max(...)` 不是标准 source-over**，是 SMAPI 的近似实现。
> 复刻错了叠图会有肉眼可见差异。

---

## 3. `FromArea` / `ToArea` 的默认值与约束

`EditImagePatch.cs:91-95`：

- `FromArea` 缺省 = 整张源图 `(0,0,srcW,srcH)`
- `ToArea` 缺省 = `(0,0,srcArea.W, srcArea.H)`
- **四个字段（X/Y/Width/Height）任一缺失即整条跳过**（`PatchLoader.cs:1096-1114`）
- **宽高必须相等**，否则警告跳过
- **`area2.Right > 目标宽度` → 报错跳过**
- 目标图不够高时先 `ExtendImage(targetWidth, area2.Bottom)` —— **向下扩展画布**（补透明行）

---

## 4. `Target` / `FromFile` 的逗号笛卡尔积

`PatchLoader.cs:163-205` `SplitPatches`：两者各自逗号切分、**大小写不敏感去重**，
然后**做笛卡尔积**，每个组合是**一条独立补丁**（同 priority、各自 `When`/`FromArea`）。

实测：96 mod 语料里 42 条多目标，**展开后 1005 个目标实例（不同资产 477 个）**。

> **判定与写入两侧必须同时展开**，否则条数口径不一致（Task 10b 的教训）。

---

## 5. 目标不存在时 CP 怎么办

**CP 侧不检查目标**——它把 patch 注册在目标资产名下，只有游戏请求加载该资产时才调用编辑。
反编译 `GameContentManager.LoadExact`：没有 load operation 时回落到 `RawLoad` → **文件不存在即抛 `ContentLoadException`**。

**结论：`EditImage` 无法作用在不存在的目标上。** 目标必须是原版已有资产，**或同包内先有 `Load` 创建**。

> 项目的 `ApplyOrder` 已保证 `Load` 恒排在编辑补丁之前，正好匹配 CP 语义。

---

## 6. `XnbConverter.Core` 的 PNG API —— 够用

唯一公开入口是 `XnbConverter.Utilities.Png.Png`
（`PngReader` / `PngWriter` 都是 **`internal`**，项目没有 `InternalsVisibleTo`）：

```csharp
public static byte[] Read(string path, out int width, out int height)   // → RGBA8 逐行 R,G,B,A
public static void Write(string path, ReadOnlySpan<byte> rgba, int width, int height)
```

| 问题 | 答案 |
|---|---|
| 读出原始 RGBA？ | ✅ |
| 写回 PNG？ | ✅ |
| 位深/调色板限制？ | 读侧支持颜色类型 0/2/3/4/6、位深 1/2/4/8/16、tRNS、Adam7；写侧输入恒 RGBA8，≤256 色自动改写成调色板 PNG |
| **依赖 MonoGame 吗？** | **不依赖**（三个文件只 `using System.*`）|

**缺的只有三件**（约 30 行数组运算）：
1. 矩形子区域拷贝
2. `Overlay` 混合（照 §2 复刻）
3. 向下扩展画布（对应 `ExtendImage`）

---

## 7. 解包树里图片资产 = `.png` + `.config`

真实例子（`D:\sdv_tree\Characters\`，125 个 `.png` / 125 个 `.config` 一一配对）：

```
Characters/Abigail.png
Characters/Abigail.config      ← Readers[0].Type = Texture2DReader
```

**`ContentWidth`/`ContentHeight` 全树都是 0** → 尺寸由 PNG 自身决定，**改尺寸不需要改 `.config`**。

---

## 8. 那一族「静态不可解析」的图片

RSV 游客族（`Data/DependencyData/CustomCompanionsTourists.json`）：
`Load` 一口气建 `Guest1..Guest10`，然后 6~7 层 `EditImage` 各自 Overlay。

5 条基础 patch 用 `key=RSVTourist` 共享同一次掷骰，**列表依次旋转**：

```
Base1&6: Body_{{Random: 1,2,3,4,5 |key=RSVTourist}}/Skin/Colour_{{Random: 1..10}}
Base2&7: Body_{{Random: 2,3,4,5,1 |key=RSVTourist}}/Skin/Colour_{{Random: 3..10,1,2}}
... 共 5 条
```

**作者要的是「10 个游客各不相同」，不是「随机」。** 全部素材是 `64x128` RGBA。

### ⚠️ 正确的烘焙姿势

**不要取 `num=0`**（那样 Guest1 与 Guest6 会同图），而应**按作者的旋转表手工指派**：

```
Guest_i → Body_{((i-1) mod 5)+1} / Colour_{f(i)}     // f 取一个双射，保证 10 个组合互不相同
```

因为 `Guest1..Guest10` **本来就是 10 个独立目标资产**——
**「各不相同」可以 100% 保住**，损失只有「每天不换装」。

### ⚠️ 订正一处我们自己的文档错误

`docs/research/2026-10-05-cp-runtime-tokens-and-native-substitutes.md` 写着
CustomCompanions 的 `Companion`「**不是 NPC**」——**字面错误**。
反编译实测 `public class Companion : NPC`（`CustomCompanions.dll` 第 20 行）。

**结论仍成立**，但理由要换成更硬的两条：
1. **游客不在 `Data/Characters` 里**（它们是 `[CC]` 的 `companion.json` 挂的），
   而 `NPC.ChooseAppearance` 由 `Game1.characterData` 驱动（`NPC.cs:956-961`）——没进就拿不到 `Appearance`。
2. **`Appearance[]` 只能选一张完整 `Sprite`**，而这里要合成的是 6~7 个独立图层。

### 第三条路
- **目标派生令牌**（`{{Target}}` / `{{TargetWithoutPath}}` / `{{TargetPathOnly}}`，108 处）：**完全静态可解析**，几乎零成本。
- **`{{season}}`**（36 处）：只有目标本身是季节后缀资产时才无损，否则逐案判断。

---

## 9. 规模（实测）

| 指标 | RSV | 96 mod 语料 |
|---|---|---|
| `EditImage` | **43** | **717** |
| 其中 FromFile 含令牌 | 34（79%）| 218（30%）|
| 图片 `Load` | 124 | **923** |
| `FromFile` 无令牌 | 9 | **499** |

**96 mod 语料里 SVE 一个包占 632 / 717。**

---

## 10. 与既有原则的冲突（要明确）

Task 10a 定过「图片复制必须逐字节一致（`File.Copy`）」。
**`EditImage` 合成必然要重编码 PNG**（`Png.Write`），与那条冲突。

**划清**：**纯 `Load` 继续 `File.Copy` 字节复制**；**只有 `EditImage` 合成才允许重编码**。

---

## 11. 不在本块范围

- `FromFile` 带**模组自定义令牌**（`NoNose` 87 / `Aerobics` 9 等）——要先有该令牌的求值器。
- `When` 里 `HasSeenEvent` / `LocationName` / `IsOutdoors` 这类**图片条件落点**——
  原版对非 NPC 贴图**没有通用条件落点**（唯一已知是 `Data/Characters.Appearance[]`，
  且只对 `Data/Characters` 里的 NPC 生效）。**需要单独一次调研。**

  > 值得一提：语料 B 里 `Season`(271) + `IsOutdoors`(219) **恰好是 `Appearance[]` 原生支持的维度**，
  > 但对 `Cursors` / `Craftables` 这类非 NPC 贴图无效。
