using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using XnbConverter.Utilities;
using File = System.IO.File;
using Math = System.Math;

namespace XnbConverter.Android.Helper;

// public static class CompressHelper
// {
//     // 解压函数
//     public static void Decompress(string file, string path, string? password = null)
//     {
//         using var archive = password == null
//             ? ArchiveFactory.Open(file)
//             : ArchiveFactory.Open(file, new ReaderOptions { Password = password });
//         var ext = Path.GetExtension(file).ToLower();
//         switch (ext)
//         {
//             case ".apk":
//             case ".zip":
//             case ".rar":
//             case ".7z":
//             case ".tar":
//             case ".gz":
//                 foreach (var entry in archive.Entries)
//                 {
//                     if (entry.IsDirectory) continue;
//                     entry.WriteToDirectory(path,
//                         new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
//                 }
//
//                 break;
//             default:
//                 throw new NotSupportedException($"Unsupported file extension: {Path.GetExtension(file)}");
//         }
//     }
//
//     // 压缩函数
//     // ".rar"
//     // ".zip"
//     // ".tar"
//     // ".gz"
//     public static void Compress(List<string> files, string path)
//     {
//         // 根据不同的文件后缀名选择不同的压缩方式
//         ArchiveType a;
//         CompressionType b;
//         var ex = Path.GetExtension(path).ToLower();
//         switch (ex)
//         {
//             case ".rar":
//                 a = ArchiveType.Rar;
//                 b = CompressionType.Deflate;
//                 break;
//             case ".zip":
//                 a = ArchiveType.Zip;
//                 b = CompressionType.Deflate;
//                 break;
//             case ".tar" or ".gz":
//                 a = ArchiveType.Tar;
//                 b = CompressionType.GZip;
//                 break;
//             default:
//                 throw new NotSupportedException($"Unsupported file extension: {Path.GetExtension(path)}");
//         }
//
//         // 使用正确的压缩类型创建 WriterOptions
//         var options = new WriterOptions(b)
//         {
//             ArchiveEncoding = new ArchiveEncoding
//             {
//                 Default = Encoding.UTF8
//             }
//         };
//         using var stream = File.OpenWrite(path);
//         using var writer = WriterFactory.Open(stream, a, options);
//         foreach (var file in files)
//         {
//             if (File.Exists(file))
//             {
//                 writer.Write(Path.GetFileName(file), file);
//             }
//             else if (Directory.Exists(file))
//             {
//                 AddDirectoryToArchive(writer, file, "");
//             }
//         }
//     }
//
//     private static void AddDirectoryToArchive(IWriter writer, string directory, string parent)
//     {
//         foreach (var file in Directory.GetFiles(directory))
//         {
//             // 构建在压缩包中的路径
//             var entryPath = Path.Combine(parent, Path.GetFileName(file));
//             writer.Write(entryPath, file);
//         }
//
//         foreach (var subDirectory in Directory.GetDirectories(directory))
//         {
//             var subDirectoryName = Path.Combine(parent, Path.GetFileName(subDirectory));
//             AddDirectoryToArchive(writer, subDirectory, subDirectoryName);
//         }
//     }
// }

public static class CompressHelper
{
    // 解压函数，添加了进度条相关逻辑以及显示更具体的进度信息
    public static void Decompress(string file, string path, string? password = null,
        Action<float, float, string>? action = null)
    {
        long ans = 0;
        long currentExtractedSize = 0;
        using var archive = password == null
            ? ArchiveFactory.Open(file)
            : ArchiveFactory.Open(file, new ReaderOptions { Password = password });
        var ext = Path.GetExtension(file).ToLower();
        switch (ext)
        {
            case ".apk":
            case ".zip":
            case ".rar":
            case ".7z":
            case ".tar":
            case ".z":
            case ".gz":
                // 计算总文件大小（要解压的所有文件大小总和）
                ans = archive.Entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size);
                int ind = -1;

                List<string> paths = [];

                foreach (var entry in archive.Entries)
                {
                    ind++;
                    if (entry.IsDirectory) continue;
                    using var stream = entry.OpenEntryStream();
                    var p = "";
                    if (ext == ".z")
                    {
                        p = Path.Combine(path, Path.GetFileName(path) + ".txt");
                    }
                    else if (entry.Key is null)
                    {
                        p = Path.Combine(path, ind + ".tar");
                        paths.Add(p);
                    }
                    else
                    {
                        p = Path.Combine(path, entry.Key);
                    }


                    using var fileStream = FileUtils.CreateDirectoryAndFile(p);
                    byte[] buffer = new byte[1024];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fileStream.Write(buffer, 0, bytesRead);
                        currentExtractedSize += bytesRead;
                        action?.Invoke(ans, currentExtractedSize,
                            $"已解压: {FileUtils.FileSize(currentExtractedSize)} / 总共: {FileUtils.FileSize(ans)}");
                    }
                }

                foreach (var p in paths)
                {
                    Decompress(p, path, password, action);
                    File.Delete(p);
                }


                break;
            default:
                throw new NotSupportedException($"Unsupported file extension: {Path.GetExtension(file)}");
        }
    }

    private static string FormatSize(long bytes)
    {
        const int scale = 1024;
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        if (bytes == 0) return "0 B";
        int order = (int)Math.Log(bytes, scale);
        double adjustedSize = bytes / Math.Pow(scale, order);
        return $"{adjustedSize:0.##} {suffixes[order]}";
    }

    public static void Compress(List<string> files, string path)
    {
        // 根据不同的文件后缀名选择不同的压缩方式
        ArchiveType a;
        CompressionType b;
        var ex = Path.GetExtension(path).ToLower();
        switch (ex)
        {
            case ".rar":
                a = ArchiveType.Rar;
                b = CompressionType.Deflate;
                break;
            case ".zip":
                a = ArchiveType.Zip;
                b = CompressionType.Deflate;
                break;
            case ".tar" or ".gz":
                a = ArchiveType.Tar;
                b = CompressionType.GZip;
                break;
            default:
                throw new NotSupportedException($"Unsupported file extension: {Path.GetExtension(path)}");
        }

        var options = new WriterOptions(b)
        {
            ArchiveEncoding = new ArchiveEncoding
            {
                Default = Encoding.UTF8
            }
        };
        using var stream = File.OpenWrite(path);
        using var writer = WriterFactory.Open(stream, a, options);
        foreach (var file in files)
        {
            if (File.Exists(file))
            {
                writer.Write(Path.GetFileName(file), file);
            }
            else if (Directory.Exists(file))
            {
                AddDirectoryToArchive(writer, file, "");
            }
        }
    }

    private static void AddDirectoryToArchive(IWriter writer, string directory, string parent)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            var entryPath = Path.Combine(parent, Path.GetFileName(file));
            writer.Write(entryPath, file);
        }

        foreach (var subDirectory in Directory.GetDirectories(directory))
        {
            var subDirectoryName = Path.Combine(parent, Path.GetFileName(subDirectory));
            AddDirectoryToArchive(writer, subDirectory, subDirectoryName);
        }
    }
}