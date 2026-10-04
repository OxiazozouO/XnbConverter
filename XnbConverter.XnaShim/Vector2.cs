using System.Runtime.Serialization;

namespace Microsoft.Xna.Framework;

// [DataContract] 的原因见 Rectangle.cs
//
// 与真 MonoGame 的一处已知差异：真 Vector2 还挂着 [TypeConverter]，Newtonsoft 会把它
// 写成字符串 "0, -20"；这里没有转换器，写成对象 {"X":0.0,"Y":-20.0}。
// 后者与工具自身实体类型的历史格式一致，所以保留 —— 代价见 XnbConverter.XnaShim.csproj 的说明。
[DataContract]
public struct Vector2
{
	[DataMember]
	public float X;

	[DataMember]
	public float Y;

	public Vector2(float x, float y)
	{
		X = x;
		Y = y;
	}

	public Vector2(float value)
	{
		X = value;
		Y = value;
	}

	public static Vector2 Zero => new Vector2(0f, 0f);
}
