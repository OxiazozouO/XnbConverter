using XnbConverter.Utilities;

namespace XnbConverter.Entity.Mono;

public class SpriteFont
{
    //用于存储生成的字体纹理的图像数据
    public Texture2D Texture;

    //用于存储每个字符在纹理中的边界。
    //每个Rectangle表示一个字符的边界框
    public List<Rectangle>? Glyphs;

    //用于存储每个字符在纹理中的裁剪区域。
    //每个Rectangle表示一个字符的裁剪区域
    //裁剪区域是字符实际占据的区域，用于在绘制时进行裁剪。
    public List<Rectangle>? Cropping;

    //一个字符列表，存储所有包含在生成的字体中的字符。每个字符都是一个Unicode字符。
    public List<char>? CharacterMap;

    //行间距，即字符行之间的垂直间距。
    //如果lineSpacing参数有值，则使用该值作为行间距；否则，使用字体信息中的行间距值。
    public int VerticalLineSpacing;

    //字符间距，即字符之间的水平间距。这个参数是在生成字体纹理时设置的。
    public float HorizontalSpacing;

    //用于存储字符之间的 字距 信息。
    //每个Vector3表示两个字符之间的字距，
    //xoff表示字符绘制时的水平偏移量，
    //yoff表示字符绘制时的垂直偏移量，
    //xadvance表示字符绘制后的水平偏移量。
    public List<Vector3>? Kerning;

    //欲显示 SpriteFont 字型定义文件中未定义的字符时，
    //就会自动显示 DefaultCharacter 属性指定的字符。
    public char? DefaultCharacter;


    public void Save(string jsonPath, string path)
    {
        Texture.SaveAsPng(path);
        // 保存JSON文件数据
        this.ToJson(jsonPath);
    }

    public static SpriteFont FormFiles(string jsonPath, string path)
    {
        var spriteFont = jsonPath.ToEntity<SpriteFont>();

        var t2d = Texture2D.FromPng(path);
        if (spriteFont is null)
            throw new XnbError(Helpers.I18N["SpriteFont.1"]);
        spriteFont.Texture.Width = t2d.Width;
        spriteFont.Texture.Height = t2d.Height;
        spriteFont.Texture.Data = t2d.Data;
        return spriteFont;
    }
}