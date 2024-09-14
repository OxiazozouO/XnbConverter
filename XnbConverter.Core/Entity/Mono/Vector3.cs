namespace XnbConverter.Entity.Mono;

public struct Vector3
{
	public static readonly Vector3 Zero = new Vector3();

	public static readonly Vector3 One = new Vector3(1f);

	public static readonly Vector3 Grid = new Vector3(31f, 63f, 31f);

	public float X;

	public float Y;

	public float Z;

	public Vector3(float x, float y, float z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public Vector3(float v)
		: this(v, v, v)
	{
	}

	public Vector3()
	{
		X = (Y = (Z = 0f));
	}

	public Vector3 Clamp(float min, float max)
	{
		if (X < min)
		{
			X = min;
		}
		else if (X > max)
		{
			X = max;
		}
		if (Y < min)
		{
			Y = min;
		}
		else if (Y > max)
		{
			Y = max;
		}
		if (Z < min)
		{
			Z = min;
		}
		else if (Z > max)
		{
			Z = max;
		}
		return this;
	}

	public Vector3 HalfAdjust()
	{
		X = (int)(X + 0.5f);
		Y = (int)(Y + 0.5f);
		Z = (int)(Z + 0.5f);
		return this;
	}

	public static Vector3 operator +(Vector3 left, Vector3 right)
	{
		return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
	}

	public static Vector3 operator -(Vector3 left, Vector3 right)
	{
		return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
	}

	public static Vector3 operator *(Vector3 left, Vector3 right)
	{
		return new Vector3(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
	}

	public static Vector3 operator /(Vector3 left, Vector3 right)
	{
		return new Vector3(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
	}

	public static Vector3 operator *(float v, Vector3 right)
	{
		return new Vector3(v * right.X, v * right.Y, v * right.Z);
	}

	public static Vector3 operator /(Vector3 left, float right)
	{
		return new Vector3(left.X / right, left.Y / right, left.Z / right);
	}

	public float Dot(Vector3 v2)
	{
		return (float)((double)X * (double)v2.X + (double)Y * (double)v2.Y + (double)Z * (double)v2.Z);
	}

	public float Dot(Vector4 v2)
	{
		return (float)((double)X * (double)v2.X + (double)Y * (double)v2.Y + (double)Z * (double)v2.Z);
	}

	public float LengthSquared()
	{
		return Dot(this);
	}

	public void Clear()
	{
		Fill(0f);
	}

	public void Fill(float f)
	{
		X = (Y = (Z = f));
	}
}
