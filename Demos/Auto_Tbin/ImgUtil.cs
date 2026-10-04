namespace Auto_Tbin;

public static class ImgUtil
{
    /// <summary>
    /// 把 <paramref name="sourceImage"/> 第 <paramref name="id2"/> 格（16x16）
    /// 贴到 <paramref name="destinationImage"/> 第 <paramref name="id1"/> 格。
    /// 保留原方法名，调用点不用动；实现改走自研 <see cref="Bitmap"/>，不再依赖 ImageSharp。
    /// </summary>
    public static void DrawImagePortion(this Bitmap destinationImage, int id1, Bitmap sourceImage, int id2)
    {
        destinationImage.DrawTile(id1, sourceImage, id2);
    }
}
