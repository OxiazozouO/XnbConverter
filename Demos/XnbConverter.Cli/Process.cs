using Newtonsoft.Json;
using XnbConverter.Xact.AudioEngine.Entity;
using XnbConverter.Xact.AudioEngine.Reader;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
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
        _success = _fail = 0;
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

    /// <summary>
    /// 解包 .xsb / .xgs：原样复制一份（打包时作为模板），并额外导出可编辑的 JSON 视图。
    /// 视图里对字段的修改会在打包时按原偏移就地写回。
    /// </summary>
    private static void XactExportRawAndViews(Dictionary<string, List<(string, string)>> dictionary)
    {
        foreach ((string extension, string viewKind) in new[] { (".xsb", "SoundBank"), (".xgs", "AudioEngine") })
        {
            if (!dictionary.TryGetValue(extension, out var value))
            {
                continue;
            }

            foreach ((string src, string destBase) in value)
            {
                try
                {
                    string text = destBase + extension;
                    string directoryName = Path.GetDirectoryName(text);
                    if (!string.IsNullOrEmpty(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    File.Copy(src, text, true);
                    File.WriteAllText(text + ".json", BuildViewJson(src, viewKind == "SoundBank"));
                    Logger.Info(Error.Process_1, text);
                    _success++;
                }
                catch (Exception ex)
                {
                    Logger.Error(Error.Process_4, src, ex.Message, ex.StackTrace);
                    _fail++;
                }
            }
        }
    }

    /// <summary>把 .xsb / .xgs 导出成可编辑的 JSON 视图。</summary>
    private static string BuildViewJson(string path, bool isSoundBank)
    {
        if (isSoundBank)
        {
            return JsonConvert.SerializeObject(SoundBankView.From(SoundBankReader.Read(path)), Formatting.Indented);
        }

        return JsonConvert.SerializeObject(AudioEngineView.From(AudioEngineReader.Read(path)), Formatting.Indented);
    }

    /// <summary>打包 .xsb / .xgs：读取 JSON 视图，套回同名模板文件后写回输出目录。</summary>
    private static void XactPackViews(CmdContent cmd)
    {
        if (!Directory.Exists(cmd.Input))
        {
            return;
        }

        string[] array = Directory.GetFiles(cmd.Input, "*.xsb.json", SearchOption.AllDirectories);
        string[] array2 = Directory.GetFiles(cmd.Input, "*.xgs.json", SearchOption.AllDirectories);
        foreach (string text in array.Concat(array2))
        {
            try
            {
                bool flag = text.EndsWith(".xsb.json", StringComparison.OrdinalIgnoreCase);
                string text2 = text[..^5];
                string text3 = Path.Combine(cmd.Output, Path.GetRelativePath(cmd.Input, text2));
                string directoryName = Path.GetDirectoryName(text3);
                if (!string.IsNullOrEmpty(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                byte[] bytes;
                if (flag)
                {
                    SoundBank soundBank = SoundBankReader.Read(text2);
                    JsonConvert.DeserializeObject<SoundBankView>(File.ReadAllText(text)).ApplyTo(soundBank);
                    bytes = SoundBankReader.Build(soundBank);
                }
                else
                {
                    AudioEngine audioEngine = AudioEngineReader.Read(text2);
                    JsonConvert.DeserializeObject<AudioEngineView>(File.ReadAllText(text)).ApplyTo(audioEngine);
                    bytes = AudioEngineReader.Build(audioEngine);
                }

                File.WriteAllBytes(text3, bytes);
                Logger.Info(Error.Process_1, text3);
                _success++;
            }
            catch (Exception ex)
            {
                Logger.Error(Error.Process_4, text, ex.Message, ex.StackTrace);
                _fail++;
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
        // 这里必须用「连续取任务」的队列，不能按 concurrency 个一批、每批 Task.WaitAll。
        // 批次栅栏的代价在文件大小不均时非常明显：一批里只要有一个几十 MB 的贴图，
        // 同批另外 14 个线程干完就只能空等，而队列里明明还有活。
        // 实测（完整 Content，3550 个 xnb）：批次栅栏 10.89 s → 长队列 8.93 s，快 18%；
        // 产出 7555 个文件逐字节不变。
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            file => fn(file.Item1, file.Item2));

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
            // ".xwb.config" 是波形库清单，交给 XACT 打包；其余是 XNB 的配置
            List<(string, string)> waveBankConfigs = new List<(string, string)>();
            List<(string, string)> xnbConfigs = new List<(string, string)>();
            foreach ((string path, string dest) in value)
            {
                if (path.EndsWith(".xwb.config", StringComparison.OrdinalIgnoreCase))
                {
                    waveBankConfigs.Add((path, dest));
                }
                else
                {
                    xnbConfigs.Add((path, dest));
                }
            }

            if (xnbConfigs.Count > 0)
            {
                xnbConfigs.CreateDirectory();
                if (cmd.IsEnableConcurrency && xnbConfigs.Count > 10 && ConfigHelper.Concurrency > 1)
                {
                    ProcessFilesAsync(Pack, xnbConfigs);
                }
                else
                {
                    ProcessFiles(Pack, xnbConfigs);
                }
            }

            if (waveBankConfigs.Count > 0)
            {
                try
                {
                    XACT.Pack(waveBankConfigs);
                    _success += waveBankConfigs.Count;
                }
                catch (Exception ex)
                {
                    Logger.Error(Error.Process_4, cmd.Input, ex.Message, ex.StackTrace);
                    _fail++;
                }
            }
        }

        // .xsb / .xgs：解包时原样复制一份作为模板，并附带可编辑的 JSON 视图
        if ((cmd.Mode & Mode.UnPack) > (Mode)0)
        {
            XactExportRawAndViews(dictionary);
        }

        // 打包时按 JSON 视图套回同名模板
        if ((cmd.Mode & Mode.Pack) > (Mode)0)
        {
            XactPackViews(cmd);
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
        // 输出被重定向（管道、日志文件、CI）时没有控制台缓冲区，
        // Console.CursorTop / SetCursorPosition 会抛 IOException，把整批处理带崩 —— 直接跳过进度条。
        if (isPrt && !Console.IsOutputRedirected)
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