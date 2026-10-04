using System.Buffers;
using XnbConverter.Entity.Mono;

namespace XnbConverter.Utilities;

public static class Pool
{

	public const int Len128 = 128;

	public const int Len512 = 512;

	public const int Len1024 = 1024;

	public const int Len8192 = 8192;

	public const int LongSize = 10485760;

	/// <summary>
	/// Shared 池最大只缓存 1MB 的数组，而 XNB 载荷、PNG 扫描线动辄好几 MB ——
	/// 用 Shared 就是每个文件都新分配一块大数组、还回去也留不住，GC 直接被拖爆。
	/// 这里放开到 32MB，让大缓冲也能循环利用。
	/// </summary>
	private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Create(1 << 25, 4);

	/// <summary>
	/// LZX 近最优解析的 DP 工作数组（recLen/recDist 各 2MB 等，合计 6MB 出头），
	/// 每次压缩都要一整批。共享池上限只有 1MB，这些数组会直接被丢弃，所以单独开一个。
	/// </summary>
	private static readonly ArrayPool<int> BigIntPool = ArrayPool<int>.Create(1 << 22, 4);

	private static readonly ArrayPool<uint> UIntPool = ArrayPool<uint>.Create(1 << 22, 4);

	public static int[] RentBigInt(int size)
	{
		return BigIntPool.Rent(size);
	}

	public static void ReturnBig(int[] arr)
	{
		BigIntPool.Return(arr);
	}

	public static uint[] RentUInt(int size)
	{
		return UIntPool.Rent(size);
	}

	public static void Return(uint[] arr)
	{
		UIntPool.Return(arr);
	}

	private static readonly ArrayPool<ushort> UShortPool = ArrayPool<ushort>.Create(8192, 50);

	private static readonly ArrayPool<int> IntPool = ArrayPool<int>.Create(16, 50);

	private static readonly ArrayPool<float> FloatPool = ArrayPool<float>.Create(16, 50);

	private static readonly ArrayPool<Vector3> Vector3Pool = ArrayPool<Vector3>.Create(16, 50);

	private static readonly ArrayPool<Vector4> Vector4Pool = ArrayPool<Vector4>.Create(16, 50);

	public static byte[] RentByte(int size)
	{
		return BytePool.Rent(size);
	}

	public static byte[] RentNewByte(int size)
	{
		byte[] array = BytePool.Rent(size);
		array.AsSpan().Fill(0);
		return array;
	}

	public static float[] RentFloat(int size)
	{
		return FloatPool.Rent(size);
	}

	public static float[] RentNewFloat(int size)
	{
		float[] array = FloatPool.Rent(size);
		Array.Fill(array, 0f);
		return array;
	}

	public static int[] RentInt(int size)
	{
		return IntPool.Rent(size);
	}

	public static int[] RentNewInt(int size)
	{
		int[] array = IntPool.Rent(size);
		Array.Fill(array, 0);
		return array;
	}

	public static Vector3[] RentVector3(int size)
	{
		Vector3[] array = Vector3Pool.Rent(size);
		for (int i = 0; i < array.Length; i++)
		{
		array[i] = new Vector3();
		}
		return array;
	}

	public static Vector4[] RentVector4(int size)
	{
		return Vector4Pool.Rent(size);
	}

	public static ushort[] RentUShort(int size)
	{
		return UShortPool.Rent(size);
	}

	public static void Return(byte[] arr)
	{
		BytePool.Return(arr);
	}

	public static void Return(ushort[] arr)
	{
		UShortPool.Return(arr);
	}

	public static void Return(float[] arr)
	{
		FloatPool.Return(arr);
	}

	public static void Return(int[] arr)
	{
		IntPool.Return(arr);
	}

	public static void Return(Vector3[] arr)
	{
		Vector3Pool.Return(arr);
	}

	public static void Return(Vector4[] arr)
	{
		Vector4Pool.Return(arr);
	}
}
