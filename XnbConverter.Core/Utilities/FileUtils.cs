using Newtonsoft.Json;
using static System.IO.Directory;
using static System.IO.File;

namespace XnbConverter.Utilities;

public static class FileUtils
{
    /**
     * 用于在具有输入/输出的路径上行走以进行处理
     * @param {Function} fn
     * @param {String} input
     * @param {String} output
     * @param {Function} cb
     */
    public static Dictionary<string, List<(string, string)>> BuildFiles(string? input, string? output)
    {
        if (Path.GetExtension(output) is not "") //输出的不为文件夹
            output = Path.GetDirectoryName(output);

        try
        {
            if (!Directory.Exists(output)) //如果不存在就创建文件夹
                Directory.CreateDirectory(output); //创建该文件夹
        }
        catch (Exception ex)
        {
            throw new XnbError(Helpers.I18N["FileUtils.1"], output, ex.Message);
        }

        //文件+文件夹 获取文件数组  保证输出文件夹存在，构建输出的文件路径数组
        //文件夹+文件夹 获取文件数组  保证输出文件夹存在，构建输出的文件路径数组
        //获取指定扩展名对应的文件

        string[] files;
        if (Directory.Exists(input)) //为文件夹且存在
            files = GetFiles(input, "*.*", SearchOption.AllDirectories);
        else if (File.Exists(input))
            files = new[] { input };
        else //无效路径
            throw new XnbError(Helpers.I18N["FileUtils.2"], Path.GetFullPath(input));
        //构建输出路径
        Dictionary<string, List<(string, string)>> map = new();
        string fileName;
        string ext;
        string secondaryPath;
        string path;
        foreach (var file in files)
        {
            fileName = Path.GetFileNameWithoutExtension(file);
            ext = Path.GetExtension(file);
            secondaryPath = file[input.Length..^(fileName.Length + ext.Length)];
            path = output + secondaryPath;
            if (!map.ContainsKey(ext)) map[ext] = new List<(string, string)>();
            map[ext].Add((file, path + fileName));
        }

        return map;
    }

    /**
     * 如果不存在就创建文件夹
     *
     */
    public static void CreateDirectory(this List<(string, string)> list)
    {
        foreach (var value in list)
        {
            var path = Path.GetDirectoryName(value.Item2);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }

    public static void ToJson(this object data, string path)
    {
        WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    public static T ToEntity<T>(this string path,bool isFileOrd=false)
    {
        if (!File.Exists(path))
            throw new FileLoadException($"文件{path}不存在！");
        string json = ReadAllText(path);
        if (isFileOrd)
        {
            json = json.Replace(@"\", @"\\").Replace(@"\n", @"\\n");
        }
        return JsonConvert.DeserializeObject<T>(json) ??
               throw new FileLoadException("读取json失败！");
    }
}