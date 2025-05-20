namespace XnbConverter.Tbin.Entity;

public class StaticTile : BaseTile
{
	public string TileSheet { get; set; }

	public int TileIndex { get; set; }

	public byte BlendMode { get; set; }

	public string GetId()
	{
		return TileSheet + "@" + TileIndex;
	}
}
