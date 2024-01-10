using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class SpecialResponsesReader : BaseReader
{
    private int characterResponseReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        characterResponseReader = readerResolver.GetIndex<CharacterResponseReader>();
    }

    public override SpecialResponses Read()
    {
        SpecialResponses result = new SpecialResponses();
        
        result.BeforeMovie = readerResolver.Read<CharacterResponse>();
        result.DuringMovie = readerResolver.Read_Null<CharacterResponse>();
        result.AfterMovie = readerResolver.Read_Null<CharacterResponse>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (SpecialResponses)content;
        
        readerResolver.Write(characterResponseReader, input.BeforeMovie);
        readerResolver.Write_Null(characterResponseReader, input.DuringMovie);
        readerResolver.Write_Null(characterResponseReader, input.AfterMovie );
    }

    public override bool IsValueType()
    {
        return false;
    }
}