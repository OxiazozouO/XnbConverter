using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace XnbConverter.Xact;

public class SoundEffectReader : BaseReader, IReaderFileUtil<SoundEffect>
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    private WaveFormReader waveFormReader = new();
    private FmtChunkReader fmtChunkReader = new();
    private DATAChunkReader dataChunkReader = new();

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        waveFormReader.Init(readerResolver);
        fmtChunkReader.Init(readerResolver);
        dataChunkReader.Init(readerResolver);
    }

    public override SoundEffect Read()
    {
        var result = new SoundEffect();
        var fmtSize = bufferReader.ReadInt32();
        if (fmtSize != FmtChunk.defSize + 2 /*fmtChunkSize + cbSize = 16 + 2 = 18*/
           ) throw new AggregateException("参数错误！");

        result.WaveForm.fmtChunk = fmtChunkReader.Read(); // cbSize 2 16~17

        result.WaveForm.fmtChunk.CheckFmtID("SoundEffect");

        result.WaveForm.dataChunk = dataChunkReader.Read();
        result.WaveForm.riffChunk.ChunkSize = 36u + result.WaveForm.dataChunk.DataSize;
        result.exData.LoopStart = bufferReader.ReadUInt32();
        result.exData.LoopLength = bufferReader.ReadUInt32();
        result.exData.DurationMs = bufferReader.ReadUInt32();

        return result;
    }

    public override void Write(object input)
    {
        var soundEffect = (SoundEffect)input;
        var wave = soundEffect.WaveForm;

        wave.fmtChunk.FmtSize = FmtChunk.defSize + 2u;
        wave.fmtChunk.CbSize = 0;

        fmtChunkReader.Write(wave.fmtChunk); // cbSize 2 16~17
        dataChunkReader.Write(wave.dataChunk);
        bufferWriter.WriteUInt32(soundEffect.exData.LoopStart);
        bufferWriter.WriteUInt32(soundEffect.exData.LoopLength);
        bufferWriter.WriteUInt32(soundEffect.exData.DurationMs);
    }

    public void Save(SoundEffect input)
    {
        waveFormReader.Save(input.WaveForm);
    }

    public static void Save(SoundEffect input, string path)
    {
        var soundEffectReader = new SoundEffectReader();
        soundEffectReader.Init(new ReaderResolver()
        {
            bufferWriter = new BufferWriter((int)(input.WaveForm.riffChunk.ChunkSize + 1000))
        });
        soundEffectReader.waveFormReader.Save(input.WaveForm);
        soundEffectReader.bufferWriter.SaveBufferToFile(path);
    }

    public SoundEffect Load()
    {
        var result = new SoundEffect();

        result.WaveForm = waveFormReader.Load();
        result.WaveForm.fmtChunk.CheckFmtID("SoundEffect");

        return result;
    }

    public static SoundEffect FormFile(string path)
    {
        //ffmpeg预处理
        FFmpegUtil.Convert(path, format: FmtChunk.AudioFormats.Pcm);

        //
        var soundEffectReader = new SoundEffectReader();
        soundEffectReader.Init(new ReaderResolver()
        {
            bufferReader = BufferReader.FormFile(path)
        });
        return soundEffectReader.Load();
    }
}