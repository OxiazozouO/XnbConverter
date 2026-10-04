using System.Runtime.Serialization;

namespace Microsoft.Xna.Framework;

// [DataContract] 不是装饰：真 MonoGame 的 Rectangle 也挂着它，Newtonsoft 因此走 OptIn，
// 只序列化 [DataMember] 的字段，派生的只读属性（Left/Top/Right/Bottom/Center/…）不会被写进 JSON。
// 少了它就会走 OptOut，把下面这些属性也吐出来，与真 MonoGame 的 JSON 不一致。
[DataContract]
public struct Rectangle
{
	[DataMember]
	public int X;

	[DataMember]
	public int Y;

	[DataMember]
	public int Width;

	[DataMember]
	public int Height;

	public Rectangle(int x, int y, int width, int height)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	public static Rectangle Empty => new Rectangle(0, 0, 0, 0);

	public int Right => X + Width;

	public int Bottom => Y + Height;
}
