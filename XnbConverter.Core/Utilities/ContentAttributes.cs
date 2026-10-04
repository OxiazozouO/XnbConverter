using System.Reflection;

namespace XnbConverter.Utilities;

/// <summary>
/// XNA / MonoGame 内容管线在实体类型上标注的序列化特性。
///
/// 这里按**全名**判断而不是按类型对象判断，因为带这些特性的类型通常来自外部游戏程序集
/// （编译时引用的是 Microsoft.Xna.Framework.Content）。按名字匹配就不必为了两个特性
/// 把整个 MonoGame.Framework 拉进本项目。
/// </summary>
public static class ContentAttributes
{
	/// <summary>标注该成员需要被内容管线序列化。</summary>
	public const string Serializer = "Microsoft.Xna.Framework.Content.ContentSerializerAttribute";

	/// <summary>标注该成员不参与序列化。</summary>
	public const string SerializerIgnore = "Microsoft.Xna.Framework.Content.ContentSerializerIgnoreAttribute";

	/// <summary>成员上是否标注了指定全名的特性。</summary>
	public static bool Has(MemberInfo member, string fullName)
	{
		foreach (object attribute in member.GetCustomAttributes(inherit: false))
		{
			if (attribute.GetType().FullName == fullName)
			{
				return true;
			}
		}

		return false;
	}
}
