using Android.Content;
using Android.Text;
using XnbConverter.Android.Activity;
using XnbConverter.Android.Adapter;
using XnbConverter.Android.Holder;
using XnbConverter.Utilities;
using static _Microsoft.Android.Resource.Designer.ResourceConstant.Drawable;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Helper;

public static class JavaFileHelper
{
    public static List<FileInfo> ListFiles(this FileInfo info)
    {
        if (!info.Root.IsDirectory) return [];
        var files = info.Root.ListFiles();
        if (files == null) return [];
        return files.Select(childNode => new FileInfo(childNode))
            .ToList();
    }

    public static readonly string AudioTypes = ".mp3|.wav|.ogg|.m4a";
    public static readonly string BinTypes = ".dll|.bin|.arsc|.dex|.prof|.profm|.rsa|.kotlin_builtins|.so";
    public static readonly string CodeTypes = ".xml|.html|.css|.js|.java|.svg|.cs|.c|.h|.cpp";
    public static readonly string ExcelTypes = ".csv|.xlsx|.tsv";
    public static readonly string FolderTypes = ".folder";
    public static readonly string FontTypes = ".ttf|.otf|.woff|.woff2|.eot|.fon";
    public static readonly string ImageTypes = ".png|.jpg|.jpeg|.gif|.bmp|.webp";
    public static readonly string JsonTypes = ".json";
    public static readonly string MediaTypes = ".mp4|.avi|.mkv|.flv|.rmvb|.wmv|.mov";
    public static readonly string XnbTypes = ".xnb";
    public static readonly string TextTypes = ".txt|.log|.md|.proto|.properties|.version|.v|.h0|.mf|.sf";
    public static readonly string ZipTypes = ".zip|.rar|.7z|.tar|.gz|.apk|.z";

    public static readonly FileTypeInfo OtherType = new()
    {
        ResId1 = ic_file,
        ResId2 = ic_file,
        Type = FileType.Empty
    };

    public static readonly Dictionary<string, FileTypeInfo> FileTypes = new Dictionary<string, FileTypeInfo>
    {
        [AudioTypes] = new()
        {
            ResId1 = ic_file_audio, ResId2 = ic_file_audio_select, Type = FileType.Audio
        },
        [BinTypes] = new()
        {
            ResId1 = ic_file_bin, ResId2 = ic_file_bin_select, Type = FileType.Bin
        },
        [CodeTypes] = new()
        {
            ResId1 = ic_file_code, ResId2 = ic_file_code_select, Type = FileType.Code
        },
        [ExcelTypes] = new()
        {
            ResId1 = ic_file_excel, ResId2 = ic_file_excel_select, Type = FileType.Excel
        },
        [ImageTypes] = new()
        {
            ResId1 = ic_file_image, ResId2 = ic_file_image_select, Type = FileType.Image
        },
        [JsonTypes] = new()
        {
            ResId1 = ic_file_json, ResId2 = ic_file_json_select, Type = FileType.Json
        },
        [MediaTypes] = new()
        {
            ResId1 = ic_file_video, ResId2 = ic_file_video_select, Type = FileType.Media
        },
        [XnbTypes] = new()
        {
            ResId1 = ic_file_xnb, ResId2 = ic_file_xnb_select, Type = FileType.Xnb
        },
        [TextTypes] = new()
        {
            ResId1 = ic_file_text, ResId2 = ic_file_text_select, Type = FileType.Text
        },
        [ZipTypes] = new()
        {
            ResId1 = ic_file_zip, ResId2 = ic_file_zip_select, Type = FileType.Zip
        },
        [FolderTypes] = new()
        {
            ResId1 = ic_folder, ResId2 = ic_folder_select, Type = FileType.Folder
        },
        [FontTypes] = new()
        {
            ResId1 = ic_file_font, ResId2 = ic_file_font_select, Type = FileType.Font
        },
        [".pptx"] = new()
        {
            ResId1 = ic_file_ppt, ResId2 = ic_file_ppt_select, Type = FileType.Other
        },
        [".pdf"] = new()
        {
            ResId1 = ic_file_pdf, ResId2 = ic_file_pdf_select, Type = FileType.Other
        },
        [".docx"] = new()
        {
            ResId1 = ic_file_word, ResId2 = ic_file_word_select, Type = FileType.Other
        }
    }.SelectMany(item => item.Key.Split('|')
        .Where(v => !string.IsNullOrEmpty(v))
        .Select(v => new { Key = v, Value = item.Value })
    ).ToDictionary(pair => pair.Key, pair => pair.Value);

    public static FileTypeInfo GetFileType(this FileInfo info)
    {
        return FileTypes.GetValueOrDefault(info.Extension, OtherType);
    }

    public static void OpenFile(
        this global::Android.App.Activity activity,
        FileInfo info,
        SelectFileData data,
        ModuleImageViewerHolder img
    )
    {
        var files = data.Holder.adapter.Models;
        switch (info.TypeInfo.Type)
        {
            case FileType.Audio:

                break;
            case FileType.Code:
                activity.StartActivity(new Intent(activity, typeof(CodeEditorActivity))
                    .PutExtra("path", info.Uri));
                break;
            case FileType.Excel:
                break;
            case FileType.Font:
                var ff = files.Where(f => FontTypes.Contains(f.Extension))
                    .Select(f => f.Uri).ToList();
                var ind = files.IndexOf(info);
                ind = ind == -1 ? 0 : ind;
                activity.StartActivity(new Intent(activity, typeof(FontViewerActivity))
                    .PutExtra("paths", ff.ToJson()));
                break;
            case FileType.Image:
                files = files.Where(f => f.TypeInfo.Type == FileType.Image).ToList();
                var index = files.IndexOf(info);
                index = index == -1 ? 0 : index;
                img.Bind(activity, files, index);
                break;
            case FileType.Json:
                activity.StartActivity(new Intent(activity, typeof(JsonEditorActivity))
                    .PutExtra("path", info.Uri));
                break;
            case FileType.Media:
                break;
            case FileType.Other:
                break;
            case FileType.Xnb:
                break;
            case FileType.Text:
                activity.StartActivity(new Intent(activity, typeof(CodeEditorActivity))
                    .PutExtra("path", info.Uri));
                break;
            case FileType.Zip:
                string path;
                bool isNext = false;

                if (data.NextHolder is null)
                {
                    path = info.Uri.Replace(info.Extension, "");
                }
                else
                {
                    isNext = true;
                    path = Path.Combine(data.NextHolder.Path, info.FileName);
                }

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                var helper = MsgBoxHelper.ShowProgressBar(activity, out var holder, "正在解压");

                try
                {
                    new Thread(() =>
                    {
                        CompressHelper.Decompress(info.Uri, path, action: holder.Bind);
                        activity.RunOnUiThread(() =>
                        {
                            helper.Dismiss();
                            if (isNext)
                            {
                                data.NextHolder.Bind(path);
                            }
                            else
                            {
                                data.Holder.Bind(path);
                            }
                        });
                    }).Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    try
                    {
                        MsgBoxHelper.Builder(activity)
                            .AddEditText("", InputTypes.ClassText, 100, "请输入密码")
                            .Show(l =>
                            {
                                var s = (string)l[0];
                                CompressHelper.Decompress(info.Uri, path, s, holder.Bind);
                                activity.RunOnUiThread(() =>
                                {
                                    helper.Dismiss();
                                    if (data.NextHolder is null)
                                    {
                                        data.Holder.Bind(path);
                                    }
                                    else
                                    {
                                        data.NextHolder.Bind(path);
                                    }
                                });
                            });
                    }
                    catch (Exception ee)
                    {
                        Console.WriteLine(ee);
                    }
                }

                break;
        }
    }
}

public enum FileType
{
    Audio,
    Bin,
    Code,
    Empty,
    Excel,
    Folder,
    Font,
    Image,
    Json,
    Media,
    Other,
    Xnb,
    Text,
    Zip
}

public class FileTypeInfo
{
    public int ResId1;
    public int ResId2;
    public FileType Type;
}