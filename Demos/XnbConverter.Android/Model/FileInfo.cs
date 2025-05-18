using XnbConverter.Android.Helper;
using XnbConverter.Utilities;
using File = Java.IO.File;

namespace XnbConverter.Android.Model;

public class FileInfo
{
    public static string RootPath = global::Android.OS.Environment.ExternalStorageDirectory + "/";

    public FileInfo(string path) : this(path.Contains(RootPath) ? new File(path) : new File(RootPath + path))
    {
    }

    public FileInfo(File root)
    {
        Root = root;
        FileName = root.Name;
        Extension = root.IsDirectory ? ".folder" : root.IsFile ? Path.GetExtension(root.Name).ToLower() : ".bin";
        Uri = root.AbsolutePath;
        TypeInfo = this.GetFileType();
        Update();
    }

    public void Update()
    {
        FileSize = Root.IsFile ? FileUtils.FileSize(Root.Length()) : "";
        UpdateTime = FileUtils.UpdateTime(Root.LastModified());
    }

    public string Uri { get; } // 文件实例
    public File Root { get; } // 文件实例
    public string FileName { get; } // 文件名称

    public FileTypeInfo TypeInfo { get; }
    public string Extension { get; } // 文件类型

    public string FileSize { get; private set; }

    /// <summary>
    /// 文件最后修改时间
    /// </summary>
    /// <returns></returns>
    public string UpdateTime { get; private set; }
}