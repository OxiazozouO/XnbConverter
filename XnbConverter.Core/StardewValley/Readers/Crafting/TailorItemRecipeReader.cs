using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.Crafting;

namespace XnbConverter.StardewValley.Readers.Crafting;

public class TailorItemRecipeReader : BaseReader
{
    private int stringListReader;

    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        stringListReader = readerResolver.GetIndex<ListReader<XnbConverter.Readers.Base.StringReader, string>>();
    }

    public override TailorItemRecipe Read()
    {
        TailorItemRecipe result = new TailorItemRecipe();
        
        result.FirstItemTags = readerResolver.Read<List<string>>();
        result.SecondItemTags = readerResolver.Read<List<string>>();
        result.SpendRightItem = bufferReader.ReadBoolean();
        result.CraftedItemID = bufferReader.ReadInt32();
        result.CraftedItemIDs = readerResolver.Read_Null<List<string>>();
        result.CraftedItemColor = readerResolver.Read<string>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (TailorItemRecipe) content;
        
        readerResolver.Write(stringListReader, input.FirstItemTags);
        readerResolver.Write(stringListReader, input.SecondItemTags);
        bufferWriter.WriteBoolean(input.SpendRightItem);
        bufferWriter.WriteInt32(input.CraftedItemID);
        readerResolver.Write_Null(stringListReader, input.CraftedItemIDs);
        readerResolver.Write(stringReader, input.CraftedItemColor);
    }

    public override bool IsValueType()
    {
        return false;
    }
}