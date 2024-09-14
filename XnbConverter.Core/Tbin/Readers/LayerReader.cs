using System;
using System.Collections.Generic;
using System.Text;
using XnbConverter.Entity.Mono;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class LayerReader : BaseReader
{
	private readonly StringReader _stringReader = new StringReader();

	private int _animatedTilerReader;

	private int _intVector2Reader;

	private int _propertieListReader;

	private int _staticTileReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		_stringReader.Init(resolver);
		_intVector2Reader = resolver.GetIndex(typeof(IntVector2));
		_propertieListReader = resolver.GetIndex(typeof(List<Propertie>));
		_staticTileReader = resolver.GetIndex(typeof(StaticTile));
		_animatedTilerReader = resolver.GetIndex(typeof(AnimatedTile));
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		Layer layer = new Layer();
		layer.Id = _stringReader.ReadByInt32();
		layer.Visible = bufferReader.ReadByte();
		layer.Description = _stringReader.ReadByInt32();
		layer.LayerSize = readerResolver.ReadValue<IntVector2>(_intVector2Reader);
		layer.TileSize = readerResolver.ReadValue<IntVector2>(_intVector2Reader);
		layer.Properties = readerResolver.ReadValue<List<Propertie>>(_propertieListReader);
		float num = layer.LayerSize.X;
		float num2 = layer.LayerSize.Y;
		layer._currTileSheet = new List<string>();
		layer._currTileSheet.Add("");
		layer.Tiles = new List<BaseTile>((int)(num * num2));
		layer.Index = new List<char>();
		layer._sizeArr = new List<int>();
		for (int i = 0; (float)i < num2; i++)
		{
			int num3 = 0;
			while ((float)num3 < num)
			{
				char c = (char)bufferReader.ReadByte();
				layer.Index.Add(c);
				switch (c)
				{
				case 'N':
				{
					int num4 = bufferReader.ReadInt32();
					layer._sizeArr.Add(num4);
					for (int j = 0; j < num4; j++)
					{
						layer.Tiles.Add(null);
					}
					num3 += num4;
					break;
				}
				case 'S':
				{
					StaticTile staticTile = readerResolver.ReadValue<StaticTile>(_staticTileReader);
					List<string> currTileSheet = layer._currTileSheet;
					staticTile.TileSheet = currTileSheet[currTileSheet.Count - 1];
					layer.Tiles.Add(staticTile);
					num3++;
					break;
				}
				case 'A':
					layer.Tiles.Add(readerResolver.ReadValue<AnimatedTile>(_animatedTilerReader));
					num3++;
					break;
				case 'T':
					layer._currTileSheet.Add(_stringReader.ReadByInt32());
					break;
				default:
					throw new Exception(Error.LayerReader_2);
				}
			}
		}
		return layer;
	}

	public override void Write(object content)
	{
		Layer layer = (Layer)content;
		_stringReader.WriteByInt32(layer.Id);
		bufferWriter.WriteByte(layer.Visible);
		_stringReader.WriteByInt32(layer.Description);
		readerResolver.WriteValue(_intVector2Reader, layer.LayerSize);
		readerResolver.WriteValue(_intVector2Reader, layer.TileSize);
		readerResolver.WriteValue(_propertieListReader, layer.Properties);
		int x = layer.LayerSize.X;
		int y = layer.LayerSize.Y;
		int num = -1;
		int num2 = 0;
		int num3 = 0;
		int num4 = -1;
		new StringBuilder();
		for (int i = 0; i < y; i++)
		{
			int num5 = 0;
			while (num5 < x)
			{
				char c = layer.Index[++num4];
				bufferWriter.WriteByte((byte)c);
				switch (c)
				{
				case 'N':
				{
					int num6 = layer._sizeArr[++num];
					bufferWriter.WriteInt32(num6);
					if (num5 + num6 > x)
					{
						throw new TbinError(Error.LayerReader_1, x, num5);
					}
					num2 += num6;
					num5 += num6;
					break;
				}
				case 'S':
				{
					StaticTile item = (StaticTile)layer.Tiles[num2++];
					readerResolver.WriteValue(_staticTileReader, item);
					num5++;
					break;
				}
				case 'A':
					readerResolver.WriteValue(_animatedTilerReader, layer.Tiles[num2++]);
					num5++;
					break;
				case 'T':
					_stringReader.WriteByInt32(layer._currTileSheet[++num3]);
					break;
				default:
					throw new TbinError(Error.LayerReader_2);
				}
			}
		}
	}
}
