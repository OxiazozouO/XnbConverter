using System;
using Squish;
using XnbConverter.Configurations;
using XnbConverter.Entity.Mono;
using XnbConverter.Exceptions;
using XnbConverter.Utilities;

namespace XnbConverter.Readers.Mono;

public class Texture2DReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		Texture2D texture2D = new Texture2D();
		texture2D.Format = bufferReader.ReadInt32();
		texture2D.Width = bufferReader.ReadInt32();
		texture2D.Height = bufferReader.ReadInt32();
		texture2D.MipCount = bufferReader.ReadInt32();
		texture2D.DataSize = bufferReader.ReadUInt32();
		if (texture2D.MipCount > 1)
		{
			Logger.Warn(Error.Texture2DReader_4, texture2D.MipCount);
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
			byte[] array = new byte[texture2D.Width * texture2D.Height * 4];
			global::Squish.Squish squish = new global::Squish.Squish(squishFlags, texture2D.Width, texture2D.Height);
			squish.DecompressImage(array, bufferReader.ReadOnly((int)texture2D.DataSize));
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
			float num = (float)(255.0 / (double)(int)data[i + 3]);
			data[i] = (byte)Math.Min(Math.Ceiling((float)(int)data[i] * num), 255.0);
			data[i + 1] = (byte)Math.Min(Math.Ceiling((float)(int)data[i + 1] * num), 255.0);
			data[i + 2] = (byte)Math.Min(Math.Ceiling((float)(int)data[i + 2] * num), 255.0);
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
		bufferWriter.WriteInt32(width);
		bufferWriter.WriteInt32(height);
		bufferWriter.WriteInt32(1);
		for (int i = 0; i < data.Length; i += 4)
		{
			float num = (float)(int)data[i + 3] / 255f;
			data[i] = (byte)Math.Floor((float)(int)data[i] * num);
			data[i + 1] = (byte)Math.Floor((float)(int)data[i + 1] * num);
			data[i + 2] = (byte)Math.Floor((float)(int)data[i + 2] * num);
		}
		SquishFlags squishFlags = texture2D.Format switch
		{
			4 => SquishFlags.kDxt1, 
			5 => SquishFlags.kDxt3, 
			6 => SquishFlags.kDxt5, 
			_ => (SquishFlags)0, 
		};
		if (squishFlags != 0)
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

	public override bool IsValueType()
	{
		return false;
	}
}
