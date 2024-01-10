using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class CharacterResponseReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override CharacterResponse Read()
    {
        CharacterResponse result = new CharacterResponse();
        
        result.ResponsePoint = readerResolver.Read_Null<string>();
        result.Script = readerResolver.Read<string>();
        result.Text = readerResolver.Read<string>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (CharacterResponse)content;
        
        readerResolver.Write_Null(stringReader, input.ResponsePoint);
        readerResolver.Write(stringReader, input.Script);
        readerResolver.Write(stringReader, input.Text);
    }

    public override bool IsValueType()
    {
        return false;
    }
}