using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace XnbConverter.Entity.Mono;

public class Texture2D
{
	private static readonly List<Task> tasks = new List<Task>();

	[JsonIgnore]
	public byte[] Data;

	public int Format;

	public int Width;

	public int Height;

	public int MipCount;

	public uint DataSize;

	public void SaveAsPng(string path)
	{
		Task task = Task.Run(async delegate
		{
			using Image<Rgba32> image = new Image<Rgba32>(Width, Height);
			int num = -1;
			for (int i = 0; i < Height; i++)
			{
				for (int j = 0; j < Width; j++)
				{
					Rgba32 value = default(Rgba32);
					value.R = Data[++num];
					value.G = Data[++num];
					value.B = Data[++num];
					value.A = Data[++num];
					image[j, i] = value;
				}
			}
			await image.SaveAsPngAsync(path);
		});
		if (task != null)
		{
			tasks.Add(task);
		}
	}

	public static Texture2D FromPng(string path)
	{
		Texture2D texture2D = new Texture2D();
		path = Path.GetFullPath(path);
		using Image<Rgba32> image = Image.Load<Rgba32>(path);
		texture2D.Width = image.Width;
		texture2D.Height = image.Height;
		texture2D.Data = new byte[texture2D.Width * texture2D.Height * 4];
		int num = -1;
		for (int i = 0; i < texture2D.Height; i++)
		{
			for (int j = 0; j < texture2D.Width; j++)
			{
				Rgba32 rgba = image[j, i];
				texture2D.Data[++num] = rgba.R;
				texture2D.Data[++num] = rgba.G;
				texture2D.Data[++num] = rgba.B;
				texture2D.Data[++num] = rgba.A;
			}
		}
		return texture2D;
	}

	public static void WaitAll()
	{
		Task.WaitAll(tasks.ToArray());
		tasks.Clear();
	}
}
