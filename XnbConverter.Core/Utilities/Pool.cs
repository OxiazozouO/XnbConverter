﻿using System.Buffers;
using XnbConverter.Entity.Mono;

namespace XnbConverter.Utilities;

public static class Pool
{
    private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
    private static readonly ArrayPool<ushort> UShortPool = ArrayPool<ushort>.Create(8192, 50);
    private static readonly ArrayPool<int> IntPool = ArrayPool<int>.Create(16, 50);
    private static readonly ArrayPool<float> FloatPool = ArrayPool<float>.Create(16, 50);
    private static readonly ArrayPool<Vector3> Vector3Pool = ArrayPool<Vector3>.Create(16, 50);
    private static readonly ArrayPool<Vector4> Vector4Pool = ArrayPool<Vector4>.Create(16, 50);

    public const int Len128 = 128; //1<<7, PRETREE_table 104, PRETREE_len 84, ALIGNED_len 72
    public const int Len512 = 512; //1<<9, ALIGNED_table 144, LENGTH_len 314
    public const int Len1024 = 1024; //1<<10, MAINTREE_len 720
    public const int Len8192 = 8192; //1<<13, MAINTREE_table 5408, LENGTH_table 4596
    public const int LongSize = 1024 * 1024 * 10;

    private static readonly object MessageLock = new();

    public static byte[] RentByte(int size)
    {
        lock (MessageLock)
        {
            return BytePool.Rent(size);
        }
    }

    public static byte[] RentNewByte(int size)
    {
        lock (MessageLock)
        {
            var result = BytePool.Rent(size);
            result.AsSpan().Fill(0);
            return result;
        }
    }

    public static float[] RentFloat(int size)
    {
        lock (MessageLock)
        {
            return FloatPool.Rent(size);
        }
    }

    public static float[] RentNewFloat(int size)
    {
        lock (MessageLock)
        {
            var result = FloatPool.Rent(size);
            Array.Clear(result);
            return result;
        }
    }

    public static int[] RentInt(int size)
    {
        lock (MessageLock)
        {
            return IntPool.Rent(size);
        }
    }

    public static int[] RentNewInt(int size)
    {
        lock (MessageLock)
        {
            var result = IntPool.Rent(size);
            Array.Clear(result);
            return result;
        }
    }

    public static Vector3[] RentVector3(int size)
    {
        lock (MessageLock)
        {
            Vector3[] v = Vector3Pool.Rent(size);
            for (var i = 0; i < v.Length; i++)
                v[i] = new Vector3();
            return v;
        }
    }

    public static Vector4[] RentVector4(int size)
    {
        lock (MessageLock)
        {
            return Vector4Pool.Rent(size);
        }
    }

    public static ushort[] RentUShort(int size)
    {
        lock (MessageLock)
        {
            return UShortPool.Rent(size);
        }
    }

    public static void Return(byte[] arr)
    {
        lock (MessageLock)
        {
            BytePool.Return(arr);
        }
    }

    public static void Return(ushort[] arr)
    {
        lock (MessageLock)
        {
            UShortPool.Return(arr);
        }
    }

    public static void Return(float[] arr)
    {
        lock (MessageLock)
        {
            FloatPool.Return(arr);
        }
    }

    public static void Return(int[] arr)
    {
        lock (MessageLock)
        {
            IntPool.Return(arr);
        }
    }

    public static void Return(Vector3[] arr)
    {
        lock (MessageLock)
        {
            Vector3Pool.Return(arr);
        }
    }

    public static void Return(Vector4[] arr)
    {
        lock (MessageLock)
        {
            Vector4Pool.Return(arr);
        }
    }
}