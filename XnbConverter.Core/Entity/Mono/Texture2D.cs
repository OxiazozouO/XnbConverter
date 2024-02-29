using Newtonsoft.Json;
using SixLabors.ImageSharp.Metadata;
using Image = SixLabors.ImageSharp.Image;

namespace XnbConverter.Entity.Mono;

public class Texture2D
{
    private static List<Task> tasks = new();

    [JsonIgnore] public byte[] Data { set; get; }
    public int Format { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int MipCount { get; set; }
    public uint DataSize { get; set; }

    /**
     * 将Texture2D转换为PNG。
     * @param {int} width
     * @param {int} height
     * @param {byte[]} buffer
     * @returns {byte[]}
     */
    public void SaveAsPng(string path)
    {
        Task? task = Task.Run(async () =>
        {
            using var image = new Image<Rgba32>(Width, Height);
            var index = -1;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var pixel = new Rgba32();
                pixel.R = Data[++index];
                pixel.G = Data[++index];
                pixel.B = Data[++index];
                pixel.A = Data[++index];
                image[x, y] = pixel;
            }

            await image.SaveAsPngAsync(path);
        });
        if (task is not null)
        {
            tasks.Add(task);
        }
    }

    /**
     * 将PNG转换为Texture2D。
     * @param {byte[]} data
     * @returns {object}
     */
    public static Texture2D FromPng(string path)
    {
        var t2d = new Texture2D();
        path = Path.GetFullPath(path);
        using var image = Image.Load<Rgba32>(path);
        t2d.Width = image.Width;
        t2d.Height = image.Height;
        t2d.Data = new byte[t2d.Width * t2d.Height * 4];
        var index = -1;
        for (var y = 0; y < t2d.Height; y++)
        for (var x = 0; x < t2d.Width; x++)
        {
            var pixel = image[x, y];
            t2d.Data[++index] = pixel.R;
            t2d.Data[++index] = pixel.G;
            t2d.Data[++index] = pixel.B;
            t2d.Data[++index] = pixel.A;
        }

        return t2d;
    }

    public static void WaitAll()
    {
        Task.WaitAll(tasks.ToArray());
        tasks.Clear();
    }
}