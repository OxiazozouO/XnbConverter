# 婚姻与婚房审计（当前产出树）

日期：2026-10-27

## 1. 可婚 NPC

`Data/Characters.json` 当前共 **59 个 `CanBeRomanced=true`**：

- 12 个原版可婚角色；
- **2 个被模组改成可婚的原版 NPC**：Wizard、Sandy；
- **45 个新增可婚 NPC**（Downtown Zuzu、RSV、SVE、East Scarp、Bear Family、LASV 等）。

`Data/NPCGiftTastes.json` 对这 59 个全部有键，且没有任何一个 `CanReceiveGifts: false`，花束/吊坠不会因为缺礼物条目或拒收礼物而送不出去。

原版婚姻流程依赖的静态字段逐项核对：

- `CanBeRomanced`：59 个全部是真布尔 `true`；
- `CanReceiveGifts`：没有 false；
- `Data/NPCGiftTastes`：59 个全部存在（本地化变体也全部补齐）；
- `SpouseRoom`：58 个有配置，Clara.Ripley 为 null；
- `MarriageDialogue<NPC>`：base 58 个存在，Clara 缺。

## 2. 婚房

对每个可婚 NPC 取：

```
MapAsset = SpouseRoom.MapAsset ?? "spouseRooms"
MapSourceRect = SpouseRoom.MapSourceRect ?? (0,0,6,9)
```

逐张解析地图 `Back` 图层尺寸后：

- 58 个婚房矩形全部在地图范围内；
- 婚房地图 XNB 全部存在；
- 全树 1,252 张地图的图集引用复核：**找不到图片的图集名产出新增 0**，
  婚房地图的图集不会空白；
- **Clara.Ripley 没有 `SpouseRoom`**：原 mod 本身没提供婚房，纯 XNB 造不出来；
  娶她时会回落到默认婚房 `spouseRooms` 的 `(0,0)`（Abigail 那套）——这算“能结婚但婚房不对”，已保留为明确提示。

典型婚房：

| NPC | MapAsset | 地图尺寸 | 源矩形 |
|---|---|---:|---|
| Wizard | `Custom_WizardsRoom` | 7×10 | 6×9 |
| Sandy | `Custom_SandysRoom` | 6×9 | 6×9 |
| RSV 13 人 | `RidgesideSpouseRooms` | 30×45 | 6×9 各格 |
| East Scarp 4 人 | `Lemurkat_EastScarp_SpouseRoom` | 30×27 | 6×9 各格 |
| Bear Family 3 人 | `MadDog.Bearfam_Decorated_SpouseRoom` | 18×9 | 6×9 各格 |
| Mateo/Cirrus/Hector | 各自 `*SpouseRoom` | 6×9 | 6×9 |
| DTZ 6 人 | 各自 `Custom_DTZ_*Room` | 6×9 或 18×18 | 6×9 |

## 3. 本地化角色表：修掉一个真问题

审计发现 `Data/Characters.<locale>` 是**整份替换**，但之前的婚姻修复只把 base 布尔归一，
没有把 base 的 NPC 行补进本地化：

| 语言 | 修复前行数 | 缺失可婚 NPC | Sandy 标记 |
|---|---:|---:|---|
| es-ES | 99 | 33 | false |
| ja-JP | 97 | 33 | false |
| tr-TR | 96 | 33 | false |
| fr-FR | 267 | 5 | false |
| ko-KR | 266 | 5 | false |
| ru-RU | 268 | 5 | false |
| zh-CN | 292 | 0 | true |

新增 `MarriageReadiness.MirrorLocalizedCharacters`：

- 缺失行从 base 整条克隆；
- 已有行只同步 `CanBeRomanced`、`SpouseRoom`、`SpousePatio`、`SpouseFloors`、
  `SpouseWallpapers`、`SpouseAdopts` / `SpouseWantsChildren` / 嫉妒相关字段；
- 本地化 `DisplayName`/`Description` 保留。

修复后 **7 个语言变体全部 292 行、0 缺失、0 错误 `CanBeRomanced`**；本地化
`Data/Characters.*.xnb` 已重新回包并安装到 `D:\tmp\gv\Content\Data`。

同样的问题也存在于 `Data/Weddings.<locale>`：fr-FR / ko-KR 只有 91 个 attendee、
33 个 EventScript，base 是 125/35。已加 `MirrorLocalizedWeddings`，只补缺失的
`Attendees`/`EventScript` 键、不覆盖已有本地化；现在 fr-FR / ko-KR 也都是 125/35，
3 个本地化 XNB 已回包并安装。

## 4. 婚后对话

- base `Characters/Dialogue/MarriageDialogue<NPC>`：除 **Clara.Ripley** 外全部存在；
- zh-CN 变体缺 10 个（DTZ 6 人 + Bianka/Lunna/Raphael）；
  缺本地化时游戏会回落到 base 英文，不是婚姻断点；
- East Scarp `JadeMalic` 的别名修复仍然有效（79 条）。

## 5. 结论

- **新增 NPC：58 个有完整婚房的可婚 NPC**，可以正常走“送花束/吊坠 → 结婚 → 婚房”；
- **修改过的原版 NPC（Wizard、Sandy）**：同样有房、有礼物表、有婚后对话；
- **Clara.Ripley 例外**：能结婚，但没有专属婚房，会落进默认房间；不臆造内容；
- **本地化语言**：修复后所有语言都有完整角色行，不会出现“中文能娶、切语言就不能娶”的差异；
- 以上为静态树 + XNB 回包 + 地图尺寸核对，**没有启动游戏**，婚礼流程仍需实机确认。

完整单测：**1672 通过，0 失败**。



