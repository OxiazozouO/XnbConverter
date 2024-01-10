using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class MovieReactionReader : BaseReader
{
    private int stringReader;
    private int stringListReader;
    private int specialResponsesReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        stringListReader = readerResolver.GetIndex<ListReader<XnbConverter.Readers.Base.StringReader, string>>();
        specialResponsesReader = readerResolver.GetIndex<SpecialResponsesReader>();
    }

    public override MovieReaction Read()
    {
        MovieReaction result = new MovieReaction();
        
        result.Tag = readerResolver.Read<string>();
        result.Response = readerResolver.Read<string>();
        result.Whitelist = readerResolver.Read_Null<List<string>>();
        result.SpecialResponses = readerResolver.Read_Null<SpecialResponses>();
        result.ID = readerResolver.Read<string>();
        
        return result;
    }
    
    public override void Write(object content)
    {
        var input = (MovieReaction)content;
        
        readerResolver.Write(stringReader, input.Tag);
        readerResolver.Write(stringReader, input.Response);
        readerResolver.Write_Null(stringListReader, input.Whitelist);
        readerResolver.Write_Null(specialResponsesReader, input.SpecialResponses);
        readerResolver.Write(stringReader, input.ID);
    }

    public override bool IsValueType()
    {
        return false;
    }
}