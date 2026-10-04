using System.Runtime.Serialization;

namespace Microsoft.Xna.Framework;

// [DataContract] 的原因见 Rectangle.cs
[DataContract]
public struct Point
{
	[DataMember]
	public int X;

	[DataMember]
	public int Y;

	public Point(int x, int y)
	{
		X = x;
		Y = y;
	}

	public static Point Zero => new Point(0, 0);
}
