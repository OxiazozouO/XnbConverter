using XnbConverter.Exceptions;
using XnbConverter.Utilities;

namespace XnbConverter.Entity.Mono;

public class SpriteFont
{
	public Texture2D Texture;

	public List<Rectangle>? Glyphs;

	public List<Rectangle>? Cropping;

	public List<char>? CharacterMap;

	public int VerticalLineSpacing;

	public float HorizontalSpacing;

	public List<Vector3>? Kerning;

	public char? DefaultCharacter;

	public void Save(string jsonPath, string path)
	{
		Texture.SaveAsPng(path);
		this.ToJson(jsonPath);
	}

	public static SpriteFont FormFiles(string jsonPath, string path)
	{
		SpriteFont spriteFont = jsonPath.ToEntity<SpriteFont>();
		Texture2D texture2D = Texture2D.FromPng(path);
		if (spriteFont == null)
		{
			throw new XnbError(Error.SpriteFont_1);
		}
		spriteFont.Texture.Width = texture2D.Width;
		spriteFont.Texture.Height = texture2D.Height;
		spriteFont.Texture.Data = texture2D.Data;
		return spriteFont;
	}
}
