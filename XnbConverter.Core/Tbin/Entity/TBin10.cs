namespace XnbConverter.Tbin.Entity;

public class TBin10
{
    public const int LayerMax = 7;
    public byte[] Data { set; get; }
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

    public (int, int) IdToPos(int id) //x,y
    {
        return (id % Layers[0].LayerSize.X, id / Layers[0].LayerSize.X);
    }

    public void RemoveTileSheetsExtension()
    {
        foreach (var t in TileSheets)
            t.Image = t.Image.Replace(".png", "");
    }

    public void RemoveNullProperties()
    {
        for (var i = 0; i < Properties.Count; i++)
        {
            var s = Properties[i].Value as string;
            if (s == "") Properties.RemoveAt(i--);
        }
    }

    public void RemovePropertiesStr()
    {
        foreach (var property in Properties)
            if (property.Value is string s)
                property.Value = s.Replace("Custom_", "");

        List<Propertie>? properties = null;
        foreach (var la in Layers)
        foreach (var t in la.Tiles)
            switch (t)
            {
                case null:
                    continue;
                case StaticTile:

                    if (t is StaticTile s) properties = s.Properties;

                    goto default;
                case AnimatedTile:
                    if (t is AnimatedTile a) properties = a.Properties;
                    goto default;
                default:
                    if (properties == null) break;
                    foreach (var pr in properties)
                        if (pr.Value is string ss)
                            pr.Value = ss.Replace("Custom_", "");
                    properties = null;
                    break;
            }
    }
}