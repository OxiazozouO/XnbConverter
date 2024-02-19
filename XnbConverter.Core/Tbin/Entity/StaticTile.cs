namespace XnbConverter.Tbin.Entity;

public class StaticTile : BaseTile
{
    /// <summary>
    /// 此磁贴从中提取的磁贴表。
    /// </summary>
    public string TileSheet { get; set; }

    /// <summary>
    /// 此磁贴的磁贴表中的索引。
    /// </summary>
    public int TileIndex { get; set; }

    /// <summary>
    /// 此平铺的混合模式。
    /// </summary>
    public byte BlendMode { get; set; }

    public string GetId() => TileSheet + "@" + TileIndex;
}