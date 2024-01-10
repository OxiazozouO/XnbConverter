using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class MovieCharacterReactionReader : BaseReader
{
    private int stringReader;
    private int movieReactionListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        movieReactionListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<MovieReactionReader>, MovieReaction>>();
    }

    public override MovieCharacterReaction Read()
    {
        MovieCharacterReaction result = new MovieCharacterReaction();
        
        result.NPCName   = readerResolver.Read<string>();
        result.Reactions = readerResolver.Read_Null<List<MovieReaction>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (MovieCharacterReaction)content;
        
        readerResolver.Write(stringReader, input.NPCName);
        readerResolver.Write_Null(movieReactionListReader, input.Reactions);
    }

    public override bool IsValueType()
    {
        return false;
    }
}