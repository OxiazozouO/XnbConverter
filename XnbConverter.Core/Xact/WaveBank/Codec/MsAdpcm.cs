using XnbConverter.Exceptions;

namespace XnbConverter.Xact.WaveBank.Codec;

/// <summary>
/// 微软 ADPCM（MS-ADPCM）编解码。星露谷波形库里用的就是它。
///
/// 数据按固定大小的块组织，每块 <c>blockAlign</c> 字节：
/// <code>
///   每声道 7 字节块头：预测器序号(1) + 差分(2) + 采样1(2) + 采样2(2)
///   其后是 4 位格，每格一个残差采样，低半字节在前
/// </code>
/// 每块含 <see cref="SamplesPerBlock"/> 个采样，其中前两个直接存在块头（无损）。
/// 块头里「采样1」是较新的那个（第二个），「采样2」是较旧的（第一个），
/// 因为预测公式 <c>(采样1*c1 + 采样2*c2)/256</c> 以采样1 为 t-1。
/// </summary>
public static class MsAdpcm
{
	/// <summary>差分自适应表，按 4 位残差索引。</summary>
	private static readonly int[] AdaptationTable =
	{
		230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230
	};

	private static readonly int[] AdaptCoeff1 = { 256, 512, 0, 192, 240, 460, 392 };

	private static readonly int[] AdaptCoeff2 = { 0, -256, 0, 64, 0, -208, -232 };

	/// <summary>每块包含的采样数（含块头里那两个）。</summary>
	public static int SamplesPerBlock(int blockAlign, int channels)
	{
		return (blockAlign - 7 * channels) * 2 / channels + 2;
	}

	/// <summary>把整段 ADPCM 数据解码成 16 位交错 PCM。</summary>
	public static short[] Decode(byte[] data, int offset, int length, int channels, int blockAlign)
	{
		int num = SamplesPerBlock(blockAlign, channels);
		int num2 = length / blockAlign;
		short[] array = new short[num2 * num * channels];
		int[] array2 = new int[channels];
		int[] array3 = new int[channels];
		int[] array4 = new int[channels];
		int[] array5 = new int[channels];
		int num3 = 0;
		for (int i = 0; i < num2; i++)
		{
			int num4 = offset + i * blockAlign;
			for (int j = 0; j < channels; j++)
			{
				int num5 = num4 + 7 * j;
				array2[j] = data[num5];
				if (array2[j] > 6)
				{
					throw new XnbError($"ADPCM 预测器序号越界：{array2[j]}");
				}

				array3[j] = ReadInt16(data, num5 + 1);
				if (array3[j] < 16)
				{
					array3[j] = 16;
				}

				array4[j] = ReadInt16(data, num5 + 3);
				array5[j] = ReadInt16(data, num5 + 5);
			}

			// 先输出各声道的第一个采样，再输出第二个，保证交错顺序正确
			for (int k = 0; k < channels; k++)
			{
				array[num3++] = (short)array5[k];
			}

			for (int l = 0; l < channels; l++)
			{
				array[num3++] = (short)array4[l];
			}

			int num6 = num4 + 7 * channels;
			int num7 = num4 + blockAlign;
			int num8 = 0;
			int num9 = 2;
			while (num6 < num7 && num9 < num)
			{
				byte b = data[num6];
				num6++;
				for (int m = 0; m < 2; m++)
				{
					int nibble = ((m == 0) ? (b & 0xF) : (b >> 4));
					int num10 = (AdaptCoeff1[array2[num8]] * array4[num8] +
					             AdaptCoeff2[array2[num8]] * array5[num8]) >> 8;
					num10 += ((nibble & 8) != 0) ? (nibble - 16) * array3[num8] : nibble * array3[num8];
					num10 = Math.Clamp(num10, -32768, 32767);
					array[num3++] = (short)num10;
					array5[num8] = array4[num8];
					array4[num8] = num10;
					array3[num8] = (AdaptationTable[nibble] * array3[num8]) >> 8;
					if (array3[num8] < 16)
					{
						array3[num8] = 16;
					}

					num8++;
					if (num8 >= channels)
					{
						num8 = 0;
						num9++;
						if (num9 >= num)
						{
							break;
						}
					}
				}
			}

			int num11 = (i + 1) * num * channels;
			while (num3 < num11)
			{
				array[num3++] = 0;
			}
		}

		return array;
	}

	/// <summary>
	/// 把交错 16 位 PCM 编码成恰好 blockCount 个块，长度 blockCount * blockAlign。
	/// 源采样不足补零、多余截断；每块单独挑选误差最小的预测器。
	/// </summary>
	public static byte[] Encode(short[] pcm, int channels, int blockAlign, int blockCount, out double error)
	{
		int num = SamplesPerBlock(blockAlign, channels);
		byte[] array = new byte[blockCount * blockAlign];
		long num2 = 0L;
		for (int i = 0; i < blockCount; i++)
		{
			int pcmOffset = i * num * channels;
			int destinationOffset = i * blockAlign;
			int num3 = 0;
			long num4 = long.MaxValue;
			for (int j = 0; j < 7; j++)
			{
				long num5 = EncodeBlock(pcm, pcmOffset, num, channels, j, array, destinationOffset, measure: true);
				if (num5 < num4)
				{
					num4 = num5;
					num3 = j;
				}
			}

			num2 += EncodeBlock(pcm, pcmOffset, num, channels, num3, array, destinationOffset, measure: false);
		}

		error = (double)num2;
		return array;
	}

	/// <summary>编码一个块；measure 为真时只统计误差，不落盘。</summary>
	private static long EncodeBlock(short[] pcm, int pcmOffset, int samplesPerBlock, int channels, int predictor,
		byte[] destination, int destinationOffset, bool measure)
	{
		int[] array = new int[channels];
		int[] array2 = new int[channels];
		int[] array3 = new int[channels];
		for (int i = 0; i < channels; i++)
		{
			array[i] = GetSample(pcm, pcmOffset + i);
			array2[i] = GetSample(pcm, pcmOffset + channels + i);
			array3[i] = Math.Max(16, Math.Abs(array2[i] - array[i]));
		}

		if (!measure)
		{
			for (int j = 0; j < channels; j++)
			{
				int num = destinationOffset + 7 * j;
				destination[num] = (byte)predictor;
				WriteInt16(destination, num + 1, array3[j]);
				WriteInt16(destination, num + 3, array2[j]);
				WriteInt16(destination, num + 5, array[j]);
			}
		}

		int[] array4 = new int[channels];
		for (int k = 0; k < channels; k++)
		{
			array4[k] = array3[k];
		}

		long num2 = 0L;
		int num3 = destinationOffset + 7 * channels;
		int num4 = 0;
		for (int l = 2; l < samplesPerBlock; l++)
		{
			for (int m = 0; m < channels; m++)
			{
				int sample = GetSample(pcm, pcmOffset + l * channels + m);
				// array2 是较新的采样(t-1)，配 coeff1；array 是较旧的(t-2)，配 coeff2
				int num5 = (AdaptCoeff1[predictor] * array2[m] + AdaptCoeff2[predictor] * array[m]) >> 8;
				int num6 = sample - num5;
				int num7 = (num6 >= 0)
					? ((num6 + array4[m] / 2) / array4[m])
					: (-((array4[m] / 2 - num6) / array4[m]));
				num7 = Math.Clamp(num7, -8, 7);
				num2 += (long)num6 * num6;
				array[m] = array2[m];
				array2[m] = Math.Clamp(num5 + num7 * array4[m], -32768, 32767);
				array4[m] = (AdaptationTable[num7 & 0xF] * array4[m]) >> 8;
				if (array4[m] < 16)
				{
					array4[m] = 16;
				}

				if (!measure)
				{
					if (num4 == 0)
					{
						destination[num3] = (byte)(num7 & 0xF);
					}
					else
					{
						destination[num3] |= (byte)((num7 & 0xF) << 4);
						num3++;
					}

					num4 ^= 1;
				}
			}
		}

		return num2;
	}

	private static int GetSample(short[] pcm, int index)
	{
		return (index >= 0 && index < pcm.Length) ? pcm[index] : 0;
	}

	private static int ReadInt16(byte[] buffer, int offset)
	{
		return (short)(buffer[offset] | (buffer[offset + 1] << 8));
	}

	private static void WriteInt16(byte[] buffer, int offset, int value)
	{
		buffer[offset] = (byte)(value & 0xFF);
		buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
	}
}
