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
        var paths = Directory.GetFiles(path, "*.tbin", SearchOption.AllDirectories);
        List<TBin10> list = new();
        foreach (var s in paths)
        {
            var tr = TBin10Reader.Create(File.ReadAllBytes(s));
            var tbin = tr.Read<TBin10>() ?? new TBin10();
            tbin.RemoveTileSheetsExtension();
            tbin.RemoveNullProperties();
            tbin.RemovePropertiesStr();
            list.Add(tbin);
        }

        var sss = new string[paths.Length];
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
            for (var i = 1; i < strings.Count; i++) Console.Write(strings[i] + " ");

            Console.WriteLine();
        }

        Console.WriteLine(w);
    }

    public static void Test_2()
    {
        var path = @"D:\1\SVE\Maps\"; //    D:\.XnbConverter\unpacked\Maps\
        var tr = TBin10Reader.Create(File.ReadAllBytes(Path.Combine(path, "Farm.tbin")));
        var tbin = tr.Read<TBin10>() ?? new TBin10();

        tr.Save(tbin, Path.Combine(path, "Highlands_1.tbin"));
    }

    public static void Test_3()
    {
        var path = @"D:\.XnbConverter\unpacked\Content\Maps";
        foreach (var file in Directory.GetFiles(path, "*.tbin", SearchOption.AllDirectories))
        {
            var tr = TBin10Reader.Create(File.ReadAllBytes(file));
            var tbin = tr.Read<TBin10>()?.RemoveRedundancyTiles(path)
                .ConsolidateNullTileSheets()
                .FindAndSetLight();
            if (tbin is not null) tr.Save(tbin, file);
        }

        // var tr = TBin10Reader.Create(File.ReadAllBytes(Path.Combine(path,"FarmHouse2_marriage.tbin")));
        // var tbin = tr.Read().RemoveRedundancyTiles(path)
        //     .ConsolidateNullTileSheets()2
        //     .FindAndSetLight();
        // tr.Save(tbin,Path.Combine(path,"FarmHouse2_marriage@1.tbin"));
    }
}