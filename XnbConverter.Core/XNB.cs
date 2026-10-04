using XnbConverter.Utilities.LZ4;
using Newtonsoft.Json;
using XnbConverter.Configurations;
using XnbConverter.Entity.Mono;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact;

namespace XnbConverter;

public class XNB : IDisposable
{
    // ==== 临时探针，量完就删 ====
    public static readonly bool ProbeOn = Environment.GetEnvironmentVariable("XNB_PROBE") != null;

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

            /// <summary>
            /// 贴图未补齐的内容尺寸（见 <see cref="Texture2D.ContentWidth"/>）。
            /// 安卓版才有值，PC 版为 0；导出时记下来，打包时原样写回。
            /// </summary>
            public int ContentWidth;

            public int ContentHeight;

            /// <summary>
            /// Effect 专用。只存「从 .fx 推不出来」的那部分：外壳（容器结构）和源码插入位置。
            /// 着色器本身不在 config 里 —— 它就是同名的 .fx 文件，打包时从那儿反译回 GLSL。
            /// 空表示没拆成，此时 <c>.cso</c> 存的是完整原始字节。
            /// </summary>
            public int ShaderOffset;

            /// <summary>挖掉着色器源码之后的 MGFX 外壳（base64）：头部、采样器表、常量缓冲表、属性表、技术/通道。这是唯一必需的一坨「魔法值」。</summary>
            public string? ShaderShell;
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
                LZ4Codec.Decode32(subArray, 0, subArray.Length, bufferReader.Buffer, 14, num2, true);
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

        ReaderResolver resolver = new ReaderResolver(array, bufferReader, list, list2);
        Data = resolver.Read(0);
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
                    content.ContentWidth = texture2D.ContentWidth;
                    content.ContentHeight = texture2D.ContentHeight;
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
                    // SpriteFont 与 DynamicSpriteFont 共用同一组扩展名，按实际数据类型分流
                    if (Data is SpriteFont spriteFont)
                    {
                        content.Format = spriteFont.Texture.Format;
                        spriteFont.Save(array2[0], array2[1]);
                        break;
                    }

                    DynamicSpriteFont dynamicSpriteFont = (DynamicSpriteFont)Data;
                    content.Format = dynamicSpriteFont.Pages.Count > 0
                        ? dynamicSpriteFont.Pages[0].Texture!.Format
                        : 0;
                    dynamicSpriteFont.Save(array2[0], array2[1]);
                    break;
                }
                case ".fx .cso":
                {
                    byte[] mgfx = ((Effect)Data).Data;
                    // 能拆：把着色器源码还原成 HLSL 效果源码（.fx），
                    //       外壳和原始 GLSL 一起内嵌进 .config —— 解包只多出一个文件。
                    // 拆不动（DX 字节码、格式不认得）：原样导出整个 MGFX，不做还原。
                    if (Utilities.Mgfx.TrySplit(mgfx, out byte[] shell, out int shaderOffset, out byte[] shader))
                    {
                        File.WriteAllText(array2[0], Utilities.FxReconstruct.Build(
                            System.Text.Encoding.UTF8.GetString(shader),
                            Utilities.Mgfx.ReadableNames(shell)));
                        content.ShaderOffset = shaderOffset;
                        content.ShaderShell = Convert.ToBase64String(shell);
                    }
                    else
                    {
                        File.WriteAllBytes(array2[1], mgfx);
                        content.ShaderOffset = 0;
                        content.ShaderShell = null;
                    }

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
            bool flag = target == TargetTags.Android || target == TargetTags.Ios ? true : false;
            // 输入原本是 LZX 压缩时，非 Android/iOS 目标也沿用 LZX 重新打包
            bool lzx = !flag && Lzx;
            if (flag)
            {
                xnbConfig.Header.CompressedFlag |= CompressedMasks.Lz4;
                Lz4 = true;
            }
            else
            {
                xnbConfig.Header.CompressedFlag = (CompressedMasks)(Hidef ? 1u : 0u)
                    | (lzx ? CompressedMasks.Lzx : 0);
                Lz4 = false;
                Lzx = lzx;
            }

            // 存到字段上而不是只用局部变量：只有这样 Dispose() 才能把它还给池。
            // 之前这里每次打包都新借一块和载荷同量级的缓冲、且从不归还，是写出端最大的一笔分配。
            // 实测（LooseSprites 26 个文件）：写出阶段分配 43 MB → 17 MB。
            //
            // 尺寸必须严格等于 GetLen()，不能随手加余量：LZ4HC 的 chainTable 是按
            // 「绝对指针 & 0xFFFF」索引的（见 LZ4Codec.Unsafe32HC.Dirty.cs），所以压缩结果
            // 依赖源缓冲落在哪个地址上；而地址由 ArrayPool 的分配顺序决定，改尺寸会改变
            // 分配顺序，进而让重打包的字节流对不上原始文件。
            bufferWriter = new BufferWriter(GetLen());
            BufferWriter writer = bufferWriter;
            char c = (char)target;
            writer.WriteAsciiString("XNB" + c);
            writer.WriteByte(formatVersion);
            writer.WriteByte((byte)xnbConfig.Header.CompressedFlag);
            writer.WriteUInt32(0u);
            if (flag || lzx)
            {
                writer.WriteUInt32(0u);
            }

            int count = xnbConfig.Readers.Count;
            writer.Write7BitNumber(count);
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
                XnbConverter.Readers.Base.StringReader.WriteValueBy7Bit(writer, readersDto.Type);
                writer.WriteUInt32(readersDto.Version);
            }

            if (xnbConfig.Content.Extension == ".json")
            {
                Type resultType = TypeReadHelper.GetResultType(xnbConfig.Readers[0].Type);
                obj = JsonConvert.DeserializeObject((string)obj, resultType, FileUtils.Settings);
            }

            long tProbe = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            writer.Write7BitNumber(0);
            new ReaderResolver(array, writer, list, list2).Write(0, obj);
            if (ProbeOn)
            {
                Console.Error.WriteLine($"CONTENT {System.Diagnostics.Stopwatch.GetTimestamp() - tProbe} {writer.BytePosition}");
            }

            if ((Lzx || Lz4) && !Lzx && Lz4)
            {
                long tProbe2 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                int num = writer.BytePosition - 14;
                int outCap = LZ4Codec.MaximumOutputLength(num);
                // 与 MonoGame 管线一致：用高压缩档 Encode32HC，且输出上限传 MaximumOutputLength。
                // 换成这一档之后，重打包的 LZ4 字节流才和原始 XNB 完全一致。
                //
                // 这里**不能**让源和目标是同一块缓冲（试过，见 git 历史）：LZ4 一个序列的
                // 写指针虽然是逐字节跟在读指针后面的，但「偏移 + 匹配长度」这几个字节是在拷完
                // 字面量之后才写的，落点等于 src_p + (字面量长度 − 余量)。只要字面量段长于余量，
                // 就会写进尚未读取的输入 —— 而字面量段最长可以等于整份载荷，所以任何有界的
                // 错位余量都堵不住，LZ4 官方也因此不支持 in-place。
                // 输出缓冲走池：它和载荷同量级，且每个文件一块
                byte[] array2 = Pool.RentByte(outCap);
                int num2 = LZ4Codec.Encode32HC(writer.Buffer, 14, num, array2, 0, outCap);
                int num3 = 14 + num2;
                writer.WriteUInt32((uint)num, 10);
                writer.WriteUInt32((uint)num3, 6);
                array2.AsSpan(0, num2).CopyTo(writer.Buffer.AsSpan(14, num2));
                writer.BytePosition = num3;
                Pool.Return(array2);
                if (ProbeOn)
                {
                    Console.Error.WriteLine($"LZ4 {System.Diagnostics.Stopwatch.GetTimestamp() - tProbe2} {num}");
                }
            }
            else if (Lzx)
            {
                // LZX：整段载荷交给压缩器，帧与块结构与原始 XNB 一致
                int num = writer.BytePosition - 14;
                byte[] array2 = LzxCompressor.Compress(writer.Buffer, 14, num);
                int num2 = 14 + array2.Length;
                writer.WriteUInt32((uint)num, 10);
                writer.WriteUInt32((uint)num2, 6);
                array2.AsSpan(0, array2.Length).CopyTo(writer.Buffer.AsSpan(14, array2.Length));
                writer.BytePosition = num2;
            }
            else
            {
                writer.WriteUInt32((uint)writer.BytePosition, 6);
            }

            long tProbe3 = ProbeOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            writer.SaveBufferToFile(path);
            if (ProbeOn)
            {
                Console.Error.WriteLine($"SAVE {System.Diagnostics.Stopwatch.GetTimestamp() - tProbe3} {writer.BytePosition}");
            }
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

        switch (extension)
        {
            case ".png":
                Texture2D texture2D = Texture2D.FromPng(array2[0]);
                texture2D.Format = XnbConfig.Content.Format;
                texture2D.ContentWidth = XnbConfig.Content.ContentWidth;
                texture2D.ContentHeight = XnbConfig.Content.ContentHeight;
                Data = texture2D;
                break;
            case ".fx .cso":
            {
                if (!string.IsNullOrEmpty(content.ShaderShell) && File.Exists(array2[0]))
                {
                    // .fx 就是着色器本体：反译回 GLSL，按 ShaderOffset 插进外壳。
                    byte[] glsl = System.Text.Encoding.UTF8.GetBytes(
                        Utilities.FxReconstruct.ToGlsl(File.ReadAllText(array2[0])));

                    try
                    {
                        Data = new Effect
                        {
                            Data = Utilities.Mgfx.Join(Convert.FromBase64String(content.ShaderShell),
                                content.ShaderOffset, glsl),
                        };
                    }
                    catch (Exception ex)
                    {
                        throw new XnbError(Error.XNB_7, ex.Message);
                    }
                }
                else
                {
                    // 没拆成：.cso 里就是完整字节
                    Data = new Effect { Data = File.ReadAllBytes(array2[1]) };
                }

                break;
            }

            case ".cso":
                Data = new Effect { Data = File.ReadAllBytes(array2[0]) };
                break;
            case ".xml":
                Data = new XmlSource { Data = File.ReadAllText(array2[0]) };
                break;
            case ".tbin":
                byte[] data2 = File.ReadAllBytes(array2[0]);
                TBin10Reader.RemoveTileSheetsExtension(ref data2);
                TBin10 data3 = new TBin10 { Data = data2 };
                Data = data3;
                break;
            case ".json":
                Data = File.ReadAllText(array2[0]);
                break;
            case ".json .png":
                if (XnbConfig.Readers[0].Type.Contains("DynamicSpriteFont"))
                {
                    Data = DynamicSpriteFont.FormFiles(array2[0], array2[1]);
                    break;
                }

                SpriteFont spriteFont = SpriteFont.FormFiles(array2[0], array2[1]);
                spriteFont.Texture.Format = XnbConfig.Content.Format;
                Data = spriteFont;
                break;
            case ".json .wav":
                SoundEffect data = SoundEffect.FormWave(array2[0], array2[1]);
                Data = data;
                break;
        }
    }

    private int GetLen()
    {
        int num = XnbConfig.JsonSize();
        return Data switch
        {
            Texture2D texture2D => num + texture2D.Data.Length,
            SpriteFont spriteFont => num + (int)((double)spriteFont.Texture.Data.Length * 1.2),
            DynamicSpriteFont dynamicSpriteFont => num + dynamicSpriteFont.Pages.Sum(page => (int)((double)(page.Texture?.Data.Length ?? 0) * 1.2)) + 4096,
            string text => num + (int)((double)text.Length * 3.5),
            Effect effect => num + effect.Data.Length,
            XmlSource xmlSource => num + xmlSource.Data.Length + 200,
            _ => Pool.LongSize
        };
    }
}