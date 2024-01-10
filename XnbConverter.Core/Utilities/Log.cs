using System.Text;

namespace XnbConverter.Utilities;

/**
 * 具有静态成员的日志类，用于将消息记录到控制台。
 * @class
 * @static
 */
public static class Log
{
    private static readonly object MessageLock = new();
    private static readonly StringBuilder InfoLog = new ();
    private static readonly StringBuilder ErrorLog = new ();
    
    private const string INFO  = "[INFO]  ";
    private const string DEBUG = "[DEBUG] ";
    private const string WARN  = "[WARN]  ";
    private const string ERROR = "[ERROR] ";
    //只是简单的控制信息，在于方便查找相关用法
    public static void Message(string message = "", params object?[] format)
        =>Console.WriteLine(message, format);
    //普通信息
    public static void Info(string message = "", params object?[] format)=>Set(Helpers.Config.PInfo,Helpers.Config.SInfo,
        ConsoleColor.Blue,INFO, message, format);
    //调试
    public static void Debug(string message = "", params object?[] format)=>Set(Helpers.Config.PDebug,Helpers.Config.SDebug,
        ConsoleColor.Gray,DEBUG, message, format);
    //警告
    public static void Warn(string message = "", params object?[] format) =>Set(Helpers.Config.PWarn, Helpers.Config.SWarn,
        ConsoleColor.Yellow, WARN, message, format);
    //错误
    public static void Error(string message = "", params object?[] format)=>Set(Helpers.Config.PError,Helpers.Config.SError,
        ConsoleColor.Red,ERROR, message, format);
    
    private static void Set(bool isPrint,bool isSave, ConsoleColor color, string head, string message, params object?[] format)
    {
        lock (MessageLock)
        {
            bool isErr = head == ERROR;

            if (Helpers.Config.LogTime)
            {
                head = DateTime.Now.ToString(Helpers.Config.TimeFormat)  +" "+ head;
            }
                
            string s1 = string.Format(message, format);
            string s2 = head + s1 + '\n';
            if (isSave)
            {
                InfoLog.Append(s2);
                if (isErr)
                {
                    ErrorLog.Append(s2);
                }
            }

            if (!isPrint) return;
            Console.ForegroundColor = color;
            Console.Write(head);
            Console.ResetColor();
            Console.WriteLine(s1);
        }
    }

    public static void Save()
    {
        if(Helpers.Config.SInfo)
            File.AppendAllText(".\\info.txt", InfoLog.ToString());
        if(Helpers.Config.SError)
            File.AppendAllText(".\\error.txt", ErrorLog.ToString());
    }
    
    
    
    public static void BigFileDebug(string path, byte[] data)=>File.WriteAllBytes(path, data);
    public static void log(this byte[] strings)=>Console.WriteLine(string.Join(" ",strings));
    public static void log(this object s)=>Console.Write(s + "\t");
    public static void logLn(this object s)=>Console.WriteLine(s);
    
    /**
     * 二进制
     */
    public static string B(this uint n) => Convert.ToString(n);
}

