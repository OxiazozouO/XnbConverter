using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using XnbConverter.Configurations;
using XnbConverter.Exceptions;
using XnbConverter.Readers.Mono;
using XnbConverter.Tbin.Entity;

namespace XnbConverter.Readers;

public static class TypeReadHelper
{
    public class ReaderInfo
    {
        public Type Entity;

        public string Extension;

        public Type Reader;
    }

    private static readonly Dictionary<string, string> ExtMap;

    private static ConcurrentDictionary<string, ReaderInfo> Map;

    private static readonly Dictionary<string, Type> ReaderTypes;

    private static readonly Dictionary<string, Type> ExtendTypes;

    private static readonly Dictionary<string, Type> Entities;

    static TypeReadHelper()
    {
        ExtMap = new Dictionary<string, string>
        {
            ["Texture2D"] = ".png",
            ["Tide"] = ".tbin",
            ["SoundEffect"] = ".json .wav",
            ["SpriteFont"] = ".json .png",
            ["XmlSource"] = ".xml",
            ["Effect"] = ".cso"
        };
        Map = new ConcurrentDictionary<string, ReaderInfo>();
        ReaderTypes = new Dictionary<string, Type>();
        ExtendTypes = new Dictionary<string, Type>();
        Entities = new Dictionary<string, Type>();
        string[] array = new string[3]
            { "XnbConverter.Readers.Base", "XnbConverter.Readers.Base.ValueReaders", "XnbConverter.Readers.Mono" };
        string[] array2 = new string[1] { "XnbConverter.Entity.Mono" };
        Type[] types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (Type type in types)
        {
            string[] array3 = array;
            foreach (string b in array3)
            {
                if (string.Equals(type.Namespace, b, StringComparison.Ordinal))
                {
                    ReaderTypes.Add(Regex.Replace(type.Name, "`\\d+$", "").Replace("Reader", ""), type);
                    break;
                }
            }

            array3 = array2;
            foreach (string b2 in array3)
            {
                if (string.Equals(type.Namespace, b2, StringComparison.Ordinal))
                {
                    Entities.Add(type.Name, type);
                    break;
                }
            }
        }

        types = new Type[]
        {
            typeof(bool),
            typeof(char),
            typeof(double),
            typeof(int),
            typeof(float),
            typeof(uint),
            typeof(Array),
            typeof(string),
            Type.GetType("XnbConverter.Entity.Enum`1"),
            Type.GetType("System.Nullable`1"),
            Type.GetType("System.Collections.Generic.List`1"),
            Type.GetType("System.Collections.Generic.Dictionary`2")
        };
        foreach (Type type2 in types)
        {
            Entities.Add(Regex.Replace(type2.Name, "`\\d+$", ""), type2);
        }

        Map["xTile.Pipeline.TideReader, xTile"] = new ReaderInfo
        {
            Reader = typeof(TBinReader),
            Entity = typeof(TBin10),
            Extension = ExtMap["Tide"]
        };
    }

    public static ReaderInfo GetReaderInfo(string full)
    {
        if (Map.TryGetValue(full, out ReaderInfo value))
        {
            return value;
        }

        ReaderInfo readerInfo = new ReaderInfo();
        List<string> nameList = new List<string>();
        List<string> fullList = new List<string>();
        ParseType(full, ref nameList, ref fullList);
        int index = 0;
        try
        {
            (readerInfo.Reader, readerInfo.Entity) = GetTypeAt(nameList, ref index);
            if (readerInfo.Entity.Name == "Enum`1")
            {
                readerInfo.Entity = readerInfo.Entity.GenericTypeArguments[0];
            }
        }
        catch (Exception ex)
        {
            string text;
            if (fullList[index].Contains("Microsoft.Xna.Framework.Content.Content"))
            {
                text = nameList[index].Split('@')[1];
                throw new XnbError(Error.TypeReadHelper_1, text, ex.Message);
            }

            text = fullList[index].Split(',').Length == 1 ? fullList[index] : fullList[index].Split(',')[1];
            throw new LoadReaderError(Error.TypeReadHelper_2, fullList[index], ConfigHelper.ExDllPath, text,
                ex.Message);
        }

        readerInfo.Extension = ExtMap.GetValueOrDefault(nameList[0].Split('@')[0], ".json");
        Map[full] = readerInfo;
        return readerInfo;
    }

    public static Type GetResultType(string full)
    {
        return Map[full].Entity;
    }

    public static string GetExtension(string full)
    {
        return Map[full].Extension;
    }

    public static BaseReader CreateReader(this Type type)
    {
        return (BaseReader)Activator.CreateInstance(type) ??
               throw new XnbError(Error.TypeReadHelper_3, type);
    }

    private static (Type, Type) GetTypeAt(List<string> list, ref int index)
    {
        string[] array = list[index].Split('@')[0].Split('`');
        string text = list[index].Split('@')[1];
        string text2 = array[0];
        if (text2.Contains('+'))
        {
            text2 = text2.Split('+')[^1];
        }

        Type type = Type.GetType(text);
        if (type == typeof(object))
        {
            return (typeof(XnbConverter.Readers.Base.StringReader), typeof(string));
        }

        if (type == null && Entities.TryGetValue(text2, out Type value))
        {
            type = value;
        }

        if (type == null && ExtendTypes.TryGetValue(text2, out Type value2))
        {
            type = value2;
        }

        if (type == null)
        {
            throw new NotImplementedException();
        }

        if (ReaderTypes.TryGetValue(text2, out var value3))
        {
            if (array.Length > 1)
            {
                int num = int.Parse(array[1]);
                Type[] array2 = new Type[num * 2];
                for (int i = 0; i < num; i++)
                {
                    index++;
                    int num2 = i;
                    Type[] array3 = array2;
                    int num3 = i + num;
                    (Type, Type) typeAt = GetTypeAt(list, ref index);
                    array2[num2] = typeAt.Item1;
                    array3[num3] = typeAt.Item2;
                }

                Type type2 = null;
                if (type.Name == "Array")
                {
                    type2 = array2[num].MakeArrayType();
                    return (value3.MakeGenericType(array2[num]), type2);
                }

                type2 = type.MakeGenericType(array2[num..(2 * num)]);
                if (value3 == ReaderTypes["Enum"])
                {
                    return (array2[0], type2);
                }

                return (value3.MakeGenericType(array2), type2);
            }

            return (value3, type);
        }

        if (ExtendTypes.TryGetValue(text2, out Type value4))
        {
            Type type3 = null;
            if (value4.IsEnum)
            {
                type3 = ReaderTypes["Enum"].MakeGenericType(value4);
            }
            else if (value4.IsClass)
            {
                type3 = ReaderTypes["Reflective"].MakeGenericType(value4);
            }

            if (type3 == null)
            {
                throw new NotImplementedException();
            }

            return (type3, value4);
        }

        if (type.IsArray)
        {
            return (ReaderTypes["Array"].MakeGenericType(type.GetElementType()), type);
        }

        throw new ReaderTypeError(Error.TypeReadHelper_4, text);
    }

    public static void InitExtendTypes(byte[] bytes)
    {
        Type[] exportedTypes = Assembly.Load(bytes).GetExportedTypes();
        foreach (Type type in exportedTypes)
        {
            ExtendTypes.Add(type.Name, type);
        }
    }

    private static void ParseType(string full, ref List<string> nameList, ref List<string> fullList)
    {
        string item = full;
        List<string> nameList2 = new List<string>();
        List<string> fullList2 = new List<string>();
        int num = full.IndexOf('`');
        int num2 = -1;
        if (num == -1)
        {
            string[] array = full.Split(',');
            if (array.Length > 1)
            {
                full = array[0].Trim('[');
            }
        }
        else
        {
            num2 = 1;
        }

        ReadOnlySpan<char> readOnlySpan;
        if (num2 == -1)
        {
            readOnlySpan = full;
        }
        else
        {
            int n = full[num + 1] - 48;
            foreach (string full2 in GetFulls(full, n))
            {
                ParseType(full2, ref nameList2, ref fullList2);
            }

            readOnlySpan = full.Substring(0, num) + "`" + n;
        }

        string text = readOnlySpan.ToString().Replace("Reader", "");
        string text2 = text;
        int num3 = text.LastIndexOf('.') + 1;
        string text3 = text2.Substring(num3, text2.Length - num3);
        if (text3 != "Reflective`1")
        {
            nameList.Add(text3 + "@" + text);
            fullList.Add(item);
        }

        nameList.AddRange(nameList2);
        fullList.AddRange(fullList2);
    }

    private static List<string> GetFulls(ReadOnlySpan<char> s, int n)
    {
        List<string> list = new List<string>();
        int num = s.IndexOf('[');
        if (n > 1)
        {
            num++;
        }

        while (n-- > 0)
        {
            int num2 = 0;
            int num3 = 0;
            for (int i = num; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '[' && c != ']')
                {
                    continue;
                }

                if (c == '[')
                {
                    num2++;
                }

                if (num2 == 1)
                {
                    if (c == ']')
                    {
                        int num4 = num3;
                        list.Add(s.Slice(num4, i - num4).ToString());
                        num = i + 1;
                        break;
                    }

                    num3 = i + 1;
                }

                if (c == ']')
                {
                    num2--;
                }
            }
        }

        return list;
    }
}