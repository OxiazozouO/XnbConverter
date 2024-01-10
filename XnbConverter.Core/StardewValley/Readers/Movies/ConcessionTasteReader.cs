using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class ConcessionTasteReader : BaseReader
{
    private int stringReader;
    private int stringListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        stringListReader = readerResolver.GetIndex<ListReader<XnbConverter.Readers.Base.StringReader, string>>();
    }

    public override ConcessionTaste Read()
    {
        ConcessionTaste result = new ConcessionTaste();
        
        result.Name = readerResolver.Read<string>();
        result.LovedTags = readerResolver.Read<List<string>>();
        result.LikedTags = readerResolver.Read<List<string>>();
        result.DislikedTags = readerResolver.Read<List<string>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (ConcessionTaste)content;
        
        readerResolver.Write(stringReader, input.Name);
        readerResolver.Write(stringListReader, input.LovedTags);
        readerResolver.Write(stringListReader, input.LikedTags);
        readerResolver.Write(stringListReader, input.DislikedTags);
    }

    public override bool IsValueType()
    {
        return false;
    }
}