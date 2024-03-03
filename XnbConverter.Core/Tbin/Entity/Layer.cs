using System.Collections.Generic;
using XnbConverter.Entity.Mono;

namespace XnbConverter.Tbin.Entity;

public class Layer
{
    /// <summary>
    ///     The layer ID.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    ///     If the layer is visible or not.
    /// </summary>
    public byte Visible { get; set; }

    /// <summary>
    ///     The layer description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    ///     The size of a layer, in tiles.
    /// </summary>
    public IntVector2 LayerSize { get; set; }

    /// <summary>
    ///     The size of a tile, in pixels.
    /// </summary>
    public IntVector2 TileSize { get; set; }

    /// <summary>
    ///     The layer properties.
    /// </summary>
    public List<Propertie> Properties { get; set; }

    /// <summary>
    ///     The list of tiles.
    /// </summary>
    public List<BaseTile> Tiles { get; set; }


    public List<char> Index { get; set; }

    public List<int> _sizeArr { get; set; }

    public List<string> _currTileSheet { get; set; }
}