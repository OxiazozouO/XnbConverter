using System;
using System.Threading.Tasks;
using CommandLine;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;
using static XnbConverter.Cli.Process;

namespace XnbConverter.Cli;

public class Program
{
    public static void Main(string[] args)
    {
        Helpers.NativeMethods.Init();
        if (1 == 1)
        {
            if (args.Length > 0)
            {
                Run(Parser.Default.ParseArguments<Version, Auto, Pack, Unpack>(args));
            }
            else while (true)
            {
                Console.Write("->");
                string? str = Console.ReadLine();
                if (str is null or "") continue;
                args = str.Split(' ');
                Run(Parser.Default.ParseArguments<Version, Auto, Pack, Unpack>(args));
            }
        }
        else
        {
            string[] unpack = @"auto -c -i .\packed -o .\unpacked".Split(' ');
            string[] pack   = @"auto -c -i .\unpacked -o .\packed".Split(' ');
            var runUnpack = Parser.Default.ParseArguments<Version, Auto, Pack, Unpack>(unpack);
            var runPack   = Parser.Default.ParseArguments<Version, Auto, Pack, Unpack>(pack  );
            int n = 100;
            while (n-- > 0)
            {
                ReckonByTime(runUnpack);
                // ReckonByTime(runPack);
            }
        }
    }
    
    [Verb("auto", HelpText = "根据输入的文件自动拆装xnb")]
    private class Auto
    {
        [Option('c', MetaValue = "concurrency", HelpText = "是否开启并发")]
        public bool EnableConcurrency { get; set; } = false;

        [Option('i', MetaValue = "input", HelpText = "输入的文件或文件夹")]
        public string? Input { get; set; }

        [Option('o', MetaValue = "output", HelpText = "输出的文件夹")]
        public string? Output { get; set; }
    }
    
    [Verb("unpack", HelpText = "解包")]
    private class Unpack
    {
        [Option('c', MetaValue = "concurrency", HelpText = "是否开启并发")]
        public bool EnableConcurrency { get; set; } = false;

        [Option('i', MetaValue = "input", HelpText = "输入的文件或文件夹")]
        public string? Input { get; set; }

        [Option('o', MetaValue = "output", HelpText = "输出的文件夹")]
        public string? Output { get; set; }
    }

    [Verb("pack", HelpText = "打包")]
    private class Pack
    {
        [Option('c', MetaValue = "concurrency", HelpText = "是否开启并发")]
        public bool EnableConcurrency { get; set; } = false;

        [Option('i', MetaValue = "input", HelpText = "输入的文件或文件夹")]
        public string? Input { get; set; }

        [Option('o', MetaValue = "output", HelpText = "输出的文件夹")]
        public string? Output { get; set; }
    }

    private static void ReckonByTime(ParserResult<object>? fun)
    {
        DateTime t1 = DateTime.Now;
        Run(fun);
        Console.WriteLine(DateTime.Now.Subtract(t1).TotalSeconds);
    }

    private static void Run(ParserResult<object>? fun)
    {
        fun.MapResult(
            (Auto e) =>
            {
                Get(e.Input, e.Output,e.EnableConcurrency);
                return 0;
            },
            (Pack e) =>
            {
                Get(e.Input, e.Output,e.EnableConcurrency,Mode.Pack);
                return 0;
            },
            (Unpack e) =>
            {
                Get(e.Input, e.Output,e.EnableConcurrency, Mode.UnPack);
                return 0;
            },
            
            error =>
            {
                string str = string.Join("\n", error);
                if(!str.Contains("CommandLine.Bad"))
                    Log.Info(str);
                return -1;
            });
        Texture2D.WaitAll();
        Task.WaitAll();
        Log.Save();
    }
}
