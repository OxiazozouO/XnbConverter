using XnbConverter.Configurations;
using XnbConverter.Readers;
using XnbConverter.Xact.SoundBank.Entity;

namespace XnbConverter.Xact.SoundBank.Reader;

public class XactSoundReader : BaseReader
{
	public XactClipReader xactClipReader = new XactClipReader();

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		xactClipReader.Init(resolver);
	}


	public override object Read()
	{
		XactSound xactSound = new XactSound();
		xactSound.FileOffset = (uint)bufferReader.BytePosition;
		xactSound.Flags = bufferReader.ReadByte();
		xactSound.CategoryId = bufferReader.ReadUInt16();
		xactSound.VolumeFlag = bufferReader.ReadByte();
		xactSound.Pitch = (float)bufferReader.ReadInt16() / 1000f;
		xactSound.Priority = bufferReader.ReadByte();
		xactSound.Filter = bufferReader.ReadUInt16();
		bool flag = (xactSound.Flags & 1) != 0;
		if (flag)
		{
			xactSound.NumClips = bufferReader.ReadByte();
		}
		else
		{
			xactSound.TrackIndex = bufferReader.ReadUInt16();
			xactSound.WaveBankIndex = bufferReader.ReadByte();
			Logger.Debug(Error.XactSoundReader_2, xactSound.TrackIndex);
			Logger.Debug(Error.SoundBankReader_23, xactSound.WaveBankIndex);
		}
		if ((xactSound.Flags & 0xEu) != 0)
		{
			int bytePosition = bufferReader.BytePosition;
			xactSound.ExtraDataLen = bufferReader.ReadUInt16();
			byte b = bufferReader.ReadByte();
			xactSound.AudioEngineFileOffsets = new uint[b];
			for (int i = 0; i < b; i++)
			{
				xactSound.AudioEngineFileOffsets[i] = bufferReader.ReadUInt32();
			}
			if (flag)
			{
				Logger.Info(Error.XactSoundReader_1, xactSound.ExtraDataLen);
			}
			bufferReader.BytePosition = bytePosition + xactSound.ExtraDataLen;
		}
		if ((xactSound.Flags & 0x10u) != 0)
		{
			bufferReader.Skip(7);
		}
		if (flag)
		{
			xactSound.SoundClips = new XactClip[xactSound.NumClips];
			for (int j = 0; j < xactSound.NumClips; j++)
			{
				xactSound.SoundClips[j] = (XactClip)xactClipReader.Read();
			}
		}
		return xactSound;
	}

	/// <summary>
	/// 就地修补 Sound 的固定头部字段（前 9 字节：Flags/CategoryId/VolumeFlag/Pitch/Priority/Filter）。
	/// 其后的内容随 Flags 变化，且含未被解析的 ExtraData 区间，所以不整段重建、保持原样。
	/// </summary>
	public override void Write(object input)
	{
		XactSound xactSound = (XactSound)input;
		if (xactSound.FileOffset == 0)
		{
			return;
		}

		bufferWriter.BytePosition = (int)xactSound.FileOffset;
		bufferWriter.WriteByte(xactSound.Flags);
		bufferWriter.WriteUInt16(xactSound.CategoryId);
		bufferWriter.WriteByte(xactSound.VolumeFlag);
		bufferWriter.WriteInt16((short)Math.Round(xactSound.Pitch * 1000f));
		bufferWriter.WriteByte(xactSound.Priority);
		bufferWriter.WriteUInt16(xactSound.Filter);
	}
}
