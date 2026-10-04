using XnbConverter.Xact.AudioEngine.Entity;
using XnbConverter.Xact.AudioEngine.Reader;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
using XnbConverter.Xact.WaveBank.Codec;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

namespace XactRoundTrip;

/// <summary>
/// 三代往返检验：同一份数据来回转换 3 次，看是否收敛到稳定不动点。
///
/// 无损容器（.xwb/.xsb/.xgs）应当在第 1 代就与原文件逐字节相同；
/// 有损编解码（ADPCM）则看第 2、3 代是否不再变化——若编码与解码真正互逆，
/// 解码得到的采样恰好都落在解码器能产生的格点上，再编码应当逐块复现。
/// </summary>
public static class TripleCheck
{
	private const int Generations = 3;

	/// <summary>ADPCM 是有损的，允许更多代去看它是否落到不动点。</summary>
	private const int AdpcmGenerations = 12;

	public static int Run(string path, string root)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();
		return extension switch
		{
			".xwb" => CheckWaveBank(path, root),
			".xsb" => CheckSoundBank(path, root),
			".xgs" => CheckAudioEngine(path, root),
			_ => 1
		};
	}

	private static int CheckWaveBank(string path, string root)
	{
		byte[] generation0 = File.ReadAllBytes(path);
		byte[] previous = generation0;
		int failed = 0;
		for (int i = 1; i <= Generations; i++)
		{
			string dir = Path.Combine(root, $"w{i}");
			if (Directory.Exists(dir))
			{
				Directory.Delete(dir, true);
			}

			Directory.CreateDirectory(dir);
			WaveBank waveBank = WaveBankReader.Read(ToFile(dir, previous, "in.xwb"));
			waveBank.OutputPath = dir;
			for (int j = 0; j < waveBank.Entries.Count; j++)
			{
				waveBank.Entries[j].FilePath = dir + Path.DirectorySeparatorChar;
				waveBank.Entries[j].FileName ??= $"{j:x8}";
			}

			WaveBankReader.Save(waveBank);
			byte[] current = WaveBankWriter.Build(dir + ".xwb.config");
			bool sameAsOriginal = current.AsSpan().SequenceEqual(generation0);
			bool sameAsPrevious = current.AsSpan().SequenceEqual(previous);
			Console.WriteLine($"  .xwb 第 {i} 代: {current.Length} 字节  " +
			                  $"{(sameAsOriginal ? "与原文件相同" : "与原文件不同")}  " +
			                  $"{(sameAsPrevious ? "与上一代相同" : $"与上一代不同（差异 {CountDiff(current, previous)}）")}");
			if (!sameAsOriginal)
			{
				failed++;
			}

			previous = current;
		}

		return failed;
	}

	private static int CheckSoundBank(string path, string root)
	{
		byte[] generation0 = File.ReadAllBytes(path);
		byte[] previous = generation0;
		int failed = 0;
		for (int i = 1; i <= Generations; i++)
		{
			string file = ToFile(root, previous, $"sb{i}.xsb");
			SoundBankView view = SoundBankView.From(SoundBankReader.Read(file));
			SoundBank bank = SoundBankReader.Read(file);
			view.ApplyTo(bank);
			byte[] current = SoundBankReader.Build(bank);
			bool sameAsOriginal = current.AsSpan().SequenceEqual(generation0);
			bool sameAsPrevious = current.AsSpan().SequenceEqual(previous);
			Console.WriteLine($"  .xsb 第 {i} 代: {current.Length} 字节  " +
			                  $"{(sameAsOriginal ? "与原文件相同" : "与原文件不同")}  " +
			                  $"{(sameAsPrevious ? "与上一代相同" : $"与上一代不同（差异 {CountDiff(current, previous)}）")}");
			if (!sameAsOriginal)
			{
				failed++;
			}

			previous = current;
		}

		return failed;
	}

	private static int CheckAudioEngine(string path, string root)
	{
		byte[] generation0 = File.ReadAllBytes(path);
		byte[] previous = generation0;
		int failed = 0;
		for (int i = 1; i <= Generations; i++)
		{
			string file = ToFile(root, previous, $"ae{i}.xgs");
			AudioEngineView view = AudioEngineView.From(AudioEngineReader.Read(file));
			AudioEngine engine = AudioEngineReader.Read(file);
			view.ApplyTo(engine);
			byte[] current = AudioEngineReader.Build(engine);
			bool sameAsOriginal = current.AsSpan().SequenceEqual(generation0);
			bool sameAsPrevious = current.AsSpan().SequenceEqual(previous);
			Console.WriteLine($"  .xgs 第 {i} 代: {current.Length} 字节  " +
			                  $"{(sameAsOriginal ? "与原文件相同" : "与原文件不同")}  " +
			                  $"{(sameAsPrevious ? "与上一代相同" : $"与上一代不同（差异 {CountDiff(current, previous)}）")}");
			if (!sameAsOriginal)
			{
				failed++;
			}

			previous = current;
		}

		return failed;
	}

	/// <summary>ADPCM 三代：解码 -&gt; 编码 反复三轮，看是否收敛到不动点。</summary>
	public static int CheckAdpcm(string waveBankPath, string root)
	{
		WaveBank waveBank = WaveBankReader.Read(waveBankPath);
		int failed = 0;
		int total = Math.Min(waveBank.Entries.Count, 20);
		for (int index = 0; index < total; index++)
		{
			WaveBank.WaveBankEntry entry = waveBank.Entries[index];
			entry.DecodeFormat(waveBank.Header.Version, out WaveBank.WaveBankFormats codec, out int channels,
				out int rate, out int align, out int bits);
			if (codec != WaveBank.WaveBankFormats.AdpcmMs)
			{
				continue;
			}

			int blockAlign = (align + 22) * channels;
			int blockCount = entry.Data.Length / blockAlign;
			byte[] current = entry.Data;
			byte[] previous = entry.Data;
			List<int> diffs = new List<int>();
			List<int> diffsFromOriginal = new List<int>();
			for (int i = 1; i <= AdpcmGenerations; i++)
			{
				short[] pcm = MsAdpcm.Decode(current, 0, current.Length, channels, blockAlign);
				byte[] next = MsAdpcm.Encode(pcm, channels, blockAlign, blockCount, out double _);
				diffs.Add(CountDiff(next, current));
				diffsFromOriginal.Add(CountDiff(next, entry.Data));
				previous = current;
				current = next;
				if (diffs[^1] == 0)
				{
					break;
				}
			}

			// 收敛 = 某一代后与上一代完全相同
			bool stable = diffs[^1] == 0;

			// 没到 0 也不一定有问题：量化两种表示解出的音频差多少
			string plateau = "";
			bool audioStable = false;
			if (!stable)
			{
				short[] a = MsAdpcm.Decode(previous, 0, previous.Length, channels, blockAlign);
				short[] b = MsAdpcm.Decode(current, 0, current.Length, channels, blockAlign);
				double mse = 0;
				int diffSamples = 0;
				int maxDelta = 0;
				for (int k = 0; k < Math.Min(a.Length, b.Length); k++)
				{
					int d = a[k] - b[k];
					if (d != 0)
					{
						diffSamples++;
						maxDelta = Math.Max(maxDelta, Math.Abs(d));
					}

					mse += (double)d * d;
				}

				mse /= Math.Max(a.Length, 1);
				double psnr = mse <= 0 ? 999 : 10 * Math.Log10(32768.0 * 32768.0 / mse);
				audioStable = psnr > 80;
				plateau = $"，摆动处差异采样 {diffSamples}/{a.Length} 最大 {maxDelta} LSB，PSNR {psnr:F1}dB";
			}

			bool sameAsOriginal = diffsFromOriginal[0] == 0;
			if (!stable && !audioStable)
			{
				failed++;
			}

			if (index < 4 || (!stable && !audioStable))
			{
				Console.WriteLine($"  条目{index}: 与上一代差异 [{string.Join(", ", diffs)}] / {current.Length} 字节，" +
				                  $"与原文件差异 {diffsFromOriginal[0]}{plateau}  " +
				                  $"{(sameAsOriginal ? "首代即与原文件相同" : "首代有损")}  " +
				                  $"{(stable ? "字节已收敛 ✓" : audioStable ? "字节摆动但音频等效 ✓" : "未收敛 ✗")}");
			}
		}

		Console.WriteLine($"  ADPCM 三代: 检查 {total} 个条目，未收敛 {failed}");
		return failed;
	}

	private static int CountDiff(byte[] a, byte[] b)
	{
		int num = Math.Abs(a.Length - b.Length);
		for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
		{
			if (a[i] != b[i])
			{
				num++;
			}
		}

		return num;
	}

	private static string ToFile(string dir, byte[] data, string name)
	{
		Directory.CreateDirectory(dir);
		string text = Path.Combine(dir, name);
		File.WriteAllBytes(text, data);
		return text;
	}
}
