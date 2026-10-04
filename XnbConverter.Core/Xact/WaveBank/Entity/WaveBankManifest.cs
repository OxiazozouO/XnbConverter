using System.Text;

namespace XnbConverter.Xact.WaveBank.Entity;

/// <summary>
/// 解包时写出的波形库清单，和各个 .wav 一起放在解包目录下，名为 "&lt;库名&gt;.xwb.config"。
///
/// 打包策略是「同尺寸原地替换」：波形数据区按各条目的 offset/length 依次拼接，
/// 只要替换进去的 wav 载荷长度和记录一致，重建结果就与原文件逐字节相同。
/// 因此清单里保存了波形数据区之前的所有字节（<see cref="Prefix"/>），
/// 它包含 header、BankData、EntryMetaData 等，原样回写即可。
/// </summary>
public class WaveBankManifest
{
	public class Entry
	{
		/// <summary>相对解包根目录的 wav 路径。</summary>
		public string FileName;

		/// <summary>在原 .xwb 中的绝对偏移，用于校验。</summary>
		public int Offset;

		/// <summary>波形数据字节数；替换的 wav 载荷必须等长。</summary>
		public int Length;

		public uint FlagsAndDuration;

		public uint Format;
	}

	/// <summary>原文件头部签名，目前恒为 WBND。</summary>
	public string Signature;

	public uint Version;

	public uint HeaderVersion;

	/// <summary>波形数据区起点，等于 Seg4.Offset。</summary>
	public int DataOffset;

	/// <summary>原文件总长度，用于重建后校验。</summary>
	public long OriginalSize;

	/// <summary>[0, DataOffset) 的原始字节，base64。</summary>
	public string Prefix;

	public List<Entry> Entries = new List<Entry>();

	public static string ToBase64(byte[] data)
	{
		return Convert.ToBase64String(data);
	}

	public byte[] PrefixBytes()
	{
		return Convert.FromBase64String(Prefix);
	}

	/// <summary>估算清单文本大小，供 BufferWriter 预分配。</summary>
	public int JsonSize()
	{
		int num = Prefix.Length + 200;
		for (int i = 0; i < Entries.Count; i++)
		{
			num += 1 + (Entries[i].FileName?.Length ?? 0) + 1 + 1;
		}

		return num;
	}
}
