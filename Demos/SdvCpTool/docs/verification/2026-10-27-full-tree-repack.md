# 全资源回包补齐（除 XACT）

日期：2026-10-27

## 1. 范围

对象：`D:\tmp\full-v11\tree` 里可回包的资源：

```
.png / .json / .tbin
排除 XACT/ 与根目录 ContentHashes.json
```

共 **17,835 个**。判据是产出树里的同名资产与 `packed` 里的 `.xnb`：

- 没有 `.xnb` ⇒ 缺；
- `.xnb` 的 mtime 早于资源 ⇒ 过期。

起始状态：**4,603 个过期资源**（Maps 已在另一轮单独补齐）。

## 2. 并行回包与失败修正

先把过期资源拆成 4 个 mini 树并行跑 `XnbConverter.Cli pack -c`。这一轮暴露出两个运行层问题：

- 4 个打包器共用 `D:\tmp\packer` 工作目录，`ConsoleRootPath.Init()` 会写 `pack.bat`，并发时出现文件锁
  `The process cannot access the file 'pack.bat'`；
- 部分进程被外部控制信号终止，退出码为 `0xC000013A`。

因此并行结果只用于生成大部分 XNB，不能当作“全绿”结论。把剩余资源重新串行打包：

```
todo 79
成功 78 / 失败 1
```

唯一失败：`UnlockableBundles/BundleThemes.json` —— 它的 `.config` 是自定义资产类型，打包器按普通 JSON 解析时报
`Unexpected character ... '['mistyspring.GiEXredux.Custom']'`。

## 3. 三个无法回包的文件（如实列出）

| 文件 | 原因 |
|---|---|
| `Mods/jibb.minecarts/config.json` | 模组自己的配置文件；它的 `.config` 声明 `Content.Extension=.png`，不是该 JSON 的类型描述符 |
| `Mods/skellady.SBVCP/SOEmojisData.json` | 同上：`.config` 指向 `.png`，不是 JSON 数据资产 |
| `UnlockableBundles/BundleThemes.json` | 自定义 DLL 资产，没有原版 `Dictionary/Array` 类型可打包 |

前两个不应算游戏资产；第三个保留为“框架专用资产，未回包”。

## 4. 最终覆盖

```
可回包资源总数     17,835
已有当前 XNB       17,832
缺失                  3（上表）
过期                  0
```

Maps 已另测：`packed/Maps` 覆盖 **1,252 / 1,252** 张 `.tbin`，缺失 0、过期 0。

## 5. 边界

- 这里验证的是**每个可回包资源都有当前 XNB**；没有对 17,832 个 XNB 逐个解包做语义对照。
- 没有重打 XACT；XACT 的 Wave Bank / Sound Bank 是独立链路，已在音频验证里单独核对。
- 没有启动游戏，也没有把这些 XNB 安装到游戏目录。
- 并行回包时的工作目录竞争属于打包器工具链问题，不是转换器产物问题；以后并行打包应给每个进程独立的 packer 工作目录。
