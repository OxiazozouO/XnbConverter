using XnbConverter.Utilities;
using File = Java.IO.File;

namespace XnbConverter.Android.Model;

public class FileInfo(File root)
{
    public File Root { get; } = root; // 文件实例
    public string FileName { get; } = root.Name; // 文件名称
    public bool IsFolder { get; } = root.IsDirectory; // 是否为文件夹
    public string FileType { get; } = root.IsDirectory ? "folder" : Path.GetExtension(root.Name); // 文件类型

    public string FileSize() => IsFolder ? "" : FileUtils.FileSize(Root.Length());

    /// <summary>
    /// 文件最后修改时间
    /// </summary>
    /// <returns></returns>
    public string UpdateTime() => FileUtils.UpdateTime(Root.LastModified());
}