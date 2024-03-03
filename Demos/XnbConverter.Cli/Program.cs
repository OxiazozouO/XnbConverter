using System.Reflection;
using System.Text;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;
using static XnbConverter.Cli.Process;

namespace XnbConverter.Cli;

public static class Program
{
    public static void Main(string[] args)
    {
        Helpers.NativeMethods.Init();
        if (1 == 1)
        {
            // if (args[0] == "test")
            // {
            //     ReckonByTime(@"auto -c -i .\packed -o .\unpacked");
            //     return;
            // }
            if (args.Length > 0)
            {
                ReckonByTime(args);
                return;
            }

            // Update();
            ReckonByTime("help");
            while (true)
            {
                Console.Write("->");
                string? str = Console.ReadLine();
                ReckonByTime(str);
            }
        }
        else
        {
            string[] arr = new string[]
            {
                @"auto -c -i .\packed -o .\unpacked",
                @"auto    -c    -i     .\packed          -o   .\unpacked",
                @"auto        -i     .\pac  ked          -o   .\unpa   ked",
                @"1",
                @"unpack -i",
                @"unpack help",
            };
            foreach (var s in arr)
            {
                int n = 1;
                while (n-- > 0)
                {
                    Console.WriteLine(s);
                    ReckonByTime(s);
                    Console.WriteLine("\n-------------------------\n");
                }
            }
        }
    }

    private static void ReckonByTime(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = args[i].Trim();
        }

        string cmd = string.Join(' ', args);
        ReckonByTime(cmd);
    }

    private static void ReckonByTime(string? args)
    {
        if (args is null or "" || args.Trim(' ') is "")
        {
            return;
        }

        var obj = Option.ParseCmd(args);
        switch (obj)
        {
            case CmdContent c:
            {
                Console.WriteLine(c.ToString());
                Get(c);
                Texture2D.WaitAll();
                Task.WaitAll();
                Log.Save();
                break;
            }
            case string s:
                Log.Message(s);
                break;
            case Action a:
                a.Invoke();
                break;
        }
    }

    // private static void Update()
    // {
    // }

    public class Option
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

        private object? Value
        {
            get => _value;
            set
            {
                _value = value;
                OnValueChanged?.Invoke(value);
            }
        }

        public event Action<object>? OnValueChanged;

        private readonly OptionType _type;
        private Dictionary<string, Option> _next;

        private Option(OptionType type)
        {
            _type = type;
        }

        private void SetHelpText(string help)
        {
            _helpText = help;
        }

        private static readonly Option Instance;
        private static readonly Option Bad;

        static Option()
        {
            Bad = new(OptionType.Help);
            var optionPack = new Dictionary<string, Option>()
            {
                ["c"] = new(OptionType.Bool),
                ["i"] = new(OptionType.String),
                ["o"] = new(OptionType.String),
            };
            Instance = new Option(OptionType.None)
            {
                _next = new Dictionary<string, Option>()
                {
                    ["auto"] = new(OptionType.Int),
                    ["unpack"] = new(OptionType.Int),
                    ["pack"] = new(OptionType.Int),
                    ["version"] = new(OptionType.Help),
                    // ["update"] = new(OptionType.Fun),
                }
            };
            Instance._next["auto"]._next =
                Instance._next["unpack"]._next =
                    Instance._next["pack"]._next = optionPack;
            // Instance._next["update"].Value = (Action)Update;
            var assembly = Assembly.GetExecutingAssembly().GetName();
            string v = assembly.Name + " v" + assembly.Version;
            Instance._next["version"].Value = v;
            Instance._next["version"].SetHelpText(v);
            BuildI18N();
        }

        public static void BuildI18N()
        {
            var optionsPack = Instance._next["auto"];
            optionsPack._next["c"].SetHelpText(Helpers.I18N["Program.1"]);
            optionsPack._next["i"].SetHelpText(Helpers.I18N["Program.2"]);
            optionsPack._next["o"].SetHelpText(Helpers.I18N["Program.3"]);
            Instance._next["auto"].SetHelpText(Helpers.I18N["Program.4"]);
            Instance._next["unpack"].SetHelpText(Helpers.I18N["Program.5"]);
            Instance._next["pack"].SetHelpText(Helpers.I18N["Program.6"]);
            // Instance._next["version"].SetHelpText(Helpers.I18N["Program.7"]);
            // Instance._next["update"].SetHelpText(Helpers.I18N["Program.8"]);
            Bad.SetHelpText(Helpers.I18N["Program.9"]);
            BuildHelp();
        }

        private static void BuildHelp()
        {
            var optionsPack = Instance._next["auto"];
            foreach (var (key, value) in optionsPack._next)
            {
                value._helpText = "-" + key + "  " + value._helpText;
            }

            StringBuilder sb2 = new StringBuilder();
            foreach (var (key, value) in Instance._next)
            {
                value._helpText = key + "  " + value._helpText;
                // sb2.Append(value._helpText).AppendLine();
                if (value._next != null)
                {
                    StringBuilder sb = new StringBuilder().AppendLine();
                    foreach (var (s, option) in value._next)
                    {
                        sb.Append("    ").Append(option._helpText).AppendLine();
                    }

                    value._helpText += sb.ToString();
                }

                sb2.Append(value._helpText).AppendLine();
            }

            Instance._helpText = sb2.ToString();
        }

        private static CmdContent BuildCEnt(string index)
        {
            CmdContent cmdContent = new CmdContent();
            var pack = Instance._next[index];
            pack.OnValueChanged += value => cmdContent.Mode = (Mode)value;
            pack._next["c"].OnValueChanged += value => cmdContent.IsEnableConcurrency = (bool)value;
            pack._next["i"].OnValueChanged += value => cmdContent.Input = value.ToString();
            pack._next["o"].OnValueChanged += value => cmdContent.Output = value.ToString();
            return cmdContent;
        }

        private static string[][] ParseStrings(string[] args)
        {
            string[][] ret = new string[args.Length][];
            for (var i = 0; i < args.Length; i++)
            {
                string s = args[i].Trim(' ');
                int n = s.IndexOf(' ');
                if (n == -1)
                {
                    ret[i] = new string[] { s.Trim(' ') };
                }
                else
                {
                    ret[i] = new string[]
                    {
                        s[..n].Trim(' '),
                        s[n..].Trim(' '),
                    };
                }
            }

            return ret;
        }

        public static object? ParseCmd(string args)
        {
            var cmd = ParseStrings(args.Split(" -"));
            return GetEnt(cmd);
        }

        private static object? GetEnt(string[][] args)
        {
            if (args[0][0] == "help")
            {
                return Instance._helpText;
            }

            if (Instance._next.TryGetValue(args[0][0], out var option))
            {
                if (args.Length == 1)
                {
                    if (option._next == null)
                    {
                        return option.Value;
                    }

                    if (args[0].Length == 1)
                    {
                        return Bad._helpText + option._helpText;
                    }
                    if (args[0][1] == "help")
                    {
                        return option._helpText;
                    }
                    return Bad._helpText + option._helpText;
                }

                if (option._next == null)
                {
                    return Bad._helpText + option._helpText;
                }
                else
                {
                    var cmd = BuildCEnt(args[0][0]);
                    switch (args[0][0])
                    {
                        case "pack":
                            option.Value = Mode.Pack;
                            break;
                        case "unpack":
                            option.Value = Mode.UnPack;
                            break;
                        case "auto":
                            option.Value = Mode.Pack | Mode.UnPack;
                            break;
                        default:
                            return "";
                    }

                    for (int i = 1; i < args.Length; i++)
                    {
                        if (option._next.TryGetValue(args[i][0], out var next))
                        {
                            if (args[i].Length == 1)
                            {
                                if (next._type == OptionType.Bool)
                                {
                                    next.Value = true;
                                }
                                else
                                {
                                    return Bad._helpText + next._helpText;
                                }
                            }
                            else if (args[i][1] == "help")
                            {
                                return next._helpText;
                            }
                            else
                            {
                                switch (next._type)
                                {
                                    case OptionType.Bool:
                                        return Bad._helpText + next._helpText;
                                    case OptionType.Help:
                                        return next._helpText;
                                    case OptionType.Fun:
                                        return next.Value;
                                    case OptionType.String:
                                        next.Value = args[i][1];
                                        break;
                                }
                            }
                        }
                        else
                        {
                            return Bad._helpText + option._helpText;
                        }
                    }

                    if (option._next == Instance._next["auto"]._next)
                    {
                        foreach (var s in new string[] { "i", "o" })
                        {
                            var op = option._next[s];
                            if (op.Value is null)
                            {
                                return Bad._helpText + op._helpText;
                            }

                            string path = op.Value.ToString();
                            path = Path.GetFullPath(path);
                            if (!File.Exists(path) && !Directory.Exists(path))
                            {
                                return Bad._helpText + op._helpText;
                            }
                        }
                    }

                    return cmd;
                }
            }

            return Bad._helpText + Instance._helpText;
        }
    }
}