using XnbConverter.Tbin.Entity;

namespace XnbConverter.Readers.Mono;
/**
 * TBin Reader
 * @class
 * @extends BaseReader
 */
public class TBinReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }

    public override TBin10 Read()
    {
        // 读取数据块的大小
        int size = bufferReader.ReadInt32();
        // 读取数据块
        byte[] data = bufferReader.Read(size);

        // 返回数据
        return new TBin10{Data = data};
    }
    
    public override void Write(object input)
    {
        var tbin = (TBin10)input;
        
        bufferWriter.WriteInt32(tbin.Data.Length);
        bufferWriter.Write(tbin.Data);
    }
    
    public override bool IsValueType()
    {
        return false;
    }
    
    public override Type GetResultType()
    {
        return typeof(TBin10);
    }
}