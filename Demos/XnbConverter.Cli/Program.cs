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
    /// <summary>命令名 -&gt; 该命令的帮助行（来自 Error.resx）。</summary>
    private static readonly Dictionary<string, string> Commands = new Dictionary<string, string>
    {
        ["auto"] = Error.Program_4,
        ["unpack"] = Error.Program_5,
        ["pack"] = Error.Program_6
    };

    /// <summary>选项字母 -&gt; 该选项的帮助行。</summary>
    private static readonly Dictionary<string, string> Options = new Dictionary<string, string>
    {
        ["c"] = Error.Program_1,
        ["i"] = Error.Program_2,
        ["o"] = Error.Program_3
    };

    private static string _helpText;

    private static string _versionText;

    public static void Main(string[] args)
    {
        ConsoleLogger.Build();
        ConsoleRootPath.Build();

        if (args.Length != 0)
        {
            ReckonByTime(args);
            return;
        }

        // 无参数：打印帮助后进入交互模式
        ReckonByTime("help");
        UpdateByGitHub("v1.0");
        while (true)
        {
            ReckonByTime(Console.ReadLine());
        }
    }

    private static string VersionText()
    {
        if (_versionText == null)
        {
            AssemblyName name = Assembly.GetExecutingAssembly().GetName();
            _versionText = string.Format(Error.Program_9, name.Name, name.Version,
                "https://github.com/OxiazozouO/XnbConverter", "171405047@qq.com");
        }

        return _versionText;
    }

    /// <summary>命令自己的帮助：命令行 + 缩进的三个选项。</summary>
    private static string CommandHelp(string command)
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append(command).Append("  ").Append(Commands[command]).AppendLine();
        foreach (KeyValuePair<string, string> option in Options)
        {
            stringBuilder.Append("    -").Append(option.Key).Append("  ").Append(option.Value).AppendLine();
        }

        return stringBuilder.ToString();
    }

    private static string OptionHelp(string option)
    {
        return "-" + option + "  " + Options[option];
    }

    private static string HelpText()
    {
        if (_helpText == null)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (string command in Commands.Keys)
            {
                stringBuilder.Append(CommandHelp(command)).AppendLine();
            }

            stringBuilder.Append("version  ").Append(VersionText()).AppendLine().AppendLine();
            stringBuilder.Append(@"
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
      ├─9.xnb                                │  ├─...
      └─10.xnb                               ...
");
            _helpText = stringBuilder.ToString();
        }

        return _helpText;
    }

    /// <summary>
    /// 解析一行命令。成功时返回 <see cref="Process.CmdContent"/>，
    /// 否则返回应当打印的文本（帮助或错误提示）。
    /// </summary>
    private static object Parse(string args)
    {
        // 按 " -" 切开：第一段是命令（也可能跟着东西，如 "unpack help"），后面每段是「选项字母[ 值]」
        string[] parts = args.Split(" -");
        string[] array = SplitFlag(parts[0]);
        string command = array[0];
        if (command == "help")
        {
            return HelpText();
        }

        if (command == "version")
        {
            return VersionText();
        }

        Process.Mode mode;
        switch (command)
        {
            case "pack":
                mode = Process.Mode.Pack;
                break;
            case "unpack":
                mode = Process.Mode.UnPack;
                break;
            case "auto":
                mode = Process.Mode.Pack | Process.Mode.UnPack;
                break;
            default:
                return Error.Program_7 + HelpText();
        }

        if (parts.Length == 1 && array.Length == 1)
        {
            // 只给了命令名，没有选项
            return Error.Program_7 + CommandHelp(command);
        }

        // 命令名后面直接跟的东西（如 "unpack help" 里的 help）也要当成一个选项段来校验
        List<string> list = new List<string>();
        if (array.Length > 1)
        {
            list.Add(array[1]);
        }

        for (int i = 1; i < parts.Length; i++)
        {
            list.Add(parts[i]);
        }

        Process.CmdContent cmdContent = new Process.CmdContent
        {
            Mode = mode
        };
        string? input = null;
        string? output = null;
        foreach (string text in list)
        {
            string[] pair = SplitFlag(text);
            string option = pair[0];
            if (!Options.ContainsKey(option))
            {
                return Error.Program_7 + CommandHelp(command);
            }

            if (pair.Length == 1)
            {
                // 没带值：只有 -c 合法
                if (option != "c")
                {
                    return Error.Program_7 + OptionHelp(option);
                }

                cmdContent.IsEnableConcurrency = true;
                continue;
            }

            if (pair[1] == "help")
            {
                return OptionHelp(option);
            }

            if (option == "c")
            {
                return Error.Program_7 + OptionHelp(option);
            }

            if (option == "i")
            {
                input = pair[1];
            }
            else
            {
                output = pair[1];
            }
        }

        if (input == null || output == null)
        {
            return Error.Program_7 + OptionHelp(input == null ? "i" : "o");
        }

        // 输入输出在调用时都必须真实存在
        string inputPath = Path.GetFullPath(input);
        string outputPath = Path.GetFullPath(output);
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            return Error.Program_7 + OptionHelp("i");
        }

        if (!File.Exists(outputPath) && !Directory.Exists(outputPath))
        {
            return Error.Program_7 + OptionHelp("o");
        }

        cmdContent.Input = input;
        cmdContent.Output = output;
        return cmdContent;
    }

    /// <summary>把「-c」或「-i 值」拆成字母与值（值里不再含空格）。</summary>
    private static string[] SplitFlag(string text)
    {
        string trimmed = text.Trim(' ');
        int space = trimmed.IndexOf(' ');
        if (space == -1)
        {
            return new[] { trimmed };
        }

        return new[] { trimmed.Substring(0, space).Trim(' '), trimmed.Substring(space).Trim(' ') };
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

        object obj = Parse(args);
        if (!(obj is Process.CmdContent cmd))
        {
            if (obj is string message)
            {
                Logger.Message(message);
            }
            else if (obj is Action action)
            {
                action();
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
