using System.Diagnostics;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XnbConverter.Cli.Configurations;
using XnbConverter.Configurations;
using XnbConverter.Entity.Mono;

namespace XnbConverter.Cli;

public static class Program
{
    private class Option
    {
        private enum OptionType
        {
            None,
            Int,
            String,
            Help,
            Bool,
            Fun
        }

        private string _helpText;

        private object? _value;

        private readonly OptionType _type;

        private Dictionary<string, Option> _next;

        private static readonly Option Instance;

        private static readonly Option Bad;

        private object? Value
        {
            get => _value;
            set
            {
                _value = value;
                this.OnValueChanged?.Invoke(value);
            }
        }

        public event Action<object>? OnValueChanged;

        private Option(OptionType type)
        {
            _type = type;
        }

        private void SetHelpText(string help)
        {
            _helpText = help;
        }

        static Option()
        {
            Bad = new Option(OptionType.Help);
            Dictionary<string, Option> next = new Dictionary<string, Option>
            {
                ["c"] = new Option(OptionType.Bool),
                ["i"] = new Option(OptionType.String),
                ["o"] = new Option(OptionType.String)
            };
            Instance = new Option(OptionType.None)
            {
                _next = new Dictionary<string, Option>
                {
                    ["auto"] = new Option(OptionType.Int),
                    ["unpack"] = new Option(OptionType.Int),
                    ["pack"] = new Option(OptionType.Int),
                    ["version"] = new Option(OptionType.Help)
                }
            };
            Instance._next["auto"]._next = Instance._next["unpack"]._next = Instance._next["pack"]._next = next;
            AssemblyName name = Assembly.GetExecutingAssembly().GetName();
            string text = string.Format(Error.Program_9, name.Name, name.Version,
                "https://github.com/OxiazozouO/XnbConverter",
                "171405047@qq.com");
            Instance._next["version"].Value = text;
            Instance._next["version"].SetHelpText(text);
            BuildI18N();
        }

        private static void BuildI18N()
        {
            Option option = Instance._next["auto"];
            option._next["c"].SetHelpText(Error.Program_1);
            option._next["i"].SetHelpText(Error.Program_2);
            option._next["o"].SetHelpText(Error.Program_3);
            Instance._next["auto"].SetHelpText(Error.Program_4);
            Instance._next["unpack"].SetHelpText(Error.Program_5);
            Instance._next["pack"].SetHelpText(Error.Program_6);
            Bad.SetHelpText(Error.Program_7);
            BuildHelp();
        }

        private static void BuildHelp()
        {
            string key;
            Option value;
            foreach (KeyValuePair<string, Option> item in Instance._next["auto"]._next)
            {
                item.Deconstruct(out key, out value);
                string text = key;
                Option option = value;
                option._helpText = "-" + text + "  " + option._helpText;
            }

            StringBuilder stringBuilder = new StringBuilder();
            foreach (KeyValuePair<string, Option> item2 in Instance._next)
            {
                item2.Deconstruct(out key, out value);
                string text2 = key;
                Option option2 = value;
                option2._helpText = text2 + "  " + option2._helpText;
                if (option2._next != null)
                {
                    StringBuilder stringBuilder2 = new StringBuilder().AppendLine();
                    foreach (KeyValuePair<string, Option> item3 in option2._next)
                    {
                        item3.Deconstruct(out key, out value);
                        Option option3 = value;
                        stringBuilder2.Append("    ").Append(option3._helpText).AppendLine();
                    }

                    option2._helpText += stringBuilder2.ToString();
                }

                stringBuilder.Append(option2._helpText).AppendLine();
            }

            var s = @"
    packed                                   unpacked
    ├─1.xnb                                  ├─1.config
    ├─2.xnb                                  ├─1.png
    ├─3.xnb           unpack.bat             ├─2.config
    ├─folder1      ───────────────>          ├─2.tbin
    │  ├─4.xnb                               ├─3.config
    │  ├─5.xnb                               ├─3.json
    │  ├─6.xnb                               ├─3.png
    │  └─7.xnb                               ├─folder1
    ├─folder3          pack.bat              │  ├─4.config
    └─...          <───────────────          │  ├─4.xml
      ├─8.xnb                                │  ├─5.config
      ├─9.xnb                                │  ...
      └─10.xnb                               ...
";
            Instance._helpText = stringBuilder.Append(s).ToString();
        }

        private static Process.CmdContent BuildCEnt(string index)
        {
            Process.CmdContent cmdContent = new Process.CmdContent();
            Option option = Instance._next[index];
            option.OnValueChanged += delegate(object value) { cmdContent.Mode = (Process.Mode)value; };
            option._next["c"].OnValueChanged += delegate(object value)
            {
                cmdContent.IsEnableConcurrency = (bool)value;
            };
            option._next["i"].OnValueChanged += delegate(object value) { cmdContent.Input = value.ToString(); };
            option._next["o"].OnValueChanged += delegate(object value) { cmdContent.Output = value.ToString(); };
            return cmdContent;
        }

        private static string[][] ParseStrings(string[] args)
        {
            string[][] array = new string[args.Length][];
            for (int i = 0; i < args.Length; i++)
            {
                string text = args[i].Trim(' ');
                int num = text.IndexOf(' ');
                if (num == -1)
                {
                    array[i] = new string[1] { text.Trim(' ') };
                    continue;
                }

                int num2 = i;
                string[] obj = new string[2]
                {
                    text.Substring(0, num).Trim(' '),
                    null
                };
                string text2 = text;
                int num3 = num;
                obj[1] = text2.Substring(num3, text2.Length - num3).Trim(' ');
                array[num2] = obj;
            }

            return array;
        }

        public static object? ParseCmd(string args)
        {
            return GetEnt(ParseStrings(args.Split(" -")));
        }

        private static object? GetEnt(string[][] args)
        {
            if (args[0][0] == "help")
            {
                return Instance._helpText;
            }

            if (Instance._next.TryGetValue(args[0][0], out Option value))
            {
                if (args.Length == 1)
                {
                    if (value._next == null)
                    {
                        return value.Value;
                    }

                    if (args[0].Length == 1)
                    {
                        return Bad._helpText + value._helpText;
                    }

                    if (args[0][1] == "help")
                    {
                        return value._helpText;
                    }

                    return Bad._helpText + value._helpText;
                }

                if (value._next == null)
                {
                    return Bad._helpText + value._helpText;
                }

                Process.CmdContent result = BuildCEnt(args[0][0]);
                switch (args[0][0])
                {
                    case "pack":
                        value.Value = Process.Mode.Pack;
                        break;
                    case "unpack":
                        value.Value = Process.Mode.UnPack;
                        break;
                    case "auto":
                        value.Value = Process.Mode.Pack | Process.Mode.UnPack;
                        break;
                    default:
                        return "";
                }

                for (int i = 1; i < args.Length; i++)
                {
                    if (value._next.TryGetValue(args[i][0], out Option value2))
                    {
                        if (args[i].Length == 1)
                        {
                            if (value2._type == OptionType.Bool)
                            {
                                value2.Value = true;
                                continue;
                            }

                            return Bad._helpText + value2._helpText;
                        }

                        if (args[i][1] == "help")
                        {
                            return value2._helpText;
                        }

                        switch (value2._type)
                        {
                            case OptionType.Bool:
                                return Bad._helpText + value2._helpText;
                            case OptionType.Help:
                                return value2._helpText;
                            case OptionType.Fun:
                                return value2.Value;
                            case OptionType.String:
                                value2.Value = args[i][1];
                                break;
                        }

                        continue;
                    }

                    return Bad._helpText + value._helpText;
                }

                if (value._next == Instance._next["auto"]._next)
                {
                    string[] array = { "i", "o" };
                    foreach (string key in array)
                    {
                        Option option = value._next[key];
                        if (option.Value == null)
                        {
                            return Bad._helpText + option._helpText;
                        }

                        string path = option.Value.ToString();
                        path = Path.GetFullPath(path);
                        if (!File.Exists(path) && !Directory.Exists(path))
                        {
                            return Bad._helpText + option._helpText;
                        }
                    }
                }

                return result;
            }

            return Bad._helpText + Instance._helpText;
        }
    }

    public static void Main(string[] args)
    {
        ConsoleLogger.Build();
        ConsoleRootPath.Build();

        if (args.Length != 0)
        {
            ReckonByTime(args);
            return;
        }

        ReckonByTime("help");
        UpdateByGitHub("v1.0");
        while (true)
        {
            ReckonByTime(Console.ReadLine());
        }
    }

    private static void UpdateByGitHub(string v)
    {
        Task.Run(async () =>
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(2);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
                var res = await httpClient.GetStringAsync(
                    "https://api.github.com/repos/OxiazozouO/XnbConverter/releases/latest");
                var data = JsonConvert.DeserializeObject<JObject>(res);
                var name = data["name"]?.ToString();
                var url = data["html_url"]?.ToString();

                if (name != v && url != null)
                {
                    Logger.Warn(Error.Program_10, name, url);
                    System.Diagnostics.Process.Start(url);
                }
            }
            catch (Exception)
            {
                // ignored
            }

            Console.Write("->");
        });
    }

    private static void test()
    {
        ReckonByTime("auto -c -i .\\unpacked -o .\\packed");
    }

    private static void test_command()
    {
        string[] array = new string[6]
        {
            "auto -c -i .\\packed -o .\\unpacked", "auto    -c    -i     .\\packed          -o   .\\unpacked",
            "auto        -i     .\\pac  ked          -o   .\\unpa   ked", "1", "unpack -i", "unpack help"
        };
        foreach (string text in array)
        {
            int num = 1;
            while (num-- > 0)
            {
                Console.WriteLine(text);
                ReckonByTime(text);
                Console.WriteLine('\n' + new string('-', 25) + '\n');
            }
        }
    }

    private static void test_loop()
    {
        int num = 10;
        while (num-- > 0)
        {
            ReckonByTime("auto -c -i .\\packed -o .\\unpacked");
        }
    }

    private static void ReckonByTime(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = args[i].Trim();
        }

        ReckonByTime(string.Join(' ', args));
    }

    private static void ReckonByTime(string? args)
    {
        bool flag = string.IsNullOrEmpty(args);
        if (flag || args?.Trim(' ') == "")
        {
            return;
        }

        object obj = Option.ParseCmd(args);
        if (!(obj is Process.CmdContent cmd))
        {
            if (!(obj is string message))
            {
                if (obj is Action action)
                {
                    action();
                }
            }
            else
            {
                Logger.Message(message);
            }
        }
        else
        {
            Stopwatch timer = Stopwatch.StartNew();
            Process.Get(cmd);
            Texture2D.WaitAll();
            Task.WaitAll();
            Logger.Save();
            PrintRunTime(timer);
        }
    }

    private static void PrintRunTime(Stopwatch timer)
    {
        timer.Stop();
        TimeSpan elapsed = timer.Elapsed;
        Logger.Message($"{(int)elapsed.TotalSeconds}.{elapsed.Milliseconds}s");
    }
}