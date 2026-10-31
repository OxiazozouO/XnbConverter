# 同 ID 多键的事件怎么选（Task 22 结案）

日期：2026-10-07
依据：`D:\sdv_src`（1.6.15 反编译源码，只读）。**逐行读过，附行号。**
结论：**两条入口，规则不同；前插顺序同时满足两条；「覆盖既有事件」必须拒绝 —— 但原来的理由是错的。**

---

## 1. 问题

我们把 CP 的 `When` 译成事件键上的一段 `G <GSQ>`（`<事件ID>/<原前置条件段>/G <GSQ>`）。
这样**同一个事件 ID 会出现多条键**（CP 里是「多条补丁覆写同一个键，后写赢」）。
运行时到底怎么在它们之间选？原先**没查清**，代码里给的理由也不对。

---

## 2. 两条入口（**规则不同**）

### 2.1 `GameLocation.checkForEvents` —— 首个**前置条件命中**的赢

```csharp
foreach (string key in events2.Keys)          // 按**文件键序**
{
    string text = checkEventPrecondition(key);
    if (!string.IsNullOrEmpty(text) && text != "-1" && IsValidLocationEvent(key, events2[key]))
    {
        currentEvent = new Event(events2[key], assetName2, text);
        break;                                 // ← 首个命中即结束
    }
}
```
（`GameLocation.cs:15659-15668`）

- 顺序 = **字典插入序 = JSON 文件里的键序**。
- 首个前置条件不成立的键会**被跳过**，继续试下一个。

### 2.2 `GameLocation.findEventById` —— 首个**ID 相同**的赢，**不看前置条件**

```csharp
foreach (KeyValuePair<string, string> item in events)
{
    if (Event.SplitPreconditions(item.Key)[0] == id)
        return new Event(item.Value, assetName, id, farmerActor);   // ← 只比 ID
}
```
（`GameLocation.cs:15680-15718`）

**它完全不检查前置条件** —— 只看键里第一个 `/` 之前那段的 ID 是否相等。
（`Game1.PlayEvent`、脚本命令 `playEvent` 等走这条。）

### 2.3 附带：同一个 ID **只会触发一次**

```csharp
if (check_seen && (Game1.player.eventsSeen.Contains(text) || Game1.eventsSeenSinceLastLocationChange.Contains(text)))
    return "-1";
```
（`checkEventPrecondition`，`GameLocation.cs:16460-16463`；`text = array[0]` 就是 ID）

⇒ ID 一旦进了 `eventsSeen`，它的**所有**键在 §2.1 那条路上都会被判 `-1`。

---

## 3. 结论：我们的「前插」是对的，而且**必须**前插

CP 是**后写赢**（多条补丁命中同一个键时后者覆盖前者），所以**CP 最后写的那条内容**才是有效内容。

我们把每条补丁变成**独立的键**并**逐条前插**（`EditDataMerger` 的 `obj.AddFirst`），
于是文件里的顺序 = **CP 顺序的逆序** ⇒ **CP 最后写的那条排在最前**。

- §2.1：最前的那条若前置条件命中 → **它赢** ✓（等于 CP 的「后写赢」）
- §2.2：最前的那条 ⚠ **无条件**被取 ✓（我们要的正是这个）

**两条入口取到的是同一条** ⇒ 一致。这也是为什么**只**满足 §2.1 是不够的 ——
漏了 §2.2 的话，同 ID 多键时 `findEventById` 会拿到错的那条。

---

## 4. ★ 订正：原代码里拒绝「覆盖既有事件」的**理由写错了**（决定不变）

`Merging/EventKeyRewriter` 原注释：

> 覆盖既有事件时改键会**新增**一条而原事件仍在，**两边都触发** —— 那不是等价转换。

**「两边都触发」是错的**：§2.1 是**首个命中即 break**，两条键不会都触发。
（按字面理解，这条理由其实站不住脚，会让人误以为改写是安全的。）

**真正拦住它的是 §2.2**：

| | CP 的结果 | 我们改键后的结果 |
|---|---|---|
| 文件里同 ID 的键数 | **1** 条（值被覆写） | **2** 条（原键 + 我们前插的） |
| `checkForEvents`（条件不成立时） | 走原版内容 | 我们的键不命中 → 落到原版键 → **也走原版内容** ✓ 对等 |
| `findEventById` | 拿到**那一条**（= 条件成立与否对应的内容） | **永远**拿到我们前插的那条 ⇒ 条件不成立时**也**给出改写后的内容 ✗ **不对等** |

⇒ **必须拒绝**（决定不变），但理由要写成 `findEventById` 这一条。

**新事件的同 ID 多键是安全的**：新 ID 在原版里没有键 ⇒ `findEventById` 只会拿到我们写的那条；
多条之间又由前插保证「最前 = CP 最后写」⇒ 两条入口一致。

---

## 5. 未确证的部分（如实记）

1. **实机未验**。以上全部是源码读出来的，没有在游戏里跑过最小案例。
2. **`When` 的求值时机有差异**（**未确证影响面**）：CP 的 `When` 在**内容加载时**判一次，
   我们译成的 `G <GSQ>` 在**每次事件检查时**判。对 `HasSeenEvent` 这类会中途改变的状态，
   两者**可能**不同。语料里这类补丁的比例**没有测**，先记为差异，不宣称等价。
3. 本次**没有改任何行为**（Task 22 的约束是「查清之前不许改行为」）；
   改的只有注释与文档，把理由说对。
