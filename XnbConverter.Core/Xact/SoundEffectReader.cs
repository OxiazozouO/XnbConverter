using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace XnbConverter.Xact;

public class SoundEffectReader : BaseReader, IReaderFileUtil<SoundEffect>
{
    private readonly DATAChunkReader _dataChunkReader = new DATAChunkReader();

    private readonly FmtChunkReader _fmtChunkReader = new FmtChunkReader();

    private readonly WaveFormReader _waveFormReader = new WaveFormReader();

    public void Save(SoundEffect input)
    {
        _waveFormReader.Save(input.WaveForm);
    }

    public SoundEffect Load()
    {
        SoundEffect soundEffect = new SoundEffect();
        soundEffect.WaveForm = _waveFormReader.Load();
        soundEffect.WaveForm.fmtChunk.CheckFmtID("SoundEffect");
        return soundEffect;
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override void Init(ReaderResolver resolver)
    {
        base.Init(resolver);
        _waveFormReader.Init(resolver);
        _fmtChunkReader.Init(resolver);
        _dataChunkReader.Init(resolver);
    }

    public override object Read()
    {
        SoundEffect soundEffect = new SoundEffect();
        if ((long)bufferReader.ReadInt32() != 18)
        {
            throw new AggregateException("参数错误！");
        }

        soundEffect.WaveForm.fmtChunk = (FmtChunk)_fmtChunkReader.Read();
        soundEffect.WaveForm.fmtChunk.CheckFmtID("SoundEffect");
        soundEffect.WaveForm.dataChunk = (DATAChunk)_dataChunkReader.Read();
        soundEffect.WaveForm.riffChunk.ChunkSize = 36 + soundEffect.WaveForm.dataChunk.DataSize;
        soundEffect.exData.LoopStart = bufferReader.ReadUInt32();
        soundEffect.exData.LoopLength = bufferReader.ReadUInt32();
        soundEffect.exData.DurationMs = bufferReader.ReadUInt32();
        return soundEffect;
    }

    public override void Write(object input)
    {
        SoundEffect soundEffect = (SoundEffect)input;
        WaveForm waveForm = soundEffect.WaveForm;
        waveForm.fmtChunk.FmtSize = 18u;
        waveForm.fmtChunk.CbSize = 0;
        _fmtChunkReader.Write(waveForm.fmtChunk);
        _dataChunkReader.Write(waveForm.dataChunk);
        bufferWriter.WriteUInt32(soundEffect.exData.LoopStart);
        bufferWriter.WriteUInt32(soundEffect.exData.LoopLength);
        bufferWriter.WriteUInt32(soundEffect.exData.DurationMs);
    }

    public static void Save(SoundEffect input, string path)
    {
        SoundEffectReader soundEffectReader = new SoundEffectReader();
        soundEffectReader.Init(new ReaderResolver
        {
            bufferWriter = new BufferWriter((int)(input.WaveForm.riffChunk.ChunkSize + 1000))
        });
        soundEffectReader._waveFormReader.Save(input.WaveForm);
        soundEffectReader.bufferWriter.SaveBufferToFile(path);
    }

    public static SoundEffect FormFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        WaveFormReader.Convert(path, bytes);//, FmtChunk.AudioFormats.Pcm
        SoundEffectReader soundEffectReader = new SoundEffectReader();
        soundEffectReader.Init(new ReaderResolver
        {
            bufferReader = BufferReader.FormFile(path)
        });
        return soundEffectReader.Load();
    }
}