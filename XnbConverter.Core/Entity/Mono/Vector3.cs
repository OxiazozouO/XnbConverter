namespace XnbConverter.Entity.Mono;

public struct Vector3
{
    public static readonly Vector3 Zero = new();
    public static readonly Vector3 One = new(1.0f);
    public static readonly Vector3 Grid = new(31.0f, 63.0f, 31.0f);
    
    public float X,Y,Z;

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3(float v) : this(v, v, v)
    {
    }

    public Vector3()
    {
        X = Y = Z = 0.0f;
    }

    public Vector3 Clamp(float min, float max)
    {
        if (X < min) X = min;
        else if (X > max) X = max;
        if (Y < min) Y = min;
        else if (Y > max) Y = max;
        if (Z < min) Z = min;
        else if (Z > max) Z = max;
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
        return (float)(X * (double)v2.X + Y * (double)v2.Y + Z * (double)v2.Z);
    }

    public float Dot(Vector4 v2)
    {
        return (float)(X * (double)v2.X + Y * (double)v2.Y + Z * (double)v2.Z);
    }

    public float LengthSquared()
    {
        return Dot(this);
    }

    public void Clear()
    {
        Fill(0.0f);
    }

    public void Fill(float f)
    {
        X = Y = Z = f;
    }
}