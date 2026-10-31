# 未转换框架内容量写进清单

日期：2026-10-28

## 问题

`_not-converted.txt` 原来只列非 CP/FTM 包的**名字和 ContentPackFor**，
看不出丢了多少内容；而且 AT/SF/CC/CCCB/MFM 实际已经由 `bake-frameworks`
增量烘焙，旧措辞“没转”也不准确。

## 实现

`tools/cppacks.py` 新增 `framework_volume(path, uid)`：

- Alternative Textures：统计 `texture.json` 的 `Variations` + `ManualVariations`
- Custom Companions：统计 `companion.json`
- Solid Foundations：统计 `building.json`
- Mail Framework：统计 `mail.json` 信件
- CCCB：统计 `content.json` 的变体对象

`convert-mods.py` 的 `_not-converted.txt` 和 stdout 都会在包名后显示内容量；
框架区标题改为“AT/SF/CC/CCCB/MFM 由 bake-frameworks 增量烘焙，其余没转”。

## 当前语料

- AT：17 个包，**1439 个纹理变体**
- CC：10 个包，**216 个同伴**
- SF：2 个包，**21 个建筑**
- MFM：2 个包，**3 封信**
- CCCB：2 个包，各 **1 个捆绑包变体**
- MoreGrass：1 个包，**52 张草贴图**（只取每季 3 个变体覆盖原版）
- BushBloom：1 个包，**1 个灌木定义**
- BusLocations：2 个包，各 **1 个站点**
- TrainStation：1 个包，**1 个火车站点**

## 验证

- 用当前 `Mods` 跑 `write_inventory`，每行都带上了可数内容量；
- 未重新跑转换/打包（这是报告工具改动，不改资源）；
- 未启动游戏。
