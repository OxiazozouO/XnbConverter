# Custom Companions：把原版 cue 映射到 `Data/Pets.BarkSound`

日期：2026-10-28

## 现状

`Custom Companions` 的 `companion.json` 可以带：

```json
"Sounds": [
  {
    "SoundName": "dog_bark",
    "WhenToPlay": "Idle",
    "TimeBetweenSound": 2500,
    "ChanceOfPlaying": 0.15
  }
]
```

之前 `ProcessCompanions` 把 `BarkSound` / `ContentSound` 都写成 `null`，报告统一写
“自定义 Sounds 丢失”。全语料统计：

- 216 个 `companion.json`
- 166 条 Sounds，36 个不同 cue
- 147 条 `Idle`、4 条 `Always`、15 条 `Moving`
- **36 个 cue 全部在原版 `XACT/Sound Bank.xsb.json` 里**（不需要注入音频）

## 映射

- `Idle` / `Always` 的第一条 `SoundName` → `Data/Pets.BarkSound`；
- `Moving` 的第一条 `SoundName` → `Walk.SoundOnStart`（原版没有持续行走声；
  Wander 的 Walk↔Idle 循环会隔一段时间重新触发）；
- 一个宠物有多个 `Idle/Always` 或 `Moving` 时只保留第一个；
- cue 不在原版 Sound Bank 时不写，避免让游戏播放不存在的 cue；
- 时序、概率、`ContentSound`（抚摸音）原版没有等价字段，仍报告损失。

## 全量结果

重新对 `D:\tmp\full-v11\tree` 跑 `bake-frameworks`：

```
合计：… / 宠物 216 / …
Data/Pets.json：219 条记录，其中 144 条带 BarkSound
```

前几个实际写入值：

```
dustMeep 21 / rabbit 18 / cat 11 / dog_bark 8 / junimoMeep1 8
SpringBirds 7 / Duck 7 / seagulls 7 / dog_pant 6 / grunt 6
```

## 验证

- 测试夹具的 `TestPet` 带一个 `dog_bark/Idle`，断言 `Data/Pets` 写入 `BarkSound=dog_bark`
- 完整单测：**1688 通过，0 失败**
- `Data/Pets.json` 回包：Packer 成功 1、失败 0；XNB 已覆盖到
  `D:\tmp\full-v11\packed\Data\Pets.xnb` 与 `D:\tmp\gv\Content\Data\Pets.xnb`
- 未启动游戏；音效听感仍需实机确认

## 补充：`IdleBehavior: Nothing`

原来所有 CC 宠物都只生成一个 `Walk` 状态，`IdleBehavior: Nothing` 的宠物也会一直走。
现在：

- `IdleBehavior: Nothing` **或字段省略** → `Data/Pets.Behaviors` 生成一个静止的 `Idle`
  （CC 的 `CompanionModel.IdleBehavior` 默认值就是 `NOTHING`）；
- `Pacing` → `Walk` 加 `IsSideBehavior`，至少限制为左右方向；
- `Wander` / 未知玩法仍用原来的 `Walk`；
- `Jumper` / `Hover` / `SIMPLE_PATH` 没有精确原版状态机，仍按 `Walk` 近似并逐条报告。

此外，`IdleBehavior: Wander` 且 `CanHalt`/`ChanceForHalting` 允许停的同伴，
现在生成 `Walk ↔ Idle` 两态循环：

- `Walk` 3–8 秒后转 `Idle`；
- `Idle` 使用 CC 的 `MinHaltTime`/`MaxHaltTime`，再转回 `Walk`；
- `Type: Flying` 的同伴不 halt（CC 的 `canHalt` 同样排除飞行）。

全量树重跑后：

- **10 个宠物**从 `Walk` 变成单 `Idle`；
- **8 个宠物**的 `Walk` 增加 `IsSideBehavior`；
- **185 个宠物**生成 `Walk ↔ Idle` 两态行为。

## 幂等性修复

新增 `SameJson`：框架烘焙第一次写入的 `int` 与从 JSON 读回的 `long` 在
`JToken.DeepEquals` 下会被当成不同值，导致第二次跑重复写同一个宠物。
现在比较前先 `ToString(Formatting.None)` 再 `Parse`，按实际 JSON 内容比较；
`FrameworkBakerTests` 的二次烘焙断言恢复为 0 新增。

## 边界

- 不是 CC 的完整 `Sounds` 系统：只有原版 `BarkSound` 一个偶尔播放槽；
- `WhenToPlay` 的时机、`TimeBetweenSound`、`ChanceOfPlaying` 都丢失；
- `Moving` 音效没有槽；
- 自定义 cue 若不在原版 Sound Bank，本工具不写入，报告会指出。
