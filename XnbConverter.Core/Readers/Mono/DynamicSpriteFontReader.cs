using XnbConverter.Entity.Mono;
using XnbConverter.Readers.Base.ValueReaders;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace XnbConverter.Readers.Mono;

/// <summary>
/// ReLogic.Graphics.DynamicSpriteFontReader 的等价实现（Terraria 专用）。
///
/// 该类型来自 ReLogic.dll —— 它被内嵌在 Terraria.exe 里，而且带着 Requires32Bit 标记，
/// 在 .NET 8 上根本加载不了，所以不去反射它，而是**按其 Read() 反编译出的格式手写**：
/// <code>
///   float spacing → int lineSpacing → char defaultCharacter → int pageCount
///   每页: Texture2D → List&lt;Rectangle&gt; glyphs → List&lt;Rectangle&gt; padding
///         → List&lt;char&gt; characters → List&lt;Vector3&gt; kerning
/// </code>
/// </summary>
public class DynamicSpriteFontReader : BaseReader
{
	private readonly CharReader _charReader = new CharReader();

	private int _charListReader;

	private int _rectangleListReader;

	private int _texture2DReader;

	private int _vector3ListReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		_charReader.Init(resolver);
		_texture2DReader = resolver.GetIndex(typeof(Texture2D));
		_rectangleListReader = resolver.GetIndex(typeof(List<Rectangle>));
		_charListReader = resolver.GetIndex(typeof(List<char>));
		_vector3ListReader = resolver.GetIndex(typeof(List<Vector3>));
	}

	public override object Read()
	{
		DynamicSpriteFont dynamicSpriteFont = new DynamicSpriteFont
		{
			Spacing = bufferReader.ReadSingle(),
			LineSpacing = bufferReader.ReadInt32(),
			DefaultCharacter = (char)_charReader.Read()
		};

		int num = bufferReader.ReadInt32();
		for (int i = 0; i < num; i++)
		{
			dynamicSpriteFont.Pages.Add(new DynamicSpriteFont.Page
			{
				Texture = readerResolver.Read<Texture2D>(_texture2DReader),
				Glyphs = readerResolver.Read<List<Rectangle>>(_rectangleListReader),
				Padding = readerResolver.Read<List<Rectangle>>(_rectangleListReader),
				Characters = readerResolver.Read<List<char>>(_charListReader),
				Kerning = readerResolver.Read<List<Vector3>>(_vector3ListReader)
			});
		}

		return dynamicSpriteFont;
	}

	public override void Write(object content)
	{
		DynamicSpriteFont dynamicSpriteFont = (DynamicSpriteFont)content;
		bufferWriter.WriteSingle(dynamicSpriteFont.Spacing);
		bufferWriter.WriteInt32(dynamicSpriteFont.LineSpacing);
		_charReader.Write(dynamicSpriteFont.DefaultCharacter);
		bufferWriter.WriteInt32(dynamicSpriteFont.Pages.Count);
		foreach (DynamicSpriteFont.Page page in dynamicSpriteFont.Pages)
		{
			// 和 SpriteFont 同理：字体页要走 XNA 写死端点的 DXT3 编码器（FontDxt3）才能逐字节还原，
			// 漏掉这一句就会落到通用 Squish 编码器 —— 像素等价但块字节不同。
			if (page.Texture != null)
			{
				page.Texture.IsFontTexture = true;
			}

			readerResolver.Write(_texture2DReader, page.Texture);
			readerResolver.Write(_rectangleListReader, page.Glyphs);
			readerResolver.Write(_rectangleListReader, page.Padding);
			readerResolver.Write(_charListReader, page.Characters);
			readerResolver.Write(_vector3ListReader, page.Kerning);
		}
	}
}
