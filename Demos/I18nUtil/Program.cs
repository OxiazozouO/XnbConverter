using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace I18nUtil;

public static class Program
{
    private static readonly string[] LocaleNames =
    {
        ".zh-CN",
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
        ".tr-TR"
    };

    private static readonly string[] GetArr =
    {
        "new XnbError",
        "new ReaderTypeError",
        "Log.Info",
        "Log.Debug",
        "Log.Warn",
        "Log.Error",
        "TbinError",
        "SetHelpText"
    };

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
        JsonToTips(".zh-CN", true);
    }

    private static void CodeToJson(string loadLocale, bool isReplaceCodeFile = false)
    {
        if (!LocaleNames.Contains(loadLocale))
        {
            throw new ArgumentException();
        }

        Dictionary<string, List<EditInfo>> map = new(); //<file, (EditInfo, int)>
        var infoSet = new InfoSet();
        //<tipId, <Locale, Tip>>
        var ordLocale = PathStr.ALL.ToEntity<Dictionary<string, Dictionary<string, string>>>();
        Dictionary<string, KeyValuePair<string, Dictionary<string, string>>> ordLocale2 = new();
        foreach (var pair in ordLocale) ordLocale2[pair.Value[loadLocale]] = pair;

        foreach (var file in Directory.GetFiles(PathStr.CD, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("Helpers.cs")) continue;
            if (file.Contains("Demos\\I18nUtil")) continue;
            var textCopy = File.ReadAllText(file);
            var text = textCopy.AsSpan();

            foreach (var s in GetArr)
            {
                var index = 0;
                while (true)
                {
                    var n = text[index..].IndexOf(s + '(');
                    if (n == -1) break;

                    index += n + s.Length;
                    var ans = 0;
                    ReadOnlySpan<char> content = "";
                    for (var i = index; i < text.Length; i++)
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

                    if (content.IndexOf('\"') == -1)
                    {
                        Console.WriteLine("\n\n坏数据：" + content.ToString() + " 在" + file + "文件\n\n");
                        continue;
                    }

                    var str = content.ToString();
                    if (str == "") continue;

                    if (!map.ContainsKey(file)) map[file] = new List<EditInfo>();

                    var info = new EditInfo(str);
                    if (info.RepStr.Contains('\"'))
                    {
                        Console.WriteLine("\n\n坏数据：" + info.RepStr + " 在" + file + "文件\n\n");
                        throw new ArgumentException();
                    }

                    Console.WriteLine(str);
                    if (content.IndexOf("Helpers.I18N[\"") == -1)
                    {
                        if (ordLocale2.TryGetValue(info.RepStr.Replace(@"\n", "\n"), out var dictionary))
                            infoSet.Add(info, dictionary.Value, dictionary.Value[loadLocale]);
                        else
                            infoSet.Add(info);
                    }
                    else
                    {
                        info.EditFlag = EditFlags.UPDATE;
                        if (ordLocale.TryGetValue(info.RepStr, out var dictionary))
                            infoSet.Add(info, dictionary, dictionary[loadLocale]);
                        else
                            throw new ArgumentException(); //原来的索引丢失了
                    }

                    map[file].Add(info);
                }
            }
        }

        Dictionary<string, int> set = new();
        foreach (var (key, value) in map)
        {
            var name = Path.GetFileNameWithoutExtension(key) + ".";
            if (!set.ContainsKey(name))
            {
                set.Add(name, 0);
            }
            
            foreach (var info in value)
            {
                var ind = infoSet.InfoList[info];
                if (infoSet.IdSet[ind] == "")
                {
                    infoSet.IdSet[ind] = name + ++set[name];
                }

                var id = infoSet.IdSet[ind];
                var repStr = info.RepStr;
                switch (info.EditFlag)
                {
                    case EditFlags.UPDATE:
                        info.CutStr = id;
                        break;
                    case EditFlags.ADD:
                        info.CutStr = $"Helpers.I18N[\"{id}\"]";
                        repStr = "\"" + repStr + "\"";
                        break;
                }

                info.UpdateStr = info.OrdStr.Replace(repStr, info.CutStr)
                    .Replace(@"\n", "\n");
            }
        }

        infoSet.GetLocaleMap(loadLocale, out var newLocale);

        newLocale.ToJson(PathStr.ALL);

        if (isReplaceCodeFile) ReplaceCodeFile(map);
    }

    private static void ReplaceCodeFile(Dictionary<string, List<EditInfo>> map)
    {
        foreach (var (key, value) in map)
        {
            var text = File.ReadAllText(key);
            foreach (var info in value) text = text.Replace(info.OrdStr, info.UpdateStr);

            File.WriteAllText(key, text);
        }
    }

    private static void JsonToTips(string localeName, bool isReplaceCodeFile = false)
    {
        if (!isReplaceCodeFile) return;

        if (!LocaleNames.Contains(localeName)) throw new ArgumentException();

        var ordLocale = PathStr.ALL.ToEntity<Dictionary<string, Dictionary<string, string>>>();
        foreach (var dictionary in ordLocale.Values)
        {
            var s = dictionary[localeName];
            if (s == "" || s.Contains('"')) throw new ArgumentException();
        }

        Dictionary<string, List<EditInfo>> map = new();
        foreach (var file in Directory.GetFiles(PathStr.CD, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("Helpers.cs")) continue;
            if (file.Contains("Demos\\I18nUtil")) continue;
            var text = File.ReadAllText(file).AsSpan();

            var s = "Helpers.I18N[";
            var index = 0;
            while (true)
            {
                var n = text[index..].IndexOf(s);
                if (n == -1) break;

                index += n + s.Length;
                n = text[index..].IndexOf(']');
                var content = text.Slice(index + 1, n - 2);
                index += n;

                if (!map.ContainsKey(file)) map[file] = new List<EditInfo>();

                var str = content.ToString();
                var info = new EditInfo("Helpers.I18N[\"" + str + "\"]");
                Console.WriteLine(content.ToString()+"    "+ordLocale[str][localeName]);
                info.UpdateStr = ("\"" + ordLocale[str][localeName] + "\"")
                    .Replace("\n", @"\n");
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
        foreach (var (localeId, tip) in value)
        {
            if (!newLocales.ContainsKey(localeId)) newLocales[localeId] = new Dictionary<string, string>();

            newLocales[localeId].Add(tipId, tip);
        }

        foreach (var (ex, obj) in newLocales)
        {
            var ext = ex == ".default" ? "" : ex;
            obj.ToJson(PathStr.FILES + "error" + ext + ".json");
        }
    }

    public static void ToJson(this object data, string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    public static T ToEntity<T>(this string path) where T : new()
    {
        return File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T() : new T();
    }

    private static class PathStr
    {
        public const string CD = @"..\..\..\..\..\";
        public const string FILES = CD + @".config\i18n\";
        public const string ALL = FILES + "error_all.json";
    }

    private class EditInfo
    {
        public readonly string OrdStr;
        public readonly string RepStr;
        public string CutStr;
        public int EditFlag = EditFlags.ADD;
        public string UpdateStr;

        public EditInfo(string ordStr)
        {
            OrdStr = ordStr;
            var l = OrdStr.IndexOf('\"') + 1;
            var r = OrdStr.LastIndexOf('\"');
            RepStr = OrdStr[l..r];
        }
    }

    private static class EditFlags
    {
        public const int ADD = 1; //新增
        public const int UPDATE = 2; //修改
    }

    private class InfoSet
    {
        public readonly List<string> CutSet = new();
        public readonly Dictionary<string, int> CutStrMap = new();
        public readonly List<string> IdSet = new();

        public readonly Dictionary<EditInfo, int> InfoList = new(); //<EditInfo, CutStrIndex>
        public readonly List<Dictionary<string, string>> LocaleList = new();

        public void Add(EditInfo info)
        {
            if (InfoList.ContainsKey(info)) return;

            if (CutStrMap.TryGetValue(info.RepStr, out var ind))
            {
                InfoList.Add(info, ind);
                return;
            }

            var index = CutSet.Count;
            CutSet.Add(info.RepStr);
            IdSet.Add("");
            CutStrMap.Add(info.RepStr, index);
            InfoList.Add(info, index);
            LocaleList.Add(new Dictionary<string, string>());
        }

        public void Add(EditInfo info, Dictionary<string, string> dictionary, string cut)
        {
            if (InfoList.ContainsKey(info)) return;

            if (CutStrMap.TryGetValue(cut, out var ind))
            {
                InfoList.Add(info, ind);
                return;
            }

            var index = CutSet.Count;
            CutSet.Add(cut);
            IdSet.Add("");
            CutStrMap.Add(cut, index);
            InfoList.Add(info, index);
            LocaleList.Add(dictionary);
        }

        public void GetLocaleMap(string localeName, out Dictionary<string, Dictionary<string, string>> newLocale)
        {
            newLocale = new Dictionary<string, Dictionary<string, string>>();
            var n = IdSet.Count;
            for (var i = 0; i < n; i++)
            {
                var dictionary = LocaleList[i];
                if (dictionary.Count == 0) dictionary.Add(localeName, CutSet[i]);

                foreach (var (key, value) in dictionary)
                {
                    dictionary[key] = value.Replace("\\\"", "\"").Replace(@"\n", "\n");
                }
                
                foreach (var name in LocaleNames) dictionary.TryAdd(name, "");

                newLocale.Add(IdSet[i], dictionary);
            }
        }
    }
}