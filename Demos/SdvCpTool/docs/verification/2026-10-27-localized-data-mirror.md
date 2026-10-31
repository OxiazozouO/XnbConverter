# 本地化 `Data` 全量补键

日期：2026-10-27

## 1. 短板

游戏对 `Data/<asset>.<locale>` 是**整份替换**，不会按 key 回落 base。全量树里实际存在：

| 资产 | base | 典型本地化 | 缺失 |
|---|---:|---:|---:|
| `Data/Locations` | 865 | ko-KR 353 / fr-FR 406 / es-ES 665 | 最多 512 |
| `Data/NPCGiftTastes` | 214 | tr-TR 85 / ja-JP 86 | 最多 129 |
| `Data/Quests` | 193 | 多语言 66–87 | 最多 127 |
| `Data/SpecialOrders` | 193 | 多语言 111 | 82 |
| `Data/EngagementDialogue` | 95 | tr-TR 26 | 69 |
| `Data/Weddings` | 125 attendee / 35 script | fr-FR / ko-KR 91 / 33 | 34 + 2 |

这些不是“未翻译”，而是**整行不存在**。中文语料大多有完整 zh-CN，但换语言会直接少地点、任务、礼物口味、婚礼 attendee。

## 2. 实现

新增 `LocalizedDataMirror`，在所有补丁应用完、落盘前统一扫描：

- `Data/**/<asset>.<locale>.json`；
- `Strings/**/<asset>.<locale>.json`；
- `Characters/Dialogue/**/<asset>.<locale>.json`；
- 与同名 base 对比；
- 字典只补**缺失的顶层键**；
- 数组只补带唯一 `Id` 的缺失对象；
- 不覆盖本地化已有值；
- 不把没有 `Id` 的数组元素硬塞进去。

这一步在 `MarriageReadinessFix` 之前执行，保证婚姻检查看到完整的
`Data/Characters` / `NPCGiftTastes` / `Weddings`。

## 3. 结果

`Data` 资产：

```
补键文件：547 个
补入键/数组元素：33,477 条
剩余顶层字典缺口：0
```

同一套规则继续覆盖 `Strings/**` 与 `Characters/Dialogue/**`：

| 资产 | base | 最差本地化 | 缺失 |
|---|---:|---:|---:|
| `Strings/StringsFromMaps` | 4,095 | it-IT 367 | 3,728 |
| `Strings/SpecialOrderStrings` | 949 | 多语言 146 | 803 |
| 其它 Strings / Dialogue | — | — | 大量 |

结果：

```
Strings/Dialogue 补键文件：831 个
补入键：89,632 条
回包：831 成功 / 0 失败
```

累计本地化资产：

```
补键文件：1,378 个
补入条目：123,109 条
回包失败：0
```

此外，已有角色的 `Appearance[]` 也可能缺季节/节日外观（只补顶层 NPC 行不够）：

```
es-ES 508 / ja-JP 508 / tr-TR 508
fr-FR 271 / ko-KR 271 / ru-RU 271
```

共补 **2,337 个 Appearance 变体**；7 个本地化 `Data/Characters` XNB 重新回包并安装。已有外观不覆盖，只补 base 有、locale 没有的 `Id`。

### 3. 嵌套字段也补

只补顶层键仍会留下“有地点行、没有 Fish/ArtifactSpots 等嵌套数组”的情况。
`MirrorToken` 进一步递归：

- 对象已有键但内部缺字段 ⇒ 继续补；
- 数组按 `Id` 找已有元素并继续补内部字段；
- 没有 `Id` 的数组元素不猜。

全量结果：

```
52 个本地化 Data 文件
5,776 个嵌套字段/元素
例如 Locations：es-ES +1789、fr-FR +1300、ko-KR +1058、zh-CN +104
回包 52 成功 / 0 失败
```

已覆盖安装到 `packed` 与 `gv/Content`。

这些 XNB 已覆盖安装到 `D:\tmp\full-v11\packed` 与 `D:\tmp\gv\Content`。

## 4. 口径

- 这修的是**结构存在**：缺的文本会回落 base 英文，不等于翻译完成；
- 不修改已有本地化文本；
- 不是把 base 的每个值覆盖回本地化；
- 没有启动游戏；
- 单测新增 `LocalizedDataMirrorTests`，完整单测 **1672 通过，0 失败**。


