# `Entries` 的条目语义：**两个容器都是整值，没有逐字段合并**（已结案）

日期：2026-10-08　　状态：**已确证**（反编译 `ContentPatcher.dll` 2.9.1 核实）

## 1. 结论

```csharp
// EditDataPatch.ApplyRecords，两条容器走的是**同一段**代码
object obj   = editor.ParseKey(record.Key.Value);
Type   type  = editor.GetEntryType(obj);
JToken val   = record.Value?.Value;
if (val == null) { editor.RemoveEntry(obj); continue; }
object value = val.ToObject(type);      // ← 整值反序列化
editor.SetEntry(obj, value);            // ← 整值写回
```

| 编辑器 | `SetEntry` |
|---|---|
| `DictionaryKeyValueEditor` | `Data[key] = value` |
| `ListKeyValueEditor` | 命中 `Data[index] = value`／未命中 `Data.Add(value)` |

**两条路都是「整值」**：补丁没提的字段不会保留、也不会被合并 —— 它们按类型的默认值出现。

**逐字段合并只出现在 `Fields`**，不在 `Entries`：

```csharp
// EditDataPatch.ApplyFields（条目是对象时）
JsonReader reader = patchObject.CreateReader();
Serializer.Value.Populate(reader, editor.GetEntry(obj));   // ← Populate：灌进**已有**条目
```

## 2. 怎么查到的

机器上装了 `ilspycmd`，而 CP 的 `ContentPatcher.dll` 就在 Mods 目录里：

```bash
ilspycmd ContentPatcher.dll > all.cs          # 23648 行
# 关键类型：ContentPatcher.Framework.Patches.EditData.{Dictionary,List,Model}KeyValueEditor
```

另外 `grep '"Id"' all.cs` 全程序集只有**两处**，都在 `ListKeyValueEditor.GetAssetKeyFunc` 里
**读**键 —— **CP 从不把键写进 `Id`**。

## 3. 这次订正了什么

| 位置 | 原来 | 改成 |
|---|---|---|
| 数组命中已有元素 | **逐字段合并**（对象） | **整格替换** |
| 数组追加元素 | 补 `Id = 键` | **不补**（值里写什么就是什么） |
| 字典命中已有键 | 整条替换 | 不变（本来就对） |

两处「原来」都是**没有依据**写的 —— 前者的注释还标着「照 `ListKeyValueEditor`」，
而它并不合并；后者是凭空多出一个字段。

实测代价（Cornucopia 给 `Data/Machines` 的触发器列表加条目）：

```json
// 兄弟条目（原版）
{"RequiredItemId": "(O)613"}   {"RequiredItemId": "(O)398"}
// 我们写出来的
{"RequiredItemId": "(O)Cornucopia_Sugarcane", "Id": "(O)Cornucopia_Sugarcane"}
//                                          ^^^^ 兄弟一个都没有，CP 也不会加
```

## 4. 受控对比（同机、同包、改动前后各一遍）

产出树的差异**只落在 `Id` 这一个字段上**：聚宝盆作物 14 处、制作 33 处、
SVE 季节性服装 **0** 处（它的 `Appearance` 追加在值里自带 `Id`，本来就没走补键那条路）。
三个包定点回包结果**完全不变**（91/0、103/0、289/0）。

## 5. 顺带确认的

- `ListKeyValueEditor` 的 `#<整数>` 是**下标**（`ParseKey` 只认 `#` + 纯空白前缀）；
- 非下标键按 `GetKey(value) == text` 比 —— **大小写敏感**（我们一致）；
- 取键：元素是 `string` 就用它本身，否则找 `Id` / `ID` 属性或字段；
- `CanAddEntries` 默认 `true`，只有 `ModelKeyValueEditor` 置 `false`
  （所以往「模型」上写新字段会被 CP 拒绝并警告）。

## 6. 仍未确证

`Data/Locations.Forage` 这类「原版条目都带 `Id`」的列表，CP 追加的元素 `Id` 会是 `null`
（原版有、新加的没有）。**游戏侧认不认 `null`** 没实地验过 ——
但这是 **CP 的行为**，而 mod 是照着 CP 写的、也照着 CP 在用，所以照做即是「复刻 CP 的效果」。
