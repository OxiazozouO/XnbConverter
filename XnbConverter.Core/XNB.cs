using System;
using System.Collections.Generic;
using System.IO;
using LZ4PCL;
using Newtonsoft.Json;
using XnbConverter.Configurations;
using XnbConverter.Entity.Mono;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact;

namespace XnbConverter;

public class XNB : IDisposable
{
    [Flags]
    public enum CompressedMasks : byte
    {
        Hidef = 1,
        Lz4 = 0x40,
        Lzx = 0x80
    }

    public enum TargetTags : byte
    {
        Windows = 119,
        WindowsPhone7 = 109,
        Xbox360 = 120,
        Android = 97,
        Ios = 105,
        Linux = 108,
        MacOSX = 88
    }

    public class XnbObject
    {
        public class HeaderDto
        {
            public TargetTags Target;

            public byte FormatVersion;

            public CompressedMasks CompressedFlag;
        }

        public class ReadersDto
        {
            public string? Type;

            public uint Version;
        }

        public class ContentDto
        {
            public string Extension;

            public int Format;
        }

        public ContentDto Content;

        public HeaderDto Header;

        public List<ReadersDto> Readers = new List<ReadersDto>();

        public int JsonSize()
        {
            int num = 0;
            for (int i = 0; i < Readers.Count; i++)
            {
                num += 1 + Readers[i].Type.Length + 1 + 1;
            }

            return num + 100;
        }
    }

    public static class Ext
    {
        public const string DEF = ".bin";

        public const string JSON = ".json";

        public const string TEXTURE_2D = ".png";

        public const string EFFECT = ".cso";

        public const string TBIN = ".tbin";

        public const string BM_FONT = ".xml";

        public const string SPRITE_FONT = ".json .png";

        public const string SOUND_EFFECT = ".json .wav";
    }

    public const int XnbCompressedPrologueSize = 14;

    public const int FileSizeIndex = 6;

    public const int ContentOriginalSizeIndex = 10;

    private BufferReader? bufferReader;

    private BufferWriter? bufferWriter;

    public object? Data;

    private int fileSize;

    public XnbObject XnbConfig = new XnbObject();

    private bool Hidef;

    private bool Lz4;

    private bool Lzx;

    public void Dispose()
    {
        bufferReader?.Dispose();
        bufferWriter?.Dispose();
    }

    private void _validateHeader(BufferReader bufferReader)
    {
        if (bufferReader == null)
        {
            throw new XnbError(Error.XNB_1);
        }

        string text = bufferReader.ReadString(3);
        if (text != "XNB")
        {
            throw new XnbError(Error.XNB_2, text);
        }

        Logger.Debug(Error.XNB_14);
        TargetTags targetTags = (TargetTags)bufferReader.ReadString(1).ToLower().ToCharArray()[0];
        if (Enum.IsDefined(typeof(TargetTags), targetTags))
        {
            Logger.Debug(Error.XNB_15, targetTags.ToString());
        }
        else
        {
            Logger.Warn(Error.XNB_23, (char)targetTags);
        }

        byte b = bufferReader.ReadByte();
        if ((uint)(b - 3) <= 2u)
        {
            Logger.Debug(Error.XNB_16, b % 3);
        }
        else
        {
            Logger.Warn(Error.XNB_24, b);
        }

        CompressedMasks compressedFlag = (CompressedMasks)bufferReader.ReadByte();
        XnbConfig.Header = new XnbObject.HeaderDto
        {
            Target = targetTags,
            FormatVersion = b,
            CompressedFlag = compressedFlag
        };
        _AnalysisFlag();
    }

    private void _AnalysisFlag()
    {
        CompressedMasks compressedFlag = XnbConfig.Header.CompressedFlag;
        Hidef = (compressedFlag & CompressedMasks.Hidef) != 0;
        Lzx = (compressedFlag & CompressedMasks.Lzx) != 0;
        Lz4 = (compressedFlag & CompressedMasks.Lz4) != 0;
        Logger.Debug(Error.XNB_17, compressedFlag.ToString());
    }

    public void Decode(string inputPath)
    {
        XnbObject xnbConfig = XnbConfig;
        Logger.Info(Error.XNB_10, inputPath);
        bufferReader = BufferReader.FormXnbFile(inputPath);
        _validateHeader(bufferReader);
        Logger.Info(Error.XNB_11);
        uint num = bufferReader.ReadUInt32();
        if (bufferReader.Size != num)
        {
            throw new XnbError(Error.XNB_3);
        }

        Logger.Debug(Error.XNB_18, num);
        if (Lz4 || Lzx)
        {
            int num2 = (int)bufferReader.ReadUInt32();
            Logger.Debug(Error.XNB_19, num2);
            if (Lzx)
            {
                int compressedTodo = (int)(num - 14);
                Utilities.Lzx.Decompress(bufferReader, compressedTodo, num2);
            }
            else if (Lz4)
            {
                byte[] subArray = bufferReader.Buffer[14..bufferReader.Size];
                LZ4Codec.Decode(subArray, 0, subArray.Length, bufferReader.Buffer, 14, num2);
            }

            bufferReader.BytePosition = 14;
        }

        Logger.Debug(Error.XNB_20, bufferReader.BytePosition);
        int num3 = bufferReader.Read7BitNumber();
        Logger.Debug(Error.XNB_21, num3);
        BaseReader[] array = new BaseReader[num3];
        List<string> list = new List<string>(num3 * 2);
        List<int> list2 = new List<int>(num3 * 2);
        for (int i = 0; i < num3; i++)
        {
            string text = Readers.Base.StringReader.ReadValueBy7Bit(bufferReader);
            uint version = bufferReader.ReadUInt32();
            TypeReadHelper.ReaderInfo readerInfo = TypeReadHelper.GetReaderInfo(text);
            array[i] = readerInfo.Reader.CreateReader();
            list.Add(readerInfo.Reader.ToString());
            list.Add(readerInfo.Entity.ToString());
            list2.Add(i);
            list2.Add(i);
            xnbConfig.Readers.Add(new XnbObject.ReadersDto
            {
                Type = text,
                Version = version
            });
        }

        xnbConfig.Content = new XnbObject.ContentDto
        {
            Extension = TypeReadHelper.GetExtension(xnbConfig.Readers[0].Type)
        };
        int num4 = bufferReader.Read7BitNumber();
        Logger.Debug(Error.XNB_22, num4);
        if (num4 != 0)
        {
            throw new XnbError(Error.XNB_4, num4);
        }

        ReaderResolver readerResolver = new ReaderResolver(array, bufferReader, list, list2);
        Data = readerResolver.Read(0);
        Logger.Info(Error.XNB_12);
    }

    public bool ExportFiles(string filename)
    {
        string directoryName = Path.GetDirectoryName(filename);
        if (!Directory.Exists(directoryName))
        {
            Directory.CreateDirectory(directoryName);
        }

        if (XnbConfig == null || XnbConfig.Content == null)
        {
            throw new XnbError(Error.XNB_5);
        }

        XnbObject.ContentDto content = XnbConfig.Content;
        if (Data != null)
        {
            if (content == null || Data == null)
            {
                throw new XnbError(Error.XNB_6);
            }

            Logger.Info(Error.XNB_13, content.Extension);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            string[] array = content.Extension.Split(' ');
            string[] array2 = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                array2[i] = Path.Combine(directoryName, fileNameWithoutExtension + array[i]);
            }

            switch (content.Extension)
            {
                case ".png":
                {
                    Texture2D texture2D = (Texture2D)Data;
                    texture2D.SaveAsPng(array2[0]);
                    content.Format = texture2D.Format;
                    break;
                }
                case ".json":
                    Data.ToJson(array2[0]);
                    content.Format = 0;
                    break;
                case ".tbin":
                {
                    TBin10 tBin = (TBin10)Data;
                    File.WriteAllBytes(array2[0], tBin.Data);
                    break;
                }
                case ".json .png":
                {
                    SpriteFont spriteFont = (SpriteFont)Data;
                    content.Format = spriteFont.Texture.Format;
                    spriteFont.Save(array2[0], array2[1]);
                    break;
                }
                case ".cso":
                    File.WriteAllBytes(array2[0], ((Effect)Data).Data);
                    break;
                case ".xml":
                    File.WriteAllText(array2[0], ((XmlSource)Data).Data);
                    break;
                case ".json .wav":
                    ((SoundEffect)Data).Save(array2[0], array2[1]);
                    break;
            }
        }

        XnbConfig.ToJson(filename);
        return true;
    }

    public void Encode(string path)
    {
        XnbObject xnbConfig = XnbConfig;
        object obj = Data;
        try
        {
            TargetTags target = xnbConfig.Header.Target;
            byte formatVersion = xnbConfig.Header.FormatVersion;
            _AnalysisFlag();
            bool flag = ((target == TargetTags.Android || target == TargetTags.Ios) ? true : false);
            if (flag)
            {
                xnbConfig.Header.CompressedFlag |= CompressedMasks.Lz4;
                Lz4 = true;
            }
            else
            {
                xnbConfig.Header.CompressedFlag = (CompressedMasks)(Hidef ? 1u : 0u);
                Lz4 = false;
                Lzx = false;
            }

            BufferWriter bufferWriter = new BufferWriter(GetLen());
            char c = (char)target;
            bufferWriter.WriteAsciiString("XNB" + c);
            bufferWriter.WriteByte(formatVersion);
            bufferWriter.WriteByte((byte)xnbConfig.Header.CompressedFlag);
            bufferWriter.WriteUInt32(0u);
            if (flag)
            {
                bufferWriter.WriteUInt32(0u);
            }

            int count = xnbConfig.Readers.Count;
            bufferWriter.Write7BitNumber(count);
            BaseReader[] array = new BaseReader[count];
            List<string> list = new List<string>(count * 2);
            List<int> list2 = new List<int>(count * 2);
            for (int i = 0; i < count; i++)
            {
                XnbObject.ReadersDto readersDto = xnbConfig.Readers[i];
                TypeReadHelper.ReaderInfo readerInfo = TypeReadHelper.GetReaderInfo(readersDto.Type);
                array[i] = readerInfo.Reader.CreateReader();
                list.Add(readerInfo.Reader.ToString());
                list.Add(readerInfo.Entity.ToString());
                list2.Add(i);
                list2.Add(i);
                XnbConverter.Readers.Base.StringReader.WriteValueBy7Bit(bufferWriter, readersDto.Type);
                bufferWriter.WriteUInt32(readersDto.Version);
            }

            if (xnbConfig.Content.Extension == ".json")
            {
                Type resultType = TypeReadHelper.GetResultType(xnbConfig.Readers[0].Type);
                obj = JsonConvert.DeserializeObject((string)obj, resultType, FileUtils.Settings);
            }

            bufferWriter.Write7BitNumber(0);
            new ReaderResolver(array, bufferWriter, list, list2).Write(0, obj);
            if ((Lzx || Lz4) && !Lzx && Lz4)
            {
                int num = bufferWriter.BytePosition - 14;
                byte[] array2 = new byte[LZ4Codec.MaximumOutputLength(num)];
                int num2 = LZ4Codec.Encode(bufferWriter.Buffer, 14, num, array2, 0, num);
                int num3 = 14 + num2;
                bufferWriter.WriteUInt32((uint)num, 10);
                bufferWriter.WriteUInt32((uint)num3, 6);
                array2.AsSpan(0, num2).CopyTo(bufferWriter.Buffer.AsSpan(14, num2));
                bufferWriter.BytePosition = num3;
            }
            else
            {
                bufferWriter.WriteUInt32((uint)bufferWriter.BytePosition, 6);
            }

            bufferWriter.SaveBufferToFile(path);
        }
        catch (Exception ex)
        {
            throw new XnbError(Error.XNB_7, ex.Message);
        }
    }

    public void ImportFiles(string filename)
    {
        XnbConfig = filename.ToEntity<XnbObject>();
        if (XnbConfig == null)
        {
            throw new XnbError(Error.XNB_8, filename);
        }

        if (XnbConfig.Content == null)
        {
            throw new XnbError(Error.XNB_9, filename);
        }

        XnbObject.ContentDto content = XnbConfig.Content;
        string[] array = content.Extension.Split(' ');
        string[] array2 = new string[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array2[i] = Path.ChangeExtension(filename, array[i]);
        }

        string extension = content.Extension;
        if (extension == null)
        {
            return;
        }

        switch (extension.Length)
        {
            case 4:
                switch (extension[1])
                {
                    case 'p':
                        if (extension == ".png")
                        {
                            Texture2D texture2D = Texture2D.FromPng(array2[0]);
                            texture2D.Format = XnbConfig.Content.Format;
                            Data = texture2D;
                        }

                        break;
                    case 'c':
                        if (extension == ".cso")
                        {
                            Data = new Effect
                            {
                                Data = File.ReadAllBytes(array2[0])
                            };
                        }

                        break;
                    case 'x':
                        if (extension == ".xml")
                        {
                            Data = new XmlSource
                            {
                                Data = File.ReadAllText(array2[0])
                            };
                        }

                        break;
                }

                break;
            case 5:
                switch (extension[1])
                {
                    case 't':
                        if (extension == ".tbin")
                        {
                            byte[] data2 = File.ReadAllBytes(array2[0]);
                            TBin10Reader.RemoveTileSheetsExtension(ref data2);
                            TBin10 data3 = new TBin10
                            {
                                Data = data2
                            };
                            Data = data3;
                        }

                        break;
                    case 'j':
                        if (extension == ".json")
                        {
                            Data = File.ReadAllText(array2[0]);
                        }

                        break;
                }

                break;
            case 10:
                switch (extension[7])
                {
                    case 'p':
                        if (extension == ".json .png")
                        {
                            SpriteFont spriteFont = SpriteFont.FormFiles(array2[0], array2[1]);
                            spriteFont.Texture.Format = XnbConfig.Content.Format;
                            Data = spriteFont;
                        }

                        break;
                    case 'w':
                        if (extension == ".json .wav")
                        {
                            SoundEffect data = SoundEffect.FormWave(array2[0], array2[1]);
                            Data = data;
                        }

                        break;
                }

                break;
        }
    }

    private int GetLen()
    {
        int num = XnbConfig.JsonSize();
        object data = Data;
        if (!(data is Texture2D texture2D))
        {
            if (!(data is SpriteFont spriteFont))
            {
                if (!(data is string text))
                {
                    if (!(data is Effect effect))
                    {
                        if (data is XmlSource xmlSource)
                        {
                            return num + (xmlSource.Data.Length + 200);
                        }

                        return 10485760;
                    }

                    return num + effect.Data.Length;
                }

                return num + (int)((double)text.Length * 3.5);
            }

            return num + (int)((double)spriteFont.Texture.Data.Length * 1.2);
        }

        return num + texture2D.Data.Length;
    }
}