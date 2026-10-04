using Newtonsoft.Json;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Codec;
using XnbConverter.Xact.WaveBank.Entity;
using static XnbConverter.Xact.WaveBank.Entity.WaveBank;
using XnbConverter.Xact.WaveBank.Reader;

namespace XnbConverter.Xact.WaveBank.Writer;

/// <summary>
/// 把解包目录重新打包回 .xwb，与 <see cref="WaveBankReader"/> 对称。
///
/// 策略是「同尺寸原地替换」：
/// 波形数据区之前的所有字节（清单里的 Prefix）原样回写，各条目的 wav 载荷按记录的
/// offset/length 依次拼接。只要替换进去的载荷等长，重建结果就与原文件逐字节相同。
/// </summary>
public static class WaveBankWriter
{
	/// <summary>读取清单并校验，返回按序排列的各条目载荷。</summary>
	private static (WaveBankManifest Manifest, List<byte[]> Payloads) Load(string manifestPath)
	{
		if (!File.Exists(manifestPath))
		{
			throw new XnbError($"找不到波形库清单：{Path.GetFullPath(manifestPath)}");
		}

		WaveBankManifest waveBankManifest = JsonConvert.DeserializeObject<WaveBankManifest>(
			File.ReadAllText(manifestPath));
		if (waveBankManifest?.Prefix == null)
		{
			throw new XnbError($"波形库清单缺少 Prefix 字段：{manifestPath}");
		}

		string baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? ".";
		List<byte[]> list = new List<byte[]>(waveBankManifest.Entries.Count);
		int num = waveBankManifest.DataOffset;
		foreach (WaveBankManifest.Entry entry in waveBankManifest.Entries)
		{
			string path = Path.Combine(baseDir, entry.FileName);
			if (!File.Exists(path))
			{
				throw new XnbError($"清单引用的波形文件不存在：{entry.FileName}");
			}

			WaveForm waveForm = LoadWaveForm(path);
			byte[] array = waveForm.dataChunk.Data;
			if (array.Length != entry.Length)
			{
				// 长度不同：按目标条目的编解码与采样率重新编码成等长载荷
				WaveBankEntry waveBankEntry = new WaveBankEntry
				{
					Format = entry.Format
				};
				waveBankEntry.DecodeFormat(waveBankManifest.Version, out WaveBankFormats codec,
					out int channels, out int rate, out int align, out int bits);
				array = WavePayload.Build(waveForm, codec, channels, rate, bits, align, entry.Length);
				if (array.Length != entry.Length)
				{
					throw new XnbError($"条目 {entry.FileName} 重编码后 {array.Length} 字节，应为 {entry.Length} 字节");
				}
			}

			// 条目之间允许有对齐填充的间隙（Terraria 的 Wave Bank 就是），但不允许重叠
			if (entry.Offset < num)
			{
				throw new XnbError(
					$"条目 {entry.FileName} 偏移 {entry.Offset}，与前一条的结尾 {num} 重叠。");
			}

			list.Add(array);
			num = entry.Offset + array.Length;
		}

		if (waveBankManifest.OriginalSize > 0 && num > waveBankManifest.OriginalSize)
		{
			throw new XnbError(
				$"重建后大小 {num} 超过原文件 {waveBankManifest.OriginalSize}，清单可能已损坏。");
		}

		return (waveBankManifest, list);
	}

	/// <summary>解析 .wav，拿到 fmt 与 data 块。</summary>
	private static WaveForm LoadWaveForm(string path)
	{
		WaveFormReader waveFormReader = new WaveFormReader();
		waveFormReader.Init(new ReaderResolver
		{
			bufferReader = BufferReader.FormFile(path)
		});
		try
		{
			return waveFormReader.Load();
		}
		finally
		{
			waveFormReader.Dispose();
		}
	}

	/// <summary>按清单重建整个 .xwb 的字节。</summary>
	public static byte[] Build(string manifestPath)
	{
		(WaveBankManifest waveBankManifest, List<byte[]> payloads) = Load(manifestPath);
		byte[] prefix = waveBankManifest.PrefixBytes();
		// 缓冲按原始文件总长分配：条目之间/末尾的对齐填充保持为 0，与原文件的写法一致
		byte[] result = new byte[Math.Max((int)waveBankManifest.OriginalSize, waveBankManifest.DataOffset)];
		prefix.CopyTo(result, 0);
		for (int i = 0; i < payloads.Count; i++)
		{
			// 按各条目自己的偏移落位，中间的间隙自然留空
			payloads[i].CopyTo(result, waveBankManifest.Entries[i].Offset);
		}

		return result;
	}

	/// <summary>按清单写出 .xwb 文件。</summary>
	public static void Write(string manifestPath, string outputPath)
	{
		byte[] array = Build(manifestPath);
		string directoryName = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}

		File.WriteAllBytes(outputPath, array);
	}
}
