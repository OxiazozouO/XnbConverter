using Squish;
using XnbConverter.Configurations;
using XnbConverter.Entity.Mono;
using XnbConverter.Exceptions;
using XnbConverter.Utilities;

namespace XnbConverter.Readers.Mono;

public class Texture2DReader : BaseReader
{
	/// <summary>
	/// 预乘/反预乘只有 256×256 种组合（通道值 × alpha），而且每像素都要算一遍浮点除法、
	/// 每通道一次浮点乘加 Ceiling/Floor。整个运算表化之后热循环里只剩查表。
	/// 表是用**原来的表达式**生成的，所以逐位结果和原来完全一致。
	/// </summary>
	private static readonly byte[] Premultiply = new byte[256 * 256];

	private static readonly byte[] Unpremultiply = new byte[256 * 256];

	static Texture2DReader()
	{
		for (int a = 0; a < 256; a++)
		{
			float pre = (float)(255.0 / (double)a);
			float un = (float)a / 255f;
			for (int c = 0; c < 256; c++)
			{
				Premultiply[(a << 8) | c] = (byte)Math.Min(Math.Ceiling((float)c * pre), 255.0);
				Unpremultiply[(a << 8) | c] = (byte)Math.Floor((float)c * un);
			}
		}
	}

    public override void Init(ReaderResolver resolver)
    {
        bufferReader = resolver.bufferReader;
        bufferWriter = resolver.bufferWriter;
    }

    public override object Read()
    {
        Texture2D texture2D = new Texture2D();
        texture2D.Format = bufferReader.ReadInt32();
        // 宽/高各 4 字节，拆成两个 uint16：低 16 位是补齐后尺寸，高 16 位是内容尺寸。
        // PC 版高 16 位恒为 0，这样读和直接 ReadInt32 结果完全一样。
        texture2D.Width = bufferReader.ReadUInt16();
        texture2D.ContentWidth = bufferReader.ReadUInt16();
        texture2D.Height = bufferReader.ReadUInt16();
        texture2D.ContentHeight = bufferReader.ReadUInt16();
        texture2D.MipCount = bufferReader.ReadInt32();
        texture2D.DataSize = bufferReader.ReadUInt32();
        if (texture2D.MipCount > 1)
        {
            Logger.Warn(Error.Texture2DReader_4, texture2D.MipCount);
        }

        if ((decimal)texture2D.Width * texture2D.Height * 4 > Int32.MaxValue)
        {
            throw new Texture2DReaderError(Error.Texture2DReader_5, texture2D.Width, texture2D.Height,
                texture2D.DataSize);
        }

        SquishFlags squishFlags = texture2D.Format switch
        {
            4 => SquishFlags.kDxt1,
            5 => SquishFlags.kDxt3,
            6 => SquishFlags.kDxt5,
            2 => throw new XnbError(Error.Texture2DReader_1),
            0 => (SquishFlags)0,
            _ => throw new XnbError(Error.Texture2DReader_2, texture2D.Format),
        };
        if (squishFlags != 0)
        {
            ReadOnlySpan<byte> blocks = bufferReader.ReadOnly((int)texture2D.DataSize);
            if (texture2D.Format == 5)
            {
                // 记下原文件全透明块是哪种写法，回写时照原样（见 FontDxt3）
                texture2D.FontDxt3ZeroTransparent = FontDxt3.DetectZeroTransparent(blocks);
            }

            byte[] array = new byte[texture2D.Width * texture2D.Height * 4];
            global::Squish.Squish squish = new global::Squish.Squish(squishFlags, texture2D.Width, texture2D.Height);
            squish.DecompressImage(array, blocks);
            squish.Dispose();
            texture2D.Data = array;
        }
        else
        {
            texture2D.Data = bufferReader.Read((int)texture2D.DataSize);
        }

        byte[] data = texture2D.Data;
        for (int i = 0; i < data.Length; i += 4)
        {
            int row = data[i + 3] << 8;
            data[i] = Premultiply[row | data[i]];
            data[i + 1] = Premultiply[row | data[i + 1]];
            data[i + 2] = Premultiply[row | data[i + 2]];
        }

        return texture2D;
    }

    public override void Write(object content)
    {
        Texture2D texture2D = (Texture2D)content;
        int format = texture2D.Format;
        int width = texture2D.Width;
        int height = texture2D.Height;
        byte[] data = texture2D.Data;
        Logger.Debug(Error.Texture2DReader_3, width, height, format);
        bufferWriter.WriteInt32(format);
        bufferWriter.WriteUInt16((ushort)width);
        bufferWriter.WriteUInt16((ushort)texture2D.ContentWidth);
        bufferWriter.WriteUInt16((ushort)height);
        bufferWriter.WriteUInt16((ushort)texture2D.ContentHeight);
        bufferWriter.WriteInt32(1);
        for (int i = 0; i < data.Length; i += 4)
        {
            int row = data[i + 3] << 8;
            data[i] = Unpremultiply[row | data[i]];
            data[i + 1] = Unpremultiply[row | data[i + 1]];
            data[i + 2] = Unpremultiply[row | data[i + 2]];
        }

        SquishFlags squishFlags = texture2D.Format switch
        {
            4 => SquishFlags.kDxt1,
            5 => SquishFlags.kDxt3,
            6 => SquishFlags.kDxt5,
            _ => (SquishFlags)0,
        };
        // 字体贴图 XNA/MonoGame 走的是写死端点的专用编码器，通用编码器压出来视觉等价
        // 但字节不同，所以这里必须分流，否则字体 XNB 无法逐字节还原。
        if (format == 5 && texture2D.IsFontTexture)
        {
            int fontBlocks = ((width + 3) / 4) * ((height + 3) / 4) * 16;
            bufferWriter.WriteUInt32((uint)fontBlocks);
            FontDxt3.Compress(data, width, height, bufferWriter.Buffer.AsSpan(bufferWriter.BytePosition, fontBlocks),
                texture2D.FontDxt3ZeroTransparent);
            bufferWriter.Skip(fontBlocks);
        }
        else if (squishFlags != 0)
        {
            global::Squish.Squish squish = new global::Squish.Squish(squishFlags, width, height);
            int storageRequirements = squish.GetStorageRequirements();
            bufferWriter.WriteUInt32((uint)storageRequirements);
            squish.CompressImage(data, bufferWriter.Buffer.AsSpan(bufferWriter.BytePosition, storageRequirements));
            squish.Dispose();
            bufferWriter.Skip(storageRequirements);
        }
        else
        {
            bufferWriter.WriteUInt32((uint)data.Length);
            bufferWriter.Write(data);
        }
    }

}