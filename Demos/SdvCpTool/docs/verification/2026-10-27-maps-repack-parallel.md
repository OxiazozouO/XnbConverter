# Maps 回包：按过期地图并行

日期：2026-10-27

## 1. 背景

之前的全量整树回包在跑到一半时被运行环境按内存上限停止，没有拿到最终 Maps 结论。
这次不改转换逻辑，先把**产出树里过期、尚未回包的 Maps** 找出来，再并行回包。

## 2. 选择范围

对比：

```
tree/Maps/**/*.tbin
packed/Maps/**/*.xnb
```

按同名 `.xnb` 的 mtime 早于 `.tbin` 判定过期。结果：**1,020 张**过期地图（当前树共 1,252 张）。
没有用内容哈希逐张解包比对；mtime 只用来选候选，最终判据仍是打包器对每张图的成功/失败。

## 3. 回包方式

1. 把过期的 `.tbin` 和同名 `.config` 复制到 4 个 mini 树的 `Maps/`；
2. 每个 mini 树独立跑：

```
D:\tmp\packer\XnbConverter.Cli.exe pack -c -i <mini> -o <out>
```

3. 4 个进程并行，各 255 张；输出目录分别为 `out0..out3`；
4. 全部结束后按路径合并回 `D:\tmp\full-v11\packed\Maps`。

## 4. 结果

```
stale maps: 1020
batch 0 exit=0  成功 255 / 失败 0
batch 1 exit=0  成功 255 / 失败 0
batch 2 exit=0  成功 255 / 失败 0
batch 3 exit=0  成功 255 / 失败 0
TOTAL xnb=1020
每批耗时约 76 s
```

合并后：

- `packed/Maps` 覆盖 `1,252 / 1,252` 张 `.tbin`，缺失 **0**；
- 按 mtime 再比：过期 **0**；
- 4 个打包器进程退出码全为 **0**，无失败。

## 5. 边界

- 这里只验证 **Maps** 的回包；没有重打整棵 Data/Characters/音频树，也没有启动游戏。
- mini 树的 `.config` 随 `.tbin` 一起带上了；缺 `.config` 的资产不会被静默跳过。
- 并行只用于回包验证，不改变转换器的执行路径。
- 后续已把同一套思路扩到全部可回包资源（含 Data/Characters/TileSheets），见
  [`2026-10-27-full-tree-repack.md`](2026-10-27-full-tree-repack.md)。
