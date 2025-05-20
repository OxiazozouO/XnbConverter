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

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		XactSound xactSound = new XactSound();
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

	public override void Write(object input)
	{
	}
}
