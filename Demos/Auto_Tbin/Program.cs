using System.Globalization;
using Auto_Tbin;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

public class Program
{
    public static void Main(string[] args)
    {
    }

    public static void Test_1()
    {
        var path = @"D:\1\SVE\Maps\"; //    D:\.XnbConverter\unpacked\Maps\
        string[] paths = Directory.GetFiles(path, "*.tbin", SearchOption.AllDirectories);
        List<TBin10> list = new List<TBin10>();
        foreach (var s in paths)
        {
            TBin10 tbin = new TBin10();
            var tr = TBin10Reader.Create(File.ReadAllBytes(s));
            tbin = tr.Read();
            tbin.RemoveTileSheetsExtension();
            tbin.RemoveNullProperties();
            tbin.RemovePropertiesStr();
            list.Add(tbin);
        }

        string[] sss = new string[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            var s = paths[i];
            sss[i] = s.Substring(s.LastIndexOf('\\') + 1, s.LastIndexOf('.') - s.LastIndexOf('\\') - 1);
        }

        list.GetWarpDirectedGraph(sss, out var w);
        foreach (var strings in w)
        {
            if (strings == null || strings.Count == 0) continue;
            Console.Write(strings[0] + ":");
            for (int i = 1; i < strings.Count; i++)
            {
                Console.Write(strings[i] + " ");
            }

            Console.WriteLine();
        }

        Console.WriteLine(w);
    }

    public static void Test_2()
    {
        var path = @"D:\1\SVE\Maps\"; //    D:\.XnbConverter\unpacked\Maps\
        TBin10 tbin = new TBin10();
        var tr = TBin10Reader.Create(File.ReadAllBytes(Path.Combine(path, "Farm.tbin")));
        tbin = tr.Read();

        tr.Save(tbin, Path.Combine(path, "Highlands_1.tbin"));
    }

    public static void Test_3()
    {
        var path = @"D:\.XnbConverter\unpacked\Content\Maps";
        foreach (var file in Directory.GetFiles(path, "*.tbin", SearchOption.AllDirectories))
        {
            var tr = TBin10Reader.Create(File.ReadAllBytes(file));
            var tbin = tr.Read().RemoveRedundancyTiles(path)
                .ConsolidateNullTileSheets()
                .FindAndSetLight();
            tr.Save(tbin,file);
        }
        
        // var tr = TBin10Reader.Create(File.ReadAllBytes(Path.Combine(path,"FarmHouse2_marriage.tbin")));
        // var tbin = tr.Read().RemoveRedundancyTiles(path)
        //     .ConsolidateNullTileSheets()2
        //     .FindAndSetLight();
        // tr.Save(tbin,Path.Combine(path,"FarmHouse2_marriage@1.tbin"));
    }
}