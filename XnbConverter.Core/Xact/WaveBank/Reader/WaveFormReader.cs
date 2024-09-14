using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Entity;
using static XnbConverter.Xact.WaveBank.Entity.WaveBank;

namespace XnbConverter.Xact.WaveBank.Reader;

public class WaveFormReader : BaseReader, IReaderFileUtil<WaveForm>, IDisposable
{
    private const int AdpcmMiniWaveFormatBlockAlignConversionOffset = 22;

    private readonly ChunkReader _chunkReader = new ChunkReader();

    private readonly DATAChunkReader _dataChunkReader = new DATAChunkReader();

    private readonly FactChunkReader _factChunkReader = new FactChunkReader();

    private readonly FmtChunkReader _fmtChunkReader = new FmtChunkReader();

    private readonly RIFFChunkReader _riffChunkReader = new RIFFChunkReader();

    public void Dispose()
    {
        bufferWriter?.Dispose();
        bufferReader?.Dispose();
    }

    public void Save(WaveForm input)
    {
        _riffChunkReader.Save(input.riffChunk);
        _fmtChunkReader.Save(input.fmtChunk);
        ushort? cbSize = input.fmtChunk.CbSize;
        if (cbSize.HasValue && cbSize.GetValueOrDefault() != 0 && input.factChunk != null)
        {
            _factChunkReader.Save(input.factChunk);
        }

        _dataChunkReader.Save(input.dataChunk);
    }

    public WaveForm Load()
    {
        WaveForm waveForm = new WaveForm();
        waveForm.riffChunk = _riffChunkReader.Load();
        waveForm.fmtChunk = _fmtChunkReader.Load();
        waveForm.fmtChunk.CheckFmtID("All");
        while (true)
        {
            switch (bufferReader.ReadString(4))
            {
                case "fact":
                    waveForm.factChunk = _factChunkReader.Load();
                    break;
                default:
                    bufferReader.Skip(bufferReader.ReadInt32());
                    break;
                case "data":
                    waveForm.dataChunk = _dataChunkReader.Load();
                    return waveForm;
            }
        }
    }

    public override void Init(ReaderResolver resolver)
    {
        base.Init(resolver);
        _riffChunkReader.Init(resolver);
        _chunkReader.Init(resolver);
        _dataChunkReader.Init(resolver);
        _fmtChunkReader.Init(resolver);
        _factChunkReader.Init(resolver);
    }

    public static void Save(byte[] data, string path, WaveBankFormats codec,
        int rate, int channels, int expBits, int align)
    {
        uint num = (uint)data.Length;
        WaveForm waveForm = new WaveForm();
        FmtChunk fmtChunk = waveForm.fmtChunk;
        waveForm.dataChunk.DataSize = num;
        waveForm.dataChunk.Data = data;
        waveForm.riffChunk.ChunkSize = 36 + waveForm.dataChunk.DataSize;
        WaveFormReader waveFormReader = new WaveFormReader();
        var writer = new BufferWriter((int)(8300 + num));
        waveFormReader.Init(new ReaderResolver
        {
            bufferWriter = writer
        });
        if (channels <= 0)
        {
            channels = 1;
        }

        switch (codec)
        {
            case WaveBankFormats.Pcm:
                fmtChunk.FmtTag = FmtChunk.AudioFormats.Pcm;
                fmtChunk.NumChannels = (ushort)channels;
                fmtChunk.SampleRate = (uint)rate;
                fmtChunk.BitsPerSample = (ushort)(8 << expBits);
                fmtChunk.BlockAlign = (ushort)(fmtChunk.BitsPerSample / 8 * fmtChunk.NumChannels);
                fmtChunk.ByteRate = fmtChunk.SampleRate * fmtChunk.BlockAlign;
                break;
            case WaveBankFormats.AdpcmMs:
            {
                fmtChunk.FmtTag = FmtChunk.AudioFormats.AdpcmMs;
                fmtChunk.NumChannels = (ushort)channels;
                fmtChunk.SampleRate = (uint)rate;
                fmtChunk.BitsPerSample = 4;
                fmtChunk.BlockAlign = (ushort)((align + 22) * fmtChunk.NumChannels);
                uint num2 = (uint)((fmtChunk.BlockAlign - 7 * fmtChunk.NumChannels) * 8 /
                    (fmtChunk.BitsPerSample * fmtChunk.NumChannels) + 2);
                fmtChunk.ByteRate = fmtChunk.SampleRate / num2 * fmtChunk.BlockAlign;
                fmtChunk.SetAdpcmMsExtensionChunk((ushort)num2);
                fmtChunk.FmtSize += fmtChunk.ExtensionChunkSize;
                uint dataFactSize = 0u;
                if (fmtChunk != null && fmtChunk.BlockAlign > 0 && fmtChunk.NumChannels > 0)
                {
                    dataFactSize =
                        (uint)((fmtChunk.BlockAlign - 7 * fmtChunk.NumChannels) * 8 / fmtChunk.BitsPerSample);
                    dataFactSize = num / fmtChunk.BlockAlign * dataFactSize;
                    dataFactSize /= fmtChunk.NumChannels;
                }

                waveForm.factChunk = new FactChunk
                {
                    DataFactSize = dataFactSize
                };
                waveForm.riffChunk.ChunkSize += fmtChunk.ExtensionChunkSize + 12;
                break;
            }
            case WaveBankFormats.Wma:
                throw new NotImplementedException();
            case WaveBankFormats.Xma:
                throw new NotImplementedException();
            default:
                throw new NotImplementedException();
        }

        waveFormReader.Save(waveForm);

        Convert(path, writer.Buffer, writer.BytePosition);//, FmtChunk.AudioFormats.Pcm

        waveFormReader.Dispose();
    }
    
    public static bool Convert(string outputPath, byte[] buffer, int? pos = null, FmtChunk.AudioFormats? toFormat = null)
    {
        //TOOL NotImplementedException
        pos ??= buffer.Length;
        // try
        // {
        //     using var ms = new MemoryStream(buffer, 0, pos.Value);
        //     using var reader = new WaveFileReader(ms);
        //     WaveFormat format;
        //
        //     switch (toFormat)
        //     {
        //         case FmtChunk.AudioFormats.Pcm:
        //             // 将输入音频转换为 16 位 PCM
        //             format = new WaveFormat(44100, reader.WaveFormat.BitsPerSample, reader.WaveFormat.Channels);
        //
        //             break;
        //         case FmtChunk.AudioFormats.AdpcmMs:
        //             format = new WaveFormat(44100, reader.WaveFormat.BitsPerSample, reader.WaveFormat.Channels);
        //
        //             break;
        //         case FmtChunk.AudioFormats.Ima4:
        //             format = new WaveFormat(44100, reader.WaveFormat.BitsPerSample, reader.WaveFormat.Channels);
        //             break;
        //         default:
                    using (var fileStream = new FileStream(outputPath, FileMode.Create))
                    {
                        fileStream.Write(buffer, 0, pos.Value);
                    }

                     return true;
        //     }
        //
        //     using var conversionStream = new WaveFormatConversionStream(format, reader);
        //     WaveFileWriter.CreateWaveFile(outputPath, conversionStream);
        //     return true;
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"转换过程中出现错误: {ex.Message}");
        //     return false;
        // }
    }


    public void xma2_header(int freq, int chans, int bits, int rawlen, string seek, int seeklen, int samples)
    {
        if (freq <= 0)
        {
            freq = 44100;
        }

        if (chans <= 0)
        {
            chans = 1;
        }

        if (bits <= 0)
        {
            bits = 16;
        }

        var xma2WaveFormAtex = new XmaHeader.Xma2WaveFormAtex
        {
            wfx =
            {
                wFormatTag = 358,
                nChannels = (ushort)chans,
                nSamplesPerSec = (uint)freq,
                nAvgBytesPerSec = (uint)rawlen,
                nBlockAlign = 4,
                wBitsPerSample = (ushort)bits,
                cbSize = 34
            },
            NumStreams = 1,
            ChannelMask = xma_quick_mask((ushort)chans),
            SamplesEncoded = (uint)samples,
            BytesPerBlock = 65536u,
            PlayBegin = 0u,
            PlayLength = (uint)samples,
            LoopBegin = 0u,
            LoopLength = 0u,
            LoopCount = 0,
            EncoderVersion = 3,
            BlockCount = 1
        };
        RIFFChunk rIffChunk = new RIFFChunk
        {
            ChunkSize = (uint)(rawlen + seeklen)
        };
        List<Chunk> list = new List<Chunk>
        {
            new()
            {
                Id = "fmt ",
                Size = rIffChunk.ChunkSize
            },
            new()
            {
                Id = "seek",
                Size = (uint)seeklen
            },
            new()
            {
                Id = "data",
                Size = (uint)rawlen
            }
        };
        _riffChunkReader.Save(rIffChunk);
        _chunkReader.Save(list[0]);
        _chunkReader.Save(list[1]);
        _chunkReader.Save(list[2]);
    }

    public uint xma_quick_mask(uint chans)
    {
        uint num = 0u;
        for (int i = 0; i < chans; i++)
        {
            num |= (uint)(1 << i);
        }

        return num;
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override object Read()
    {
        throw new NotImplementedException();
    }

    public override void Write(object input)
    {
        throw new NotImplementedException();
    }
}