using System;
using System.Collections.Generic;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class AnimatedTilerReader : BaseReader
{
	private readonly StringReader stringReader = new StringReader();

	private int propertieListReader;

	private int staticTileReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		stringReader.Init(resolver);
		staticTileReader = resolver.GetIndex(typeof(StaticTile));
		propertieListReader = resolver.GetIndex(typeof(List<Propertie>));
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		AnimatedTile animatedTile = new AnimatedTile();
		animatedTile.FrameInterval = bufferReader.ReadInt32();
		animatedTile._frameCount = bufferReader.ReadInt32();
		animatedTile.Frames = new List<StaticTile>();
		animatedTile._currTileSheet = new List<string>();
		animatedTile._currTileSheet.Add("");
		int num = 0;
		animatedTile.Index = new List<char>();
		int num2 = 0;
		while (num2 < animatedTile._frameCount)
		{
			char c = (char)bufferReader.ReadByte();
			animatedTile.Index.Add(c);
			switch (c)
			{
			case 'T':
				animatedTile._currTileSheet.Add(stringReader.ReadByInt32());
				num++;
				break;
			case 'S':
			{
				StaticTile staticTile = readerResolver.ReadValue<StaticTile>(staticTileReader);
				staticTile.TileSheet = animatedTile._currTileSheet[num];
				animatedTile.Frames.Add(staticTile);
				num2++;
				break;
			}
			default:
				throw new Exception("Bad animated tile data");
			}
		}
		animatedTile.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
		return animatedTile;
	}

	public override void Write(object content)
	{
		AnimatedTile animatedTile = (AnimatedTile)content;
		bufferWriter.WriteInt32(animatedTile.FrameInterval);
		bufferWriter.WriteInt32(animatedTile._frameCount);
		int num = 0;
		int num2 = -1;
		int num3 = -1;
		int num4 = 0;
		while (num4 < animatedTile._frameCount)
		{
			char c = animatedTile.Index[++num3];
			bufferWriter.WriteByte((byte)c);
			switch (c)
			{
			case 'T':
				stringReader.WriteByInt32(animatedTile._currTileSheet[++num]);
				break;
			case 'S':
			{
				StaticTile item = animatedTile.Frames[++num2];
				readerResolver.WriteValue(staticTileReader, item);
				num4++;
				break;
			}
			default:
				throw new Exception("Bad animated tile data");
			}
		}
		readerResolver.WriteValue(propertieListReader, animatedTile.Properties);
	}
}
