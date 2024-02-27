using System.Net.Http.Json;
using Newtonsoft.Json;

public class Program
{
    public static void Main(string[] args)
    {
        // TipsToMap();
        MapToTips();
    }

    private static void TipsToMap()
    {
        string[] getArr = new[]
        {
            "new XnbError", 
            "new ReaderTypeError",
            "Log.Info",
            "Log.Debug",
            "Log.Warn",
            "Log.Error",
            "TbinError"
        };
        string path = @"..\..\..\..\..\";

        Dictionary<string, List<I18nInfo>> map = new();
        List<I18nInfo> infos = new List<I18nInfo>();
        foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("Helpers.cs")) continue;
            if (file.Contains("Demos\\I18nUtil")) continue;
            string textCopy = File.ReadAllText(file);
            ReadOnlySpan<char> text = textCopy.AsSpan();

            for (var j = 0; j < getArr.Length; j++)
            {
                var s = getArr[j];
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
                        if (text[i] != '(' && text[i] != ')')
                        {
                            continue;
                        }

                        if (text[i] == '(')
                        {
                            ++ans;
                        }

                        if (ans == 1)
                        {
                            if (text[i] == ')')
                            {
                                content = text[(index + 1)..i];
                                index = i + 1;
                                break;
                            }
                        }


                        if (text[i] == ')')
                        {
                            ans--;
                        }
                    }

                    if (content.IndexOf('\"') == -1) continue;
                    if (!map.ContainsKey(file))
                    {
                        map[file] = new List<I18nInfo>();
                    }

                    I18nInfo info = new I18nInfo(content.ToString());
                    if (content.IndexOf("Helpers.I18N[\"") != -1)
                    {
                        info.Id = info.CutStr;
                    }

                    infos.Add(info);
                    map[file].Add(info);
                }
            }
        }

        var f = Directory.GetFiles(path + @".config\i18n\", "error.*", SearchOption.AllDirectories)[0];
        Dictionary<string, string> ord = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(f));


        foreach (var (key, value) in map)
        {
            string name = Path.GetFileNameWithoutExtension(key) + ".";
            for (var i = 0; i < value.Count; i++)
            {
                var info = value[i];
                if (info.Id != null)
                {
                    string id = info.Id;
                    info.CutStr = ord[id];
                    info.Id = name + i;
                    info.UpdateStr = info.OrdStr.Replace(id, info.Id);
                }
                else
                {
                    info.Id = name + i;
                    info.UpdateStr =
                        info.OrdStr.Replace($"\"{info.CutStr}\"", $"Helpers.I18N[\"{info.Id}\"]");
                }

                info.UpdateStr=info.UpdateStr.Replace("\\\"", "\"").Replace(@"\n","\n");
            }
        }

        Dictionary<string, string> ret = infos.ToDictionary(info => info.Id, info => info.CutStr);

        foreach (var (key, value) in map)
        {
            string text = File.ReadAllText(key);
            foreach (var info in value)
            {
                text = text.Replace(info.OrdStr, info.UpdateStr);
            }
            File.WriteAllText(key, text);
        }

        File.WriteAllText(path + @".config\i18n\error.zh-CN.json",
            JsonConvert.SerializeObject(ret, Formatting.Indented));
    }

    public static void MapToTips()
    {
        string path = @"..\..\..\..\..\";
        Dictionary<string, string> ord = JsonConvert.DeserializeObject<Dictionary<string, string>>(
            File.ReadAllText(path + @".config\i18n\error.zh-CN.json"));
        Dictionary<string, List<I18nInfo>> map = new();
        List<I18nInfo> infos = new List<I18nInfo>();
        foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
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
                ReadOnlySpan<char> content =  text.Slice(index+1,n-2);
                index += n;
                
                if (!map.ContainsKey(file))
                {
                    map[file] = new List<I18nInfo>();
                }

                I18nInfo info = new I18nInfo();
                Console.WriteLine(content.ToString());
                info.OrdStr = $"Helpers.I18N[\"{content.ToString()}\"]";
                info.UpdateStr = $"\"{ord[content.ToString()]}\"".Replace("\\\"", "\"").Replace(@"\n","\n");

                infos.Add(info);
                map[file].Add(info);
            }
        }
        foreach (var (key, value) in map)
        {
            string text = File.ReadAllText(key);
            foreach (var info in value)
            {
                text = text.Replace(info.OrdStr, info.UpdateStr);
            }
            File.WriteAllText(key,text);
        }
    }

    private class I18nInfo
    {
        public string OrdStr;
        public string UpdateStr;
        public string CutStr;
        public string Id;

        public I18nInfo()
        {
        }
        public I18nInfo(string ordStr)
        {
            OrdStr = ordStr;
            int l = OrdStr.IndexOf('\"') + 1;
            int r = OrdStr.LastIndexOf('\"');

            CutStr = OrdStr[l..r];
        }
    }
}