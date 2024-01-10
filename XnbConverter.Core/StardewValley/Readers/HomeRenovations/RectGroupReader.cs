using XnbConverter.Entity.Mono;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.HomeRenovations;

namespace XnbConverter.StardewValley.Readers.HomeRenovations;

public class RectGroupReader : BaseReader
{
    private int rectListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        rectListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<RectReader>,Rect>>();
    }

    public override RectGroup Read()
    {
        var rects = readerResolver.Read_Null<List<Rect>>();
        return new RectGroup
        {
            Rects = rects
        };
    }

    public override void Write(object content)
    {
        var input = (RectGroup)content;
        
        readerResolver.Write_Null(rectListReader, input.Rects);
    }
    
    public override bool IsValueType()
    {
        return false;
    }
}