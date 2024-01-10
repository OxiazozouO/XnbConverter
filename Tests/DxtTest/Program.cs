using Squish;

namespace XnbConverter.Test;

public static class Program
{
    
    public static void Main()
    {
        ByteItem Dxt3Test = new(2048, 2236, "原始.bin", "OutDxt3.bin", SquishFlags.kDxt3);
        byte[] testData = Dxt3Test.CopyData();
        
        byte[] newData = new byte[Dxt3Test.CompressedLen];
        
        int n = 100;
        for (int i = 0; i < n; ++i)
        {
            Dxt3Test.CompressImage(testData, newData);
            Dxt3Test.DecompressImage(testData, newData);
            Console.WriteLine(i);
        }
        Dxt3Test.Save(testData);
    }

    private static void test2()
    {
        
    }

    private static void test1()
    {
        ByteItem Dxt3Test = new(2048, 2236, "原始.bin", "OutDxt3.bin", SquishFlags.kDxt3);
        byte[] testData = Dxt3Test.CopyData();
        
        byte[] newData = new byte[Dxt3Test.CompressedLen];
        
        int n = 100;
        for (int i = 0; i < n; ++i)
        {
            Dxt3Test.CompressImage(testData, newData);
            Dxt3Test.DecompressImage(testData, newData);
            Console.WriteLine(i);
        }
        Dxt3Test.Save(testData);
    }
    private class ByteItem
    {
        private const string P = @"..\..\Files\";
        private readonly byte[] Data;
        private readonly string OutPath;
        private readonly Squish.Squish Dxt;
        public readonly int CompressedLen;

        public ByteItem(int width, int height, string ordPath, string outPath, SquishFlags flag)
        {
            Data = File.ReadAllBytes(P + ordPath);
            OutPath = outPath;
            Dxt = new Squish.Squish(flag, width, height);
            CompressedLen = Dxt.GetStorageRequirements();
        }

        public void CompressImage(byte[] a, byte[] b) => Dxt.CompressImage(a, b);

        public void DecompressImage(byte[] a, byte[] b) => Dxt.DecompressImage(a, b);

        public void Save(byte[] bytes) => File.WriteAllBytes(P + OutPath, bytes);

        public byte[] CopyData()
        {
            byte[] testData = new byte[Data.Length];
            Data.CopyTo(testData, 0);
            return testData;
        }
    }
}