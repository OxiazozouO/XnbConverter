using XnbConverter.Tbin.Readers;

namespace XnbConverter.Tbin.Entity;

public class TBin10
{
    public static readonly int LayerMax = 7;
    public byte[] Data{ set; get; }
    public string Format { get; set; }
    public string Id { get; set; }
    public string Description { get; set; }
    public List<Propertie> Properties { get; set; }
    public List<TileSheet> TileSheets { get; set; }
    public List<Layer> Layers { get; set; }


    public void SaveAsTbin()
    {
        
    }

    public static TBin10 FormTbin(string path)
    {
        TBin10 tbin = new TBin10{
            Data = File.ReadAllBytes(path)
        };
        //删除图块集的后缀名，不然地图在星露谷不能正常用
        TBin10Reader.RemoveTileSheetsExtension(ref tbin);
        return tbin;
    }
    
}