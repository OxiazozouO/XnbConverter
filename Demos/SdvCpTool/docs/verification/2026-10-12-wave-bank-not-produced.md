# ⚠ 发布阻断：装机里产不出主波形库 `Wave Bank.xwb`（注入的音乐全哑）

日期：2026-10-12（2026-10-12 晚**重写**：原先「原样搬运」那条判据是错的，见下）
对象：**打包器** `XnbConverter.Cli`（`D:\tmp\packer`，重建版）＋我们的 `Wave Bank.xwb.config`
状态：**根因已确证（三处，都是 32 位）**；修法在**我们**这边（注入音乐改用 MS-ADPCM），
**已实现并验证**，见 [`2026-10-12-injected-music-as-adpcm.md`](2026-10-12-injected-music-as-adpcm.md)
（⚠ 还剩一条**只能由人做**的验收：进游戏听）

## 症状（发布闸门抓到的）

对 `prof74`（新语料 232 包）整树回包（`tools/pack-tree.py`）：`成功 15374 / 失败 25`，其中

```
[ERROR] 文件名：D:\tmp\prof74\tree  Destination array was not long enough.
        Check the destination index, length, and the array's lower bounds. (Parameter 'destinationArray')
```

产出目录 `XACT/` 里：`FarmerSounds.xgs` ✓、`Sound Bank.xsb`（51881 B，我们注入过的）✓、
`Wave Bank(1.4).xwb`（37258692 B）✓ —— **`Wave Bank.xwb` 根本没有** ✗。

⚠ **严重性**（按安装方式分开；这一条 2026-10-12 修正过一次，别写成"所有音频全没有"）：

- **覆盖式安装**（把产出铺在原版 Content 上）：原版的 `Wave Bank.xwb` 还在 ⇒ 游戏有波形库 ⇒
  **只有我们注入的那 204 条音乐是哑的**（cue 在声库里、波形不在库里）；
- **替换式安装**（只放产出）：没有波形库 ⇒ **整个音频没有**。

## 隔离（两步，都可复现）

| 输入 | 结果 |
|---|---|
| **原版** `D:\sdv_tree\XACT`（437 条、无追加条目） | ✅ `Destination array` 0 次，产出 `Wave Bank.xwb` **460453688 B** |
| 我们产出树的 `XACT`（641 条，其中 **204 条**是 `Offset=-1/Length=0` 的追加条目） | ❌ `Destination array` 1 次，**没有 `Wave Bank.xwb`**；`Sound Bank.xsb` 照常产出 |

⇒ 同一份波形数据、只换清单 ⇒ **是我们的清单触发的**（不是"整树太大"、不是内存压力：
只装 `XACT/` 单独打包同样复现）。

`Offset = -1` 是打包器自己定的追加哨兵（`WaveBankWriter.AppendToEnd = -1`），我们的写法与它的契约一致。

## 二分：阈值是 ~2 GB（实测）

底树用**我们自己的** XACT（那些 ogg 只有我们有），把清单里的**追加条目**截到 K 条，各打一遍：

| K（追加条目） | `Destination array` | 产出 `Wave Bank.xwb` |
|---|---|---|
| 0 | 0 次 | 439.1 MB |
| 10 | 0 次 | 624.8 MB |
| 50 | 0 次 | **1314.0 MB** |
| 100 | **1 次** | **没有** |
| 150 | 1 次 | 没有 |
| 204（我们现在的） | 1 次 | 没有 |

每追加一条约 **+13 MB**（ogg 解成 PCM 进库）⇒ K=100 时本该 ≈2 GB ⇒ **正好卡在 2 GB**。

⚠ 顺带一个**我自己的错误**（记下来免得再犯）：第一版二分把底树取成**原版** XACT，
于是 K≥10 全是「清单引用的波形文件不存在」——**另一种失败**，却长得像同一条。
底树必须与被测清单同源（这次两者都取自我们自己的树）。

## 根因：三处**各自独立**的 32 位上限（都确证了）

原先这里只写了打包器的 `byte[]`。**不够** —— 就算打包器改成流式，这份库也产不出来：

1. **打包器 `Build()` 把整份 `.xwb` 拼成一个 `byte[]`**
   （`XnbConverter.Core/Xact/WaveBank/Writer/WaveBankWriter.cs:341`：
   `new byte[Math.Max((int)OriginalSize, DataOffset)]`，逐条 `payloads[i].CopyTo(result, offset)`）。
   `Array.MaxLength` ≈ 2.1 GB ⇒ 超了必抛，且**一个字节都不产出**。
2. **清单本身存不下 >2 GB 的偏移**：
   `WaveBankManifest.Entry.Offset` 与 `WaveBankManifest.DataOffset` 是 **`int`**
   （`Entity/WaveBankManifest.cs:24/37`），且 `GrowHeader` 里 `entry.Offset += shift` 用的是**绝对偏移**；
   数据区一旦超过 2.1 GB，追加条目的绝对偏移就溢出 ⇒ **发布侧和生产侧都描述不了这份库**。
   （两个字段在 `D:\XnbConverter` 里，用户约束不能改。）
3. **引擎侧同样是 int**（**未确证，但风险方向一致**）：MonoGame 的波形库读取器按 `int` 算偏移，
   >2 GB 的库大概率读不动。⇒ 就算前两条都绕过去，产出的东西也**不能保证能播**。

⇒ **结论：2.6 GB 的波形库在这条链路上根本不可能发布**，不是"换个参数/改成流式"能解决的。
修法只能是**把库做小**。

## 试过并**放弃**的路：我们自己流式组装（附实测，避免以后重走）

思路：我们把整份 `.xwb` 自己流式写进产出树，绕开打包器的 `byte[]`（复用它的
`OggVorbis.ToPcm16` 与 `WaveBankManifest`，不移植第二份解码器）。

实现过一版并做了**等价性验证**（单包 Crystal Island）：

```
我们自己流式组   500115024 B  sha1 f1b749de74837155
打包器组         500115024 B  sha1 f1b749de74837155
逐字节相同： True
```

也就是说**格式理解是对的、代码是对的** —— 但这条路**不通**，两条各自独立的理由：

- **交付不到装机里**：装机是 `tools/pack-tree.py` 跑**打包器 pack** 的产物。而 pack 模式
  **只产出有 `.config` 的东西**（`Demos/XnbConverter.Cli/Process.cs:250-305`：只处理 `.config` 桶 —
  XNB 走 `Pack`、`*.xwb.config` 走 `XACT.Pack` — 外加 `*.xsb.json`/`*.xgs.json` 两种视图），
  **没有任何"其余文件照搬"**。输入树里一份裸 `.xwb` 在 pack 时被**静默忽略**
  （`.xwb` 桶只在 **UnPack** 分支里用，`Process.cs:322`）。
  ⇒ 我们写进未打包树的 `XACT/Wave Bank.xwb` **进不了安装**。
  ⚠ 所以下面这段**别信**（本文件早先版本写的，是错的，也没验过）：
  >> ~"打包器对『没有 `.config` 的 `.xwb`』是原样搬运的"~
  观察到的 `Wave Bank(1.4).xwb` 落地其实是**从 `Wave Bank(1.4).xwb.config` 组装**出来的 ——
  原版树里那份 `.config` 是有的（`ls D:\sdv_tree\XACT`）。
- **就算交付了也描述不了**：见上面根因第 2 条（清单 `int` 偏移）。

## 真正的修法：注入的音乐改用 **MS-ADPCM**（4:1，把库拉回 ~1.1 GB）

据 [`../research/2026-10-11-injected-music-is-pcm-vanilla-is-adpcm.md`](../research/2026-10-11-injected-music-is-pcm-vanilla-is-adpcm.md)：

- **原版波形库本来就是 ADPCM**：437 条里 **431 条是 MS-ADPCM**（2ch / 44.1k / blockAlign 140 / 4bit），
  `Wave Bank(1.4)` 26/26 全 ADPCM ⇒ "引擎能不能播 ADPCM"不是假设，它就是原版内容的主体；
- 我们注入的 ogg 被**解成 16 位 PCM**（`WaveBankWriter.DecodeOggEntries`，`Format` 写死 PCM 那一档）
  ⇒ 57.9 MB 的 ogg 变 ~635 MB。现在语料长到 **204 条 ⇒ ≈2.6 GB**，于是**必然踩线**。
- 按 ADPCM 存：204 条约 **650 MB**，加上原版 460 MB ⇒ **≈1.1 GB**
  —— 正是 2026-10-11 那次**由打包器成功产出过**的尺寸（1 095 519 576 B）⇒ **链路本身是通的**，
  只要把注入的那部分换成 ADPCM。

**在哪做**：在**我们这边**（转换期），不碰 `D:\XnbConverter` ——
追加条目不再指向 `.ogg`，而是我们自己写出**MS-ADPCM 的 `.wav`**（RIFF，`data` 块里就是进库的那串字节），
并把条目的 `Length` / `Format`（ADPCM 那一档，含 mini 格式的 blockAlign 换算）写好。
打包器那条 `Payload` 路会 `LoadWaveForm(path).dataChunk.Data` 且**长度相等 ⇒ 不触发重编码**，
直接把这段字节搬进库 ⇒ 与它搬原版那些 ADPCM 条目**走的是同一条路**。

**验收标准**（先立好，别事后找补）：

1. **`Format` 逐位核对**（离线可判、必须过硬）：把原版 431 条 ADPCM 的 `Format` 字
   用**我们写的那套编码**从 (codec=ADPCM, ch=2, rate=44100, align=140, bits=4) 反推，
   要求**与原版字节完全相同**；再与 `WaveBank.DecodeFormat` 往返一致。
2. **往返 SNR**：`MsAdpcm.Encode` → `MsAdpcm.Decode` 与原 PCM 比误差
   （`Tests/XactRoundTrip/CodecCheck.cs` 已有这个套路），逐条过门槛。
3. **整库回解**：对产出树跑一次 pack，把产出的 `Wave Bank.xwb` 用打包器的 **unpack** 解回来，
   逐条 `MsAdpcm.Decode` 与我们的 PCM 比 SNR ⇒ 证明**经过打包器那条路之后**仍然对。
4. **进游戏听**（没法替代，**得由人来**）：挑几条注入的音乐确认**能播、不是噪音**。
   ⚠ 这一条没做之前，第 1–3 条只能说明"结构对"，说明不了"好听"。

**风险**：ADPCM 条目写错的后果是**音乐变噪音或者干脆不响** —— 比"体积大"糟得多。
所以 1–3 必须全绿才动产出；4 交给用户。

## 复现配方（约 2 分钟）

把 `prof74/tree/XACT` 拷成一个迷你树 → `D:\tmp\packer` 打一遍 → 看
`[ERROR] … Destination array …` 与产出里有没有 `Wave Bank.xwb`；把清单换成
`D:\sdv_tree\XACT\Wave Bank.xwb.config` 再打一遍就正常 ⇒ 差异只在清单。
（二分脚本：`D:\tmp\bisect-wavebank.py`，⚠ 底树要取**我们自己的** XACT。）
