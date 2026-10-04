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
        // 格式块长度（WAVEFORMATEX 的 18 字节）由 FmtChunkReader.Read 一并读走，
        // 这里**不能再预读一次** —— 否则会把紧随其后的 FmtTag/SampleRate 当成这个字段，
        // 整段错位 4 字节。Terraria 的 Content/Sounds/*.xnb 就是这么被读崩的
        // （报「格式为 -21436」，其实是把 SampleRate 44100 当成了 FmtTag）。
        soundEffect.WaveForm.fmtChunk = (FmtChunk)_fmtChunkReader.Read();
        if (soundEffect.WaveForm.fmtChunk.FmtSize != 18)
        {
            throw new AggregateException("参数错误！");
        }

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
        // 借出来用完必须还：BufferWriter 走的是池，不 Dispose 就等于每次导出一份
        // 和音频数据同量级的垃圾。下面两处波形写出（这里和 WaveFormReader.Save）原来都漏了。
        using BufferWriter writer = new BufferWriter((int)(input.WaveForm.riffChunk.ChunkSize + 1000));
        soundEffectReader.Init(new ReaderResolver
        {
            bufferWriter = writer
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