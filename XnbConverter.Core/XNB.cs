using System;
using System.Text;
using Newtonsoft.Json;
using XnbConverter.Entity;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Utilities.LZX;
using static XnbConverter.Entity.XnbObject.TargetTags;
using LZ4 = LZ4PCL.LZ4Codec;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter;

/// <summary>
///     用于读取和写入XNB文件的XNB文件类
/// </summary>
public class XNB : IDisposable
{
    // 由XNB文件使用的读取器数组。
    private BufferReader bufferReader;
    private BufferWriter bufferWriter;
    public object? Data;

    // 格式版本
    private byte FormatVersion;

    // HiDef标志
    private bool Hidef;

    private bool Lz4;

    // 压缩标志
    // 压缩类型
    private bool Lzx;

    // 目标平台
    private XnbObject.TargetTags Target;

    //Xnb配置文件
    public XnbObject? XnbConfig;

    public void Dispose()
    {
        bufferReader?.Dispose();
        bufferWriter?.Dispose();
    }

    /**
     * 将文件加载到XNB类中。
     * @param {String} filename 要加载的XNB文件。
     */
    public void Load(string inputPath)
    {
        XnbConfig = new XnbObject();
        var json = XnbConfig;
        Log.Info(Helpers.I18N["XNB.6"], inputPath);

        // XNB缓冲区读取器
        bufferReader = BufferReader.FormXnbFile(inputPath);
        // Console.WriteLine(XnbObject.CompressedMask.Lzx.ToString());

        //验证XNB文件头
        var flags = _validateHeader(bufferReader);

        // 我们成功验证了该文件
        Log.Info(Helpers.I18N["XNB.7"]);
        // 读取文件大小
        // 文件大小
        var fileSize = bufferReader.ReadUInt32();
        // 验证文件大小
        if (bufferReader.Size != fileSize)
            throw new XnbError(Helpers.I18N["XNB.1"]);

        // 打印文件大小
        Log.Debug(Helpers.I18N["XNB.9"], fileSize);
        // 如果文件被压缩，则需要解压缩
        if (Lz4 || Lzx)
        {
            // 获取解压缩后的大小
            var decompressedSize = (int)bufferReader.ReadUInt32();
            Log.Debug(Helpers.I18N["XNB.10"], decompressedSize);
            // 解压缩LZX格式
            if (Lzx)
            {
                // 获取需要压缩的数据量
                var compressedTodo = (int)fileSize - XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE;
                // 根据文件大小解压缩缓冲区
                LZX.Decompress(bufferReader, compressedTodo, decompressedSize);
            }
            // 解压缩LZ4格式
            else if (Lz4)
            {
                // 为LZ4解码分配缓冲区
                var trimmed = bufferReader.Buffer[XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE..bufferReader.Size];
                // 将修剪后的缓冲区解码到解压缩缓冲区
                LZ4.Decode(trimmed, 0, trimmed.Length,
                    bufferReader.Buffer, XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE, decompressedSize);
            }

            // 重置字节位置以读取内容
            bufferReader.BytePosition = XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE;
        }

        Log.Debug(Helpers.I18N["XNB.11"], bufferReader.BytePosition);

        // 注意：假设缓冲区现在已解压缩

        // 获取读取器的7位值
        var count = bufferReader.Read7BitNumber();
        // 记录读取器的数量
        Log.Debug(Helpers.I18N["XNB.12"], count);

        // 用于导出的读取器的本地副本
        json.Header = new XnbObject.HeaderDTO
        {
            Target = Target,
            FormatVersion = FormatVersion,
            CompressedFlag = flags
        };

        // 创建StringReader的实例
        // 循环读取读取器的数量
        var sb = new StringBuilder();
        var readerArr = new BaseReader[count];
        List<string> typeList = new List<string>(count * 2);
        List<int> typeIndexList = new List<int>(count * 2);

        for (var i = 0; i < count; i++)
        {
            // 读取类型
            var type = StringReader.ReadValueBy7Bit(bufferReader);
            sb.Append(type).Append('\n');
            // 读取版本
            var version = bufferReader.ReadUInt32();
            // 获取此类型的读取器  并将读取器添加到列表中
            var info = TypeReadHelper.GetReaderInfo(type);
            readerArr[i] = info.Reader.CreateReader();

            typeList.Add(info.Reader.ToString());
            typeList.Add(info.Entity.ToString());
            typeIndexList.Add(i);
            typeIndexList.Add(i);
            // 添加本地读取器
            json.Readers.Add(new XnbObject.ReadersDTO { Type = type, Version = version });
        }

        json.Content = new ContentDTO
        {
            Extension = TypeReadHelper.GetExtension(json.Readers[0].Type)
        };

        // 获取共享资源的7位值
        var shared = bufferReader.Read7BitNumber();
        // 记录共享资源的数量
        Log.Debug(Helpers.I18N["XNB.13"], shared);

        // 不接受共享资源，因为SDV XNB文件没有共享资源
        if (shared != 0)
            throw new XnbError(Helpers.I18N["XNB.2"], shared);
        // sb.ToString().log();
        // 由已加载的读取器创建内容读取器 并读取内容
        var readerResolver = new ReaderResolver(readerArr, bufferReader, typeList, typeIndexList);
        Data = readerResolver.Read(0);
        // 成功加载XNB文件
        Log.Info(Helpers.I18N["XNB.8"]);
    }

    /**
     * 将JSON转换为XNB文件结构
     * @param {Object} json 要转换为XNB文件的JSON对象
     */
    public void Convert(string path)
    {
        var json = XnbConfig;
        var data = Data;
        // 捕获无效的JSON文件格式的异常
        try
        {
            // 设置头信息
            Target = json.Header.Target; // 目标平台
            FormatVersion = json.Header.FormatVersion; // 格式版本

            _AnalysisFlag(json.Header.CompressedFlag);
            // Hidef = json.Header.Hidef; // 高清标志
            // 如果是Android平台，则暂时写入LZ4解压缩大小
            var lz4Compression = Target is Android or Ios;
            // 是否启用压缩（根据目标平台和LZ4压缩标志判断） 
            if (lz4Compression)
            {
                //write the decompression size temporarily if android
                json.Header.CompressedFlag |= XnbObject.CompressedMasks.Lz4;
                Lz4 = true;
            }
            //其他无压缩
            else
            {
                json.Header.CompressedFlag = (XnbObject.CompressedMasks)(byte)(Hidef ? 1 : 0);
                Lz4 = false;
                Lzx = false;
            }

            // 用于存储文件的输出缓冲区
            var outBuffer = new BufferWriter();

            // 将头信息写入缓冲区
            // 文件标识  // 目标平台
            outBuffer.WriteAsciiString("XNB" + (char)(byte)Target);
            // 格式版本
            outBuffer.WriteByte(FormatVersion);
            outBuffer.WriteByte((byte)json.Header.CompressedFlag); // 高清标志和压缩标志

            // 写入临时文件大小
            outBuffer.WriteUInt32(0u);

            // write the decompression size temporarily if android
            if (lz4Compression)
                outBuffer.WriteUInt32(0u);

            // 写入读取器的数量
            int count = json.Readers.Count;
            outBuffer.Write7BitNumber(count);
            var readerArr = new BaseReader[count];
            List<string> typeList = new List<string>(count * 2);
            List<int> typeIndexList = new List<int>(count * 2);
            for (var i = 0; i < count; i++)
            {
                var reader = json.Readers[i];
                var info = TypeReadHelper.GetReaderInfo(reader.Type);
                readerArr[i] = info.Reader.CreateReader();

                typeList.Add(info.Reader.ToString());
                typeList.Add(info.Entity.ToString());
                typeIndexList.Add(i);
                typeIndexList.Add(i);

                StringReader.WriteValueBy7Bit(outBuffer, reader.Type);
                outBuffer.WriteUInt32(reader.Version);
            }

            if (json.Content.Extension == TypeReadHelper.Ext.JSON)
            {
                var t = TypeReadHelper.GetResultType(json.Readers[0].Type);
                data = JsonConvert.DeserializeObject((string)data, t, FileUtils.Settings);
            }

            // 写入0个共享资源
            outBuffer.Write7BitNumber(0);

            // 创建内容读取器并写入内容 并将内容写入读取器解析器
            new ReaderResolver(readerArr, outBuffer, typeList, typeIndexList).Write(0, data);

            if (Lzx || Lz4) // 文件需要压缩
            {
                // 压缩LZX格式
                if (Lzx)
                {
                }
                // 压缩LZ4格式
                else if (Lz4)
                {
                    // 原始内容大小
                    var contentSize = outBuffer.BytePosition - XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE;
                    // 创建用于存储压缩数据的缓冲区
                    var maximumLength = LZ4.MaximumOutputLength(contentSize);
                    var compressed = new byte[maximumLength];

                    // 将数据压缩到缓冲区中
                    var compressedSize = LZ4.Encode(outBuffer.Buffer, XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE,
                        contentSize,
                        compressed, 0, contentSize);
                    // 生成的xnb大小
                    var fileLen = XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE + compressedSize;

                    // 将解压缩大小写入缓冲区
                    outBuffer.WriteUInt32((uint)contentSize, XnbConstants.CONTENT_ORIGINAL_SIZE_INDEX);
                    // 将文件大小写入缓冲区
                    outBuffer.WriteUInt32((uint)fileLen, XnbConstants.FILE_SIZE_INDEX);

                    compressed.AsSpan(0, compressedSize) // 切掉多余的部分
                        .CopyTo(outBuffer.Buffer.AsSpan(XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE,
                            compressedSize)); // 将内容插入返回缓冲区
                    //切掉多余的部分
                    outBuffer.BytePosition = fileLen;
                    // 返回缓冲区
                    goto save;
                }
            }

            // 将文件大小写入缓冲区
            outBuffer.WriteUInt32((uint)outBuffer.BytePosition, XnbConstants.FILE_SIZE_INDEX);

            save:
            outBuffer.SaveBufferToFile(path);
        }
        catch (Exception ex)
        {
            throw new XnbError(Helpers.I18N["XNB.3"], ex.Message);
        }
    }

    /**
     * 确保XNB文件头部是有效的。
     * @private
     * @method _validateHeader
     */
    private XnbObject.CompressedMasks _validateHeader(BufferReader bufferReader)
    {
        // 确保缓冲区不为null
        if (bufferReader == null)
            throw new XnbError(Helpers.I18N["XNB.4"]);

        // 从文件开头获取魔术值
        var magic = bufferReader.ReadString(3);
        // 检查魔术值是否正确
        if (magic != "XNB")
            throw new XnbError(Helpers.I18N["XNB.5"], magic);

        // 调试打印找到有效的XNB魔术值
        Log.Debug(Helpers.I18N["XNB.14"]);

        // 加载目标平台
        Target = (XnbObject.TargetTags)(byte)bufferReader.ReadString(1).ToLower().ToCharArray()[0];

        // 读取目标平台
        if (Enum.IsDefined(typeof(XnbObject.TargetTags), Target))
            Log.Debug(Helpers.I18N["XNB.15"], Target.ToString());
        else
            Log.Warn(Helpers.I18N["XNB.18"], (char)Target);

        // 读取格式版本
        FormatVersion = bufferReader.ReadByte();

        // 读取XNB格式版本
        switch (FormatVersion)
        {
            case 3 or 4 or 5:
                Log.Debug(Helpers.I18N["XNB.16"], FormatVersion % 3);
                break;
            default:
                Log.Warn(Helpers.I18N["XNB.19"], FormatVersion);
                break;
        }

        // 读取标志位
        var flags = (XnbObject.CompressedMasks)bufferReader.ReadByte();
        _AnalysisFlag(flags);
        return flags;
    }

    private void _AnalysisFlag(XnbObject.CompressedMasks flags)
    {
        // 获取HiDef标志
        Hidef = (flags & XnbObject.CompressedMasks.Hidef) != 0;
        Lzx = (flags & XnbObject.CompressedMasks.Lzx) != 0;
        Lz4 = (flags & XnbObject.CompressedMasks.Lz4) != 0;
        // 打印压缩状态
        Log.Debug(Helpers.I18N["XNB.17"], flags.ToString());
    }

    // 用于掩码的常量
    public record struct XnbConstants
    {
        public const int XNB_COMPRESSED_PROLOGUE_SIZE = 14;

        public const int FILE_SIZE_INDEX = 6;
        public const int CONTENT_ORIGINAL_SIZE_INDEX = 10;
    }
}