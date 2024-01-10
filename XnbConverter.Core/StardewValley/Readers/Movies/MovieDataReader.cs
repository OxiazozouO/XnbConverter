using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class MovieDataReader : BaseReader
{
    private int stringReader;
    private int stringListReader;
    private int movieSceneListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader         = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        stringListReader     = readerResolver.GetIndex<ListReader<XnbConverter.Readers.Base.StringReader, string>>();
        movieSceneListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<MovieSceneReader>, MovieScene>>();
    }

    public override MovieData Read()
    {
        MovieData result = new MovieData();
        
        result.ID = readerResolver.Read_Null<string>();
        result.SheetIndex = bufferReader.ReadInt32();
        result.Title = readerResolver.Read<string>();
        result.Description = readerResolver.Read<string>();
        result.Tags = readerResolver.Read_Null<List<string>>();
        result.Scenes = readerResolver.Read<List<MovieScene>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (MovieData)content;
        
        readerResolver.Write_Null(stringReader, input.ID);
        bufferWriter.WriteInt32(input.SheetIndex);
        readerResolver.Write(stringReader, input.Title);
        readerResolver.Write(stringReader, input.Description);
        readerResolver.Write_Null(stringListReader, input.Tags);
        readerResolver.Write(movieSceneListReader, input.Scenes);
    }

    public override bool IsValueType()
    {
        return false;
    }
}