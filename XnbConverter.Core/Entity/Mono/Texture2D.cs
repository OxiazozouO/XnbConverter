using System.Collections.Concurrent;
using Newtonsoft.Json;
using XnbConverter.Utilities.Png;

namespace XnbConverter.Entity.Mono;

public class Texture2D
{
	/// <summary>
	/// 在飞的 PNG 编码任务上限。到顶就改成本线程直接写 —— 继续排队并不会更快
	/// （线程池已经满了），只会把还没编码的纹理数据一直钉在内存里。
	/// </summary>
	private static readonly int MaxPendingWrites = Math.Max(8, Environment.ProcessorCount * 2);

	/// <summary>
	/// 延后执行的编码任务。多个工作线程会并发入队（CLI 的 -c 就是多线程解包），
	/// 所以必须是并发安全的容器。
	/// </summary>
	private static readonly ConcurrentQueue<Task> PendingWrites = new ConcurrentQueue<Task>();

	private static int pendingWriteCount;

	[JsonIgnore]
	public byte[] Data;

	public int Format;

	public int Width;

	public int Height;

	/// <summary>
	/// 未补齐的内容尺寸。
	///
	/// 安卓版的 XNB 会把每张贴图补齐到 2 的幂，宽/高在流里仍是 4 字节，
	/// 但拆成两个 uint16：低 16 位是补齐后的 <see cref="Width"/>/<see cref="Height"/>，
	/// 高 16 位是这两个值（无需补齐时为 0）。PC 版高 16 位恒为 0，两者等价。
	/// 回写时原样还原，保证逐字节对称。
	/// </summary>
	public int ContentWidth;

	public int ContentHeight;

	public int MipCount;

	public uint DataSize;

	/// <summary>
	/// 这张贴图是不是字体页。
	///
	/// XNA/MonoGame 对字体页用的是专门写死端点的 DXT3 编码器（见 <see cref="Utilities.FontDxt3"/>），
	/// 和通用 DXT 编码器不逐字节兼容。该值由 SpriteFontReader 在写之前置上，
	/// 只影响编码方式、不属于内容，所以不落进 JSON。
	/// </summary>
	[JsonIgnore]
	public bool IsFontTexture;

	/// <summary>
	/// 字体 DXT3 里"全透明块"的写法：true 表示写全 0，false（默认）表示写常量端点。
	/// 两种写法解码结果都是全透明，但字节不同；不同批次的字体用的不是同一支编码器，
	/// 所以解包时按原文件探测（见 <see cref="Utilities.FontDxt3.DetectZeroTransparent"/>），
	/// 随实体一起进 JSON 以便打包时还原。
	/// </summary>
	public bool FontDxt3ZeroTransparent;

	public void SaveAsPng(string path)
	{
		// Data 本身就是 PNG 需要的 R,G,B,A 逐行排布，直接交给编码器，
		// 不再像以前那样先逐像素灌进一个图像对象、再让它自己保存。
		byte[] data = Data;
		int width = Width;
		int height = Height;

		if (Interlocked.Increment(ref pendingWriteCount) > MaxPendingWrites)
		{
			try
			{
				Png.Write(path, data, width, height);
			}
			finally
			{
				Interlocked.Decrement(ref pendingWriteCount);
			}

			return;
		}

		PendingWrites.Enqueue(Task.Run(() =>
		{
			try
			{
				Png.Write(path, data, width, height);
			}
			finally
			{
				Interlocked.Decrement(ref pendingWriteCount);
			}
		}));
	}

	public static Texture2D FromPng(string path)
	{
		Texture2D texture2D = new Texture2D();
		texture2D.Data = Png.Read(Path.GetFullPath(path), out int width, out int height);
		texture2D.Width = width;
		texture2D.Height = height;
		return texture2D;
	}

	public static void WaitAll()
	{
		List<Task> pending = new List<Task>();
		while (PendingWrites.TryDequeue(out Task? task))
		{
			pending.Add(task);
		}

		if (pending.Count != 0)
		{
			Task.WaitAll(pending.ToArray());
		}
	}
}
