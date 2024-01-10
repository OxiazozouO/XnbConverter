using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity.FishPond;

namespace XnbConverter.StardewValley.Readers.FishPond;

public class FishPondRewardReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }
    /**
     * 从缓冲区读取反射数据。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {Mixed} 反射数据
     */
    public override FishPondReward Read()
    {
        FishPondReward result = new FishPondReward();
        
        result.RequiredPopulation = bufferReader.ReadInt32();
        result.Chance             = bufferReader.ReadSingle();
        result.ItemID             = bufferReader.ReadInt32();
        result.MinQuantity        = bufferReader.ReadInt32();
        result.MaxQuantity        = bufferReader.ReadInt32();
        
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
        var input = (FishPondReward)content;
        
        bufferWriter.WriteInt32(input .RequiredPopulation );
        bufferWriter.WriteSingle(input.Chance);
        bufferWriter.WriteInt32(input .ItemID);
        bufferWriter.WriteInt32(input .MinQuantity);
        bufferWriter.WriteInt32(input .MaxQuantity);
    }
    
    /// <summary>
    /// 获取是否为值类型。
    /// </summary>
    /// <returns>是否为值类型</returns>
    public override bool IsValueType()
    {
        return false;
    }
}