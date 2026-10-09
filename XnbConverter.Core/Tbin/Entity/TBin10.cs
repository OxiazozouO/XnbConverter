namespace XnbConverter.Tbin.Entity;

public class TBin10
{
	public const int LayerMax = 100;

	public byte[] Data { get; set; }

	public string Format { get; set; }

	public string Id { get; set; }

	public string Description { get; set; }

	public List<Propertie> Properties { get; set; }

	public List<TileSheet> TileSheets { get; set; }

	public List<Layer> Layers { get; set; }

	public int PosToId(int x, int y)
	{
		return Layers[0].LayerSize.X * y + x;
	}

	public (int, int) IdToPos(int id)
	{
		return (id % Layers[0].LayerSize.X, id / Layers[0].LayerSize.X);
	}

	public void RemoveTileSheetsExtension()
	{
		foreach (TileSheet tileSheet in TileSheets)
		{
			// 只剥**图片**扩展名，且**大小写不敏感**。
			//
			// 原来是 `Replace(".png", "")`，只认小写。实测踩到过：SVE 的 `Woods2.tmx` 里那张图集写的是
			// `spring_outdoorsTileSheet2.PNG`（大写）⇒ 剥不掉 ⇒ 游戏去加载
			// `Maps/spring_outdoorsTileSheet2.PNG` ⇒ 找不到 ⇒ `Couldn't create the 'Woods' location`
			// ⇒ **整张地图连同传送点一起消失**（症状：mod 新增的地图全都看不到，
			// 而它对原版地图的 EditMap 改动正常 —— 后者基于原版 tbin，那里的名字本来就没扩展名）。
			//
			// 依据：抽查 150 张原版地图的 TileSheet.Image，带 .png/.jpg/.bmp 的是 **0 个**。
			tileSheet.Image = StripImageExtension(tileSheet.Image);
		}
	}

	private static string StripImageExtension(string image)
	{
		if (string.IsNullOrEmpty(image)) return image;

		string ext = Path.GetExtension(image);
		if (ext.Length == 0) return image;

		return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
			|| ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
			? image[..^ext.Length]
			: image;
	}

	public void RemoveNullProperties()
	{
		for (int i = 0; i < Properties.Count; i++)
		{
			if (Properties[i].Value as string == "")
			{
				Properties.RemoveAt(i--);
			}
		}
	}

	public void RemovePropertiesStr()
	{
		foreach (Propertie property in Properties)
		{
			if (property.Value is string text)
			{
				property.Value = text.Replace("Custom_", "");
			}
		}
		List<Propertie> list = null;
		foreach (Layer layer in Layers)
		{
			foreach (BaseTile tile in layer.Tiles)
			{
				if (tile == null)
				{
					continue;
				}
				if (!(tile is StaticTile))
				{
					if (tile is AnimatedTile && tile is AnimatedTile animatedTile)
					{
						list = animatedTile.Properties;
					}
				}
				else if (tile is StaticTile staticTile)
				{
					list = staticTile.Properties;
				}
				if (list == null)
				{
					continue;
				}
				foreach (Propertie item in list)
				{
					if (item.Value is string text2)
					{
						item.Value = text2.Replace("Custom_", "");
					}
				}
				list = null;
			}
		}
	}
}
