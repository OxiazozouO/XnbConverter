# 全量资源包重新打包（含天气地图与同伴音效）

日期：2026-10-28

## 1. 本轮增量

在 `D:\tmp\full-v11` 上完成了：

- 13 条天气/时间地图补丁写进 11 张地图；
- 17 个地图/图集 XNB 回包成功；
- `Data/Pets` 写入 144 个同伴 `BarkSound`，10 个 `IdleBehavior: Nothing` 改成静止；
- `Data/Pets.xnb` 回包成功。

## 2. 重新打 zip

用 `7z` 把 `D:\tmp\full-v11\packed` 以 `Content/` 为根打包：

```
Folders: 380
Files:   19368
Size:    1,694,287,120 bytes (1616 MiB)
Zip:     1,312,980,566 bytes (1253 MiB)
```

7z 完整性测试：

```
Everything is Ok
Folders: 380
Files:   19368
```

SHA-256：

```
64E23D4DC8A935A89F96BF49EF1C852F48EF715FC0D11DA716F6A8DC8AF6F81C
```

文件：

- 新主包：`D:\tmp\sdv-share\sdv-content-baked.zip`
- 旧包保留：`D:\tmp\sdv-share\sdv-content-baked-2026-10-27.zip`

## 3. 下载

本机 HTTP 服务仍在 8000 端口：

```
http://10.10.20.21:8000/sdv-content-baked.zip
```

HEAD 已核对：HTTP 200、Content-Length 1,312,980,566。

## 4. XNB 抽查

- 17 个天气/图集 XNB 解包：成功 17 / 失败 0；
  `Forest.xnb` 解包后的 tbin 仍是 16 层、`DayTiles` 与源树逐字符相同。
- `Data/Pets.xnb` 解包：219 条记录、144 条带 `BarkSound`、17 条带
  `Walk.SoundOnStart`、10 条单 `Idle` 行为、8 条带 `IsSideBehavior` 的 `Walk`、
  185 条 `Walk ↔ Idle` 两态行为。

## 5. 全树静态自检与幂等

- `verify-tree.py`：天气地图新增后，`每张地图引用的图集图片`、`图集落点被地图占着`、
  `CreateOnLoad.MapPath`、`季节性图集变体` 均为 **0 新增问题**；
- 其余非零项仍是原有已知问题（语料 tmx 110、地图 Message 23、贴图引用 9 等），
  不是这几张天气图引入的；
- 再跑一次 `bake-frameworks`：**0 新增**（宠物/行为/音效都幂等）。

## 6. 边界

- 未启动游戏；仍只有静态打包、XNB 解包与 zip 完整性验证。
- zip 是当前 `packed` 的快照；后续再改树/框架内容，需要再打一次。
