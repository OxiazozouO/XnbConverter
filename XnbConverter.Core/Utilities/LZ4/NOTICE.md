# LZ4（vendored）

## 来源

原样取自 MonoGame 的 Content Pipeline：

```
D:\MonoGame-develop\MonoGame.Framework.Content.Pipeline\Utilities\LZ4\
    LZ4Codec.cs                    167 行
    LZ4Codec.Unsafe.cs             610 行
    LZ4Codec.Unsafe32.Dirty.cs     823 行
    LZ4Codec.Unsafe32HC.Dirty.cs   520 行
    LZ4Codec.Unsafe64.Dirty.cs     815 行
    LZ4Codec.Unsafe64HC.Dirty.cs   529 行
                                   ─────────
                                   3464 行
```

代码本身是 **lz4net**（Milosz Krajewski，2013），BSD-3-Clause，许可声明保留在每个文件开头的
`#region license` 里。MonoGame 那份是 `*.Dirty.cs` 命名的快照；本目录一个文件都没裁剪，
**唯一的改动是把 namespace 从 `Microsoft.Xna.Framework.Content.Pipeline.Utilities.LZ4`
改成 `XnbConverter.Utilities.LZ4`**（避开与 `XnbConverter.XnaShim` 那套 `Microsoft.Xna.Framework.*`
的潜在冲突）。

要把本目录与上游逐字节对齐，只需反向改回这个 namespace：

```sh
sed -i 's/^namespace XnbConverter\.Utilities\.LZ4$/namespace Microsoft.Xna.Framework.Content.Pipeline.Utilities.LZ4/' *.cs
```

## 为什么必须用它，而不是换个更现代的 LZ4 库

XNB 里的 LZ4 是用 `LZ4Codec.Encode32HC`（高压缩档）压的，**对称性要求逐字节复现**。
同一份 LZ4 规范的不同实现（K4os 等）压缩流必然不同 —— 换库会立刻破坏往返一致性。
MonoGame 内容管线用的就是这份代码，所以它才是"参考实现本体"。

之前用的是 NuGet 包 `LZ4PCL 1.0.0`（lz4net 的 PCL 构建）。两者在真实语料上行为一致
（3480 个 XNB 逐字节复现），换成 vendored 源码只是为了去掉一个 2015 年、无人维护、
在 net8.0 下靠兼容层加载的包。`Tests/XnbDump` 仍引用 `LZ4PCL`，作为两套实现的交叉校验。

## 用到的是哪几个入口

| 调用 | 位置 |
|---|---|
| `LZ4Codec.MaximumOutputLength(int)` | `LZ4Codec.cs` |
| `LZ4Codec.Decode32(byte[], int, int, byte[], int, int, bool)` | `LZ4Codec.Unsafe.cs` |
| `LZ4Codec.Encode32HC(byte[], int, int, byte[], int, int)` | `LZ4Codec.Unsafe.cs` |

64 位那一半（`LZ4Codec.Unsafe64*.Dirty.cs`）在本项目里没有被调用，保留是为了和上游保持一致、
方便以后 diff。
