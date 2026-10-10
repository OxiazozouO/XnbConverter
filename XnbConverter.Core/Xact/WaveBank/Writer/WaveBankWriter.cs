using System.Buffers.Binary;
using Newtonsoft.Json;
using XnbConverter.Audio;
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
/// 默认策略是「同尺寸原地替换」：
/// 波形数据区之前的所有字节（清单里的 Prefix）原样回写，各条目的 wav 载荷按记录的
/// offset/length 依次拼接。只要替换进去的载荷等长，重建结果就与原文件逐字节相同。
///
/// 清单里的条目**多于**头部记录的条目数时，改为「追加」：
/// 新条目在清单里的 <see cref="WaveBankManifest.Entry.Offset"/> 写 <see cref="AppendToEnd"/>，
/// 此时会重建头部（BankData 区里的 EntryCount、5 段表）并把新波形按 Alignment
/// 追加到数据区末尾；已存在的条目只是整体平移，相对数据区的偏移不变。
/// 条目**少于**头部记录数（删条目）不支持，会直接报错。
/// </summary>
public static class WaveBankWriter
{
	/// <summary>清单里新条目的 Offset 哨兵：追加到数据区末尾，由这里算出实际偏移。</summary>
	public const int AppendToEnd = -1;

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

		// 指向 .ogg 的条目先解成 PCM 并回填 Length/Format。
		// 必须在 GrowHeader 之前 —— 它要靠这两个字段写条目元数据。
		Dictionary<string, byte[]> oggPayloads = DecodeOggEntries(waveBankManifest, baseDir);

		// 清单条目多于头部记录的条目数 ⇒ 重建头部（追加波形）
		int headerCount = ReadHeaderEntryCount(waveBankManifest);
		if (waveBankManifest.Entries.Count < headerCount)
		{
			throw new XnbError(
				$"清单条目数 {waveBankManifest.Entries.Count} 少于头部记录的 {headerCount}，不支持删条目：{manifestPath}");
		}

		if (waveBankManifest.Entries.Count > headerCount)
		{
			GrowHeader(waveBankManifest, headerCount);
		}

		List<byte[]> list = new List<byte[]>(waveBankManifest.Entries.Count);
		int num = waveBankManifest.DataOffset;
		foreach (WaveBankManifest.Entry entry in waveBankManifest.Entries)
		{
			string path = Path.Combine(baseDir, entry.FileName);
			if (!File.Exists(path))
			{
				throw new XnbError($"清单引用的波形文件不存在：{entry.FileName}");
			}

			WaveForm waveForm = null;
			byte[] array;
			if (oggPayloads.TryGetValue(Path.GetFullPath(path), out byte[] oggPayload))
			{
				// 已经解好的 PCM —— ogg 没法当 wav 解析，这里直接用
				array = oggPayload;
			}
			else
			{
				waveForm = LoadWaveForm(path);
				array = waveForm.dataChunk.Data;
			}

			if (array.Length != entry.Length)
			{
				if (waveForm == null)
				{
					throw new XnbError(
						$"条目 {entry.FileName} 是 ogg，解码后 {array.Length} 字节与清单记录的 {entry.Length} 不符");
				}

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

	/// <summary>
	/// 清单里指向 .ogg 的条目：用移植进来的 NVorbis 解成 PCM，回填 Length 与 Format
	/// （清单里这两项写 0 就行）。
	///
	/// 波形库本身只认 PCM / ADPCM / XMA / WMA，模组给的却是 ogg，所以在**打包这一层**解开：
	/// 生产侧（SdvCpTool）只要把 ogg 放进解包目录、往清单里加一条 Offset = AppendToEnd 的条目，
	/// 不必自己引解码器。
	///
	/// 返回「全路径 → PCM」，后面的拼接直接取用，跳过 wav 解析（ogg 也当不了 wav 解析）。
	/// </summary>
	private static Dictionary<string, byte[]> DecodeOggEntries(WaveBankManifest manifest, string baseDir)
	{
		Dictionary<string, byte[]> decoded =
			new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		foreach (WaveBankManifest.Entry entry in manifest.Entries)
		{
			if (entry.FileName == null ||
				!entry.FileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string path = Path.Combine(baseDir, entry.FileName);
			if (!File.Exists(path))
			{
				throw new XnbError($"清单引用的波形文件不存在：{entry.FileName}");
			}

			(int channels, int rate, byte[] pcm) = OggVorbis.ToPcm16(path);
			int blockAlign = channels * 2;
			entry.Length = pcm.Length;
			entry.Format = (uint)(0                   // PCM
								  | (channels << 2)
								  | (rate << 5)
								  | (blockAlign << 23)
								  | (1 << 31));       // 16bit
			if (entry.LoopLength == 0)
			{
				entry.LoopLength = (uint)pcm.Length;  // 音乐默认整段循环
			}

			decoded[Path.GetFullPath(path)] = pcm;
		}

		return decoded;
	}

	/// <summary>从 Prefix 的 BankData 区读出头部记录的条目数。</summary>
	private static int ReadHeaderEntryCount(WaveBankManifest manifest)
	{
		byte[] prefix = manifest.PrefixBytes();
		if (prefix.Length < 16)
		{
			throw new XnbError("波形库清单的 Prefix 太短，读不出段表。");
		}

		int bankOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(12));
		if (bankOffset < 0 || bankOffset + 8 > prefix.Length)
		{
			throw new XnbError($"波形库清单的 BankData 区偏移 {bankOffset} 越界。");
		}

		return (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(bankOffset + 4));
	}

	/// <summary>向上取整到 alignment；alignment 非正时按 4。</summary>
	private static int AlignUp(int value, int alignment)
	{
		if (alignment <= 0)
		{
			alignment = 4;
		}

		int remainder = value % alignment;
		return remainder == 0 ? value : value + (alignment - remainder);
	}

	/// <summary>
	/// 清单里比头部多出条目时重建头部：改写 BankData 区的 EntryCount 与 5 段表，
	/// 在原条目元数据表后面补上新条目的记录，新波形按 Alignment 追加到数据区末尾。
	/// 已存在的条目只整体平移，它们在数据区内的相对偏移不变。
	///
	/// 只支持本项目实际会遇到的那种布局（非 compact、无条目名表、无 seek tables、
	/// 元数据表紧贴数据区）；其余布局明确报错，不做猜测性的重建。
	/// </summary>
	private static void GrowHeader(WaveBankManifest manifest, int oldCount)
	{
		byte[] prefix = manifest.PrefixBytes();
		if (prefix.Length < 52)
		{
			throw new XnbError("波形库清单的 Prefix 太短，读不出头部。");
		}

		uint version = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(4));
		if (version < 4)
		{
			throw new XnbError($"波形库版本 {version} 的头部布局不支持追加条目（只支持 >= 4）。");
		}

		// 段表：Seg0 BankData / Seg1 EntryMetaData / Seg2 SeekTables / Seg3 EntryNames / Seg4 EntryWaveData
		int bankOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(12));
		int metaOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(20));
		int seekLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(32));
		int nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(40));
		int dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(44));
		int dataLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(48));

		if (bankOffset < 0 || bankOffset + 84 > prefix.Length)
		{
			throw new XnbError($"波形库清单的 BankData 区偏移 {bankOffset} 越界。");
		}

		uint flags = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(bankOffset));
		int metaElemSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(bankOffset + 72));
		int alignment = (int)BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(bankOffset + 80));

		if ((flags & (uint)Flags.Compact) != 0)
		{
			throw new XnbError("紧凑格式（compact_format）的波形库不支持追加条目。");
		}

		if (nameLength != 0)
		{
			throw new XnbError("带条目名表（entry_names）的波形库不支持追加条目。");
		}

		if (seekLength != 0)
		{
			throw new XnbError("带 seek tables 的波形库不支持追加条目。");
		}

		if (metaElemSize < 24)
		{
			throw new XnbError($"条目元数据元素只有 {metaElemSize} 字节，读不出播放区，不支持追加。");
		}

		if (metaOffset + (long)oldCount * metaElemSize != dataOffset)
		{
			throw new XnbError(
				$"条目元数据表（{metaOffset} 起 {oldCount}×{metaElemSize}）没有紧贴数据区起点 {dataOffset}，不支持追加。");
		}

		int newDataOffset = metaOffset + manifest.Entries.Count * metaElemSize;
		int shift = newDataOffset - dataOffset;

		// 已存在的条目整体平移；它们在数据区内的相对偏移不变
		for (int i = 0; i < oldCount; i++)
		{
			manifest.Entries[i].Offset += shift;
		}

		int rel = dataLength;
		for (int i = oldCount; i < manifest.Entries.Count; i++)
		{
			rel = AlignUp(rel, alignment);
			manifest.Entries[i].Offset = newDataOffset + rel;
			rel += manifest.Entries[i].Length;
		}

		int newDataLength = rel;

		// 新前缀 = 旧前缀（改完段表与 EntryCount）+ 新条目的元数据记录
		byte[] newPrefix = new byte[newDataOffset];
		Array.Copy(prefix, newPrefix, dataOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(bankOffset + 4), (uint)manifest.Entries.Count);
		BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(24), (uint)(manifest.Entries.Count * metaElemSize));
		BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(28), (uint)newDataOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(44), (uint)newDataOffset);
		BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(48), (uint)newDataLength);

		for (int i = oldCount; i < manifest.Entries.Count; i++)
		{
			WaveBankManifest.Entry entry = manifest.Entries[i];
			int at = dataOffset + (i - oldCount) * metaElemSize;
			BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(at), entry.FlagsAndDuration);
			BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(at + 4), entry.Format);
			BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(at + 8), (uint)(entry.Offset - newDataOffset));
			BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(at + 12), (uint)entry.Length);
			BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(at + 16), entry.LoopOffset);
			BinaryPrimitives.WriteUInt32LittleEndian(newPrefix.AsSpan(at + 20), entry.LoopLength);
		}

		manifest.Prefix = WaveBankManifest.ToBase64(newPrefix);
		manifest.DataOffset = newDataOffset;
		manifest.OriginalSize = newDataOffset + newDataLength;
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
