# sprite 索引 → 图内矩形：算术普查

日期：2026-10-06
依据：`D:\sdv_src`（1.6 反编译）、`D:\sdv_before`（原版 Content 实测尺寸/像素）、`D:\sdv_gd`（字段语义）
配套：`2026-10-06-implicit-conventions.md` §8/§9、`2026-10-06-grand-total.md` §2

---

## 0. 一句话

**一个整数索引 `i` 变成矩形，全部是同一个动作：`(i % N, i / N)` 的行优先拆包**，
`N` = 每行格数 = 纹理宽 / 格宽（几何量）。
各物件类型之间的差别**只有常量**：格宽 `Cw`、行高 `Rh`、起点 `(x0,y0)`、矩形尺寸 `(Wc,Hc)`。
**没有第二套机制**——除家具的"旋转帧"用的是右邻偏移（`X += Width * k`）之外。

---

## 1. ★ 核心表：物件类型 → 换算函数 → 行/列/换行规则 → 源码

| 物件类型 | 数据字段 | 换算函数（源码位置） | 列数 N | 格宽 Cw | 行高 Rh | 换行规则 | 矩形尺寸 |
|---|---|---|---|---|---|---|---|
| **家具** | `Data/Furniture` idx **8**（<0 则 `int.TryParse(ID)`）| `Furniture.GetDefaultSourceRect` → `getDefaultSourceRect`<br>`Furniture.cs:236-252` / `1638-1642` | `texture.Width/16`<br>furniture.png = **32** | 16 | **16** | `x=(i*16)%W`，`y=(i*16/W)*16` | `W H`×16 |
| **BigCraftable** | `Data/BigCraftables.SpriteIndex` | `Object.getSourceRectForBigCraftable`<br>`Object.cs:1082-1090` | `W/16`<br>Craftables.png = **8** | 16 | **32** | `x=(i%N)*16`，`y=(i/N)*32` | 16×32 固定 |
| **衬衫** | `Data/Shirts.SpriteIndex` | `ShirtDataDefinition.GetSourceRect`<br>`ShirtDataDefinition.cs:47-59` | `(W/2)/8`<br>shirts.png = **16** | 8 | **32** | `x=(i*8)%(W/2)`，`y=(i*8/(W/2))*32` | 8×8 |
| **裤子** | `Data/Pants.SpriteIndex` | `PantsDataDefinition.GetSourceRect`<br>`PantsDataDefinition.cs:46-57` | `W/192`<br>pants.png = **10** | 192 | **688** | `x=192*(i%N)`，`y=672+688*(i/N)` | 16×16 |
| **帽子** | `Data/hats` idx **6**（<0 则 parse ID）| `HatDataDefinition.GetSourceRect`<br>`HatDataDefinition.cs:46-57` | `W/20`<br>hats.png = **12** | 20 | **80**（=20×4） | `x=(i*20)%W`，`y=(i*20/W)*80` | 20×20 |
| **靴子** | `Data/Boots` idx **8**（<0 则 parse ID）| `Game1.getSourceRectForStandardTileSheet`<br>`Game1.cs:15897-15908` | `W/16`<br>springobjects = **24** | 16 | 16 | `x=(i*16)%W`，`y=(i*16/W)*16` | 16×16 |
| **NPC 动画帧** | `Data/animationDescriptions` 帧号 | `AnimatedSprite.GetSourceRect`<br>`AnimatedSprite.cs:545-560` + `618-621` | **`texture.Width/SpriteWidth`**<br>Abigail.png = 64/16 = **4** | `SpriteWidth` | `SpriteHeight` | `x=(f*Sw)%Tw`，`y=(f*Sw/Tw)*Sh` | Sw×Sh |
| **Farmer 动画帧** | `FarmerSprite.AnimationFrame.frame` | `FarmerSprite.UpdateSourceRect`<br>`FarmerSprite.cs:832-834` | **写死 96/16 = 6** | 16 | 32 | `x=(f*16)%96`，`y=(f*16/96)*32` | 16×32 |
| **TemporaryAnimatedSprite** | 构造时给的 `sourceRect` | `update()` 递推<br>`TemporaryAnimatedSprite.cs:1866-1914` | `Texture.Width/rect.Width` | rect.W | rect.H | 见 §5 | 调用方给 |
| **家具旋转帧** | `Data/Furniture` idx **4**（rotations）| `Furniture.rotate` `Furniture.cs:1199-1238`；绘制时 `Furniture.cs:1875 / 1971` | — | — | — | **右邻**：`X += Width * k` | 同默认 |

> `Data/ClothingInformation` 在 1.6 **不存在**（`D:\sdv_src` 全库 0 命中），已拆成 `Data/Shirts` + `Data/Pants`。

---

## 2. ★ 统一公式

```
输入：index i, 每行格数 N, 格宽 Cw, 行高 Rh, 原点 (x0,y0), 单元尺寸 (Wc,Hc)

col  = i % N
row  = i / N                       ← 全是整数除法
rect = new Rectangle( x0 + col*Cw, y0 + row*Rh, Wc, Hc )
```

**覆盖性检验**：

| 类型 | 代入后 | 是否吻合 |
|---|---|---|
| 家具 | `N=W/16, Cw=16, Rh=16, x0=y0=0` → `x=(i*16)%W, y=(i*16/W)*16` | ✅ 与 `Furniture.cs:1641` 逐字相同 |
| BigCraftable | `N=W/16, Cw=16, Rh=32, 0,0` → `x=(i%N)*16, y=(i/N)*32` | ✅ 与 `Object.cs:1089` 同值 |
| 靴子 | `N=W/16, Cw=16, Rh=16, 0,0` | ✅ 与 `Game1.cs:15907` 完全相同 |
| 衬衫 | `N=(W/2)/8, Cw=8, Rh=32, 0,0` | ✅ 与 `ShirtDataDefinition.cs:57-58` 相同 |
| 帽子 | `N=W/20, Cw=20, Rh=80, 0,0` | ✅ 与 `HatDataDefinition.cs:56` 相同 |
| 裤子 | `N=W/192, Cw=192, Rh=688, y0=672` | ✅ 与 `PantsDataDefinition.cs:56` 相同 |
| NPC 帧 | `N=Tw/Sw, Cw=Sw, Rh=Sh, 0,0` | ✅ 与 `AnimatedSprite.cs:620` 相同 |
| Farmer 帧 | 同上但 `N=6` **写死** | ✅ 与 `FarmerSprite.cs:834` 相同 |

**结论：能用一个式子覆盖家具 / BigCraftable / 衣服（衬衫·裤子·帽子·靴子）。**
三处"不规整"全部落在常量上，不在公式形状上：

1. **裤子**是唯一 `y0≠0` 的（`+672`，pants.png 顶部 672px 是别的用途）；
2. **衬衫**是唯一 `N` 取**半宽**的（`W/2`：贴图左右对半，**左半=衣服、右半=染料遮罩**，`ShirtDataDefinition.cs:11` 的 XML 注释明写）；
3. **Farmer** 是唯一 `N` **写死**的（96，见 §4）。

这也再次印证 §2.1 的结论：**`N` 永远是"纹理宽 / 格宽"，不是语义维度基数。**
衬衫 `N=16`、裤子 `N=10`、帽子 `N=12` —— 三者都没有对应的 16/10/12 值语义维度。

---

## 3. 问题 1：家具

### 3.1 spriteIndex 从哪来

```csharp
// FurnitureDataDefinition.cs:90-102
int num = ArgUtility.GetInt(fields, 8, -1);   // 0-based 第 8 字段
if (num > -1) return num;                     // 有则用
if (int.TryParse(itemId, out var result))     // 否则解析条目 ID
    return result;
return -1;                                    // 都没有 → -1（会变成 error item）
```
默认贴图 `TileSheets\furniture`（`FurnitureDataDefinition.cs:43`，0-based 第 9 字段可覆盖）。

### 3.2 换算函数

`FurnitureDataDefinition.GetSourceRect`（`:47-58`）**直接委托**给 `Furniture.GetDefaultSourceRect(itemId, texture)`。

```csharp
// Furniture.cs:236-252
if (data[2].Equals("-1"))                                  // 0-based 第 2 字段 == "-1"
    return getDefaultSourceRectForType(..., getTypeNumberFromName(data[1]), texture);
string[] array = ArgUtility.SplitBySpace(data[2]);          // 否则 "W H"
...
return getDefaultSourceRect(dataOrErrorItem, spriteWidth, spriteHeight, texture);

// Furniture.cs:1638-1642  ← 唯一的像素换算
return new Rectangle(itemData.SpriteIndex * 16 % texture.Width,
                     itemData.SpriteIndex * 16 / texture.Width * 16,
                     spriteWidth * 16, spriteHeight * 16);
```
（同一式子另有一份 1×1 的退化版在 `Furniture.cs:316`。）

### 3.3 行 / 列 / 换行规则

- **列数 = `texture.Width / 16`**；`furniture.png` 实测 **512×1488** → **32 列**。
- **行高 = 16px**（注意：不是 spriteHeight！）；`1488 / 16 = 93` 行。
- 换行规则：`row = SpriteIndex / 32`，`y = row * 16`。
- **索引就是这张 32×93 细网格里的格子号**（行优先）。多格家具占"以该格为左上角的一块"。

### 3.4 尺寸会不会影响排布？——**不影响换算，影响预留**

- `W H`（`data[2]`；为 `"-1"` 时按**类型名**查表 `getTypeNumberFromName`，`Furniture.cs:1722-1748`，表在 `Furniture.cs:1560-1636`，15 个 case：chair 1×2、bench 2×2、couch 3×2、long table 5×3、fireplace 2×5…）
  **只决定 `rect.Width/Height`，不进入起点公式**。起点只由 `SpriteIndex` 与 `texture.Width` 决定。
- **游戏不做任何自动排版**——它假定"格子号 + 尺寸"那块不与人重叠，直接裁。排版是作者手工完成的。
  （实证：347 条数字 ID 家具按上式放回 `furniture.png`，零重叠；见 `2026-10-06-implicit-conventions.md` §8.3。）
- ⚠️ **但"旋转帧"会撑大 ID 步长**：
  ```csharp
  // Furniture.cs:1199-1238  rotate()
  case 1/3: sourceRect = new Rectangle(X + W,        Y, H-16+..., W+16+...);
  case 2  : sourceRect = new Rectangle(X + W+H-16,   Y, W, H);
  ```
  即 1~3 号朝向帧被放在**默认矩形的右侧邻接处**，绘制时按 `sourceIndexOffset` 选取（`Furniture.cs:1875` 与 `1971`：`value.X += value.Width * sourceIndexOffset.Value`）。
  椅子默认 rect 16×32、`rotations=4` → 四帧占 `X..X+47` = **48px = 3 格**。
  **这就是 `Data/Furniture` 里椅子 ID 是 `0,3,6,9,…` 而 `1,2` 空缺的原因**（实测 `Data/Furniture.json` 数字键 `0,3,…,30,31,64,67,…`，`1`/`2` 确实不存在）。

---

## 4. 问题 2 / 3：BigCraftable 与衣服

### 4.1 BigCraftable（与家具**同构**，仅行高不同）

```csharp
// Object.cs:1087-1090
return new Rectangle(index % (texture.Width / 16) * 16,
                     index * 16 / texture.Width * 16 * 2,   // ← 行高 32
                     16, 32);
```
默认贴图 `TileSheets\Craftables`（`BigCraftableDataDefinition.cs:44`），实测 **128×1472** → **8 列 × 46 行（行高 32）**。
`SpriteIndex` 直接取自数据（`BigCraftableDataDefinition.cs:44`），无 ID 回退。
（季节机制 `idx - idx%4 + seasonIndex` 见既有文档 `implicit-conventions.md` §1，此处不重复。）

### 4.2 衬衫 / 裤子 / 帽子 / 靴子（**与家具同构**，常量不同）

| 类 | 公式（源码逐字） | 实测贴图 | 列数 |
|---|---|---|---|
| 衬衫 | `new Rectangle(spriteIndex*8 % (W/2), spriteIndex*8/(W/2)*32, 8, 8)` `ShirtDataDefinition.cs:57-58` | shirts.png **256×608** | **16** |
| 裤子 | `new Rectangle(192*(i%(W/192)), 688*(i/(W/192))+672, 16, 16)` `PantsDataDefinition.cs:56` | pants.png **1920×1376** | **10** |
| 帽子 | `new Rectangle(i*20 % W, i*20/W*20*4, 20, 20)` `HatDataDefinition.cs:56` | hats.png **240×880** | **12** |
| 靴子 | `Game1.getSourceRectForStandardTileSheet(texture, i, 16, 16)` `BootsDataDefinition.cs:56` → `Game1.cs:15907` | springobjects.png **384×624** | **24** |

- 帽子的行高 `20*4=80` 是**给 4 个朝向帧留的**（`+direction*20` 选朝向），与家具的"右邻留旋转帧"是同一思路，只是这里**向下**排。
- 衬衫的 `W/2` 明确来自"贴图左右对半"的硬约定（注释在 `ShirtDataDefinition.cs:11`）。
- 靴子**没有专用图集**，塞在 `Maps/springobjects` 里（默认值 `BootsDataDefinition.cs:42`）。
- 帽子/靴子的 `SpriteIndex` 字段缺失时都回退到 `int.TryParse(ID)`（`HatDataDefinition.cs:89-101`、`BootsDataDefinition.cs:89-101`），与家具同构。

**结论：家具 / BigCraftable / 衬衫 / 裤子 / 帽子 / 靴子 —— 六类同构。**

---

## 5. 问题 4：动画帧（animationDescriptions / FarmerSprite）

### 5.1 动画 ID → 帧列表

```csharp
// NPC.cs:4665-4670
if (DataLoader.AnimationDescriptions(Game1.content).TryGetValue(name, out value)) {
    string[] array = value.Split('/');
    routeEndIntro     = Utility.parseStringToIntArray(array[0]);   // intro
    routeEndAnimation = Utility.parseStringToIntArray(array[1]);   // 主循环
    routeEndOutro     = Utility.parseStringToIntArray(array[2]);   // outro
    ...
}
```
- 值格式：`"intro/main/outro[/消息[/元数据…]]"`；每段是**空格分隔的整数帧号**（`Utility.parseStringToIntArray`，`Utility.cs:5047`，默认分隔符 `' '`）。
- 三段分别是 **intro / 主循环 / outro**，**不是朝向**（与既有文档 §9.1 一致）。
- 帧号随后被逐帧塞进 `Sprite.AddFrame(new FarmerSprite.AnimationFrame(frame, ms, …))`（如 `NPC.cs:4452/4456/4472/4563`），最终由 `AnimatedSprite` 的帧号→矩形换算。

### 5.2 列数到底是多少、从哪来 ——**核实结果：不是固定 6**

```csharp
// AnimatedSprite.cs:72
protected int textureWidth => Texture?.Width ?? 96;

// AnimatedSprite.cs:545-560  UpdateSourceRect()
SourceRect = GetSourceRect(textureWidth, SpriteWidth, SpriteHeight, currentFrame);

// AnimatedSprite.cs:618-621
return new Rectangle(frame * spriteWidth % textureWidth,
                     frame * spriteWidth / textureWidth * spriteHeight,
                     spriteWidth, spriteHeight);
```
→ **`列数 = texture.Width / SpriteWidth`，运行时现算**。
NPC 默认 `SpriteWidth=16 / SpriteHeight=32`（`NPC.cs:115/118`）；实测 `Characters/Abigail.png = 64×448` → **4 列 × 14 行**。

```csharp
// FarmerSprite.cs:832-834  ← 唯一的写死
base.SourceRect = new Rectangle(CurrentFrame * base.SpriteWidth % 96,
                                CurrentFrame * base.SpriteWidth / 96 * base.SpriteHeight,
                                base.SpriteWidth, base.SpriteHeight);
```
→ **只有 Farmer 写死 96**（= 6 列 × 16px）。实测 `farmer_base.png = 288×672`，288 = 3×96：
贴图是**横向叠了 3 个 96px 宽的图层**（`FarmerRenderer.cs:62 armOffset = 96`），帧网格只用最左 96px。
帧数 6×21 = **126**，正是 `FarmerRenderer.featureXOffsetPerFrame[126]` 的长度（`:68/85`）——**交叉印证 6 列**。

> **"有 6 列"这个说法只对 Farmer 成立，不能推广到 NPC。** NPC 是 4 列（`textureWidth/16`）。

### 5.3 帧能不能跨行续接？——**能，而且很常见**

`GetSourceRect` 是一个纯粹的**行优先线性化**：`col = f % N`、`row = f / N`。
**没有任何代码要求"一个动画的帧必须在同一行"**——帧号是绝对索引，跨行自动落到下一行。

实测（把全部 250 条 `Data/animationDescriptions` 按 4 列代入）：**97 条（38.8%）的帧列表跨行**。
例：`maru_tinker = "24 25/16 16 … 21 22 23/25 24"` —— 主循环含 `16..23`，4 列下横跨第 4、5 行。
例：`clint_hammer = "4/8 8 … 10 11 9 8/8"` —— 主循环里 `8→10→11→9` 在同一行内乱跳，但 `abigail_flute` 的 `16..19` 与后续帧同样靠索引自然换行。

**兜底**：`UpdateSourceRect`（`:554-558`）若算出的矩形越界（`Right > W || Bottom > H`），会把 `currentFrame` 归零重算 —— **越界帧会被静默改成第 0 帧**，转换时须留意。

### 5.4 朝向与列数的关系

`AnimatedSprite.AnimateUp/Right/Down/Left`（`:310-340`）用 `textureWidth / SpriteWidth` 切四个**连续帧块**：
```csharp
currentFrame = textureWidth / SpriteWidth * 2 + currentFrame % (textureWidth / SpriteWidth);  // :324
currentFrame = textureWidth / SpriteWidth     + currentFrame % (textureWidth / SpriteWidth);  // :327
```
→ 行走图的 4 个朝向 = 4 个长度为"列数"的连续段。**这也是"列数"必须现算而不能写死的原因**：写死就切错朝向。

---

## 6. 问题 5：TemporaryAnimatedSprite 的跨行帧续接

`sourceRect` 是**可变矩形**，不是由帧号现算的。递推在 `update()` 里：

```csharp
// TemporaryAnimatedSprite.cs:1866-1914
if (timer > interval) {
    currentParentTileIndex += pingPongMotion;
    sourceRect.X += sourceRect.Width * pingPongMotion;        // ① 每次前进一格
    if (Texture != null) {
        if (!pingPong && sourceRect.X >= Texture.Width)       // ② 越过右边界
            sourceRect.Y += sourceRect.Height;                //    → 换行：Y 加一个格高
        if (!pingPong)
            sourceRect.X %= Texture.Width;                    // ③ X 回卷
        ...
        else if (totalNumberOfLoops >= 1 && sourceRect.X + (sourceRect.Y - sourceRectStartingPos.Y) / sourceRect.Height * Texture.Width
                     >= sourceRectStartingPos.X + sourceRect.Width * animationLength) {   // :1910
            sourceRect.X = (int)sourceRectStartingPos.X;      // ④ 播完 animationLength 帧 → 回到起点
            sourceRect.Y = (int)sourceRectStartingPos.Y;
        }
    }
```
**逐帧递推公式**（非 pingPong）：
```
lin += 1
col  = lin % N ;  row = lin / N          (N = Texture.Width / sourceRect.Width)
X = start.X + col * Width
Y = start.Y + row * Height
```
与 §2 的拆包式**等价**，只是用增量实现。**帧当然跨行**——②就是专门为跨行写的。

⚠️ **同文件里还有第二套（不一致的）写法**：
```csharp
// TemporaryAnimatedSprite.cs:2086-2094  setSourceRectToCurrentTileIndex()
sourceRect.X = (int)(sourceRectStartingPos.X + currentParentTileIndex * sourceRect.Width) % texture.Width;
sourceRect.Y = (int)sourceRectStartingPos.Y;      // ← Y 不推进，只回卷 X
```
它**假设单行**，与 `update()` 的跨行语义冲突。凡是从 `currentParentTileIndex` 复原矩形的路径，都要按"它到底走的是哪一套"来判断。
（另注：`TileSheets/animations` 里帧是 64×64，行距 64，见 `TemporaryAnimatedSprite.cs:605` 的 `rowInAnimationTexture * 64`。）

---

## 7. 问题 6：`@Rotation` / `@Flip` 在 `XnaDisplayDevice.DrawImpl` 里被用到吗？

### 7.1 核实结果（**部分未确证**）

| 断言 | 核实结果 |
|---|---|
| 原版 C# 侧读瓦片属性 `@Rotation`/`@Flip` | **否**。`D:\sdv_src` 全库搜 `"Rotation"` / `"Flip"` → **0 命中**。原版读的瓦片属性是 Action / TouchAction / Passable / Water / Shadow / NPCBarrier / TemporaryBarrier / ProjectilePassable / NPCPassable / GreenRain / TreeStump / Diggable / Type…（`GameLocation.cs:2784-2905`、`Farmer.cs:6296`、`Monster.cs:883`、`InteriorDoor.cs:78` 等），**没有旋转/翻转**。 |
| `XnaDisplayDevice.DrawImpl` 的源码 | **不在给定材料内**。`D:\sdv_src` 里 `XnaDisplayDevice` **只有引用没有定义**：`Game1.cs:447`（`public static IDisplayDevice mapDisplayDevice;`）与 `Game1.cs:2785-2788`（工厂方法 `return new XnaDisplayDevice(content, graphicsDevice);`）。该类属于 `xTile.Display` 命名空间，随游戏程序集打包，**未反编译进 `sdv_src`**。 |
| 「`DrawImpl` 写死 `0f` + `SpriteEffects.None`」 | **无法给出准确行号 → 标「未确证」**。 |

### 7.2 ⚠️ 顺带纠正一处可能的归因错误

对 `Stardew Valley.dll` / `StardewModdingAPI.dll` 做符号普查：

| 标识符 | Stardew Valley.dll | StardewModdingAPI.dll |
|---|---|---|
| `XnaDisplayDevice` | ✅ | ✅ |
| `IDisplayDevice` | ✅ | ✅ |
| `BeginScene` / `EndScene` | ✅ | ❌ |
| **`DrawTile`** | ✅ | ❌ |
| **`DrawImpl`** | **❌** | ✅ |

- 原版显示设备实现的是 `xTile.Display.IDisplayDevice`，其方法名是 **`DrawTile`**（连同 `BeginScene`/`EndScene`），**程序集里根本没有 `DrawImpl` 这个标识符**。
- `DrawImpl` 只出现在 **SMAPI** 里（`SDisplayDevice` 同表亦只在本列命中）。
- 因此「原版 `XnaDisplayDevice.DrawImpl` 硬编码 0f/SpriteEffects.None」这一表述**很可能把 SMAPI 的方法名安到了原版头上**，建议按 **`XnaDisplayDevice.DrawTile`** 重新核对。

### 7.3 能确证的边界（结论不变）

**从 `sdv_src` 可确证的部分**：原版游戏自己的 C# 代码从不读取瓦片属性 `@Rotation`/`@Flip`，
也从不据此设置旋转或翻转；`XnaDisplayDevice` 的绘制参数在此材料内**无法查证**。
→ **数据侧（XNB/tbin 里的 `@Rotation`/`@Flip`）确实完整存在，但"原版渲染会不会用"这一层，本材料不足以证实或证伪。**

---

## 8. 无依据 / 未确证清单

| 项 | 状态 |
|---|---|
| `XnaDisplayDevice.DrawImpl` 的具体行号与硬编码值 | **未确证**（源不在 `D:\sdv_src`；且 `DrawImpl` 疑似 SMAPI 侧名字）|
| 家具 `_frontTextureName = textureName + "Front"` 的逐像素分层细节（`Furniture.cs:1883-1899`）| 只确认存在，未展开 |
| 裤子 `y0=672` 的 672px 顶部区域用途 | 未查 |
| `Data/Boots` 只有 7 个字段却按 idx 8 取 spriteIndex | 已知会回退到 `int.TryParse(ID)`，语义上等价，但**"为什么是 8"未查** |

---

## 9. 可复用的最小实现（给转换器）

```csharp
// 唯一需要的函数
Rectangle CellRect(int index, int cols, int cellW, int rowH,
                   int x0 = 0, int y0 = 0, int w = 0, int h = 0)
{
    int col = index % cols, row = index / cols;      // cols = 纹理宽 / cellW
    return new Rectangle(x0 + col * cellW, y0 + row * rowH,
                         w == 0 ? cellW : w, h == 0 ? rowH : h);
}
```
常数表：
- 家具 `cols = W/16, cellW=16, rowH=16`，`w,h` 来自 `Data/Furniture` 第 2 字段（`-1` → 类型名表）
- BigCraftable `cols = W/16, cellW=16, rowH=32, w=16, h=32`
- 靴 `cols = W/16, cellW=16, rowH=16`
- 衬衫 `cols = (W/2)/8, cellW=8, rowH=32`
- 裤子 `cols = W/192, cellW=192, rowH=688, y0=672`
- 帽子 `cols = W/20, cellW=20, rowH=80`
- 动画帧 `cols = 纹理宽/SpriteWidth, cellW=SpriteWidth, rowH=SpriteHeight`（Farmer 例外：`cols=6`）
