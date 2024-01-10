using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Tbin.Entity;
using Vector2 = XnbConverter.Entity.Mono.Vector2;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class LayerReader: BaseReader
{
    private readonly StringReader stringReader = new ();
    private int vector2Reader;
    private int propertieListReader;
    private int staticTileReader;
    private int animatedTilerReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        vector2Reader       = readerResolver.GetIndex<Vector2Reader>();
        propertieListReader = readerResolver.GetIndex<ListReader<PropertieReader, Propertie>>();
        staticTileReader    = readerResolver.GetIndex<StaticTileReader>();
        animatedTilerReader = readerResolver.GetIndex<AnimatedTilerReader>();
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override Layer Read()
    {
        Layer result = new Layer();
        
        result.Id = stringReader.ReadByInt32();
        result.Visible = bufferReader.ReadByte();
        result.Description = stringReader.ReadByInt32();
        result.LayerSize = readerResolver.ReadValue<Vector2>(vector2Reader);
        result.TileSize = readerResolver.ReadValue<Vector2>(vector2Reader);

        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
        
        float x = result.LayerSize.X;
        float y = result.LayerSize.Y;
        
        result._currTileSheet = new List<string>();
        result._currTileSheet.Add("");
        int currTileSheetIdenx = 0;
        result.Tiles = new List<BaseTile>((int)(x * y));
        result.Index = new List<char>();
        result._sizeArr = new List<int>();
        for (int j = 0; j < y; ++j)
        {
            int i = 0;
            while ( i < x )
            {
                char c = (char)bufferReader.ReadByte();
                result.Index.Add(c);
                switch (c)
                {
                    case 'N':
                    {
                        int size = bufferReader.ReadInt32();
                        result._sizeArr.Add(size);
                        for (int n = 0; n < size; ++n)
                            result.Tiles.Add(null);
                        i += size;
                    }
                        break;
                    case 'S':
                        StaticTile tmp = readerResolver.ReadValue<StaticTile>(staticTileReader);
                        tmp.TileSheet = result._currTileSheet[currTileSheetIdenx];
                        result.Tiles.Add(tmp);//reader, currTileSheet
                        ++i;
                        break;
                    case 'A':
                        result.Tiles.Add(readerResolver.ReadValue<AnimatedTile>(animatedTilerReader));
                        ++i;
                        break;
                    case 'T':
                        currTileSheetIdenx++;
                        result._currTileSheet.Add(stringReader.ReadByInt32());
                        break;
                    default:
                        throw new Exception("Bad tile data");
                }
            }
        }
        
        return result;
    }

    public override void Write(object content)
    {
        Layer input = (Layer)content;
        
        stringReader.WriteByInt32(input.Id);
        bufferWriter.WriteByte(input.Visible);
        stringReader.WriteByInt32(input.Description);
        readerResolver.WriteValue(vector2Reader, input.LayerSize);
        readerResolver.WriteValue(vector2Reader, input.TileSize);
        readerResolver.WriteValue(propertieListReader, input.Properties);
        
        float x = input.LayerSize.X;
        float y = input.LayerSize.Y;
        
        int sizeIndex = -1;
        int TilesIndex = 0;
        int currTileSheetIndex = 0;
        int Index = -1;
        
        for (int j = 0; j < y; ++j)
        {
            int i = 0;
            while ( i < x )
            {
                char c = input.Index[++Index];
                bufferWriter.WriteByte((byte)c);
                switch (c)
                {
                    case 'N':
                    {
                        int size = input._sizeArr[++sizeIndex];
                        bufferWriter.WriteInt32(size);
                        TilesIndex += size;
                        i += size;
                    }
                        break;
                    case 'S':
                        StaticTile tmp = (StaticTile) input.Tiles[TilesIndex++];
                        readerResolver.WriteValue(staticTileReader, tmp);
                        ++i;
                        break;
                    case 'A':
                        readerResolver.WriteValue(animatedTilerReader, input.Tiles[TilesIndex++]);
                        ++i;
                        break;
                    case 'T':
                        stringReader.WriteByInt32(input._currTileSheet[++currTileSheetIndex]);
                        break;
                    default:
                        throw new Exception("Bad tile data");
                }
            }
        }
    }
}