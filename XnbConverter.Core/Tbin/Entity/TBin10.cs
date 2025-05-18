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
			tileSheet.Image = tileSheet.Image.Replace(".png", "");
		}
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
