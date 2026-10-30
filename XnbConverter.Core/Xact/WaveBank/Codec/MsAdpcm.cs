using XnbConverter.Exceptions;

namespace XnbConverter.Xact.WaveBank.Codec;

/// <summary>
/// 微软 ADPCM（MS-ADPCM）编解码。星露谷波形库里用的就是它。
///
/// 数据按固定大小的块组织，每块 <c>blockAlign</c> 字节：
/// <code>
///   块头按**字段**交错，而不是按声道各占 7 字节：
///     预测器序号：每声道 1 字节
///     差分：      每声道 2 字节
///     采样1：     每声道 2 字节（较新的那个，t-1）
///     采样2：     每声道 2 字节（较旧的，t-2）
///   其后是 4 位格，**高半字节在前**；立体声一个字节装同一帧的左右（高 = 声道 0）
/// </code>
/// 每块含 <see cref="SamplesPerBlock"/> 个采样，其中前两个直接存在块头（无损）。
/// 预测公式是 <c>(采样1*c1 + 采样2*c2)/256</c>，以采样1 为 t-1。
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
				array2[j] = data[num4 + j];
				if (array2[j] > 6)
				{
					throw new XnbError($"ADPCM 预测器序号越界：{array2[j]}");
				}

				array3[j] = ReadInt16(data, num4 + channels + 2 * j);
				if (array3[j] < 16)
				{
					array3[j] = 16;
				}

				array4[j] = ReadInt16(data, num4 + 3 * channels + 2 * j);
				array5[j] = ReadInt16(data, num4 + 5 * channels + 2 * j);
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
					int nibble = ((m == 0) ? (b >> 4) : (b & 0xF));
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
		byte[] array = new byte[blockCount * blockAlign];
		Encode(pcm, channels, blockAlign, blockCount, array, out error);
		return array;
	}

	/// <summary>同 <see cref="Encode(short[], int, int, int, out double)"/>，但写进调用方的缓冲区（省一次分配）。</summary>
	public static void Encode(short[] pcm, int channels, int blockAlign, int blockCount,
		byte[] destination, out double error)
	{
		int num = SamplesPerBlock(blockAlign, channels);
		// 试跑时把候选块的字节写进这块 scratch；选中的那份直接拷进产出，
		// 省掉「量误差 7 次 + 再正式编码 1 次」里的最后那次整块编码（约 1/8 的活）。
		byte[] scratch = new byte[blockAlign];
		int[] s0 = new int[channels], s1 = new int[channels];
		int[] s2 = new int[channels], s3 = new int[channels];
		long total = 0L;
		int seed = 0;
		for (int i = 0; i < blockCount; i++)
			total += EncodeOneBlockCore(pcm, i * num * channels, num, channels,
				i * blockAlign, blockAlign, destination, scratch, s0, s1, s2, s3, seed, out seed);

		error = (double)total;
	}

	private static long EncodeOneBlockCore(short[] pcm, int pcmOffset, int samplesPerBlock,
		int channels, int destinationOffset, int blockAlign, byte[] destination, byte[] scratch,
		Span<int> s0, Span<int> s1, Span<int> s2, Span<int> s3,
		int seed, out int best)
	{
		best = -1;
		long bestError = long.MaxValue;
		// 相邻块通常用同一个预测器：先试上一块的，提前压低下界。
		for (int k = 0; k < 7; k++)
		{
			int j = k == 0 ? seed : k - 1;
			if (k > 0 && j >= seed) j++;
			// 试跑也写字节（进 scratch）：早停时只写了一半，但那份不会被选中。
			long e = EncodeBlock(pcm, pcmOffset, samplesPerBlock, channels, j, scratch,
				0, measure: true, s0, s1, s2, s3, bestError);
			// 显式按索引打破平局：先试 seed 不改变“误差相同时取较小索引”的原结果。
			if (e < bestError || (e == bestError && (best < 0 || j < best)))
			{
				bestError = e;
				best = j;
				Buffer.BlockCopy(scratch, 0, destination, destinationOffset, blockAlign);
			}
		}

		return bestError;
	}

	/// <summary>
	/// 只编码**一个块**到 <paramref name="destination"/> 的指定偏移。给流式 OGG→ADPCM 用：
	/// 每块独立选预测器，与 <see cref="Encode"/> 的逐块结果逐字节一致。
	/// </summary>
	public static void EncodeOneBlock(short[] pcm, int channels, int blockAlign,
		byte[] destination, int destinationOffset, out double error)
		=> EncodeOneBlock(pcm, channels, blockAlign, destination, destinationOffset,
			seed: 0, out error, out _);

	/// <summary>带“上一块预测器”种子与结果回传；输出与无种子版本逐字节一致。</summary>
	public static void EncodeOneBlock(short[] pcm, int channels, int blockAlign,
		byte[] destination, int destinationOffset, int seed, out double error, out int best)
		=> EncodeOneBlock(pcm, channels, blockAlign, destination, destinationOffset, seed,
			new byte[blockAlign], out error, out best);

	/// <summary>
	/// 同上，但由调用方提供块级 scratch —— 流式编码逐块调用时不必每块分配一块 140 字节的数组。
	/// </summary>
	public static void EncodeOneBlock(short[] pcm, int channels, int blockAlign,
		byte[] destination, int destinationOffset, int seed, byte[] blockScratch,
		out double error, out int best)
	{
		int samplesPerBlock = SamplesPerBlock(blockAlign, channels);
		Span<int> scratch = channels <= 16
			? stackalloc int[channels * 4]
			: new int[channels * 4];
		Span<int> s0 = scratch[..channels];
		Span<int> s1 = scratch.Slice(channels, channels);
		Span<int> s2 = scratch.Slice(channels * 2, channels);
		Span<int> s3 = scratch.Slice(channels * 3, channels);

		long final = EncodeOneBlockCore(pcm, 0, samplesPerBlock, channels, destinationOffset,
			blockAlign, destination, blockScratch, s0, s1, s2, s3, seed, out best);
		error = (double)final;
	}

	/// <summary>
	/// 编码一个块。字节总是写进 <paramref name="destination"/>：试跑时由调用方传 scratch，
	/// <paramref name="measure"/> 只控制「误差超过 <paramref name="cutoff"/> 就提前放弃」。
	/// </summary>
	private static long EncodeBlock(short[] pcm, int pcmOffset, int samplesPerBlock, int channels, int predictor,
		byte[] destination, int destinationOffset, bool measure,
		Span<int> array, Span<int> array2, Span<int> array3, Span<int> array4, long cutoff)
	{
		// 音乐/音效绝大多数是 1/2 声道；把这两档展开成局部变量，消掉热循环里的
		// Span 索引、边界检查与 Math.Clamp 调用，算术与交错顺序逐字保持。
		if (channels == 2)
			return EncodeBlockStereo(pcm, pcmOffset, samplesPerBlock, predictor, destination,
				destinationOffset, measure, cutoff);
		if (channels == 1)
			return EncodeBlockMono(pcm, pcmOffset, samplesPerBlock, predictor, destination,
				destinationOffset, measure, cutoff);

		for (int i = 0; i < channels; i++)
		{
			array[i] = GetSample(pcm, pcmOffset + i);
			array2[i] = GetSample(pcm, pcmOffset + channels + i);
			array3[i] = Math.Max(16, Math.Abs(array2[i] - array[i]));
		}

		// 试跑也写（进 scratch），选中后整块拷进产出 —— 少一次正式编码。
		for (int j = 0; j < channels; j++)
		{
			destination[destinationOffset + j] = (byte)predictor;
			WriteInt16(destination, destinationOffset + channels + 2 * j, array3[j]);
			WriteInt16(destination, destinationOffset + 3 * channels + 2 * j, array2[j]);
			WriteInt16(destination, destinationOffset + 5 * channels + 2 * j, array[j]);
		}

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
				if (measure && num2 > cutoff) return long.MaxValue;

				if (num4 == 0)
				{
					destination[num3] = (byte)((num7 & 0xF) << 4);
				}
				else
				{
					destination[num3] |= (byte)(num7 & 0xF);
					num3++;
				}

				num4 ^= 1;
			}
		}

		return num2;
	}

	private static int Quantize(int diff, int delta) =>
		diff >= 0
			? Math.Min(7, (diff + delta / 2) / delta)
			: Math.Max(-8, -((delta / 2 - diff) / delta));

	private static int ClampSample(int value) =>
		value < -32768 ? -32768 : value > 32767 ? 32767 : value;

	/// <summary>立体声专用热循环；与通用路径逐字节一致。</summary>
	private static long EncodeBlockStereo(short[] pcm, int pcmOffset, int samplesPerBlock,
		int predictor, byte[] destination, int destinationOffset, bool measure, long cutoff)
	{
		int old0 = GetSample(pcm, pcmOffset), old1 = GetSample(pcm, pcmOffset + 1);
		int @new0 = GetSample(pcm, pcmOffset + 2), @new1 = GetSample(pcm, pcmOffset + 3);
		int delta0 = Math.Max(16, Math.Abs(@new0 - old0));
		int delta1 = Math.Max(16, Math.Abs(@new1 - old1));
		destination[destinationOffset] = (byte)predictor;
		destination[destinationOffset + 1] = (byte)predictor;
		WriteInt16(destination, destinationOffset + 2, delta0);
		WriteInt16(destination, destinationOffset + 4, delta1);
		WriteInt16(destination, destinationOffset + 6, @new0);
		WriteInt16(destination, destinationOffset + 8, @new1);
		WriteInt16(destination, destinationOffset + 10, old0);
		WriteInt16(destination, destinationOffset + 12, old1);

		int c1 = AdaptCoeff1[predictor], c2 = AdaptCoeff2[predictor];
		long error = 0;
		int byteAt = destinationOffset + 14;
		for (int l = 2; l < samplesPerBlock; l++)
		{
			int at = pcmOffset + l * 2;
			int p0 = (c1 * @new0 + c2 * old0) >> 8;
			int p1 = (c1 * @new1 + c2 * old1) >> 8;
			int d0 = GetSample(pcm, at) - p0;
			int d1 = GetSample(pcm, at + 1) - p1;
			int q0 = Quantize(d0, delta0);
			int q1 = Quantize(d1, delta1);
			error += (long)d0 * d0 + (long)d1 * d1;

			old0 = @new0; old1 = @new1;
			@new0 = ClampSample(p0 + q0 * delta0);
			@new1 = ClampSample(p1 + q1 * delta1);
			delta0 = (AdaptationTable[q0 & 0xF] * delta0) >> 8;
			delta1 = (AdaptationTable[q1 & 0xF] * delta1) >> 8;
			if (delta0 < 16) delta0 = 16;
			if (delta1 < 16) delta1 = 16;
			if (measure && error > cutoff) return long.MaxValue;

			destination[byteAt++] = (byte)(((q0 & 0xF) << 4) | (q1 & 0xF));
		}

		return error;
	}

	/// <summary>单声道专用热循环；与通用路径逐字节一致。</summary>
	private static long EncodeBlockMono(short[] pcm, int pcmOffset, int samplesPerBlock,
		int predictor, byte[] destination, int destinationOffset, bool measure, long cutoff)
	{
		int old = GetSample(pcm, pcmOffset);
		int @new = GetSample(pcm, pcmOffset + 1);
		int delta = Math.Max(16, Math.Abs(@new - old));
		destination[destinationOffset] = (byte)predictor;
		WriteInt16(destination, destinationOffset + 1, delta);
		WriteInt16(destination, destinationOffset + 3, @new);
		WriteInt16(destination, destinationOffset + 5, old);

		int c1 = AdaptCoeff1[predictor], c2 = AdaptCoeff2[predictor];
		long error = 0;
		int byteAt = destinationOffset + 7;
		for (int l = 2; l < samplesPerBlock; l++)
		{
			int p = (c1 * @new + c2 * old) >> 8;
			int d = GetSample(pcm, pcmOffset + l) - p;
			int q = Quantize(d, delta);
			error += (long)d * d;

			old = @new;
			@new = ClampSample(p + q * delta);
			delta = (AdaptationTable[q & 0xF] * delta) >> 8;
			if (delta < 16) delta = 16;
			if (measure && error > cutoff) return long.MaxValue;

			if ((l & 1) == 0) destination[byteAt] = (byte)((q & 0xF) << 4);
			else destination[byteAt++] |= (byte)(q & 0xF);
		}

		return error;
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
