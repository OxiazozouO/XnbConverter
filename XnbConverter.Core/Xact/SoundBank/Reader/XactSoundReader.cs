using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.SoundBank.Entity;

namespace XnbConverter.Xact.SoundBank.Reader;

public class XactSoundReader : BaseReader
{
    public XactClipReader xactClipReader = new();

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        xactClipReader.Init(readerResolver);
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override XactSound Read()
    {
        var result = new XactSound();
        result.Flags = bufferReader.ReadByte();
        result.CategoryId = bufferReader.ReadUInt16();
        result.VolumeFlag = bufferReader.ReadByte();
        result.Pitch = bufferReader.ReadInt16() / 1000.0f;
        result.Priority = bufferReader.ReadByte();
        result.Filter = bufferReader.ReadUInt16();

        var complexSound = (result.Flags & 1) != 0;

        if (complexSound)
        {
            result.NumClips = bufferReader.ReadByte();
        }
        else
        {
            result.TrackIndex = bufferReader.ReadUInt16();
            result.WaveBankIndex = bufferReader.ReadByte();
            // wave
            Log.Debug("轨道索引: {0}", result.TrackIndex);
            Log.Debug("WaveBank 索引: {0}", result.WaveBankIndex);
        }

        if ((result.Flags & 14) != 0)
        {
            var current = bufferReader.BytePosition;

            result.ExtraDataLen = bufferReader.ReadUInt16();

            var numPresets = bufferReader.ReadByte();
            result.AudioEngineFileOffsets = new uint[numPresets];
            for (var i = 0; i < numPresets; i++)
                result.AudioEngineFileOffsets[i] = bufferReader.ReadUInt32();

            if (complexSound) Log.Info("额外数据长度: {0}", result.ExtraDataLen);

            // TODO: 解析 RPC+DSP 相关内容
            bufferReader.BytePosition = current + result.ExtraDataLen;
        }

        //
        if ((result.Flags & 16) != 0)
            bufferReader.Skip(7);

        if (complexSound)
        {
            result.SoundClips = new XactClip[result.NumClips];
            for (var i = 0; i < result.NumClips; i++)
                result.SoundClips[i] = (XactClip)xactClipReader.Read();
        }

        return result;
    }

    public override void Write(object input)
    {
    }
}