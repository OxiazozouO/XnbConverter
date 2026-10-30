using XnbConverter.Configurations;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Xact.SoundBank.Entity;

namespace XnbConverter.Xact.SoundBank.Reader;

public class XactClipReader : BaseReader
{

	public override object Read()
	{
		XactClip xactClip = new XactClip();
		xactClip.VolumeDb = bufferReader.ReadByte();
		xactClip.ClipOffset = bufferReader.ReadUInt32();
		xactClip.FilterQAndFlags = bufferReader.ReadUInt16();
		xactClip.FilterFrequency = bufferReader.ReadUInt16();
		Logger.Debug(Error.XactClipReader_1, xactClip.ClipOffset);
		int bytePosition = bufferReader.BytePosition;
		bufferReader.BytePosition = (int)xactClip.ClipOffset;
		byte b = bufferReader.ReadByte();
		xactClip.WaveIndexs = new XactClip.WaveIndex[b];
		for (int i = 0; i < b; i++)
		{
			XactClip.WaveIndex waveIndex = new XactClip.WaveIndex();
			waveIndex.EventInfo = bufferReader.ReadUInt32();
			waveIndex.RandomOffset = (float)(int)bufferReader.ReadUInt16() * 0.001f;
			switch (waveIndex.EventInfo & 0x1F)
			{
			case 0u:
				throw new NotImplementedException("Stop event");
			case 1u:
				waveIndex.unkn.Add(bufferReader.Read(1));
				waveIndex.EventFlags = bufferReader.ReadByte();
				waveIndex.TrackIndex = bufferReader.ReadUInt16();
				waveIndex.WaveBankIndex = bufferReader.ReadByte();
				waveIndex.LoopCount = bufferReader.ReadByte();
				waveIndex.PanAngle = (float)(int)bufferReader.ReadUInt16() / 100f;
				waveIndex.PanArc = (float)(int)bufferReader.ReadUInt16() / 100f;
				Logger.Debug(Error.XactClipReader_2, waveIndex.TrackIndex);
				break;
			case 3u:
			{
				waveIndex.unkn.Add(bufferReader.Read(1));
				waveIndex.EventFlags = bufferReader.ReadByte();
				waveIndex.LoopCount = bufferReader.ReadByte();
				waveIndex.PanAngle = (float)(int)bufferReader.ReadUInt16() / 100f;
				waveIndex.PanArc = (float)(int)bufferReader.ReadUInt16() / 100f;
				waveIndex.NumTracks = bufferReader.ReadUInt16();
				waveIndex.MoreFlags = bufferReader.ReadByte();
				waveIndex.unkn.Add(bufferReader.Read(5));
				waveIndex.WaveBanks = new int[waveIndex.NumTracks];
				waveIndex.Tracks = new int[waveIndex.NumTracks];
				waveIndex.Weights = new byte[waveIndex.NumTracks][];
				int num = 0;
				for (int k = 0; k < waveIndex.NumTracks; k++)
				{
					waveIndex.Tracks[k] = bufferReader.ReadUInt16();
					waveIndex.WaveBanks[k] = bufferReader.ReadByte();
					byte b4 = bufferReader.ReadByte();
					byte b5 = bufferReader.ReadByte();
					waveIndex.Weights[k] = new byte[2] { b4, b5 };
					num += b5 - b4;
				}
				break;
			}
			case 4u:
				waveIndex.unkn.Add(bufferReader.Read(1));
				waveIndex.EventFlags = bufferReader.ReadByte();
				waveIndex.TrackIndex = bufferReader.ReadUInt16();
				waveIndex.WaveBankIndex = bufferReader.ReadByte();
				waveIndex.LoopCount = bufferReader.ReadByte();
				waveIndex.PanAngle = (int)bufferReader.ReadUInt16();
				waveIndex.PanArc = (int)bufferReader.ReadUInt16();
				waveIndex.MinPitch = (float)bufferReader.ReadInt16() / 1000f;
				waveIndex.MaxPitch = (float)bufferReader.ReadInt16() / 1000f;
				waveIndex.MinVolumeDecibels = bufferReader.ReadByte();
				waveIndex.MaxVolumeDecibels = bufferReader.ReadByte();
				waveIndex.MinFrequency = bufferReader.ReadSingle();
				waveIndex.MaxFrequency = bufferReader.ReadSingle();
				waveIndex.MinQ = bufferReader.ReadSingle();
				waveIndex.MaxQ = bufferReader.ReadSingle();
				waveIndex.unkn.Add(bufferReader.Read(1));
				waveIndex.VariationFlags = bufferReader.ReadByte();
				Logger.Debug(Error.XactClipReader_3);
				break;
			case 6u:
			{
				waveIndex.unkn.Add(bufferReader.Read(1));
				waveIndex.EventFlags = bufferReader.ReadByte();
				waveIndex.LoopCount = bufferReader.ReadByte();
				waveIndex.PanAngle = (float)(int)bufferReader.ReadUInt16() / 100f;
				waveIndex.PanArc = (float)(int)bufferReader.ReadUInt16() / 100f;
				waveIndex.MinPitch = (float)bufferReader.ReadInt16() / 1000f;
				waveIndex.MaxPitch = (float)bufferReader.ReadInt16() / 1000f;
				waveIndex.MinVolumeDecibels = bufferReader.ReadByte();
				waveIndex.MaxVolumeDecibels = bufferReader.ReadByte();
				waveIndex.MinFrequency = bufferReader.ReadSingle();
				waveIndex.MaxFrequency = bufferReader.ReadSingle();
				waveIndex.MinQ = bufferReader.ReadSingle();
				waveIndex.MaxQ = bufferReader.ReadSingle();
				waveIndex.unkn.Add(bufferReader.Read(1));
				waveIndex.VariationFlags = bufferReader.ReadByte();
				waveIndex.NumTracks = bufferReader.ReadUInt16();
				waveIndex.MoreFlags = bufferReader.ReadByte();
				waveIndex.unkn.Add(bufferReader.Read(5));
				waveIndex.Tracks = new int[waveIndex.NumTracks];
				waveIndex.WaveBanks = new int[waveIndex.NumTracks];
				waveIndex.Weights = new byte[waveIndex.NumTracks][];
				int num = 0;
				for (int j = 0; j < waveIndex.NumTracks; j++)
				{
					waveIndex.Tracks[j] = bufferReader.ReadUInt16();
					waveIndex.WaveBanks[j] = bufferReader.ReadByte();
					byte b2 = bufferReader.ReadByte();
					byte b3 = bufferReader.ReadByte();
					waveIndex.Weights[j] = new byte[2] { b2, b3 };
					num += b3 - b2;
				}
				break;
			}
			case 7u:
				throw new NotImplementedException("Pitch event");
			case 8u:
				waveIndex.unkn.Add(bufferReader.Read(2));
				bufferReader.ReadByte();
				throw new NotImplementedException();
			case 17u:
				throw new NotImplementedException("Volume repeat event");
			case 9u:
				throw new NotImplementedException("Marker event");
			default:
				throw new Exception();
			}
			xactClip.WaveIndexs[i] = waveIndex;
		}
		bufferReader.BytePosition = bytePosition;
		return xactClip;
	}

	/// <summary>
	/// 写一条 Clip：头部 9 字节（VolumeDb + 指向事件区的 u32 + FilterQAndFlags + FilterFrequency），
	/// 事件区紧随其后（首字节是事件数）。
	/// <para>
	/// 支持两种事件：
	/// <list type="bullet">
	/// <item><b>类型 1</b>（单条播放）—— 原版音乐的循环就是这一种
	/// （实测 <c>spring_day_ambient</c>：<c>EventInfo=0x20000001</c>、<c>LoopCount=255</c>、
	/// <c>EventFlags=0x0C</c>）。</item>
	/// <item><b>类型 6</b>（多轨随机变体）—— 一个 clip 事件里带多条
	/// <c>(TrackIndex, WaveBankIndex, weight)</c>；MonoGame 按变体类型随机挑一条。
	/// 用来表达 CP `FilePaths` 的多文件随机音乐。</item>
	/// </list>
	/// 其它类型（音高/停止/标记）仍明确报错，绝不写半条出去。
	/// </para>
	/// </summary>
	public override void Write(object input)
	{
		XactClip clip = (XactClip)input;
		int at = bufferWriter.BytePosition;

		bufferWriter.WriteByte(clip.VolumeDb);
		bufferWriter.WriteUInt32((uint)(at + 9));
		bufferWriter.WriteUInt16(clip.FilterQAndFlags);
		bufferWriter.WriteUInt16(clip.FilterFrequency);

		XactClip.WaveIndex[] waves = clip.WaveIndexs ?? System.Array.Empty<XactClip.WaveIndex>();
		bufferWriter.WriteByte((byte)waves.Length);
		foreach (XactClip.WaveIndex w in waves)
		{
			uint kind = w.EventInfo & 0x1Fu;
			switch (kind)
			{
				case 1u:
					WriteSingle(w);
					break;
				case 6u:
					WriteVariants(w);
					break;
				default:
					throw new XnbError($"只支持写「类型 1（单条）」和「类型 6（多轨随机变体）」的 clip 事件，收到类型 {kind}");
			}
		}
	}

	private void WriteSingle(XactClip.WaveIndex w)
	{
		bufferWriter.WriteUInt32(w.EventInfo);
		bufferWriter.WriteUInt16((ushort)System.Math.Round(w.RandomOffset * 1000f));
		bufferWriter.Write(w.unkn.Count > 0 ? w.unkn[0] : new byte[1]);
		bufferWriter.WriteByte(w.EventFlags);
		bufferWriter.WriteUInt16((ushort)w.TrackIndex);
		bufferWriter.WriteByte((byte)w.WaveBankIndex);
		bufferWriter.WriteByte(w.LoopCount);
		bufferWriter.WriteUInt16((ushort)System.Math.Round(w.PanAngle * 100f));
		bufferWriter.WriteUInt16((ushort)System.Math.Round(w.PanArc * 100f));
	}

	/// <summary>
	/// 类型 6 的字段顺序照 MonoGame 的 <c>XactClip</c> 读取器：
	/// 事件头 → 音高/音量/滤波随机范围 → 变体标志 → 轨道表。
	/// 我们只填“固定值 + 多轨道”，不启用音高/音量/滤波随机范围。
	/// </summary>
	private void WriteVariants(XactClip.WaveIndex w)
	{
		int[] tracks = w.Tracks ?? System.Array.Empty<int>();
		int[] banks = w.WaveBanks ?? new int[tracks.Length];
		if (banks.Length != tracks.Length)
		{
			throw new XnbError("多轨事件的 Tracks 与 WaveBanks 数量不一致");
		}

		bufferWriter.WriteUInt32(w.EventInfo);
		bufferWriter.WriteUInt16((ushort)System.Math.Round(w.RandomOffset * 1000f));
		bufferWriter.Write(w.unkn.Count > 0 ? w.unkn[0] : new byte[] { 0xFF });
		bufferWriter.WriteByte(w.EventFlags);
		bufferWriter.WriteByte(w.LoopCount);
		bufferWriter.WriteUInt16((ushort)System.Math.Round(w.PanAngle * 100f));
		bufferWriter.WriteUInt16((ushort)System.Math.Round(w.PanArc * 100f));
		bufferWriter.WriteInt16((short)System.Math.Round(w.MinPitch * 1000f));
		bufferWriter.WriteInt16((short)System.Math.Round(w.MaxPitch * 1000f));
		bufferWriter.WriteByte(w.MinVolumeDecibels);
		bufferWriter.WriteByte(w.MaxVolumeDecibels);
		bufferWriter.WriteSingle(w.MinFrequency);
		bufferWriter.WriteSingle(w.MaxFrequency);
		bufferWriter.WriteSingle(w.MinQ);
		bufferWriter.WriteSingle(w.MaxQ);
		bufferWriter.Write(w.unkn.Count > 1 ? w.unkn[1] : new byte[] { 0 });
		bufferWriter.WriteByte(w.VariationFlags);
		bufferWriter.WriteUInt16((ushort)tracks.Length);
		bufferWriter.WriteByte(w.MoreFlags);
		bufferWriter.Write(w.unkn.Count > 2 ? w.unkn[2] : new byte[5]);

		for (int i = 0; i < tracks.Length; i++)
		{
			byte[] weight = w.Weights != null && i < w.Weights.Length ? w.Weights[i] : new byte[] { 0, 1 };
			bufferWriter.WriteUInt16((ushort)tracks[i]);
			bufferWriter.WriteByte((byte)banks[i]);
			bufferWriter.WriteByte(weight[0]);
			bufferWriter.WriteByte(weight[1]);
		}
	}
}
