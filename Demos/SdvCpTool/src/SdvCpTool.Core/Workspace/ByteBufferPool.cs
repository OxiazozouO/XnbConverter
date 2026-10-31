namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 大像素/字节缓冲的精确长度池。
/// <para>
/// **为什么不用 <c>ArrayPool&lt;byte&gt;.Shared</c>**：它按 2 的幂租借，返回的数组通常比
/// <c>Width×Height×4</c> 长；而图片路径里不少地方直接用 <c>pixels.Length</c> 拷贝/定位，
/// 多出来的尾部会被当成像素。这里按**精确长度**分桶，池里的数组长度就是调用方要的长度，
/// 所有既有 <c>.Length</c> 语义保持不变。
/// </para>
/// <para>
/// 图片合成/地图扩展这类缓冲每包都会申请同样的尺寸；跨包复用可以避开 LOH 反复
/// 分配/释放（实测 16 包样本 GC 峰值暂停 0.5–0.7 s/s）。池有总字节上限，超过就丢弃，
/// 不会为了复用无限占内存。
/// </para>
/// </summary>
public static class ByteBufferPool
{
    private const long MaxRetainedBytes = 128L * 1024 * 1024;
    private static readonly Dictionary<int, Stack<byte[]>> Free = new();
    private static readonly HashSet<byte[]> Rented = new(ReferenceEqualityComparer.Instance);
    private static long _retained;
    private static readonly object Gate = new();

    public static byte[] Rent(int length) => RentCore(length, clear: true);

    /// <summary>
    /// 租一块调用方会**完整覆盖**的缓冲（地图 `.tbin` 的 `ReadExactly`）。命中池时不 memset。
    /// </summary>
    public static byte[] RentUninitialized(int length) => RentCore(length, clear: false);

    private static byte[] RentCore(int length, bool clear)
    {
        if (length <= 0) return Array.Empty<byte>();
        lock (Gate)
        {
            if (Free.TryGetValue(length, out Stack<byte[]>? stack) && stack.Count > 0)
            {
                byte[] hit = stack.Pop();
                _retained -= hit.Length;
                Rented.Add(hit);
                // 与 `new byte[]` 的零初始化语义保持一致：池里的旧像素/旧地图不能漏进新资产。
                if (clear) Array.Clear(hit);
                return hit;
            }
        }

        var array = new byte[length];
        lock (Gate) Rented.Add(array);
        return array;
    }

    /// <summary>归还一个由 <see cref="Rent"/> 得到的数组；别的数组不会被收下。</summary>
    public static void Return(byte[]? array)
    {
        if (array == null || array.Length == 0) return;
        lock (Gate)
        {
            if (!Rented.Remove(array)) return;   // 不是池里租出去的，别收
            if (_retained + array.Length > MaxRetainedBytes) return;
            if (!Free.TryGetValue(array.Length, out Stack<byte[]>? stack))
                Free[array.Length] = stack = new Stack<byte[]>();
            stack.Push(array);
            _retained += array.Length;
        }
    }

    /// <summary>这个数组是不是池租出去的（用于缓存层决定归还责任）。</summary>
    public static bool IsRented(byte[] array)
    {
        lock (Gate) return Rented.Contains(array);
    }
}
