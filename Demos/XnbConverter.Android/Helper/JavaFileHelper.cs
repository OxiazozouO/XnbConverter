using System.Text;
using XnbConverter.Android.Configurations;
using static _Microsoft.Android.Resource.Designer.ResourceConstant.Drawable;
using File = Java.IO.File;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Helper;

public static class JavaFileHelper
{
    /// <summary>
    /// 文件目录树根节点
    /// </summary>
    /// <param name="file">根节点开始，BFS遍历一层子节点</param>
    /// <returns></returns>
    public static List<FileInfo> GetFileList(File file)
    {
        List<FileInfo> list = new List<FileInfo>();
        if (!file.IsDirectory) return list;
        File[]? files = file.ListFiles();
        if (files == null) return list;
        foreach (File childNode in files)
        {
            FileInfo fileInfo = new FileInfo(childNode);
            list.Add(fileInfo);
        }

        return list;
    }

    public static readonly Dictionary<string, Tuple<int, int>> FileTypes = new Dictionary<Tuple<int, int>, string>
    {
        [new(ic_file_xnb, ic_file_xnb_select)] = ".xnb",
        [new(ic_file_code, ic_file_code_select)] = ".xml.html.log.md.css.js",
        [new(ic_file_image, ic_file_image_select)] = ".png.jpg.jpeg.gif.bmp",
        [new(ic_file_audio, ic_file_audio_select)] = ".mp3.wav.ogg.m4a",
        [new(ic_file_video, ic_file_video_select)] = ".mp4.avi.mkv.flv.rmvb.wmv.mov",
        [new(ic_file_zip, ic_file_zip_select)] = ".zip.rar.7z.tar.gz.bz2.xz.apk",
        [new(ic_file_text, ic_file_text_select)] = ".txt",
        [new(ic_file_json, ic_file_json_select)] = ".json",
        [new(ic_file_excel, ic_file_excel_select)] = ".csv.xlsx.tsv",
        [new(ic_file_ppt, ic_file_ppt_select)] = ".pptx",
        [new(ic_file_pdf, ic_file_pdf_select)] = ".pdf",
        [new(ic_file_word, ic_file_word_select)] = ".docx",
    }.SelectMany(item => item.Value.Split('.')
        .Where(v => !string.IsNullOrEmpty(v))
        .Select(v => new { Key = '.' + v, Value = item.Key })
    ).ToDictionary(pair => pair.Key, pair => pair.Value);


    //图片文件
    public static readonly string ImageTypes = ".png.jpg.jpeg.gif.bmp";

    //多媒体
    public static readonly string MediaTypes = ImageTypes + ".mp4.avi.mkv.flv.rmvb.wmv.mov";
}