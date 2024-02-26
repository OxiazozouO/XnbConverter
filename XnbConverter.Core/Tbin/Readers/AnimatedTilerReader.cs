using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class AnimatedTilerReader : BaseReader
{
    private readonly StringReader stringReader = new();
    private int staticTileReader;
    private int propertieListReader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        staticTileReader = readerResolver.GetIndex(typeof(StaticTile));
        propertieListReader = readerResolver.GetIndex(typeof(List<Propertie>));
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override object Read()
    {
        var result = new AnimatedTile();

        result.FrameInterval = bufferReader.ReadInt32();

        result._frameCount = bufferReader.ReadInt32();
        result.Frames = new List<StaticTile>();

        result._currTileSheet = new List<string>();
        result._currTileSheet.Add("");
        var currTileSheetIndex = 0;
        result.Index = new List<char>();
        for (var i = 0; i < result._frameCount;)
        {
            var b = (char)bufferReader.ReadByte();
            result.Index.Add(b);
            switch (b)
            {
                case 'T':
                    result._currTileSheet.Add(stringReader.ReadByInt32());
                    currTileSheetIndex++;
                    break;
                case 'S':
                    var staticTile = readerResolver.ReadValue<StaticTile>(staticTileReader);
                    staticTile.TileSheet = result._currTileSheet[currTileSheetIndex];
                    result.Frames.Add(staticTile);
                    ++i;
                    break;
                default:
                    throw new Exception("Bad animated tile data");
            }
        }

        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
        return result;
    }

    public override void Write(object content)
    {
        var input = (AnimatedTile)content;

        bufferWriter.WriteInt32(input.FrameInterval);
        bufferWriter.WriteInt32(input._frameCount);

        var currTileSheetIndex = 0;
        var p = -1;
        var index = -1;
        for (var i = 0; i < input._frameCount;)
        {
            var b = input.Index[++index];
            bufferWriter.WriteByte((byte)b);
            switch (b)
            {
                case 'T':
                    stringReader.WriteByInt32(input._currTileSheet[++currTileSheetIndex]);
                    break;
                case 'S':
                    var staticTile = input.Frames[++p];
                    readerResolver.WriteValue(staticTileReader, staticTile);
                    ++i;
                    break;
                default:
                    throw new Exception("Bad animated tile data");
            }
        }

        readerResolver.WriteValue(propertieListReader, input.Properties);
    }
}