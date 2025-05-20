using XnbConverter.Configurations;

namespace XnbConverter.Cli.Configurations;

public class ConsoleLogger : Log
{
    public static void Build() => new ConsoleLogger();

    public override void Message(string message = "", params object?[] format)
    {
        Console.WriteLine(message, format);
    }

    public override void Info(string message = "", params object?[] format)
    {
        Set<ConsoleColor>(ConfigHelper.PInfo, ConfigHelper.SInfo, ConsoleColor.Blue, INFO, message, format);
    }

    public override void Debug(string message = "", params object?[] format)
    {
        Set<ConsoleColor>(ConfigHelper.PDebug, ConfigHelper.SDebug, ConsoleColor.Gray, DEBUG, message, format);
    }

    public override void Warn(string message = "", params object?[] format)
    {
        Set<ConsoleColor>(ConfigHelper.PWarn, ConfigHelper.SWarn, ConsoleColor.Yellow, WARN, message, format);
    }

    public override void Error(string message = "", params object?[] format)
    {
        Set<ConsoleColor>(ConfigHelper.PError, ConfigHelper.SError, ConsoleColor.Red, ERROR, message, format);
    }

    protected override void Set<ConsoleColor>(bool isPrint, bool isSave, System.ConsoleColor color, string head,
        string message, params object?[] format)
    {
        lock (MessageLock)
        {
            bool flag = head == ERROR;
            if (ConfigHelper.LogTime)
            {
                head = DateTime.Now.ToString(ConfigHelper.TimeFormat) + " " + head;
            }

            string text = string.Format(message, format);
            string value = head + text;
            if (isSave)
            {
                InfoLog.AppendLine(value);
                if (flag)
                {
                    ErrorLog.AppendLine(value);
                }
            }

            if (isPrint)
            {
                Console.ForegroundColor = color;
                Console.Write(head);
                Console.ResetColor();
                Console.WriteLine(text);
            }
        }
    }

    public override void Save()
    {
        string text = "info.txt";
        string text2 = "error.txt";
        DateTime now = DateTime.Now;
        DateTime lastWriteTime = new FileInfo(text).LastWriteTime;

        if (File.Exists(text) && lastWriteTime.Day != now.Day)
        {
            string fullPath = Path.GetFullPath(text);
            string fullPath2 = Path.GetFullPath(text.Replace(".", lastWriteTime.ToString(".yyyy-MM-dd.")));
            File.Move(fullPath, fullPath2, overwrite: true);
        }

        lastWriteTime = new FileInfo(text2).LastWriteTime;
        if (File.Exists(text2) && lastWriteTime.Day != now.Day)
        {
            string fullPath3 = Path.GetFullPath(text2);
            string fullPath4 = Path.GetFullPath(text2.Replace(".", lastWriteTime.ToString(".yyyy-MM-dd.")));
            File.Move(fullPath3, fullPath4, overwrite: true);
        }

        if (ConfigHelper.SInfo)
        {
            string text3 = InfoLog.ToString();
            if (text3 != "")
            {
                File.AppendAllText(text, text3);
            }
        }

        if (ConfigHelper.SError)
        {
            string text4 = ErrorLog.ToString();
            if (text4 != "")
            {
                File.AppendAllText(text2, text4);
            }
        }
    }
}