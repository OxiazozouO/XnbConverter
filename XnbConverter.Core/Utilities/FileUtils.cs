using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using XnbConverter.Exceptions;

namespace XnbConverter.Utilities;

public static class FileUtils
{
    public class IgnoreJsonIgnoreResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty jsonProperty = base.CreateProperty(member, memberSerialization);
            if (member.GetCustomAttribute(typeof(ContentSerializerIgnoreAttribute)) != null)
            {
                jsonProperty.ShouldSerialize = (object _) => false;
            }

            return jsonProperty;
        }
    }

    public static readonly JsonSerializerSettings Settings = new()
    {
        Converters = { new StringEnumConverter() },
        ContractResolver = new IgnoreJsonIgnoreResolver(),
        Formatting = Formatting.Indented
    };

    public static Dictionary<string, List<(string, string)>> BuildFiles(string? input, string? output)
    {
        if (Path.GetExtension(output) != "")
        {
            output = Path.GetDirectoryName(output);
        }

        try
        {
            if (!Directory.Exists(output))
            {
                Directory.CreateDirectory(output);
            }
        }
        catch (Exception ex)
        {
            throw new XnbError(Error.FileUtils_1, output, ex.Message);
        }

        string[] array;
        if (Directory.Exists(input))
        {
            array = Directory.GetFiles(input, "*.*", SearchOption.AllDirectories);
        }
        else
        {
            if (!File.Exists(input))
            {
                throw new XnbError(Error.FileUtils_2, Path.GetFullPath(input));
            }

            array = new string[1] { input };
        }

        Dictionary<string, List<(string, string)>> dictionary = new Dictionary<string, List<(string, string)>>();
        string[] array2 = array;
        foreach (string text in array2)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
            string extension = Path.GetExtension(text);
            string text2 = text;
            int length = input.Length;
            int num = fileNameWithoutExtension.Length + extension.Length;
            string text3 = text2.Substring(length, text2.Length - num - length);
            string text4 = output + text3;
            if (!dictionary.ContainsKey(extension))
            {
                dictionary[extension] = new List<(string, string)>();
            }

            dictionary[extension].Add((text, text4 + fileNameWithoutExtension));
        }

        return dictionary;
    }

    public static void CreateDirectory(this List<(string, string)> list)
    {
        foreach (var item in list)
        {
            string directoryName = Path.GetDirectoryName(item.Item2);
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
        }
    }

    public static void ToJson(this object data, string path, bool isFileOrd = false)
    {
        string text = JsonConvert.SerializeObject(data, Settings);
        if (isFileOrd)
        {
            text = text.Replace("\\\\", "\\").Replace("\\n", "\n");
        }

        File.WriteAllText(path, text);
    }

    public static T ToEntity<T>(this string path, bool isFileOrd = false)
    {
        if (!File.Exists(path))
        {
            throw new FileLoadException("文件" + path + "不存在！");
        }

        string text = File.ReadAllText(path);
        if (isFileOrd)
        {
            text = text.Replace("\\", "\\\\").Replace("\\n", "\\\\n");
        }

        T val = JsonConvert.DeserializeObject<T>(text, Settings);
        if (val == null)
        {
            throw new FileLoadException("读取json失败！");
        }

        return val;
    }


    public static T ToEntity<T>(this Stream stream, bool isFileOrd = false)
    {
        string text = "";
        using (StreamReader streamReader =
               new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = streamReader.ReadToEnd();
        }

        if (isFileOrd)
        {
            text = text.Replace("\\", "\\\\").Replace("\\n", "\\\\n");
        }

        T val = JsonConvert.DeserializeObject<T>(text, Settings);
        if (val == null)
        {
            throw new FileLoadException("读取json失败！");
        }

        return val;
    }

    public static string RandomName =>
        new StringBuilder().Append(DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"))
            .Append(Guid.NewGuid().ToString().Replace("-", "")).ToString();

    public static string FileSize(long size)
    {
        return size switch
        {
            < 1048576L and >= 1024 => $"{size / 1024L:F2} KB",
            < 1048576L => size + " B",
            < 1073741824L => $"{size / 1048576L:F2} MB",
            _ => $"{size / 1073741824L:F2} MB"
        };
    }

    public static string UpdateTime(DateTime t) =>
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMilliseconds(t.Ticks)
            .ToString("yyyy/M/d H:m");

    public static string UpdateTime(long t) =>
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMilliseconds(t)
            .ToString("yyyy/M/d H:m");
}