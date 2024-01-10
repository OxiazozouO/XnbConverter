using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity.Movies;

namespace XnbConverter.StardewValley.Readers.Movies;

public class ConcessionItemDataReader : BaseReader
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

    /**
     * 从缓冲区读取反射数据。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {Mixed} 反射数据
     */
    public override ConcessionItemData Read()
    {
        ConcessionItemData result = new ConcessionItemData();
        
        result.ID = bufferReader.ReadInt32();
        result.Name = readerResolver.Read<string>();
        result.DisplayName = readerResolver.Read<string>();
        result.Description = readerResolver.Read<string>();
        result.Price = bufferReader.ReadInt32();
        result.ItemTags = readerResolver.Read_Null<List<string>>();
        
        return result;
    }

    /**
     * 将反射数据写入缓冲区。
     * @param {BufferWriter} buffer 缓冲区写入器
     * @param {Number} content 反射数据
     * @param {ReaderResolver} resolver 解析器
     */
    public override void Write(object content)
    {
        var input = (ConcessionItemData) content;
        
        bufferWriter.WriteInt32(input.ID);
        readerResolver.Write(stringReader, input.Name);
        readerResolver.Write(stringReader, input.DisplayName);
        readerResolver.Write(stringReader, input.Description);
        bufferWriter.WriteInt32(input.Price);
        readerResolver.Write_Null(stringListReader, input.ItemTags);
    }

    public override bool IsValueType()
    {
        return false;
    }
}