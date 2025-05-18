using XnbConverter.Configurations;

namespace XnbConverter.Android;

public class AndroidLogger : Log
{
    public static void Build() => new AndroidLogger();

    public override void Message(string message = "", params object?[] format)
    {
    }

    public override void Info(string message = "", params object?[] format)
    {
    }

    public override void Debug(string message = "", params object?[] format)
    {
    }

    public override void Warn(string message = "", params object?[] format)
    {
    }

    public override void Error(string message = "", params object?[] format)
    {
    }

    protected override void Set<Color>(bool isPrint, bool isSave, ConsoleColor color, string head, string message,
        params object?[] format)
    {
    }

    public override void Save()
    {
    }
}