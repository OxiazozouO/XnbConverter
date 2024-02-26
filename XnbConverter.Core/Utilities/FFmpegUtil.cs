using System.Diagnostics;
using static XnbConverter.Xact.WaveBank.Entity.FmtChunk;

namespace XnbConverter.Utilities;

public static class FFmpegUtil
{
    public static bool Convert(string inputPath, string? outputPath = null, AudioFormats? format = null)
    {
        if (Helpers.FFmpegPath is null)
            throw new Exception("FFmpeg not found");

        inputPath = Path.GetFullPath(inputPath);
        outputPath ??= inputPath + ".wav";

        var fmt = GetConfig(format);

        var arguments = string.Format("{0} {1} {2} {3} {4} {5} {6} {7} {8}",
            "-i", $"\"{inputPath}\"",
            "-v quiet", //不输出任何信息
            "-acodec", fmt, //指定输出音频使用 PCM 编码,并指定音频采样格式
            "-nostdin", //禁用标准输入，防止FFmpeg尝试读取输入流。
            "-map_metadata -1", //禁用元数据映射，即不复制输入文件的元数据到输出文件	
            "-y", //自动覆盖输出文件，如果输出文件已存在。
            $"\"{outputPath}\""
        );

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Helpers.FFmpegPath,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            Arguments = arguments
        });
        process?.WaitForExit();
        File.Move(outputPath, inputPath, true);
        return process is not null && process.ExitCode == 0;
    }

    private static string GetConfig(AudioFormats? format)
    {
        return format switch
        {
            AudioFormats.Ima4 => "ima_adpcm",
            AudioFormats.AdpcmMs => "adpcm_ms",
            AudioFormats.Pcm => "pcm_s16le",
            // case AudioFormats.Ieee:
            //     return "";
            null => "copy",
            _ => "copy"
        };
    }
}