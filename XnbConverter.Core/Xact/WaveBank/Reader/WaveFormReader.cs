using System;
using System.Collections.Generic;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;
using static XnbConverter.Xact.WaveBank.Entity.WaveBank;

namespace XnbConverter.Xact.WaveBank.Reader;

public class WaveFormReader : BaseReader, IReaderFileUtil<WaveForm>, IDisposable
{
    private const int AdpcmMiniWaveFormatBlockAlignConversionOffset = 22;
    private readonly ChunkReader chunkReader = new();
    private readonly DATAChunkReader dataChunkReader = new();
    private readonly FactChunkReader factChunkReader = new();
    private readonly FmtChunkReader fmtChunkReader = new();
    private readonly RIFFChunkReader riffChunkReader = new();

    public void Dispose()
    {
        bufferWriter?.Dispose();
        bufferReader?.Dispose();
    }

    public void Save(WaveForm input)
    {
        riffChunkReader.Save(input.riffChunk);
        fmtChunkReader.Save(input.fmtChunk); //16

        if (input.fmtChunk.CbSize is not null and not 0 && input.factChunk is not null)
            factChunkReader.Save(input.factChunk);

        dataChunkReader.Save(input.dataChunk); //8
        //44
    }

    public WaveForm Load()
    {
        var result = new WaveForm();
        result.riffChunk = riffChunkReader.Load();
        result.fmtChunk = fmtChunkReader.Load();
        result.fmtChunk.CheckFmtID("All");

        while (true)
        {
            var chunkId = bufferReader.ReadString(4);
            switch (chunkId)
            {
                case WaveMarks.data:
                    goto data;
                case WaveMarks.LIST:
                    if (1 == 2)
                    {
                        var str = chunkId + " ";
                        var s = bufferReader.ReadInt32();
                        var bites = new char[s];
                        if (s == 26)
                        {
                            str += bufferReader.ReadString(4) + " ";
                            str += bufferReader.ReadString(4) + " ";
                            var s2 = bufferReader.ReadUInt32(); //14
                            if (s2 != 14)
                                throw new Exception("LIST chunk size is too small");
                            str += bufferReader.ReadString((int)s2);
                            Log.Debug(str);
                            Console.WriteLine(str);
                        }
                    }

                    goto default;
                case WaveMarks.fact:
                    result.factChunk = factChunkReader.Load();
                    break;
                default:
                    bufferReader.Skip(bufferReader.ReadInt32());
                    break;
            }
        }

        data:
        result.dataChunk = dataChunkReader.Load();
        return result;
    }


    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        riffChunkReader.Init(readerResolver);
        chunkReader.Init(readerResolver);
        dataChunkReader.Init(readerResolver);
        fmtChunkReader.Init(readerResolver);
        factChunkReader.Init(readerResolver);
    }

    public static void Save(byte[] data, string path, WaveBankFormats codec, int rate, int channels, int expBits,
        int align)
    {
        var size = (uint)data.Length;
        var wave = new WaveForm();
        var fmtChunk = wave.fmtChunk;
        wave.dataChunk.DataSize = size;
        wave.dataChunk.Data = data;
        wave.riffChunk.ChunkSize = 36u + wave.dataChunk.DataSize;

        var waveFormReader = new WaveFormReader();
        waveFormReader.Init(new ReaderResolver
        {
            bufferWriter = new BufferWriter(8300 + (int)size)
        });

        if (channels <= 0) channels = 1; // useless?
        switch (codec)
        {
            case WaveBankFormats.Pcm:
            {
                fmtChunk.FmtTag = FmtChunk.AudioFormats.Pcm;
                fmtChunk.NumChannels = (ushort)channels;
                fmtChunk.SampleRate = (uint)rate;
                fmtChunk.BitsPerSample = (ushort)(8 << expBits);
                fmtChunk.BlockAlign = (ushort)(fmtChunk.BitsPerSample / 8 * fmtChunk.NumChannels);
                fmtChunk.ByteRate = fmtChunk.SampleRate * fmtChunk.BlockAlign;
                break;
            }
            case WaveBankFormats.AdpcmMs:
            {
                fmtChunk.FmtTag = FmtChunk.AudioFormats.AdpcmMs;
                fmtChunk.NumChannels = (ushort)channels;
                fmtChunk.SampleRate = (uint)rate;
                fmtChunk.BitsPerSample = 4;
                fmtChunk.BlockAlign =
                    (ushort)((align + AdpcmMiniWaveFormatBlockAlignConversionOffset) * fmtChunk.NumChannels);
                var dw = (uint)((fmtChunk.BlockAlign - 7 * fmtChunk.NumChannels) * 8 /
                    (fmtChunk.BitsPerSample * fmtChunk.NumChannels) + 2);
                fmtChunk.ByteRate = fmtChunk.SampleRate / dw * fmtChunk.BlockAlign;

                fmtChunk.SetAdpcmMsExtensionChunk((ushort)dw);

                fmtChunk.FmtSize += fmtChunk.ExtensionChunkSize;

                /*
                  http://download.microsoft.com/download/9/8/6/9863C72A-A3AA-4DDB-B1BA-CA8D17EFD2D4/RIFFNEW.pdf
                  Fact Chunk
                  该Chunk是所有非WAVE_FORMAT_PCM格式的WAVE文件所需的。
                  它存储了WAVE数据内容的文件依赖信息。
                  它目前指定数据的时长以样本表示。
                */
                uint dataFactSize = 0;
                if (fmtChunk is { BlockAlign: > 0, NumChannels: > 0 })
                {
                    dataFactSize =
                        (uint)((fmtChunk.BlockAlign - 7 * fmtChunk.NumChannels) * 8 / fmtChunk.BitsPerSample);
                    dataFactSize = size / fmtChunk.BlockAlign * dataFactSize;
                    dataFactSize /= fmtChunk.NumChannels;
                }

                wave.factChunk = new FactChunk
                {
                    DataFactSize = dataFactSize
                };
                wave.riffChunk.ChunkSize += fmtChunk.ExtensionChunkSize + FactChunk.Size;
                //8192+64+44=8300
                break;
            }
            case WaveBankFormats.Wma:
            {
                // WMA is ready to play
                throw new NotImplementedException();
                break;
            }
            case WaveBankFormats.Xma:
            {
                throw new NotImplementedException();
                waveFormReader.xma2_header(rate, channels, 16, (int)size, null, 0, 0); // samples?
                break;
            }
            default:
                throw new NotImplementedException();
                break;
        }

        waveFormReader.Save(wave);
        waveFormReader.bufferWriter.SaveBufferToFile(path);
        FFmpegUtil.Convert(path, format: FmtChunk.AudioFormats.Pcm);
        // FFmpegUtil.Convert(path);
        waveFormReader.Dispose();
    }

    /**
     * XMA2:
     * fmt
     * data
     * seek
     */
    public void xma2_header(int freq, int chans, int bits, int rawlen, string seek, int seeklen, int samples)
    {
        // throw new NotImplementedException();
        var fmt = new XmaHeader.Xma2WaveFormAtex();

        if (freq <= 0) freq = 44100;
        if (chans <= 0) chans = 1;
        if (bits <= 0) bits = 16;

        fmt.wfx.wFormatTag = 0x0166;
        fmt.wfx.nChannels = (ushort)chans;
        fmt.wfx.nSamplesPerSec = (uint)freq;
        fmt.wfx.nAvgBytesPerSec = (uint)rawlen; // used only by the encoder
        fmt.wfx.nBlockAlign = 4;
        fmt.wfx.wBitsPerSample = (ushort)bits;
        fmt.wfx.cbSize = 34;

        fmt.NumStreams = 1;
        fmt.ChannelMask = xma_quick_mask((ushort)chans);
        fmt.SamplesEncoded = (uint)samples;
        fmt.BytesPerBlock = 0x10000;
        fmt.PlayBegin = 0;
        fmt.PlayLength = (uint)samples;
        fmt.LoopBegin = 0;
        fmt.LoopLength = 0;
        fmt.LoopCount = 0;
        fmt.EncoderVersion = 3; // or 4
        fmt.BlockCount = 1;

        var riffChunk = new RIFFChunk
        {
            // Size = 4 + sizeof(mywav_chunk) // RIFF
            //       + sizeof(fmt) // fmt
            //       + sizeof(mywav_chunk) // data
            //       + sizeof(mywav_chunk); // seek

            ChunkSize = 0 + (uint)(rawlen + seeklen)
        };

        List<Chunk> chunks = new()
        {
            new Chunk { Id = WaveMarks.fmt, Size = riffChunk.ChunkSize },
            new Chunk { Id = WaveMarks.seek, Size = (uint)seeklen },
            new Chunk { Id = WaveMarks.data, Size = (uint)rawlen }
        };


        riffChunkReader.Save(riffChunk);

        chunkReader.Save(chunks[0]);
        chunkReader.Save(chunks[1]);
        // data must be placed at the end so that the main tool can write the data after it
        chunkReader.Save(chunks[2]);
    }

    public uint xma_quick_mask(uint chans)
    {
        uint result = 0;
        for (var i = 0; i < chans; i++)
            result |= (uint)1 << i;
        return result;
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