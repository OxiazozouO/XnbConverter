using Newtonsoft.Json;
using SixLabors.ImageSharp.Metadata;
using Image = SixLabors.ImageSharp.Image;

namespace XnbConverter.Entity.Mono;

public class Texture2D
{
    private static List<Task> tasks = new List<Task>();
    
    [JsonIgnore]
    public byte[] Data{ set; get; }
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
        tasks.Add(Task.Run(async () =>
        {
            using Image<Rgba32> image = new Image<Rgba32>(Width, Height);
            int index = -1;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Rgba32 pixel = new Rgba32();
                    pixel.R = Data[++index];
                    pixel.G = Data[++index];
                    pixel.B = Data[++index];
                    pixel.A = Data[++index];
                    image[x, y] = pixel;
                }
            }

            await image.SaveAsPngAsync(path);
        }));
    }

    /**
     * 将PNG转换为Texture2D。
     * @param {byte[]} data
     * @returns {object}
     */
    public static Texture2D FromPng(string path)
    {
        Texture2D t2d = new Texture2D();
        path = Path.GetFullPath(path);
        using Image<Rgba32> image = Image.Load<Rgba32>(path);
        t2d.Width = image.Width;
        t2d.Height = image.Height;
        t2d.Data = new byte[t2d.Width * t2d.Height * 4];
        int index = -1;
        for (int y = 0; y < t2d.Height; y++)
        {
            for (int x = 0; x < t2d.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                t2d.Data[++index] = pixel.R;
                t2d.Data[++index] = pixel.G;
                t2d.Data[++index] = pixel.B;
                t2d.Data[++index] = pixel.A;
            }
        }
        return t2d;
    }

    public static void WaitAll()
    {
        Task.WaitAll(tasks.ToArray());
        tasks.Clear();
    }
}