using System.Text;

namespace XnbConverter.Configurations;

public abstract class Log
{
    protected static readonly string INFO = "[INFO]  ";

    protected static readonly string DEBUG = "[DEBUG] ";

    protected static readonly string WARN = "[WARN]  ";

    protected static readonly string ERROR = "[ERROR] ";

    protected static readonly object MessageLock = new object();

    protected static readonly StringBuilder InfoLog = new StringBuilder();

    protected static readonly StringBuilder ErrorLog = new StringBuilder();

    protected Log() => Logger.Instance = this;

    public abstract void Message(string message = "", params object?[] format);

    public abstract void Info(string message = "", params object?[] format);

    public abstract void Debug(string message = "", params object?[] format);

    public abstract void Warn(string message = "", params object?[] format);

    public abstract void Error(string message = "", params object?[] format);

    protected abstract void Set<T>(bool isPrint, bool isSave, ConsoleColor color, string head, string message,
        params object?[] format);

    public abstract void Save();
}