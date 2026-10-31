# CP 的值与 XNB 树的值是**两种 JSON 方言**

日期：2026-10-08

## 1. 一句话

同一份数据，CP 的 `content.json` 与 XNB 树对**结构化标量**允许的写法不同 ——
CP 那边可以写 `"PositionOffset": "0, -32"`，XNB 树那边一律写 `{"X": 0.0, "Y": -32.0}`，
而**打包器只认后者**：见到字符串直接报

```
[ERROR] 无效的JSON文件格式: Error converting value "0, -32"
        to type 'Microsoft.Xna.Framework.Vector2'.
```

⇒ **该资产打不出包**。这不是「少写一个字段」，而是值本身要**换一种方言**。

## 2. 两条独立的线索都指向它

| 线索 | 看到的东西 |
|---|---|
| 产出**自检**（`Validating/GameDataValidator`，按游戏源码生成的类型图） | `expected=Struct, actual=scalar` —— 声明为结构化标量的字段上是个字符串 |
| **打包器**（唯一的硬判据） | `Error converting value … to type 'Vector2'` |

自检那侧先前只**报**不修，报告里就一直是「有 8 处形状不符」但产物照样写出去。

## 3. 判据：按**声明的类型**，不按逗号个数猜

「`"0, -32"` 该拆成哪两个分量」只有类型说了算。所以：

- 生成器（`tools/gen-game-data-schema.py`）改成让 `Struct` 字段带上**结构类型名**
  （`Vector2` / `Vector3` / `Rectangle`）—— 这是从游戏源码读出来的**声明事实**；
- 转换器（`Validating/ValueDialectCoercion`）按类型名取分量表：
  `Vector2 → X,Y`、`Vector3 → X,Y,Z`、`Rectangle → X,Y,Width,Height`。

按逗号个数反推类型是**推断**，遇到「两个数但其实是 Rectangle」只会写错 —— 不做。

分量数与声明类型不符（`Vector2` 位上写了三个数）一律**原样不动**：那是另一类问题，
凑出来的东西更难查。

## 4. 只做形状转换，不做别的

| 做 | 不做 |
|---|---|
| `"0, -32"` → `{"X":0.0,"Y":-32.0}` | 改取值 |
| 分量数与类型相符才换 | 分量数不符时硬凑 |
| 只认模型里有的字段 | 对认不出的资产表态 |

## 5. 落在哪一步：落盘前**唯一的漏斗**

`Merging/TranslationApplier.WriteLocalized` 是 `EditData` 与 `Load` 两条写入路径的唯一落盘点。
换在这里，基础资产与**各语言变体**拿到的是同一份已换好的数据 ——
变体在游戏眼里是**独立资产**，漏一个照样回包失败（实测 Cornucopia 就是基础 + 8 个变体一起失败）。

## 6. 受控实测（同参数、逐文件对比）

### 6.1 Cornucopia「聚宝盆 制作」

| | 换之前 | 换之后 |
|---|---|---|
| 回包 | 成功 93 / **失败 10** | **成功 103 / 失败 0** |
| `written` / `writeFailed` | 95 / 1 | 95 / 1（**不变**） |

产出树**逐文件结构对比**：只有 10 个文件变了（`Data/Machines.json` + 9 个语言变体），
每处都是 `PositionOffset` 的 `str → dict`，共 80 处，**没有别的改动**。

### 6.2 SVE

| 资产 | 换之前 | 换之后 |
|---|---|---|
| `Data/Buildings`（`DrawLayers[].DrawPosition`） | 打包**失败** | 成功 |
| `Data/FarmAnimals`（`*PetHitboxTileSize`） | 打包**失败** | 成功 |
| `Data/Fences`（`HeldObjectDrawOffset`） | 打包**失败** | 成功 |

结构差异 **20 处**，与自检报的 20 处 `Struct/scalar` **逐条对上**。

### 6.3 全语料

`Struct/scalar` 命中共 29 处，分布在 3 个包（SVE 20 / 聚宝盆 8 / 食谱技能 1），
字段只有 4 个：`PositionOffset`、`DrawPosition`、`*PetHitboxTileSize`、`HeldObjectDrawOffset`
—— **全是 `Vector2`**。`Rectangle` 那一支今天在语料里**没有实例**（未确证其字符串写法真会出现），
但分量顺序由类型定义给出，不靠猜。

SVE **整包**（改过的 2923 个文件、含地图与贴图，不只是数据资产）定点回包：

| | 改动前 | 改动后 |
|---|---|---|
| SVE 全量 | 成功 1597 / **失败 4** | **成功 1595 / 失败 0** |

那 4 个失败分别是 `Data/Buildings`、`Data/FarmAnimals`、`Data/Fences`（本条改动）
与 `Data/WorldMap`（字段名大小写，见另一份文档）。

⚠ 成功数 1597 → 1595 这个**差额没对上**，如实记：
把两轮的「改过的文件」逐文件对了一遍（`D:\tmp\svesetdiff.py`），差的是 **12 个文件**，
且**全部**是那 6 个现在被拒写的 `Mods/<其它 mod>/…` 资产（各 `.json` + `.config`），
「改动后新有的」是 **0** 个 —— 也就是说**没有少写别的东西**。
按「一个数据资产出一个 xnb」本该少 6 个成功，实际只少 2 个。这个 4 的出入**未确证**
（可能是打包器计数的口径，也可能是我对旧那次的读数有偏），**不影响结论**：
失败 4 → 0 是打包器自己的输出，确定。

## 7. 顺带查清的两件事

### 7.1 `Color` **不是**这类问题（探针证伪）

生成器刻意不收 `Color`，注释说「它在数据里可能是字符串，未确证」。现在有实证了 ——
拿原版 `Data/Machines` 做母本，把 `Color` 依次替换成下列写法**逐个打包**：

| 写法 | 打包 |
|---|---|
| `"#bfbf00bf"` | ✅ |
| `"White"` | ✅ |
| `"0 255 0"` | ✅ |
| `"255, 0, 0"` | ✅ |
| `"red"` | ✅ |

**打包器的 Color 转换器什么写法都认**，所以这里不需要换方言。语料里这几种写法都真实出现过
（`"0 255 0"` 131 处、`"White"` 等命名色若干）；实测产出的 `Data/Machines.json` 里
`"White"` / `"Lime"` / `"Yellow"` / `"DarkCyan"` 与 `"#BFBF00BF"` 并存，而该资产回包成功。

### 7.2 空字符串写在数组字段上**打得出包**（不修，只记）

自检对 SVE 的 `Data/FarmAnimals` 报了 3 处 `StatToIncrementOnProduce`「期望 Array 实际 scalar」。
查下来这是 **mod 自己写的**（SVE 的 `code/Other/FarmAnimals.json` 原文就是 `"StatToIncrementOnProduce": ""`），
模型是 `List<StatIncrement>`。**实测它打得出来**：单独打包该资产 **成功 1 / 失败 0**；
往返（打包再解回 JSON）后它是 `null` —— 也就是「没有要加的游戏计数」，与 mod 的原意一致。

所以这条**不是**方言问题：打包器自己就把空串当空值收下了，我们再「纠正」一遍没有收益。
自检照旧报它（如实描述形状与声明不符），不去给它开后门。

### 7.3 自检**看不见语言变体**（已修）

`GameDataSchema.AssetElement` 认的是主资产名，`Data/Machines.zh-CN` 查不到就被**静默跳过** ——
而变体在游戏眼里是独立资产，打包器照样会报它。实测代价：Cornucopia 的 `Data/Machines`
连 8 个变体回包全失败，自检只报了基础资产那 8 处。修法：查模型前按区域码形状（`<2 字母>-<2 字母>`）
去掉变体后缀，判据复用 `PackTranslations.LanguageCodeString` 那一份，不另立语言表。

修后：聚宝盆两个包的 `checkedAssets` 33 → 63 / 69，`shapeMismatch` 0。

## 8. 第二种方言：**整数值写成了小数记号**（2026-10-12 补，已修）

同一类问题的第二个实例，判据与落点完全一样 —— 所以并到同一份文档、同一个转换器
（`Validating/ValueDialectCoercion`，原先叫 `StructScalarCoercion`：它现在管两条规则，
旧名字会变成误导，改掉了）。

### 8.1 现象与定性

整树回包失败 25 条里的 6 条是：

```
[ERROR] Data/Objects.config 无效的JSON文件格式: Input string '0.0' is not a valid integer.
        Path '['MadDog.HashtagBearFam_BrazierSecretAlfredo'].Buffs[0].IconSpriteIndex'
```

- 字段类型是**声明的**：`D:\sdv_gd\...\ObjectBuffData.cs:37` 是 `public int IconSpriteIndex`；
- 值来自**模组**（Bear Family 的 `EditData Data/Objects` 写 `0.0`）—— CP 那边能过，
  XNB 树这边打包器不认；
- **`0.0` 与 `0` 是同一个数** ⇒ 换成整数写法**不改变取值**（这是敢换的全部理由）。

失败的 6 个文件 = `Data/Objects` ＋ 它的 5 个语言变体（`zh-CN`/`ko-KR`/`ja-JP`/`fr-FR`/`es-ES`；
只有**翻译过那些条目**的语言才有这个值 —— `de-DE` 等 6 个变体里没有，它们本来就打得出来）。

### 8.2 判据：新增一张**整数字段表**（不是按"看起来像整数"猜）

`GameDataSchema` 原先只收**容器型**字段（标量没有形状可查），所以新加了一张
`IntegerFields`：**类 → 声明为整数类型的字段名**，来源还是 `D:\sdv_gd` 那份模型
（`byte/sbyte/short/ushort/int/uint/long/ulong` 八种）。生成器多收这一档，表里有 **56 个类**。

⚠ **没有这张表就不能做**：浮点字段上 `0.0` 是**正常写法**（原版满树都是 `{"X": 0.0}`），
一刀切会把几千个文件的写法都改掉 —— 树里一大堆无谓差异，还与原版的方言不一致。

### 8.3 落点与边界

- 落点不变：`TranslationApplier.WriteLocalized`（唯一漏斗），所以基础资产与各语言变体拿到的是同一份；
- 只换**声明为整数类型**的字段上的、**值为整数**的 token；
- 带**小数部分**的**原样不动**（见 8.4）；认不出的资产不表态（与结构化标量那条一致）。

### 8.4 同一天**不修**的两类（都在上游数据里，不是我们的账）

| 资产 | 值 | 为什么不修 |
|---|---|---|
| `Data/Weapons` | `Precision: 0.3 / 0.5`（`WeaponData.Precision` 也是 `int`） | 换写法就会**改取值**（0.5→0）。按「宁可少写，不可写错」不猜 ⇒ 这 4 个文件（基础 + 3 个变体）仍然打不出包，装机时**那一个资产回落到原版** |
| `Data/Characters` | `Antiunn.EndSlideShow: true`（字段是枚举 `EndSlideShowBehavior`） | 上游 1.5 时代这里是布尔，1.6 改成枚举 ⇒ `true` 该映射成哪个枚举成员**没有依据**。8 个文件同样打不出包 |

### 8.5 A/B 实测（只打这几个文件，同参数）

`D:\tmp\packer`、`cwd` 是打包器自己的目录：

| | 结果 |
|---|---|
| 改前（`prof79` 的 `Data/Objects{,.zh-CN,.ko-KR}.json`） | **3 × `Input string '0.0' is not a valid integer`**，一个 XNB 都没产出 |
| 改后（同一份数据过一遍转换器） | **无 ERROR，`Objects.xnb` / `Objects.zh-CN.xnb` 产出** ✅ |

（该资产在全语料上的受控对比见 `verification/2026-10-12-integer-notation-dialect.md`。）

## 9. 仍未确证

- `Rectangle` 的字符串写法在语料里**没有实例**，所以「CP 侧能不能写 `"x, y, w, h"`」没有实证；
  转换器那一支是**按声明类型**补的，不是按观察补的。
- `Vector3` 在可达类型图里**根本不存在**（40 个 `Struct` 字段只有 `Vector2` 10 个、`Rectangle` 15 个），
  分量表里留着它只是为了这张表与 `STRUCT_TYPES` 同源。
- 8.4 那两类**游戏自己到底怎么读**（截断？报错？）**未确证**：CP 用户在用那些包，
  所以游戏侧大概率宽容，但我们没去验 —— 也就不敢照"猜"的对应关系改写。
