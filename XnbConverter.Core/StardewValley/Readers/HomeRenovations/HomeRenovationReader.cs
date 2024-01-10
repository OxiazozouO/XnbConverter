using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.HomeRenovations;

namespace XnbConverter.StardewValley.Readers.HomeRenovations;

public class HomeRenovationReader : BaseReader
{
    private int stringReader;
    private int renovationValueListReader;
    private int rectGroupListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        renovationValueListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<RenovationValueReader>, RenovationValue>>();
        rectGroupListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<RectGroupReader>, RectGroup>>();
    }

    public override HomeRenovation Read()
    {
        HomeRenovation result = new HomeRenovation();
        
        result.TextStrings = readerResolver.Read_Null<string>();
        result.AnimationType = readerResolver.Read_Null<string>();
        result.CheckForObstructions = bufferReader.ReadBoolean();
        result.Requirements = readerResolver.Read_Null<List<RenovationValue>>();
        result.RenovateActions = readerResolver.Read_Null<List<RenovationValue>>();
        result.RectGroups = readerResolver.Read_Null<List<RectGroup>>();
        result.SpecialRect = readerResolver.Read_Null<string>();
        
        return result;
    }
    
    public override void Write(object content)
    {
        var input = (HomeRenovation)content;
        
        readerResolver.Write_Null(stringReader , input.TextStrings);
        readerResolver.Write_Null(stringReader , input.AnimationType);
        bufferWriter.WriteBoolean(input.CheckForObstructions);
        readerResolver.Write_Null(renovationValueListReader , input.Requirements);
        readerResolver.Write_Null(renovationValueListReader , input.RenovateActions);
        readerResolver.Write_Null(rectGroupListReader , input.RectGroups);
        readerResolver.Write_Null(stringReader , input.SpecialRect);
    }

    public override bool IsValueType()
    {
        return false;
    }
}