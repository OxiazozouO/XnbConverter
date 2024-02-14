using XnbConverter.Utilities;

namespace Auto_Tbin;

public class qq
{
    public static void run()
    {
        HashSet<string> set = FileUtils.ToEntity<HashSet<string>>(@".\1.json");
        Console.WriteLine(set);
    }
}