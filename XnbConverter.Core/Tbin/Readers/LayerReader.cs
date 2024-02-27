using System.Text;
using XnbConverter.Entity.Mono;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class LayerReader : BaseReader
{
    private readonly StringReader stringReader = new();
    private int intVector2Reader;
    private int propertieListReader;
    private int staticTileReader;
    private int animatedTilerReader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        intVector2Reader = readerResolver.GetIndex(typeof(IntVector2));
        propertieListReader = readerResolver.GetIndex(typeof(List<Propertie>));
        staticTileReader = readerResolver.GetIndex(typeof(StaticTile));
        animatedTilerReader = readerResolver.GetIndex(typeof(AnimatedTile));
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override Layer Read()
    {
        var result = new Layer();

        result.Id = stringReader.ReadByInt32();
        result.Visible = bufferReader.ReadByte();
        result.Description = stringReader.ReadByInt32();
        result.LayerSize = readerResolver.ReadValue<IntVector2>(intVector2Reader);
        result.TileSize = readerResolver.ReadValue<IntVector2>(intVector2Reader);

        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);

        float x = result.LayerSize.X;
        float y = result.LayerSize.Y;

        result._currTileSheet = new List<string>();
        result._currTileSheet.Add("");
        result.Tiles = new List<BaseTile>((int)(x * y));
        result.Index = new List<char>();
        result._sizeArr = new List<int>();
        for (var j = 0; j < y; ++j)
        for (var i = 0; i < x;)
        {
            var c = (char)bufferReader.ReadByte();
            result.Index.Add(c);
            switch (c)
            {
                case 'N': //NULL SIZE
                {
                    var size = bufferReader.ReadInt32();
                    result._sizeArr.Add(size);
                    for (var n = 0; n < size; ++n)
                        result.Tiles.Add(null);
                    i += size;
                }
                    break;
                case 'S': //Static Tile
                    var tmp = readerResolver.ReadValue<StaticTile>(staticTileReader);
                    tmp.TileSheet = result._currTileSheet[^1];
                    result.Tiles.Add(tmp); //reader, currTileSheet
                    ++i;
                    break;
                case 'A': //Animated Tile
                    result.Tiles.Add(readerResolver.ReadValue<AnimatedTile>(animatedTilerReader));
                    ++i;
                    break;
                case 'T': //
                    result._currTileSheet.Add(stringReader.ReadByInt32());
                    break;
                default:
                    throw new Exception("Bad tile data");
            }
        }

        return result;
    }

    public override void Write(object content)
    {
        var input = (Layer)content;

        stringReader.WriteByInt32(input.Id);
        bufferWriter.WriteByte(input.Visible);
        stringReader.WriteByInt32(input.Description);
        readerResolver.WriteValue(intVector2Reader, input.LayerSize);
        readerResolver.WriteValue(intVector2Reader, input.TileSize);
        readerResolver.WriteValue(propertieListReader, input.Properties);

        var x = input.LayerSize.X;
        var y = input.LayerSize.Y;

        var sizeIndex = -1;
        var tilesIndex = 0;
        var currTileSheetIndex = 0;
        var index = -1;
        var sb = new StringBuilder();
        int ans;
        for (var j = 0; j < y; ++j)
            // sb.Length = 0;
            // ans = tilesIndex;
        for (var i = 0; i < x;)
        {
            var c = input.Index[++index];
            bufferWriter.WriteByte((byte)c);
            switch (c)
            {
                case 'N':
                {
                    var size = input._sizeArr[++sizeIndex];
                    bufferWriter.WriteInt32(size);
                    if (i + size > x)
                    {
                        throw new TbinError("i 最大为{0},现在为{1}",x,i);
                    }

                    tilesIndex += size;
                    i += size;
                    // sb.Append('+').Append(size);
                }
                    break;
                case 'S':
                    var tmp = (StaticTile)input.Tiles[tilesIndex++];
                    readerResolver.WriteValue(staticTileReader, tmp);
                    ++i;
                    // sb.Append('+').Append(1);
                    break;
                case 'A':
                    readerResolver.WriteValue(animatedTilerReader, input.Tiles[tilesIndex++]);
                    ++i;
                    // sb.Append('+').Append(1);
                    break;
                case 'T':
                    stringReader.WriteByInt32(input._currTileSheet[++currTileSheetIndex]);
                    break;
                default:
                    throw new TbinError("Bad tile data");
            }
        }
        // sb.Append("ans:" + (tilesIndex-ans) + "  w:"+x+"\n");
        // // if(tilesIndex-ans !=x)
        //     Console.WriteLine(sb.ToString());
        // Console.WriteLine("\n\n\n");
    }
}