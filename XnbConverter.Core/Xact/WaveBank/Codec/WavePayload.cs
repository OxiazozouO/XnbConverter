using XnbConverter.Exceptions;
using XnbConverter.Xact.WaveBank.Entity;
using static XnbConverter.Xact.WaveBank.Entity.WaveBank;

namespace XnbConverter.Xact.WaveBank.Codec;

/// <summary>
/// 把一段音频（.wav 里的 PCM 或 MS-ADPCM）转换成某个波形库条目所需的裸载荷。
///
/// 波形库是「同尺寸原地替换」，所以输出长度严格等于目标条目的原长度：
/// 采样多了截断、少了补静音。PCM 直接按采样写，MS-ADPCM 则按整块编码。
/// </summary>
public static class WavePayload
{
	/// <summary>解码 wav 的 data 块成 16 位交错 PCM。</summary>
	public static short[] DecodeToPcm(FmtChunk.AudioFormats formatTag, int channels, int bitsPerSample,
		int blockAlign, byte[] data)
	{
		if (formatTag == FmtChunk.AudioFormats.AdpcmMs)
		{
			return MsAdpcm.Decode(data, 0, data.Length, channels, blockAlign);
		}

		if (formatTag != FmtChunk.AudioFormats.Pcm)
		{
			throw new XnbError($"源音频格式 {formatTag} 暂不支持（目前只支持 PCM 和 MS-ADPCM）；" +
			                   "要换成 mp3 请先用工具转成 wav");
		}

		int num = bitsPerSample / 8;
		if (num != 1 && num != 2)
		{
			throw new XnbError($"源 PCM 位深 {bitsPerSample} 暂不支持（只支持 8/16 位）");
		}

		int num2 = data.Length / num;
		short[] array = new short[num2];
		for (int i = 0; i < num2; i++)
		{
			if (num == 1)
			{
				// 8 位 PCM 是无符号的
				array[i] = (short)((data[i] - 128) << 8);
			}
			else
			{
				array[i] = (short)(data[2 * i] | (data[2 * i + 1] << 8));
			}
		}

		return array;
	}

	/// <summary>
	/// 重采样并转换声道数，返回按目标声道交错、最多 maxFrames 帧的 PCM。
	/// 帧数不足的补零，超出的截断。
	/// </summary>
	public static short[] Convert(short[] pcm, int srcChannels, int srcRate, int dstChannels, int dstRate,
		int maxFrames)
	{
		short[] array = new short[maxFrames * dstChannels];
		if (maxFrames <= 0)
		{
			return array;
		}

		int num = pcm.Length / Math.Max(srcChannels, 1);
		if (num == 0)
		{
			return array;
		}

		for (int i = 0; i < maxFrames; i++)
		{
			// 目标第 i 帧对应源的第 i * srcRate / dstRate 帧（线性插值）
			double num2 = (double)i * srcRate / dstRate;
			int num3 = (int)num2;
			double num4 = num2 - num3;
			if (num3 >= num)
			{
				break;
			}

			int num5 = Math.Min(num3 + 1, num - 1);
			for (int j = 0; j < dstChannels; j++)
			{
				short num6;
				if (dstChannels == srcChannels)
				{
					num6 = pcm[num3 * srcChannels + j];
				}
				else if (srcChannels == 1)
				{
					num6 = pcm[num3];
				}
				else if (dstChannels == 1)
				{
					int num7 = 0;
					for (int k = 0; k < srcChannels; k++)
					{
						num7 += pcm[num3 * srcChannels + k];
					}

					num6 = (short)(num7 / srcChannels);
				}
				else
				{
					num6 = pcm[num3 * srcChannels + Math.Min(j, srcChannels - 1)];
				}

				// 同一帧的下一采样用于插值
				int num8;
				if (dstChannels == srcChannels)
				{
					num8 = pcm[num5 * srcChannels + j];
				}
				else if (srcChannels == 1)
				{
					num8 = pcm[num5];
				}
				else
				{
					num8 = num6;
				}

				array[i * dstChannels + j] = (short)Math.Clamp((int)Math.Round(num6 + (num8 - num6) * num4),
					-32768, 32767);
			}
		}

		return array;
	}

	/// <summary>
	/// 按目标条目的编解码/采样率/声道，把源 wav 的数据转成等长载荷。
	/// </summary>
	public static byte[] Build(WaveForm source, WaveBankFormats targetCodec, int targetChannels,
		int targetRate, int targetBits, int targetAlign, int length)
	{
		FmtChunk fmtChunk = source.fmtChunk;
		int channels = fmtChunk.NumChannels;
		int bitsPerSample = fmtChunk.BitsPerSample;
		int blockAlign = fmtChunk.BlockAlign;
		short[] pcm = DecodeToPcm(fmtChunk.FmtTag, channels, bitsPerSample, blockAlign,
			source.dataChunk.Data);

		if (channels <= 0)
		{
			channels = 1;
		}

		if (targetChannels <= 0)
		{
			targetChannels = 1;
		}

		if (targetRate <= 0)
		{
			targetRate = (int)fmtChunk.SampleRate;
		}

		switch (targetCodec)
		{
			case WaveBankFormats.Pcm:
				return BuildPcm(pcm, channels, (int)fmtChunk.SampleRate, targetChannels, targetRate, targetBits,
					length);

			case WaveBankFormats.AdpcmMs:
			{
				int num = (targetAlign + 22) * targetChannels;
				if (num <= 7 * targetChannels || length % num != 0)
				{
					throw new XnbError($"目标 ADPCM 块大小 {num} 与载荷长度 {length} 不匹配，无法等长编码");
				}

				int num2 = MsAdpcm.SamplesPerBlock(num, targetChannels);
				int blockCount = length / num;
				short[] pcm2 = Convert(pcm, channels, (int)fmtChunk.SampleRate, targetChannels, targetRate,
					num2 * blockCount);
				return MsAdpcm.Encode(pcm2, targetChannels, num, blockCount, out double _);
			}

			default:
				throw new XnbError($"目标编解码 {targetCodec} 暂不支持重新编码（只有 PCM / MS-ADPCM 可以）");
		}
	}

	private static byte[] BuildPcm(short[] pcm, int srcChannels, int srcRate, int dstChannels, int dstRate,
		int targetBits, int length)
	{
		int num = (targetBits > 0) ? 16 : 8;
		int num2 = num / 8;
		int num3 = length / (num2 * dstChannels);
		short[] array = Convert(pcm, srcChannels, srcRate, dstChannels, dstRate, num3);
		byte[] array2 = new byte[length];
		int num4 = 0;
		for (int i = 0; i < num3 * dstChannels; i++)
		{
			if (num2 == 1)
			{
				array2[num4++] = (byte)Math.Clamp(array[i] / 256 + 128, 0, 255);
			}
			else
			{
				array2[num4++] = (byte)(array[i] & 0xFF);
				array2[num4++] = (byte)((array[i] >> 8) & 0xFF);
			}
		}

		return array2;
	}
}
