using XnbConverter.Entity.Mono;

namespace XnbConverter.Tbin.Entity;

public class Layer
{
	public string Id;

	public byte Visible;

	public string Description;

	public IntVector2 LayerSize;

	public IntVector2 TileSize;

	public List<Propertie> Properties;

	public List<BaseTile> Tiles;

	public List<char> Index;

	public List<int> _sizeArr;

	public List<string> _currTileSheet;
}
