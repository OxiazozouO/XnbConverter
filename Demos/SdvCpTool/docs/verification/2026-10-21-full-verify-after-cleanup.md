# 清理缓存后的全量验证（2026-10-21）

口径：先按用户要求清空 `D:\tmp` 缓存，再重新构建、跑全量测试、全量转换 + 框架烘焙，
最后对新增条件落点、静态近似和去重做定点核对。

## 1. 缓存清理

```
node tools/clean-cache.js --size
共 648 项，96.1 GB
已清 648/648 项，释放约 96.1 GB
保留：cp_decompile、gv、gv2
```

清理后 `D:\tmp` 约 2.7 GB（只剩白名单）。

## 2. 构建与单测

```
dotnet build src/SdvCpTool.Cli -c Release   → 0 错误
dotnet test ... -c Release                  → 1625 / 1625 通过
```

## 3. 全量转换 + 框架烘焙

```
python tools/convert-mods.py D:/tmp/full-verify --bake-frameworks
```

- 232 个内容包 ×2 趟（Load / Edit）跑完；
- 日志无 `Traceback` / `Unhandled` / `Exception`；
- 进程最终 `exit=1`：因为很多单包报告里仍有 `NoLandingPoint/NeedsReview` 等未落地项
  （CLI 单包返回 1 是“有未落地”，不是崩溃）；最终日志列出了这些包名；
- 框架烘焙汇总：

```
家具 1059 / 装饰 4 / 作物 20 / 可制作 150 / 邮件 3 /
捆绑包 27 / 建筑 21 / 宠物 216 / 草地 4 / 交通 2 / 相似去重 0
```

## 4. 全量报告判定（232 包）

```
patches 32,883
ConvertedNoCondition 11,307
NotApplicable        10,856
Converted             5,837
NoLandingPoint        1,850
NeedsReview           1,558
LocalEdit               591
Emitted                 533
CreatesNewAsset         138
TargetMissing           124
NonTranslatableCondition 35
TargetContainsTokens     34
NeedsRuntime             14
UnsupportedAction         6

written=false（写入失败） 616
```

写入失败前几名仍是 `Data/Festivals/*`、`Tilesheets/junimo_furniture`、
`Maps/Custom_Ridgeside_LogCabinHotel2ndFloor` 等旧问题；`Data/Shops` / `Data/Weddings`
不在前几名，新增落点没有制造新的写入失败潮。

## 5. 新增条件落点

| 资产 | 判定 | 产物里的条件数 |
|---|---|---:|
| `Data/Shops` | `Converted` 29，`NoLandingPoint` 17 | Items 1,117 / 2,677 |
| `Data/Weddings` | `Converted` 26，`NoLandingPoint` 5 | Attendees 109 / 125 |
| `Data/Locations.Forage` | — | 676 / 3,677 |
| `Data/Locations.ArtifactSpots` | — | 521 / 1,438 |
| `Data/Locations.Fish` | — | 1,116 / 2,235 |

`Bear Family - Custom NPCs` 单包核对：

```
Data/Shops: 5 条 NoLandingPoint → Converted
Data/Weddings: 8 条 Converted
Gus 的 Attendee 条件 = ANY "…Sigurd…" "…Gunnar…" "…Teren…"
```

商店商品示例（原条件保留 + AND）：

```
SEASON fall, PLAYER_HAS_MAIL Current Cornucopia.MoreCrops_HerbsPackEnabled Any
```

## 6. 静态近似

- `TerrainFeatures/grass.png`：66×240；原版 `P`/`a4ab15849a9d` → 产出 `RGBA`/`a1194321c6b8`；
  y=0/20/40/80 四行均与 vanilla 不同 ⇒ 四季草都被替换；
- `Maps/BusStop.tbin`：`Warp 17 42 Custom_DTZ_ZuzuCity1`；
- `Maps/Railroad.tbin`：`Warp 7 5 Custom_DTZ_TrainStation`；
- 地图里能看到 `Passable` 属性（设施不堵路）。

## 7. 去重复验（vanilla 底座，AT 17 包）

```
家具 707 / 装饰 4 / 可制作 137 / 相似去重 69
Nano Basic Window 1–7 全部保留
Nano Artist Bookcase 1–9 全部保留
Low Hedge Fall/Spring/Winter Flowering + Seasonal 均保留
第二次重跑：0 新增
手动删掉侧车 version 再跑：0 新增（没有过度补回）
pack-check：918 / 0
```

## 8. 回包

- 新增条件/静态资产定点回包：11 个改过资产，22 个文件（含 `.config`），
  packer 实际产出 11 个 XNB（Weddings/Shops/Fish/Locations/Objects/Characters/
  FarmAnimals/Buildings + 两张地图 + grass），无失败；
  `pack-check.py` 的汇总行在这个只有 Data/地图的小树上打印成 `成功: 0  失败: 0`，
  以实际 `tree_mini_xnb` 的 11 个 XNB 为准。
- 去重小树：`pack-check` **918 / 0**。
- **全量整树 pack-check 未完成**：后台任务在跑到中途时被运行环境以“内存不足”停止，
  不是回包失败；日志为空，残留临时目录已清理。没有拿到全量回包的最终成功/失败数。

## 9. 结论与残留

已确认：

- 清理、Release 构建、全量单测、232 包双趟转换、框架烘焙都完成；
- 新增的 Shops/Weddings 字典/数组条件落点在真实产物里出现，且单包/全量报告都写成 `Converted`；
- 静态近似（草、交通）确实写进了产出树；
- 去重修复的恢复项和幂等性复验通过；
- 全量整树回包因环境内存限制**未完成**，不能声称“整树回包全绿”。
