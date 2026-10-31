# 服装通道端到端：`Data/Shirts` 条目 + 专属贴图 → XNB → 读回

日期：2026-10-11

**为什么单独验这一条**：32 包语料里**服装一条都没有**（报告与 `Mods/` 全零命中），
而它是「以后遇到服装框架包」时**唯一现成的原版落点**（见
`../research/2026-10-11-non-cp-frameworks-what-can-bake.md`）。
没有语料就只能**自己造输入**：造一份合成包，把「转 → 打包 → 解包读回」跑通。

## 输入（合成，不是真实 mod）

```
pack/manifest.json     ContentPackFor = Pathoschild.ContentPatcher
pack/content.json      Load  Characters/Farmer/testsuit  ← assets/TestShirt.png
                       EditData Data/Shirts  Entries["SdvCpTool_TestSuit"]
                              { Texture: "Characters/Farmer/testsuit", SpriteIndex: 0, CanBeDyed: true, … }
pack/assets/TestShirt.png   16×8、1 个 8×8 精灵（左半精灵 / 右半染色遮罩 —— 原版格子约定）
```

## 三步证据（同参数、可复现）

**① 转**（`SdvCpTool.Cli convert --pack … --tree D:\sdv_tree --out … --content-hashes …`）

- `Data/Shirts.json`：**303 → 304** 条，新增键 `SdvCpTool_TestSuit`，字段为
  `{"Name":"Test Suit", "Texture":"Characters/Farmer/testsuit", "SpriteIndex":0, "CanBeDyed":true, "HasSleeves":true, …}`；
  原版 `1000` 那条一字未动 ✓
- `Characters/Farmer/testsuit.png`（80 字节，逐字节＝源图）**且** `testsuit.config`
  **与原版 `shirts.config` 逐字节相同** ✓
  —— 少了这个 `.config`，打包器会**静默跳过**这张图（见 `2026-10-09-packer-silent-skips.md`）。

**② 打包**（`XnbConverter.Cli.exe pack`，在打包器自己的目录下跑）

```
输出文件已保存：Data\Shirts.xnb
输出文件已保存：Characters\Farmer\testsuit.xnb
成功: 2  失败: 0
```

**③ 解包读回**（`unpack`）

- 两个 XNB 都「验证成功 / 读取成功」；
- 解出来的 `Data/Shirts.json` 仍是 **304** 条，我们那条**逐字段还原**：
  `{"Name":"Test Suit","DisplayName":"合成衬衫","Description":"通道验证用","Price":10,
    "Texture":"Characters/Farmer/testsuit","SpriteIndex":0,"DefaultColor":null,
    "CanBeDyed":true,"IsPrismatic":false,"HasSleeves":true,
    "CanChooseDuringCharacterCustomization":false,"CustomFields":null}`
  —— 多出来的字段是模型默认值（打包器把缺失字段补成全字段），`Texture`/`SpriteIndex` 原样 ✓；
- 贴图也解出来了 ✓。

**回归钉子**：`tests/SdvCpTool.Core.Tests/ClothingChannelTests.cs`（钉「转」这一侧的两件事：
条目带 `Texture` 指过去 + 新贴图带上 `Texture2DReader` 的 `.config`）。全量单测 1278 通过。

## 边界（如实记）

- **贴图布局由包自己负责**：原版按 `SpriteIndex` 算格子（衬衫 8×8、左半精灵右半遮罩；
  裤子 16×16 —— 公式见 `2026-10-06-sprite-index-arithmetic.md`）。**我们不校验**贴图尺寸，
  画错了是包的问题，工具照搬。
- **`Texture` 为 null 时**回落 `Characters/Farmer/shirts`（原版图集）—— 那种条目不需要额外贴图，
  本工具本来就转得了（与普通 `EditData` 无异）。
- 同构的还有 `Data/Pants`、`Data/HairData`、`Data/hats`：落点形态一样，走的是同一条写入路径。

## 未确证

- **进游戏看**：这套只验到「XNB 里有这条数据、有这张图」。**穿上身长什么样**（格子对不上会不会
  画歪、染色遮罩灵不灵）没验 —— 要实机，且需要一个真的服装包才值得跑。
