using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using XnbConverter.Readers;
using XnbConverter.Readers.Mono;
using XnbConverter.Tbin.Entity;
using static XnbConverter.Utilities.Helpers.SysPath;

namespace XnbConverter.Utilities;

public static class TypeReadHelper
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
        public const string SOUND_EFFECT = ".json .wav";
    }

    private static readonly Dictionary<string, string> ExtMap = new()
    {
        ["Texture2D"] = Ext.TEXTURE_2D,
        ["Tide"] = Ext.TBIN,
        ["SoundEffect"] = Ext.SOUND_EFFECT,
        ["SpriteFont"] = Ext.SPRITE_FONT,
        ["XmlSource"] = Ext.BM_FONT,
        ["Effect"] = Ext.EFFECT
    };

    public static ReaderInfo GetReaderInfo(string full)
    {
        if (Map.TryGetValue(full, out var info)) return info;
        ReaderInfo newInfo = new ReaderInfo();
        List<string> className = new List<string>();
        List<string> classFull = new List<string>();
        ParseType(full, ref className, ref classFull);
        int n = 0;
        try
        {
            (newInfo.Reader, newInfo.Entity) = GetTypeAt(className, ref n);
        }
        catch (Exception e)
        {
            string str;
            if (classFull[n].Contains("Microsoft.Xna.Framework.Content"))
            {
                str = className[n].Split('@')[1];
                throw new XnbError(Helpers.I18N["TypeReadHelper.1"], str, e.Message);
            }

            string[] strings = classFull[n].Split(',');
            str = strings.Length == 1 ? classFull[n] : classFull[n].Split(',')[1];
            throw new XnbError(Helpers.I18N["TypeReadHelper.2"],
                classFull[n], Path.GetFullPath(Dll), str, e.Message);
        }

        newInfo.Extension = ExtMap.TryGetValue(className[0].Split('@')[0], out var value) ? value : Ext.JSON;
        Map[full] = newInfo;
        return newInfo;
    }

    public static BaseReader CreateReader(this Type type)
    {
        return (BaseReader)Activator.CreateInstance(type)
               ?? throw new XnbError(Helpers.I18N["TypeReadHelper.3"], type);
    }

    // private static string ssss =
    //     typeof(Dictionary<List<int>, Dictionary<List<string>, Dictionary<List<float>, Dictionary<List<int>, Dictionary<
    //         List<char>, Dictionary<
    //             List<int>, Dictionary<int, List<string>>>>>>>>).FullName;
    // typeof(Int32).FullName;
    // typeof(TailorItemRecipe).FullName;

    private static ConcurrentDictionary<string, ReaderInfo> Map = new();

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

        throw new ReaderTypeError(Helpers.I18N["TypeReadHelper.4"], full);
    }

    private static readonly Dictionary<string, Type> ReaderTypes = new();
    private static readonly Dictionary<string, Type> ExtendTypes = new();
    private static readonly Dictionary<string, Type> Entities = new();
    static TypeReadHelper()
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
        foreach (var type in tt)
        {
            Entities.Add(Regex.Replace(type.Name, @"`\d+$", ""), type);
        }

        InitExtendTypes();
        Map["xTile.Pipeline.TideReader, xTile"] = new ReaderInfo
        {
            Reader = typeof(TBinReader),
            Entity = typeof(TBin10),
            Extension = ExtMap["Tide"]
        };
        // Map[""]
    }

    private static void InitExtendTypes()
    {
        var files = Dll.ToEntity<List<string>>(true);
        if (files == null) return;
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Log.Warn(Helpers.I18N["TypeReadHelper.5"],
                    file, Path.GetFullPath(Dll));
                continue;
            }

            foreach (var type in Assembly.LoadFile(Path.GetFullPath(file)).GetExportedTypes())
            {
                ExtendTypes.Add(type.Name, type);
            }
        }

        files.ToJson(Dll);
    }

    private static void ParseType(string full, ref List<string> nameList, ref List<string> fullList)
    {
        string fullCopy = full;
        List<string> tNames = new List<string>();
        List<string> tFulls = new List<string>();
        var i = full.IndexOf('`');
        ReadOnlySpan<char> l;
        var tag = -1;
        if (i == -1)
        {
            var strings = full.Split(',');
            if (strings.Length > 1)
            {
                full = strings[0].Trim('[');
            }
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

            foreach (var tf in GetFulls(full, n))
            {
                ParseType(tf, ref tNames, ref tFulls);
            }

            l = full[..i] + "`" + n;
        }

        var ret = l.ToString().Replace("Reader", "");
        var name = ret[(ret.LastIndexOf('.') + 1)..];
        if (name != "Reflective`1")
        {
            nameList.Add(name + "@" + ret);
            fullList.Add(fullCopy);
        }

        nameList.AddRange(tNames);
        fullList.AddRange(tFulls);
    }

    private static List<string> GetFulls(ReadOnlySpan<char> s, int n)
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