using XnbConverter.Entity.Mono;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using Rectangle = XnbConverter.Entity.Mono.Rectangle;

namespace XnbConverter.Readers.Mono;

public class SpriteFontReader : BaseReader
{
    private readonly NullableReader<CharReader, char> nullableReader = new NullableReader<CharReader, char>();

    private int charListReader;

    private int rectangleListReader;

    private int texture2DReader;

    private int vector3ListReader;

    public override void Init(ReaderResolver resolver)
    {
        base.Init(resolver);
        nullableReader.Init(resolver);
        texture2DReader = resolver.GetIndex(typeof(Texture2D));
        rectangleListReader = resolver.GetIndex(typeof(List<Rectangle>));
        charListReader = resolver.GetIndex(typeof(List<char>));
        vector3ListReader = resolver.GetIndex(typeof(List<Vector3>));
    }

    public override object Read()
    {
        return new SpriteFont
        {
            Texture = readerResolver.Read<Texture2D>(texture2DReader),
            Glyphs = readerResolver.Read<List<Rectangle>>(rectangleListReader),
            Cropping = readerResolver.Read<List<Rectangle>>(rectangleListReader),
            CharacterMap = readerResolver.Read<List<char>>(charListReader),
            VerticalLineSpacing = bufferReader.ReadInt32(),
            HorizontalSpacing = bufferReader.ReadSingle(),
            Kerning = readerResolver.Read<List<Vector3>>(vector3ListReader),
            DefaultCharacter = nullableReader.Read() as char?
        };
    }

    public override void Write(object content)
    {
        SpriteFont spriteFont = (SpriteFont)content;
        readerResolver.Write(texture2DReader, spriteFont.Texture);
        readerResolver.Write(rectangleListReader, spriteFont.Glyphs);
        readerResolver.Write(rectangleListReader, spriteFont.Cropping);
        readerResolver.Write(charListReader, spriteFont.CharacterMap);
        bufferWriter.WriteInt32(spriteFont.VerticalLineSpacing);
        bufferWriter.WriteSingle(spriteFont.HorizontalSpacing);
        readerResolver.Write(vector3ListReader, spriteFont.Kerning);
        nullableReader.Write(spriteFont.DefaultCharacter);
    }

    public override bool IsValueType()
    {
        return false;
    }
}