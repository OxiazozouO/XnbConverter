using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Auto_Tbin;

public static class ImgUtil
{
    public static void DrawImagePortion(this Image<Rgba32> destinationImage, int id1, Image<Rgba32> sourceImage,
        int id2) //长宽不超过4w
    {
        // 截取源图片的指定区域
        var sourcePortion = sourceImage.Clone(x => x.Crop(IdToRectangle(sourceImage.Width, id2)));
        destinationImage.Mutate(x =>
            x.DrawImage(sourcePortion, IdToRectangle(destinationImage.Width, id1).Location, 1));
    }

    public static Rectangle IdToRectangle(int width, int id)
    {
        var w = width / 16;
        return new Rectangle(id % w * 16, id / w * 16, 16, 16);
    }
}