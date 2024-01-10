using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class MovieSceneReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override MovieScene Read()
    {
        MovieScene result = new MovieScene();
        
        result.Image = bufferReader.ReadInt32();
        result.Music = readerResolver.Read<string>();
        result.Sound = readerResolver.Read<string>();
        result.MessageDelay = bufferReader.ReadInt32();
        result.Script = readerResolver.Read<string>();
        result.Text = readerResolver.Read<string>();
        result.Shake = bufferReader.ReadBoolean();
        result.ResponsePoint = readerResolver.Read_Null<string>();
        result.ID = readerResolver.Read<string>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (MovieScene)content;
        
        bufferWriter.WriteInt32(input.Image);
        readerResolver.Write(stringReader, input.Music);
        readerResolver.Write(stringReader, input.Sound);
        bufferWriter.WriteInt32(input.MessageDelay);
        readerResolver.Write(stringReader, input.Script);
        readerResolver.Write(stringReader, input.Text);
        bufferWriter.WriteBoolean(input.Shake);
        readerResolver.Write_Null(stringReader, input.ResponsePoint);
        readerResolver.Write(stringReader, input.ID);
    }

    public override bool IsValueType()
    {
        return false;
    }
}