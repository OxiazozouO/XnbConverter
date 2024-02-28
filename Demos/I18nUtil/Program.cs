using Newtonsoft.Json;

namespace I18nUtil;

public static class Program
{
    public static void Main(string[] args)
    {
    }

    public static void Edit()
    {
        CodeToJson(".zh-CN", true);
        AllToAt();
    }

    public static void BackEdit()
    {
        JsonToTips(".zh-CN");
    }

    private static readonly string[] LocaleNames = new[]
    {
        ".default",
        ".de-DE",
        ".es-ES",
        ".fr-FR",
        ".hu-HU",
        ".it-IT",
        ".ja-JP",
        ".ko-KR",
        ".pt-BR",
        ".ru-RU",
        ".tr-TR",
        ".zh-CN"
    };

    private static readonly string[] GetArr = new[]
    {
        "new XnbError",
        "new ReaderTypeError",
        "Log.Info",
        "Log.Debug",
        "Log.Warn",
        "Log.Error",
        "TbinError"
    };


    private static void CodeToJson(string codeLocaleName, bool isReplaceCodeFile = false)
    {
        if (!LocaleNames.Contains(codeLocaleName))
        {
            throw new ArgumentException();
        }

        Dictionary<string, List<I18NInfo>> map = new();
        List<I18NInfo> infos = new List<I18NInfo>();
        Dictionary<string, I18NInfo> set = new();
        foreach (var file in Directory.GetFiles(PathStr.CD, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("Helpers.cs")) continue;
            if (file.Contains("Demos\\I18nUtil")) continue;
            string textCopy = File.ReadAllText(file);
            ReadOnlySpan<char> text = textCopy.AsSpan();

            foreach (var s in GetArr)
            {
                int index = 0;
                while (true)
                {
                    int n = text[index..].IndexOf(s + '(');
                    if (n == -1)
                    {
                        break;
                    }

                    index += n + s.Length;
                    int ans = 0;
                    ReadOnlySpan<char> content = "";
                    for (int i = index; i < text.Length; i++)
                    {
                        if (text[i] == '(')
                        {
                            ++ans;
                        }
                        else if (text[i] == ')')
                        {
                            if (ans == 1)
                            {
                                content = text[(index + 1)..i];
                                index = i + 1;
                                break;
                            }

                            ans--;
                        }
                    }

                    if (content.IndexOf('\"') == -1)
                    {
                        Console.WriteLine("\n\n坏数据：" + content.ToString() + "\n\n");
                        continue;
                    }

                    if (!map.ContainsKey(file))
                    {
                        map[file] = new List<I18NInfo>();
                    }

                    string str = content.ToString();
                    if (str == "")
                    {
                        continue;
                    }

                    I18NInfo info = new I18NInfo(str);
                    Console.WriteLine(str);
                    if (set.TryGetValue(info.CutStr, out var value))
                    {
                        info = value;
                        info.Flag = 1;
                    }
                    else
                    {
                        if (content.IndexOf("Helpers.I18N[\"") != -1)
                        {
                            info.Id = info.CutStr;
                        }
                        set.Add(info.CutStr, info);
                    }
                    map[file].Add(info);
                    infos.Add(info);
                }
            }
        }

        //<tipId, <Locale, Tip>>
        var ordLocale = PathStr.ALL.ToEntity<Dictionary<string, Dictionary<string, string>>>();
        var newLocale = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (key, value) in map)
        {
            string name = Path.GetFileNameWithoutExtension(key) + ".";
            int index = 0;
            foreach (var info in value)
            {
                Dictionary<string, string> dictionary;
                if (info.Id == "") //Text
                {
                    dictionary = new Dictionary<string, string>
                    {
                        [codeLocaleName] = info.CutStr
                    };
                    foreach (var s in LocaleNames)
                    {
                        dictionary.TryAdd(s, "");
                    }

                    info.Id = name + ++index;
                    info.UpdateStr =
                        info.OrdStr.Replace($"\"{info.CutStr}\"", $"Helpers.I18N[\"{info.Id}\"]");
                    if (info.Flag == 1)
                    {
                        info.Flag = 2;
                    }
                }
                else //Helpers.I18N[] or 重复赋值
                {
                    if (info.Flag == 1)
                    {
                        info.Flag = 2;
                    }
                    else if (info.Flag > 1)
                    {
                        continue;
                    }

                    string id = info.Id; //旧id
                    dictionary = ordLocale[id];
                    info.CutStr = dictionary[codeLocaleName]; //<tipId, <Locale, Tip>>
                    info.Id = name + ++index;
                    info.UpdateStr = info.OrdStr.Replace(id, info.Id);
                }

                info.UpdateStr = info.UpdateStr.Replace("\\\"", "\"").Replace(@"\n", "\n");
                newLocale.Add(info.Id, dictionary);
            }
        }

        newLocale.ToJson(PathStr.ALL);

        if (isReplaceCodeFile)
        {
            ReplaceCodeFile(map);
        }
    }

    private static void ReplaceCodeFile(Dictionary<string, List<I18NInfo>> map)
    {
        foreach (var (key, value) in map)
        {
            string text = File.ReadAllText(key);
            foreach (var info in value)
            {
                text = text.Replace(info.OrdStr, info.UpdateStr);
            }

            File.WriteAllText(key, text);
        }
    }

    private static class PathStr
    {
        public const string CD = @"..\..\..\..\..\";
        public const string FILES = CD + @".config\i18n\";
        public const string ALL = FILES + "error_all.json";
    }

    private static void JsonToTips(string localeName)
    {
        if (!LocaleNames.Contains(localeName))
        {
            throw new ArgumentException();
        }

        var ordLocale = PathStr.ALL.ToEntity<Dictionary<string, Dictionary<string, string>>>();
        foreach (var dictionary in ordLocale.Values)
        {
            if (dictionary[localeName] == "")
            {
                throw new ArgumentException();
            }
        }

        Dictionary<string, List<I18NInfo>> map = new();
        foreach (var file in Directory.GetFiles(PathStr.CD, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("Helpers.cs")) continue;
            if (file.Contains("Demos\\I18nUtil")) continue;
            ReadOnlySpan<char> text = File.ReadAllText(file).AsSpan();

            var s = "Helpers.I18N[";
            int index = 0;
            while (true)
            {
                int n = text[index..].IndexOf(s);
                if (n == -1)
                {
                    break;
                }

                index += n + s.Length;
                n = text[index..].IndexOf(']');
                ReadOnlySpan<char> content = text.Slice(index + 1, n - 2);
                index += n;

                if (!map.ContainsKey(file))
                {
                    map[file] = new List<I18NInfo>();
                }

                I18NInfo info = new I18NInfo();
                Console.WriteLine(content.ToString());
                info.OrdStr = $"Helpers.I18N[\"{content.ToString()}\"]";
                info.UpdateStr = $"\"{ordLocale[content.ToString()][localeName]}\"".Replace("\\\"", "\"")
                    .Replace(@"\n", "\n");
                map[file].Add(info);
            }
        }

        ReplaceCodeFile(map);
    }

    private static void AllToAt()
    {
        var ordLocale =
            PathStr.ALL.ToEntity<Dictionary<string, Dictionary<string, string>>>(); //<tipId, <localeId,tip> >
        var newLocales = new Dictionary<string, Dictionary<string, string>>(); //<localeId, <tipId,tip> >
        foreach (var (tipId, value) in ordLocale)
        {
            foreach (var (localeId, tip) in value)
            {
                if (!newLocales.ContainsKey(localeId))
                {
                    newLocales[localeId] = new Dictionary<string, string>();
                }

                newLocales[localeId].Add(tipId, tip);
            }
        }

        foreach (var (ex, obj) in newLocales)
        {
            string ext = ex == ".default" ? "" : ex;
            obj.ToJson(PathStr.FILES + "error" + ext + ".json");
        }
    }

    private class I18NInfo
    {
        public string OrdStr;
        public string UpdateStr;
        public string CutStr;
        public string Id = "";
        public int Flag = 0; //可以忽略

        public I18NInfo()
        {
        }

        public I18NInfo(string ordStr)
        {
            OrdStr = ordStr;
            int l = OrdStr.IndexOf('\"') + 1;
            int r = OrdStr.LastIndexOf('\"');

            CutStr = OrdStr[l..r];
        }
    }

    public static void ToJson(this object data, string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    public static T ToEntity<T>(this string path) where T : new()
    {
        return File.Exists(path) ? (JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T()) : new T();
    }
}