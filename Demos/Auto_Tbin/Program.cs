using Auto_Tbin;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

public class Program
{
    public static void Main(string[] args)
    {
        var path = @"D:\1\SVE\Maps\";

        var tbin = new TBin10
        {
            Data = File.ReadAllBytes(Path.Combine(path, "Highlands.tbin"))//
        };

        var tr = TBin10Reader.Create(tbin);

        tbin = tr.Read();
        
        tbin.ConsolidateLayers(path);
        tbin.RemoveTileSheetsExtension();
        tr.Save(tbin, Path.Combine(path, "Highlands_1.tbin"));
    }


    public static void rrr()
    {
        var i1 = Image.Load<Rgba32>(@"D:\2\Crop.png");
        var i2 = Image.Load<Rgba32>(@"D:\2\Seeds.png");
        i2.DrawImagePortion(0, i1, 12);
        i2.Save(@"D:\2\2.png");
    }
}