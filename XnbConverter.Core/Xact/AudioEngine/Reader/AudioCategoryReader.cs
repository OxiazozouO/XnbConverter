using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class AudioCategoryReader : BaseReader
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override AudioCategory Read()
    {
        var result = new AudioCategory();
        result.maxInstances = bufferReader.ReadByte();
        result.instanceLimit = result.maxInstances != 0xff;

        result.fadeIn = bufferReader.ReadUInt16() / 1000f;
        result.fadeOut = bufferReader.ReadUInt16() / 1000f;
        result.instanceFlags = bufferReader.ReadByte();
        result.fadeType = (AudioCategory.CrossfadeType)(result.instanceFlags & 0x7);
        result.InstanceBehavior = (AudioCategory.MaxInstanceBehavior)(result.instanceFlags >> 3);
        result.unkn = bufferReader.ReadUInt16(); //unkn
        result.volumeDecibels = bufferReader.ReadByte();

        result.visibilityFlags = bufferReader.ReadByte();
        result.isBackgroundMusic = (result.visibilityFlags & 0x1) != 0;
        result.isPublic = (result.visibilityFlags & 0x2) != 0;

        return result;
    }

    public override void Write(object input)
    {
        var audioCategory = (AudioCategory)input;
    }
}