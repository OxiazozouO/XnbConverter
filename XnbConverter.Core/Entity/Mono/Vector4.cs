namespace XnbConverter.Entity.Mono;

public struct Vector4
{
	public static readonly Vector4 Zero = new Vector4();

	public static readonly Vector4 HalfHalf2 = new Vector4(0.5f, 0.5f, 0.5f, 0.25f);

	public static readonly Vector4 Grid = new Vector4(31f, 63f, 31f, 0f);

	public float X;

	public float Y;

	public float Z;

	public float W;

	public Vector4()
	{
		X = Y = Z = W = 0f;
	}

	public Vector4(float x, float y, float z, float w)
	{
		X = x;
		Y = y;
		Z = z;
		W = w;
	}

	public Vector4(float v)
		: this(v, v, v, v)
	{
	}

	public static Vector4 operator +(Vector4 left, Vector4 right)
	{
		return new Vector4(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
	}

	public static Vector4 operator -(Vector4 left, Vector4 right)
	{
		return new Vector4(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
	}

	public static Vector4 operator *(Vector4 left, Vector4 right)
	{
		return new Vector4(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);
	}

	public static Vector4 operator /(Vector4 left, Vector4 right)
	{
		return new Vector4(left.X / right.X, left.Y / right.Y, left.Z / right.Z, left.W / right.W);
	}

	public static Vector4 operator +(Vector4 left, float right)
	{
		return new Vector4(left.X + right, left.Y + right, left.Z + right, left.W + right);
	}

	public static Vector4 operator -(Vector4 left, float right)
	{
		return new Vector4(left.X - right, left.Y - right, left.Z - right, left.W - right);
	}

	public static Vector4 operator *(float left, Vector4 right)
	{
		return new Vector4(left * right.X, left * right.Y, left * right.Z, left * right.W);
	}

	public static Vector4 operator /(Vector4 left, float right)
	{
		return new Vector4(left.X / right, left.Y / right, left.Z / right, left.W / right);
	}

	public Vector4 HalfAdjust()
	{
		X = (int)(X + 0.5f);
		Y = (int)(Y + 0.5f);
		Z = (int)(Z + 0.5f);
		W = (int)(W + 0.5f);
		return this;
	}

	public Vector4 Clamp(float min, float max)
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
		if (W < min)
		{
			W = min;
		}
		else if (W > max)
		{
			W = max;
		}
		return this;
	}

	public bool CompareAnyLessThan(Vector4 right)
	{
		if (!(X < right.X) && !(Y < right.Y) && !(Z < right.Z))
		{
			return W < right.W;
		}
		return true;
	}

	public void Clear()
	{
		Fill(0f);
	}

	public void Fill(float f)
	{
		X = Y = Z = W = f;
	}
}
