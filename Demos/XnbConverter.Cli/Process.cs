using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

    // 用于显示成功和失败的计数
    private static int _success; //成功计数

    private static int _fail; //失败计数

    /**
     * 显示处理结果的详细信息
     */
    private static void Details()
    {
        // 显示文件处理结果的最终分析
        Log.Message("成功: {0}\n失败: {1}\n——————————————————————————", _success, _fail);
        _success = _fail = 0;
        // TypeReader.Print();
    }

    /**
     * 接受输入并处理输入以进行拆包。
     * @param {String} input
     * @param {String} output
     */
    public static void Unpack(string input, string output)
    {
        output += ".config";
        XNB xnb = null;
        // 捕获任何异常以保持文件批处理的进行
        try
        {
            // 加载XNB并从中获取对象
            xnb = new XNB();
            xnb.Load(input);
            // 保存文件
            if (xnb.ExportFile(output))
            {
                // 记录文件已保存
                Log.Info(Helpers.I18N["Process.1"], output);
                // 增加成功计数
                _success++;
            }
            else
            {
                Log.Error(Helpers.I18N["Process.2"], output);
                _fail++;
            }
        }
        catch (Exception ex)
        {
            // 记录错误日志
            Log.Error(Helpers.I18N["Process.3"], input, ex);
            // 增加失败计数
            _fail++;
        }
        finally
        {
            xnb?.Dispose();
        }
    }

    /**
     * 将文件包处理到xnb
     * @param {String} input
     * @param {String} output
     * @param {Function} done
     */
    public static void Pack(string input, string output)
    {
        output += ".xnb";
        Log.Info(Helpers.I18N["XNB.6"], input);
        XNB xnb = null;
        FileStream fs = null;

        // 捕获任何异常以保持文件批处理的进行
        try
        {
            xnb = new XNB();
            // 解析导入项
            xnb.ResolveImports(input);
            // 将JSON转换为XNB 并保存
            xnb.Convert(output);
            // 记录文件已保存
            Log.Info(Helpers.I18N["Process.1"], output);

            // 增加成功计数
            _success++;
        }
        catch (Exception ex)
        {
            // 记录错误日志
            Log.Error(Helpers.I18N["Process.4"], input, ex.Message, ex.StackTrace);
            // 增加失败计数
            _fail++;
        }
        finally
        {
            xnb?.Dispose();
            fs?.Dispose();
        }
    }

    private static void ProcessFilesAsync(Action<string, string> fn, List<(string, string)> files)
    {
        Helpers.EnableMultithreading();
        var concurrency = Helpers.Config.Concurrency;
        List<Task> list = new(concurrency);
        for (var i = 0; i < files.Count; i += concurrency)
        {
            list.Clear();
            var end = Math.Min(i + concurrency, files.Count);
            for (var j = i; j < end; j++)
            {
                var file = files[j];
                var task = Task.Run(() => { fn(file.Item1, file.Item2); });
                list.Add(task);
            }

            Task.WaitAll(list.ToArray());
        }

        // 完成遍历
        Details();
        Helpers.TurnOffMultithreading();
    }

    private static void ProcessFiles(Action<string, string> fn, List<(string, string)> files)
    {
        foreach (var file in files)
            fn(file.Item1, file.Item2);
        // 完成遍历
        Details();
    }

    public static void Get(CmdContent cmd)
    {
        var t1 = DateTime.Now;
        var files = FileUtils.BuildFiles(cmd.Input, cmd.Output);
        if ((cmd.Mode & Mode.Pack) > 0)
        {
            if (files.TryGetValue(".config", out var xnb))
            {
                xnb.CreateDirectory();
                //组装路径并执行函数
                if (cmd.IsEnableConcurrency && xnb.Count > 10 &&
                    Helpers.Config.Concurrency > 1)
                    ProcessFilesAsync(Pack, xnb);
                else
                    ProcessFiles(Pack, xnb);
            }
        }
        if ((cmd.Mode & Mode.UnPack) > 0)
        {
            if (files.TryGetValue(".xnb", out var xnb))
            {
                xnb.CreateDirectory();
                //组装路径并执行函数
                if (cmd.IsEnableConcurrency && xnb.Count > 10 && Helpers.Config.Concurrency > 1)
                    ProcessFilesAsync(Unpack, xnb);
                else
                    ProcessFiles(Unpack, xnb);
            }

            if (files.TryGetValue(".xwb", out var xwb))
            {
                List<(string, string)> xact = new();
                xact.AddRange(xwb);
                if (files.TryGetValue(".xgs", out var xgs)) xact.AddRange(xgs);

                if (files.TryGetValue(".xsb", out var xsb)) xact.AddRange(xsb);
                // xact.CreateDirectory();
                XACT.Load(xact)?.Save();
            }
        }

        if (_success == _fail && _fail == 0)
        {
            return;
        }
        Console.WriteLine(DateTime.Now.Subtract(t1).TotalSeconds);
    }
}