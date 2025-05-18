namespace XnbConverter.Tbin.Entity;

public class AnimatedTile : BaseTile
{
	public int FrameInterval { get; set; }

	public List<StaticTile> Frames { get; set; }

	public int _frameCount { get; set; }

	public List<char> Index { get; set; }

	public List<string> _currTileSheet { get; set; }
}
