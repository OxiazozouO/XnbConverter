// 必须用别名：Rectangle 住在替身程序集里，而本项目 global using 了 SixLabors.ImageSharp，
// 那边也有同名同形的 Rectangle，直接 using 命名空间会撞成 CS0104。
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using XnbConverter.Exceptions;
using XnbConverter.Utilities;

namespace XnbConverter.Entity.Mono;

/// <summary>
/// ReLogic 的 DynamicSpriteFont。
///
/// Terraria 不用 XNA 自带的 SpriteFont，而是自己定义了这个类型，由
/// ReLogic.Graphics.DynamicSpriteFontReader 读取（格式反编译自该 reader）：
/// <code>
///   float spacing; int lineSpacing; char defaultCharacter; int pageCount;
///   每个页面: Texture2D → List&lt;Rectangle&gt; glyphs → List&lt;Rectangle&gt; padding
///             → List&lt;char&gt; characters → List&lt;Vector3&gt; kerning
/// </code>
/// 一个字体有多张图集（页面），导出时除首页用 &lt;名字&gt;.png 外，
/// 其余页面依次写成 &lt;名字&gt;.1.png、&lt;名字&gt;.2.png ……
/// </summary>
public class DynamicSpriteFont
{
	public class Page
	{
		public Texture2D? Texture;

		public List<Rectangle>? Glyphs;

		public List<Rectangle>? Padding;

		public List<char>? Characters;

		public List<Vector3>? Kerning;
	}

	public float Spacing;

	public int LineSpacing;

	public char DefaultCharacter;

	public List<Page> Pages = new List<Page>();

	/// <summary>第 index 页对应的图片路径（首页就是 basePath，其余加序号）。</summary>
	public static string PagePath(string basePath, int index)
	{
		if (index == 0)
		{
			return basePath;
		}

		string directoryName = Path.GetDirectoryName(basePath) ?? ".";
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(basePath);
		string extension = Path.GetExtension(basePath);
		return Path.Combine(directoryName, fileNameWithoutExtension + "." + index + extension);
	}

	public void Save(string jsonPath, string firstPagePath)
	{
		if (Pages.Count > 1)
		{
			// 多页时把每页的图片路径记进 json，方便回读
			for (int i = 0; i < Pages.Count; i++)
			{
				Pages[i].Texture?.SaveAsPng(PagePath(firstPagePath, i));
			}
		}
		else if (Pages.Count == 1)
		{
			Pages[0].Texture?.SaveAsPng(firstPagePath);
		}

		this.ToJson(jsonPath);
	}

	public static DynamicSpriteFont FormFiles(string jsonPath, string firstPagePath)
	{
		DynamicSpriteFont dynamicSpriteFont = jsonPath.ToEntity<DynamicSpriteFont>();
		if (dynamicSpriteFont == null)
		{
			throw new XnbError(Error.DynamicSpriteFont_1);
		}

		for (int i = 0; i < dynamicSpriteFont.Pages.Count; i++)
		{
			Page page = dynamicSpriteFont.Pages[i];
			if (page.Texture == null)
			{
				continue;
			}

			Texture2D texture2D = Texture2D.FromPng(PagePath(firstPagePath, i));
			page.Texture.Width = texture2D.Width;
			page.Texture.Height = texture2D.Height;
			page.Texture.Data = texture2D.Data;
		}

		return dynamicSpriteFont;
	}
}
