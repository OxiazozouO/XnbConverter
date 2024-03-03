using System.Collections.Generic;

namespace XnbConverter.Tbin.Entity;

public class AnimatedTile : BaseTile
{
    /// <summary>
    ///     每帧动画的间隔，以毫秒为单位。
    /// </summary>
    public int FrameInterval { get; set; }

    /// <summary>
    ///     此平铺的框架，作为静态平铺。
    /// </summary>
    public List<StaticTile> Frames { get; set; }

    public int _frameCount { get; set; }

    public List<char> Index { get; set; }

    public List<string> _currTileSheet { get; set; }
}