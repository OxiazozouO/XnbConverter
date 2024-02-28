using System.Diagnostics;
using static XnbConverter.Xact.WaveBank.Entity.FmtChunk;

namespace XnbConverter.Utilities;

public static class FFmpegUtil
{
    public static bool Convert(string inputPath, string? outputPath = null, AudioFormats? format = null)
    {
        if (Helpers.SysPath.FFmpeg is null)
            throw new Exception("FFmpeg not found");

        inputPath = Path.GetFullPath(inputPath);
        outputPath ??= inputPath + ".wav";

        var fmt = GetConfig(format);

        var arguments =
            $"-i \"{inputPath}\" -v quiet -acodec {fmt} -nostdin -map_metadata -1 -y \"{outputPath}\"";

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Helpers.SysPath.FFmpeg,
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