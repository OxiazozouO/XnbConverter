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
	/// **只写「类型 1（单条播放）」的事件** —— 原版音乐的循环就是这一种
	/// （实测 <c>spring_day_ambient</c>：<c>EventInfo=0x20000001</c>、<c>LoopCount=255</c>、
	/// <c>EventFlags=0x0C</c>）。别的类型（多轨/随机/音高）读得出来但写不了，
	/// 遇到就**报错**，绝不写半条出去。
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
			if (kind != 1u)
			{
				throw new XnbError($"只支持写「类型 1（单条播放）」的 clip 事件，收到类型 {kind}");
			}

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
	}
}
