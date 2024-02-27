using System.Reflection;
using System.Text.RegularExpressions;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public static class TypeReader
{
    public static class Ext
    {
        public const string DEF = ".bin";
        public const string JSON = ".json";
        public const string TEXTURE_2D = ".png";
        public const string EFFECT = ".cso";
        public const string TBIN = ".tbin";
        public const string BM_FONT = ".xml";
        public const string SPRITE_FONT = ".json .png";
        public const string SoundEffect = ".json .wav";
    }

    private static readonly Dictionary<string, string> ExtMap = new()
    {
        ["Texture2DReader"] = Ext.TEXTURE_2D,
        ["TideReader"] = Ext.TBIN,
        ["SoundEffectReader"] = Ext.SoundEffect,
        ["SpriteFontReader"] = Ext.SPRITE_FONT,
        ["XmlSourceReader"] = Ext.BM_FONT,
        ["EffectReader"] = Ext.EFFECT,
        ["Effect"] = Ext.EFFECT
    };

    public static ReaderInfo GetReaderInfo(string full)
    {
        if (Map.TryGetValue(full, out var info)) return info;
        List<string> className = ParseType(full);
        var n = 0;
        var type = GetTypeAt(className, ref n);
        var ext = ExtMap.TryGetValue(className[0], out var value) ? value : Ext.JSON;
        Map[full] = new ReaderInfo { Reader = type.Item1, Entity = type.Item2, Extension = ext };
        return Map[full];
    }

    public static BaseReader CreateReader(this Type type)
    {
        return (BaseReader)Activator.CreateInstance(type)
               ?? throw new XnbError(Helpers.TextTip.UnrealizedTypes, type);
    }

    // private static string ssss =
    //     typeof(Dictionary<List<int>, Dictionary<List<string>, Dictionary<List<float>, Dictionary<List<int>, Dictionary<
    //         List<char>, Dictionary<
    //             List<int>, Dictionary<int, List<string>>>>>>>>).FullName;
    // typeof(Int32).FullName;
    // typeof(TailorItemRecipe).FullName;

    private static readonly Dictionary<string, ReaderInfo> Map = new();

    private static (Type, Type) GetTypeAt(List<string> list, ref int index)
    {
        string[] a0 = list[index].Split('@')[0].Split('`');
        var full = list[index].Split('@')[1];
        var name = a0[0];

        if (ReaderTypes.TryGetValue(name, out var ga))
        {
            if (a0.Length > 1)
            {
                var j = int.Parse(a0[1]);
                Type[] readers = new Type[j * 2];
                for (var k = 0; k < j; k++)
                {
                    ++index;
                    (readers[k], readers[k + j]) = GetTypeAt(list, ref index);
                }

                Type main = null;
                for (var k = 0; k < j; k++)
                {
                    var vv = Type.GetType(full);
                    if (vv == null && Entities.TryGetValue(name, out var ent)) vv = ent;

                    if (vv == null && ExtendTypes.TryGetValue(name, out var gam)) vv = gam;

                    if (vv == null) throw new Exception();

                    if (main == null) main = vv.MakeGenericType(readers[j..(2 * j)]);
                }

                return (ga.MakeGenericType(readers), main);
            }
            else
            {
                var vv = Type.GetType(full);
                if (vv == null && Entities.TryGetValue(name, out var ent)) vv = ent;

                if (vv == null && ExtendTypes.TryGetValue(name, out var gam)) vv = gam;

                if (vv == null) throw new Exception();

                return (ga, vv);
            }
        }
        else if (ExtendTypes.TryGetValue(name, out var ex))
        {
            var exx = ReaderTypes["Reflective"].MakeGenericType(ex);
            return (exx, ex);
        }

        throw new Exception();
    }

    private static readonly Dictionary<string, Type> ReaderTypes = new();
    private static readonly Dictionary<string, Type> ExtendTypes = new();
    private static readonly Dictionary<string, Type> Entities = new();

    static TypeReader()
    {
        string[] reads =
            { "XnbConverter.Readers.Base", "XnbConverter.Readers.Base.ValueReaders", "XnbConverter.Readers.Mono" };
        string[] ents =
            { "XnbConverter.Entity.Mono" };
        Type[] types = Assembly.GetExecutingAssembly().GetTypes();
        // 获取指定命名空间下的所有类型
        foreach (var t in types)
        {
            foreach (var name in reads)
                if (string.Equals(t.Namespace, name, StringComparison.Ordinal))
                {
                    ReaderTypes.Add(Regex.Replace(t.Name, @"`\d+$", "").Replace("Reader", ""), t);
                    break;
                }

            foreach (var name in ents)
                if (string.Equals(t.Namespace, name, StringComparison.Ordinal))
                {
                    Entities.Add(t.Name, t);
                    break;
                }
        }

        Type[] tt = new[]
        {
            typeof(bool), typeof(char), typeof(double), typeof(int), typeof(float),
            typeof(uint), typeof(Array), typeof(Nullable), typeof(string),
            Type.GetType("System.Collections.Generic.List`1"), Type.GetType("System.Collections.Generic.Dictionary`2")
        };
        foreach (var type in tt) Entities.Add(Regex.Replace(type.Name, @"`\d+$", ""), type);

        foreach (var file in Directory.GetFiles(@".\dll\", "*.dll", SearchOption.AllDirectories))
        foreach (var type in Assembly.LoadFile(Path.GetFullPath(file)).GetExportedTypes())
            ExtendTypes.Add(type.Name, type);
    }

    public static List<string> ParseType(string full)
    {
        List<string> ss = new();
        List<string> ts = new();
        var i = full.IndexOf('`');
        ReadOnlySpan<char> l;
        var tag = -1;
        if (i == -1)
        {
            var strings = full.Split(',');
            if (strings.Length > 1) full = strings[0].Trim('[');
        }
        else
        {
            tag = 1;
        }

        if (tag == -1)
        {
            l = full;
        }
        else
        {
            var n = full[i + 1] - '0';
            var names = GetNames(full, n);
            foreach (var t in names) ts.AddRange(ParseType(t));

            l = full[..i] + "`" + n;
        }

        var ret = l.ToString().Replace("Reader", "");
        var name = ret[(ret.LastIndexOf('.') + 1)..];
        if (name != "Reflective`1") ss.Add(name + "@" + ret);

        ss.AddRange(ts);
        return ss;
    }

    public static List<string> GetNames(ReadOnlySpan<char> s, int n)
    {
        List<string> list = new();
        var index = s.IndexOf('[');
        if (n > 1) ++index;

        while (n-- > 0)
        {
            var ans = 0;
            var l = 0;
            for (var i = index; i < s.Length; i++)
            {
                var t = s[i];
                if (t != '[' && t != ']') continue;

                if (t == '[') ans++;

                if (ans == 1)
                {
                    if (t == ']')
                    {
                        list.Add(s[l..i].ToString());
                        index = i + 1;
                        break;
                    }

                    l = i + 1;
                }

                if (t == ']') ans--;
            }
        }

        return list;
    }
}