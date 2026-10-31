# 把自定义 NPC 变成原版可结婚：数据要求、语料缺口与转换方案

日期：2026-10-18

## 0. 结论

原版「可结婚」= **`Data/Characters.CanBeRomanced: true`（布尔）+ 能收礼 + 婚房 + 对话/立绘/行走图**。
只加一个 bool 不够：`Data/NPCGiftTastes` 缺键时花束/吊坠**根本送不出去**；
`SpouseRoom` 缺失会落到 **Abigail 的房间**；地图/矩形越界会在进农舍时**崩溃**。

## 1. 最小资产清单（源码依据）

| 资产/字段 | 必须性 | 缺失后果 | 依据 |
|---|---|---|---|
| `Data/Characters.<NPC>.CanBeRomanced` | 必须（布尔） | 不可攻略；花束/吊坠拒绝 | `NPC.cs:819`、`:2087`、`:2246` |
| `Data/Characters.<NPC>.Home` | 必须 | 回退 Town (29,67)；Location 无效则 NPC 不生成 | `NPC.cs:851-869` |
| `Data/NPCGiftTastes.<NPC>` | **必须** | `CanReceiveGifts()` false ⇒ 花束/吊坠送不出去 | `NPC.cs:1391-1398` |
| `Characters/<TextureName>` | 必须 | NPC 不生成 | `Game1.cs:7334-7343` |
| `Portraits/<TextureName>` | 必须 | NPC 不生成 | `Game1.cs:7338` |
| 行走图婚礼帧（女 36-38 / 男 48-50） | 必须（视觉） | 婚礼显示空白/错误姿势 | `Event.cs:830-833`；`Data/Weddings.json` |
| `Data/Characters.<NPC>.SpouseRoom.MapAsset`+`MapSourceRect` | 强烈建议 | 缺失 ⇒ `spouseRooms` 0,0,6,9 = Abigail 房间；地图缺失/矩形越界 ⇒ **进农舍崩溃** | `FarmHouse.cs:1991-2005`；`GameLocation.cs:1762` |
| `Characters/Dialogue/MarriageDialogue<NPC>` | 建议 | 回退通用文件；`spouseRoom_<NPC>` 缺失显示错误占位 | `NPC.cs:3542-3599` |
| `Data/EngagementDialogue.<NPC>0/1` | 可选 | 通用订婚句 | `NPC.cs:1658-1695` |
| `Data/Weddings` 默认脚本 | 原版已有 | 不要删 | `Utility.cs:2275-2280` |
| `setDating`/`setEngaged`（SpaceCore） | **删除** | 纯 XNB 下是未知命令，什么也不会做 | `SpaceCore.decompiled.cs:6625-6710` |

关键细节：
- `SpouseRoom` 的矩形**不硬性 6×9**，但农舍预留区是 6×9；`Paths` 层 index 7 决定配偶站位。
- `loadSpouseRoom` 用 `ApplyMapOverride` **原样复制**图块 ⇒ 婚房地图上**不能有 `ConditionalDoor`/`TouchAction`**（会被复制进农舍）。
- 婚房地图可以是一张**共享网格**（不同 NPC 用不同 `MapSourceRect`）或**整张自定义图**（通常 0,0）。
- 原版没有「按存档条件切换 `CanBeRomanced`/SpouseRoom」的机制；纯 XNB 只能冻结一个值。

## 2. 语料盘点（`prof-fix13` 前的 269 条 `Data/Characters`）

- `CanBeRomanced: true` **38 条**；核心数据齐全的 **34 条**可直接走原版花束/吊坠/婚礼。
- 婚房地图形态：
  - 原版 `spouseRooms`（30×27，5×3 网格）：12 个原版角色 + Shane 的 `0,18` 格（SVE 补丁）。
  - 自定义整图：SVE `Custom_*Room`（7×10）、DTZ `Custom_DTZ_*Room`、Jonghyuk 等。
  - 自定义共享网格：`RidgesideSpouseRooms`、`LASVSpouseRooms`、`SBVSpouseRooms`、`Lemurkat_EastScarp_SpouseRoom`、`ARVSpouseRooms`。
- **没有模组覆盖整张 `FarmHouse1_marriage`/`FarmHouse2_marriage`**；主屋地图与 `sdv_tree` 逐字节相同。

## 3. 发现的系统性缺口

1. **RSV 39 个 `Data/Characters` 条目整条被跳过**：
   `WinterStarParticipant: "{{Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals |contains=true}}"` 是
   **跨模组配置令牌**，原 `DynamicTokenResolver` 不认 ⇒ 整条条目丢弃（其中 13 个本可结婚的 NPC：
   Ysabelle/Flor/Philip/Ian/Kenneth/Maddie/Corine/Kiarra/Jeric/Shiro/Blair/Alissa/June）。
   后续换装补丁的 `TargetField` 只创建出只剩 `Appearance` 的半截条目。
   **已修**：新增 `ModConfigTokens`，读目标 mod 的 `config.json` 求 `{{<UniqueID>/<field>}}`。
2. **East Scarp 婚房地图被覆盖成 12×9**：
   新包 `[CP] East Scarp NPCs` 的 12×9 覆盖旧包 30×27；但旧包的
   `CorwinLK (0,18)`、`DaleWaede (6,18)`、`JadeMalic (18,9)` 矩形仍在 `Data/Characters` 里
   ⇒ **越界**，婚后进农舍会崩。需要保留 30×27 版本/合并，或给这 3 人换格。
3. **S&S / Bear Family 的 `Data/Characters` 被动态令牌挡住**（**已修**，见 §4）：
   `{{MateoRelationshipStatus}}` 等有多个条件块，默认“有损确定化一律拒绝” ⇒ 整个补丁不写。
   Mateo/Cirrus/Hector/Gunnar/Sigurd/Teren 的婚房地图、立绘、`MarriageDialogue*` 都在；
   已加「婚姻目标」模式：带婚房的条目把这些令牌冻结为可结婚值。
   - Bear 还多两道坎：`Include.FromFile` 里的 `{{SeasonalOutfits}}` 在解析阶段没代（整个 Include
     被当成文件不存在）；条目内 `Appearance[].Condition` 的 `{{Query: {{Year}} % 2 = 1}}`
     让整条 NPC 因「无法静态展开」被拒。**均已修**（§4）。
   - Hector 的 `DisplayName: {{TrueName}}` 有损展开后只剩 `{{i18n:…}}`，整条被拒 ——
     已允许「有损但结果只剩 i18n」的展开。
4. **Lance / CorwinLK 缺 `Data/NPCGiftTastes`**：
   Lance 的礼物条目是 `HasSeenEvent 6951319` 条件补丁，被丢；Corwin 的条目含代不掉的令牌。
   ⇒ 花束/吊坠送不出去。需要无条件写入或补一份通用礼物条目。
5. **Clara.Ripley**：`CanBeRomanced: true` 但 `SpouseRoom = null`（Abigail 房间），
   无 `MarriageDialogueClara`/`EngagementDialogue`；只做 `setDating`，没有婚姻内容。
6. **JadeMalic 婚后对话 0 键**：`MarriageDialogueJadeMalic` 是空资产，文本被写到
   `MarriageDialogueJade`；游戏按 NPC 名加载前者。
7. **Sterling 的 `CanBeRomanced` 是字符串 `"true"`**：需要改成布尔。
8. **S&S 婚后文本 2230 条 `(no translation:...)`**（**已修**）：i18n 源存在，但 `PackTranslations`
   只扫 `i18n/` 顶层文件；S&S 用 `i18n/default/*.json`、East Scarp NPCs 用 `i18n/Default|Zh/*.json`
   —— 语言名在**目录**上、同语言多文件需合并。改递归扫描后（S&S+Bear 4 包、同参数对照）
   占位次数 **3760 → 128**；Jade 的显示名从 `(no translation:Name.Jade)` 恢复为 `Jade`
   （East Scarp 另跑，见验收文档）。

## 4. 已落地

- **配偶房间地图不设门禁**（`SpouseRoomMaps` + `PatchJudge`）：门禁会被 `ApplyMapOverride`
  复制进农舍。`Maps/spouseRooms` 的 `ConditionalDoor` 已从 51 块降到 0。
- **跨模组配置令牌**（`ModConfigTokens`）：读目标 mod 的 `config.json` 求
  `{{<UniqueID>/<field>}}`。定点验证（只跑 RSV 主包）：
  `Data/Characters` 从 108 条恢复到 **108 条 + 39 条完整条目**（Ysabelle/Flor/Philip/Ian/…），
  13 个可婚 NPC 全部有 `CanBeRomanced: true` + `SpouseRoom` + `WinterStarParticipant: true`。
- **配偶房间共享图保留更大版本**（`LoadApplier.PlaceMap`）：后写的更小地图不会覆盖已有大图
  （East Scarp 新包 12×9 不再盖掉旧包 30×27），避免 `MapSourceRect` 越界崩溃。
- **婚姻可用性最小修复**（`MarriageReadiness`，落盘前）：
  `CanBeRomanced` 字符串→布尔（Sterling）；可婚但缺 `Data/NPCGiftTastes` 的 NPC 补中性 10 段条目
  （Lance / CorwinLK），否则花束/吊坠送不出去。
- **关系状态令牌冻结**（`ValueTokenSubstitutor.FreezeRomanceFlags`）：
  `CanBeRomanced: {{XRelationshipStatus}}` 且条目带 `SpouseRoom.MapAsset` ⇒ 冻结为布尔 `true`。
  定点（S&S+Bear）恢复 Mateo / Cirrus / Hector / Sigurd / Gunnar / Teren 六人，
  六人都有婚房、礼物条目。没有婚房的 Hurst/Nora/Rodney/Cambria 不冻结（只报提示）。
- **Include 路径里的配置令牌**（`ContentPack.RootConfigSchema` + `ConfigTokenSubstitutor.ReplaceForPath`）：
  `Data/{{SeasonalOutfits}}_NPCData.json` 不再被当成"文件不存在"。
- **角色条目里的令牌条件**（`SanitizeCharacterConditions`，在值代换**之后**跑）：
  `Appearance[].Condition` / `Home[].Condition` 里仍残留 `{{…}}` ⇒ 只删那一条变体；
  代出来的 `true`/`false` 再折成合法 GSQ（游戏只认大写 `TRUE`/`FALSE`，
  `GameStateQuery.cs:2076`）⇒ SCC 的节日外观不再因 `IS_EVENT …, true` 永久失效。
  `Home[].Location` / `Tile.X/Y` 的地图令牌允许**有损确定化到默认块**（Gunnar/Teren 的住所）。
- **跨包外观不再被整条替换冲掉**（`EditDataMerger`）：`Data/Characters` 的整条替换若新条目
  没写 `Appearance`，保留已有的（CP 全局按 Priority 应用，`Late` 外观补丁在整条数据之后；
  我们按包顺序跑）。语料：Eyvinder 的 7 条 SCC 季节外观 + 3 条 Drawberry 生成外观全部保住。
- **同名生成外观资产复用**（`AppearanceEmitter.TryWriteVariant`）：两个包先后为同一 NPC
  发射 `_Appearance<n>` 时，后一组不再整组失败；否则先发射的一组只写了行走图，立绘一份都没有
  （Eyvinder 3 张肖像就是这么丢的）。
- **TMX 路径也保住更大的配偶房间共享图**（`LoadApplier.LoadMapTmx`）：原来只有 `.tbin` 那条
  `PlaceMap` 有长度守卫，TMX 走 `MapCache` 绕过了它 ⇒ 全量链里 `[CP] East Scarp NPCs` 的
  12×9 盖掉 `East Scarp NPCs` 的 30×27，CorwinLK（0,18）/DaleWaede（6,18）/JadeMalic（18,9）
  矩形全部越界。
- **发射器补上跨包已有外观**（`AppearanceEmitter.TryBuildCharacterPatch`）：本包新条目没写
  `Appearance` 时，把产出树里已有的那批一并写进条目；否则整条替换只留本次发射的 3 条
  （Eyvinder 的 15 条显式＋生成外观被压成 3 条）。
- **`TrueName` 的有损 i18n 展开**：有损结果只剩 `{{i18n:…}}` 时放行（Hector 整条恢复）。
- **婚后对话资产名别名**（`MarriageReadiness.FindDialogueAlias` + CLI）：
  East Scarp 的 `JadeMalic` 从 `MarriageDialogueJade` 复制 79 条，含 `.zh-CN` 变体与 `.config`。
- **SpaceCore 恋爱状态命令**（`EventCommandFixes.RemoveStateCommands`）：
  删除事件里的 `setDating` / `setEngaged`（含 `quickQuestion` 答案分支）。
- **嵌套 i18n 目录**（`PackTranslations`）：递归扫描，语言取第一层目录名，同语言多文件合并。
- 单测 **1516 条全绿**。

## 5. 推荐转换流程（待实现）

1. 目标列表 + 策略（`VanillaMarriage` 推荐；`HybridNarrative` 备选；`MailOnly` 不推荐）。
2. 合并/修正 `Data/Characters`：`CanBeRomanced=true`（布尔）、`SpouseRoom` 指向有效地图/矩形、
   `Home`/`Appearance`/`TextureName` 保留；令牌冻结为具体值。
3. 婚房：A 类自定义图→整张进树并校验；B 类 `spouseRooms` 格→`ToArea==MapSourceRect`、
   不设门禁、`Paths 7`；越界/缺失→回退 `spouseRooms` 空格 18,18 / 24,18（或扩格）。
4. 对话/文本：`Data/EngagementDialogue`、`MarriageDialogue<NPC>`（至少 `spouseRoom_<NPC>`）、
   `AcceptBouquet`/`AcceptMermaidPendant`；`Data/NPCGiftTastes` 补键。
5. 事件：删除 `setDating`/`setEngaged`（让玩家用原版花束 458 / 吊坠 460）；
   或混合方案：给玩家邮件 + 花束/吊坠，让真实状态最终同步。
6. 静态校验 + 实机探针：地图存在/矩形界内/Paths 7/无门禁/对话键/立绘；
   实机：送花束→订婚→婚礼→进农舍看婚房/配偶。
