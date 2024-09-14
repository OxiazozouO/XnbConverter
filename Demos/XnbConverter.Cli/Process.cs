using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using XnbConverter.Configurations;
using XnbConverter.Utilities;

namespace XnbConverter.Cli;

public static class Process
{
    [Flags]
    public enum Mode
    {
        Pack = 1,
        UnPack = 2
    }

    public class CmdContent
    {
        public string? Input;

        public string? Output;

        public bool IsEnableConcurrency;

        public Mode Mode;

        public override string ToString()
        {
            return $" Input: {Input}\n Output: {Output}\n IsEnableConcurrency: {IsEnableConcurrency}\n Mode: {Mode}\n";
        }
    }

    private static int _success;

    private static int _fail;

    private static int _total;

    private static object wait = new object();

    private static bool isPrt;

    private static int currentLineCursor = 0;

    private static void Details()
    {
        Logger.Message("成功: {0}\n失败: {1}\n——————————————————————————", _success, _fail);
        _success = (_fail = 0);
    }

    public static void Unpack(string input, string output)
    {
        output += ".config";
        XNB xNB = null;
        try
        {
            xNB = new XNB();
            xNB.Decode(input);
            if (xNB.ExportFiles(output))
            {
                Logger.Info(Error.Process_1, output);
                _success++;
            }
            else
            {
                Logger.Error(Error.Process_2, output);
                _fail++;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(Error.Process_3, input, ex);
            _fail++;
        }
        finally
        {
            xNB?.Dispose();
            lock (wait)
            {
                UpdateProgress();
            }
        }
    }

    public static void Pack(string input, string output)
    {
        output += ".xnb";
        Logger.Info(Error.XNB_10, input);
        XNB xNB = null;
        FileStream fileStream = null;
        try
        {
            xNB = new XNB();
            xNB.ImportFiles(input);
            xNB.Encode(output);
            Logger.Info(Error.Process_1, output);
            _success++;
        }
        catch (Exception ex)
        {
            Logger.Error(Error.Process_4, input, ex.Message, ex.StackTrace);
            _fail++;
        }
        finally
        {
            xNB?.Dispose();
            fileStream?.Dispose();
            lock (wait)
            {
                UpdateProgress();
            }
        }
    }

    private static void ProcessFilesAsync(Action<string, string> fn, List<(string, string)> files)
    {
        isPrt = true;
        _total = files.Count;
        ConfigHelper.EnableMultithreading();
        int concurrency = ConfigHelper.Concurrency;
        List<Task> list = new List<Task>(concurrency);
        for (int i = 0; i < files.Count; i += concurrency)
        {
            list.Clear();
            int num = Math.Min(i + concurrency, files.Count);
            for (int j = i; j < num; j++)
            {
                (string, string) file = files[j];
                Task task = Task.Run(delegate { fn(file.Item1, file.Item2); });
                if (task != null)
                {
                    list.Add(task);
                }
            }

            Task.WaitAll(list.ToArray());
        }

        Details();
        ConfigHelper.TurnOffMultithreading();
    }

    private static void ProcessFiles(Action<string, string> fn, List<(string, string)> files)
    {
        _total = files.Count;
        foreach (var file in files)
        {
            fn(file.Item1, file.Item2);
        }

        Details();
    }

    public static void Get(CmdContent cmd)
    {
        isPrt = false;
        DateTime now = DateTime.Now;
        Dictionary<string, List<(string, string)>> dictionary = FileUtils.BuildFiles(cmd.Input, cmd.Output);
        if ((cmd.Mode & Mode.Pack) > (Mode)0 && dictionary.TryGetValue(".config", out var value))
        {
            value.CreateDirectory();
            if (cmd.IsEnableConcurrency && value.Count > 10 && ConfigHelper.Concurrency > 1)
            {
                ProcessFilesAsync(Pack, value);
            }
            else
            {
                ProcessFiles(Pack, value);
            }
        }

        if ((cmd.Mode & Mode.UnPack) > (Mode)0)
        {
            if (dictionary.TryGetValue(".xnb", out var value2))
            {
                value2.CreateDirectory();
                if (cmd.IsEnableConcurrency && value2.Count > 10 && ConfigHelper.Concurrency > 1)
                {
                    ProcessFilesAsync(Unpack, value2);
                }
                else
                {
                    ProcessFiles(Unpack, value2);
                }
            }

            if (dictionary.TryGetValue(".xwb", out var value3))
            {
                List<(string, string)> list = new List<(string, string)>();
                list.AddRange(value3);
                if (dictionary.TryGetValue(".xgs", out var value4))
                {
                    list.AddRange(value4);
                }

                if (dictionary.TryGetValue(".xsb", out var value5))
                {
                    list.AddRange(value5);
                }

                XACT.Load(list)?.Save();
            }
        }

        if (_success != _fail || _fail != 0)
        {
            Logger.Message(DateTime.Now.Subtract(now).TotalSeconds.ToString());
        }
    }

    private static void UpdateProgress()
    {
        if (isPrt)
        {
            if (currentLineCursor < Console.CursorTop - 1)
            {
                currentLineCursor = Console.CursorTop;
            }

            Console.SetCursorPosition(0, currentLineCursor);
            int num = _success + _fail;
            double num2 = num * 1f / _total;
            int num3 = 50;
            int num4 = (int)(num2 * num3);
            string prt = string.Format("〔{0}{1}〕 总数：{2}/ 成功：{3} / 失败：{4}",
                new string('\u2593', num4),
                new string(' ', num3 - num4),
                _total, _success, _fail
            );
            Console.WriteLine(prt);
            currentLineCursor = Console.CursorTop - 1;
        }
    }
}