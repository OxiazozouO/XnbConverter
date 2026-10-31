# FTM 的 `Days` 区间被写成了**非法 GSQ** —— 那些觅食条目的条件整条失效

日期：2026-10-13
怎么发现的：把实机 SMAPI 日志**整份**扫一遍（自检 mod 只检查它自己覆盖的东西），
`Failed parsing condition 'DAY_OF_MONTH 1-4': '1-4' isn't a valid day of month.` **121 行**。

## 0. 判据（游戏源码）

`GameStateQuery.cs` 的 `DAY_OF_MONTH` 走 `Helpers.AnyArgMatches(query, 1, …)`：
**多个参数 = 任一命中**，另外只认 `even` / `odd` —— **没有区间语法**。

```csharp
public static bool DAY_OF_MONTH(string[] query, GameStateQueryContext context)
    => Helpers.AnyArgMatches(query, 1, rawDay => { … int.TryParse(rawDay, out var d) … });
```

⇒ `DAY_OF_MONTH 1 2 3 4` ✓ 合法；`DAY_OF_MONTH 1-4` ✗ 非法 ⇒ 游戏报错，
那条 Condition **整条失效**（本该"这几号里任一天刷新"的觅食条目一下都不刷）。

## 1. 谁写的它：**我们的 FTM 翻译器**

FTM 的 `ExtraConditions.Days` **可以写区间**（语料实测 `"1-4"` 5 处、`"12-14"` 1 处；
源码注释里早就写着"实测取值全是 `1..28` 与 `13-15` 这类区间"）。
`FtmPackTranslator.Unexpressible` 把它拼成逗号串交给 CP→GSQ 通路，
GSQ 侧又是**原样透传**（`TokenTranslator` 的 `"day" => Ok("DAY_OF_MONTH", values)`）⇒
产出就是那个非法写法。产出树里数得出 **48 个条目**（`SdvCpTool_FTM_*`，来自 2 个包）。

⚠ 这一类与「`Season |contains=` 求值器丢参数」是**同一个病根**：
**我们把包里的语义翻成 GSQ 时，目标那边的写法/语法不一样**。
翻完要按**目标的语法**核一遍，不能想当然。

## 2. 修法

`FtmPackTranslator` 里加 `ExpandDayRanges`：`"1-4"` ⇒ `1, 2, 3, 4`；单个 `"13"` 原样。
**只在 `1..28` 内展开**，越界或认不出的形状**原样留着**（宁可让游戏报错，也不替作者猜）。

`Years` 的 `"2+"`（「2 年及以后」）与多值**没修**：GSQ 的 `YEAR` 是闭区间（min [max]），
`2+` 得写 `YEAR 2`、多值得写 `ANY "…" "…"`（那种带引号的形式本项目还没验证过）。
本语料 23 处 `"N+"` **全在刷怪/刷矿/大件段**（那三段本来就整段落不了）⇒ 现在没有实例可验，
按"不做投机改动"先留着 —— 哪天真出现在觅食段再补（缺口与修法都记在源码注释里）。

## 3. 验证

测试：`FtmPackTranslatorTests` +2（区间展开 ⇒ `DAY_OF_MONTH 1 2 3 4 13` 且不再有 `-`；越界原样保留），
并把原先那条**断言了非法输出**的期望值（`YEAR 2+`）改成有效形式（`YEAR 2 2`）。**1428 条全绿**。

受控对比（同参数 232 包，prof93 → prof94）：

| 侧 | 结果 |
|---|---|
| 报告 | **231 包完全相同**（补丁级 0 变化）|
| 产出树 | 31593 个文件里**只有 `Data/Locations.json` 一个不同**；无新增/无消失 |
| 那一个文件 | **54 个条目**的 Condition 从 `DAY_OF_MONTH A-B` 变成 `A B C D`；无残留区间写法 |

实机（把修好的 `Data/Locations.xnb` 单文件装机 —— 单文件打包与整树打包字节等价已证过）：

| | 改前 | 改后 |
|---|---|---|
| `Failed parsing condition` / `isn't a valid day of month` | **121** 行 | **0** 行 |
| 全量探针 | PASS 1245 / 贴图跳过 1249 / FAIL 1 | 同左（唯一失败仍是上游缺 `spring_silo`）|
| 巡图 / 音乐 / 跨天 | 34·4 / 6 / 3 | 同左 |
