using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class AudioCategoryReader : BaseReader
{

	public override object Read()
	{
		AudioCategory obj = new AudioCategory
		{
			maxInstances = bufferReader.ReadByte()
		};
		obj.instanceLimit = obj.maxInstances != 255;
		obj.fadeIn = (float)(int)bufferReader.ReadUInt16() / 1000f;
		obj.fadeOut = (float)(int)bufferReader.ReadUInt16() / 1000f;
		obj.instanceFlags = bufferReader.ReadByte();
		obj.fadeType = (AudioCategory.CrossfadeType)(obj.instanceFlags & 7);
		obj.InstanceBehavior = (AudioCategory.MaxInstanceBehavior)(obj.instanceFlags >> 3);
		obj.unkn = bufferReader.ReadUInt16();
		obj.volumeDecibels = bufferReader.ReadByte();
		obj.visibilityFlags = bufferReader.ReadByte();
		obj.isBackgroundMusic = (obj.visibilityFlags & 1) != 0;
		obj.isPublic = (obj.visibilityFlags & 2) != 0;
		return obj;
	}

	public override void Write(object input)
	{
		AudioCategory audioCategory = (AudioCategory)input;
		bufferWriter.WriteByte((byte)audioCategory.maxInstances);
		bufferWriter.WriteUInt16((ushort)Math.Round(audioCategory.fadeIn * 1000f));
		bufferWriter.WriteUInt16((ushort)Math.Round(audioCategory.fadeOut * 1000f));
		bufferWriter.WriteByte(audioCategory.instanceFlags);
		bufferWriter.WriteUInt16(audioCategory.unkn);
		bufferWriter.WriteByte(audioCategory.volumeDecibels);
		bufferWriter.WriteByte(audioCategory.visibilityFlags);
	}
}
