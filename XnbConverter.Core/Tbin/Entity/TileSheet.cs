using System.Collections.Generic;
using XnbConverter.Entity.Mono;

namespace XnbConverter.Tbin.Entity;

public class TileSheet
{
    public string Id { get; set; }
    public string Description { get; set; }
    public string Image { get; set; }
    public IntVector2 SheetSize { get; set; }
    public IntVector2 TileSize { get; set; }
    public IntVector2 Margin { get; set; }
    public IntVector2 Spacing { get; set; }
    public List<Propertie> Properties { get; set; }
}