namespace XnbConverter.Configurations;

public static class Logger
{
    public static Log? Instance;

    #region TOOL

    public static void BigFileDebug(string path, byte[] data)
    {
        File.WriteAllBytes(path, data);
    }

    public static string ToJoinStr(this IEnumerable<string?> strings)
    {
        return string.Join(",", strings);
    }

    public static string ToJoinStr<T>(this IEnumerable<T> strings)
    {
        return string.Join(",", strings);
    }

    public static void Put(this object s)
    {
        Console.Write(s?.ToString() + '\t');
    }

    public static void Putln(this object s)
    {
        Console.WriteLine(s);
    }

    public static string B(this uint n)
    {
        return Convert.ToString(n);
    }

    #endregion


    #region 外部使用

    public static void Message(string message = "", params object?[] format) =>
        Instance?.Message(message, format);

    public static void Info(string message = "", params object?[] format) =>
        Instance?.Info(message, format);

    public static void Debug(string message = "", params object?[] format) =>
        Instance?.Debug(message, format);

    public static void Warn(string message = "", params object?[] format) =>
        Instance?.Warn(message, format);

    public static void Error(string message = "", params object?[] format) =>
        Instance?.Error(message, format);

    public static void Save() => Instance?.Save();

    #endregion
}