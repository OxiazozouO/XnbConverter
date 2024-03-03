using System;
using System.Collections.Generic;
using XnbConverter;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Reader;

namespace XactTest;

/**
 * 用于打包和解包xact文件
 * @class
 * @public
 */
public static class Program
{
    public static void Main()
    {
        Helpers.NativeMethods.Init();
        WaveBankReaderTest();
    }

    public static void WaveFormLoadTest()
    {
        var waveFormReader = new WaveFormReader();
        waveFormReader.Init(new ReaderResolver
        {
            bufferReader = BufferReader.FormFile(@"D:\XnbConverter\XactTest\bin\Files\1_1.wav")
        });
        waveFormReader.Load();
    }

    public static void WaveBankReaderTest()
    {
        var inputPath = @"..\..\Files\";
        List<(string, string)> files = new();
        if (1 == 1)
        {
            inputPath += "Stardew";
            files.Add((inputPath + "\\FarmerSounds.xgs", inputPath + "(unpack)\\FarmerSounds"));
            files.Add((inputPath + "\\Sound Bank.xsb", inputPath + "(unpack)\\Sound Bank"));
            files.Add((inputPath + "\\Wave Bank.xwb", inputPath + "(unpack)\\Wave Bank"));
            files.Add((inputPath + "\\Wave Bank(1.4).xwb", inputPath + "(unpack)\\Wave Bank(1.4)"));
        }
        else
        {
            inputPath += "Terraria";
            files.Add((inputPath + "\\TerrariaMusic.xgs", inputPath + "(unpack)\\TerrariaMusic"));
            files.Add((inputPath + "\\Sound Bank.xsb", inputPath + "(unpack)\\Sound Bank"));
            files.Add((inputPath + "\\Wave Bank.xwb", inputPath + "(unpack)\\Wave Bank"));
        }

        try
        {
            files.CreateDirectory();
            XACT.Load(files)?.Save();
        }
        catch (Exception ex)
        {
            Log.Error(Helpers.I18N["Program.10"]);
            Log.Error(ex.ToString());
        }
    }
}