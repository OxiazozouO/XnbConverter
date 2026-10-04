using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;
using Simd4 = System.Numerics.Vector4;

namespace Squish;

/// <summary>
/// 聚类拟合：枚举沿主轴的所有分簇方案，取误差最小的一组端点。
/// 内层循环是热路径，这里全部改用 <see cref="System.Numerics.Vector4"/>（硬件 SIMD），
/// 并把除法换成乘倒数、把 Clamp 换成无分支 Min/Max。算术顺序与原版一致。
/// </summary>
public class ClusterFit : ColourFit
{
	private static readonly Simd4 V1 = new Simd4(3f, 3f, 3f, 9f);

	private static readonly Simd4 V2 = new Simd4(2f, 2f, 2f, 4f);

	private static readonly Simd4 TwothirdsTwothirds2 = V2 / V1;

	/// <summary>V1 的逐分量倒数：内层循环用乘法代替除法。</summary>
	private static readonly Simd4 V1Rcp = new Simd4(1f / 3f, 1f / 3f, 1f / 3f, 1f / 9f);

	private static readonly Simd4 Grid = new Simd4(31f, 63f, 31f, 0f);

	/// <summary>grid 的逐分量倒数。原移植版直接除以 (31,63,31,0)，W 会除零产生 NaN。</summary>
	private static readonly Simd4 GridRcp = new Simd4(1f / 31f, 1f / 63f, 1f / 31f, 0f);

	private static readonly Simd4 HalfHalf2 = new Simd4(0.5f, 0.5f, 0.5f, 0.25f);

	private readonly int IterationCount;

	private readonly byte[] Order = Pool.RentByte(128);

	private readonly Simd4[] PointsWeights = new Simd4[16];

	private Simd4 BestError = default;

	private Simd4 Metric = default;

	private Simd4 Principle = default;

	private Simd4 XsumWsum = default;

	public ClusterFit(ColourSet colours, bool isDxt1, bool isColourIterativeClusterFit)
		: base(colours, isDxt1)
	{
		IterationCount = !isColourIterativeClusterFit ? 1 : 8;
	}

	public override void Init()
	{
		Metric = Simd4.One;
		BestError = new Simd4(float.MaxValue);
		Order.AsSpan().Fill(0);
		XsumWsum = default;
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Vector3 principle = Sym3x3.ExtractIndicesFromPackedBytes(count, points, Colours.Weights);
		Principle = new Simd4(principle.X, principle.Y, principle.Z, 0f);
	}

	private static Simd4 Clamp01(Simd4 v)
	{
		return Simd4.Clamp(v, Simd4.Zero, Simd4.One);
	}

	private static Simd4 HalfAdjust(Simd4 v)
	{
		return new Simd4((int)(v.X + 0.5f), (int)(v.Y + 0.5f), (int)(v.Z + 0.5f), (int)(v.W + 0.5f));
	}

	/// <summary>对应原版 CompareAnyLessThan：XYZ 任一分量更小即胜，否则比 W。</summary>
	private static bool CompareAnyLessThan(Simd4 a, Simd4 b)
	{
		if (!(a.X < b.X) && !(a.Y < b.Y) && !(a.Z < b.Z))
		{
			return a.W < b.W;
		}

		return true;
	}

	/// <summary>端点转 565，沿用 ColourBlock 的取整规则。</summary>
	private static int To565(Simd4 c)
	{
		return new XnbConverter.Entity.Mono.Vector4(c.X, c.Y, c.Z, 0f).To565();
	}

	private void ConstructOrdering(Simd4 axis)
	{
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Span<float> keys = stackalloc float[16];
		for (int i = 0; i < count; i++)
		{
			keys[i] = points[i].X * axis.X + points[i].Y * axis.Y + points[i].Z * axis.Z;
			Order[i] = (byte)i;
		}
		for (int j = 0; j < count; j++)
		{
			int num = j;
			while (num > 0 && keys[num] < keys[num - 1])
			{
				(keys[num], keys[num - 1]) = (keys[num - 1], keys[num]);
				(Order[num], Order[num - 1]) = (Order[num - 1], Order[num]);
				num--;
			}
		}

		float[] weights = Colours.Weights;
		XsumWsum = default;
		for (int k = 0; k < count; k++)
		{
			int num4 = Order[k];
			Simd4 vector2 = weights[num4] * new Simd4(points[num4].X, points[num4].Y, points[num4].Z, 1f);
			PointsWeights[k] = vector2;
			XsumWsum += vector2;
		}
	}

	private bool ConstructOrdering(Simd4 axis, int iteration)
	{
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Span<float> keys = stackalloc float[16];
		int offset = 16 * iteration;
		for (int i = 0; i < count; i++)
		{
			keys[i] = points[i].X * axis.X + points[i].Y * axis.Y + points[i].Z * axis.Z;
			Order[offset + i] = (byte)i;
		}
		for (int j = 0; j < count; j++)
		{
			int num = j;
			while (num > 0 && keys[num] < keys[num - 1])
			{
				(keys[num], keys[num - 1]) = (keys[num - 1], keys[num]);
				(Order[offset + num], Order[offset + num - 1]) = (Order[offset + num - 1], Order[offset + num]);
				num--;
			}
		}

		for (int k = 0; k < iteration; k++)
		{
			bool flag = true;
			for (int l = 0; l < count; l++)
			{
				if (Order[offset + l] != Order[16 * k + l])
				{
					flag = false;
					break;
				}
			}

			if (flag)
			{
				return false;
			}
		}

		float[] weights = Colours.Weights;
		XsumWsum = default;
		for (int m = 0; m < count; m++)
		{
			int num5 = Order[offset + m];
			Simd4 vector2 = weights[num5] * new Simd4(points[num5].X, points[num5].Y, points[num5].Z, 1f);
			PointsWeights[m] = vector2;
			XsumWsum += vector2;
		}

		return true;
	}

	protected override void Compress3(Span<byte> block)
	{
		int count = Colours.Count;
		ConstructOrdering(Principle);
		Simd4 vector = default;
		Simd4 vector2 = default;
		Simd4 vector3 = BestError;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		Simd4 axis;
		do
		{
			Simd4 vector4 = default;
			for (int i = 0; i < count; i++)
			{
				Simd4 vector5 = i == 0 ? PointsWeights[0] : default;
				int num5 = i == 0 ? 1 : i;
				while (true)
				{
					Simd4 vector6 = XsumWsum - vector5 - vector4;
					Simd4 vector7 = vector5 * HalfHalf2;
					Simd4 vector8 = vector7 + vector4;
					float w = vector8.W;
					Simd4 vector9 = vector7 + vector6;
					float w2 = vector9.W;
					float w3 = vector7.W;
					float num6 = w * w2 - w3 * w3;
					float num6Rcp = 1f / num6;
					Simd4 vector10 = num6Rcp * (w2 * vector8 - w3 * vector9);
					Simd4 vector11 = num6Rcp * (w * vector9 - w3 * vector8);
					vector10 = GridRcp * HalfAdjust(Grid * Clamp01(vector10));
					vector11 = GridRcp * HalfAdjust(Grid * Clamp01(vector11));
					Simd4 vector12 = w * vector10 * vector10 + w2 * vector11 * vector11;
					Simd4 vector13 = w3 * vector10 * vector11 - vector10 * vector8 - vector11 * vector9;
					Simd4 vector14 = (2f * vector13 + vector12) * Metric;
					Simd4 vector15 = new Simd4(vector14.X + vector14.Y + vector14.Z);
					if (CompareAnyLessThan(vector15, vector3))
					{
						vector = vector10;
						vector2 = vector11;
						num2 = i;
						num3 = num5;
						vector3 = vector15;
						num = num4;
					}
					if (num5 == count)
					{
						break;
					}
					vector5 += PointsWeights[num5];
					num5++;
				}
				vector4 += PointsWeights[i];
			}
			if (num != num4)
			{
				break;
			}
			num4++;
			if (num4 == IterationCount)
			{
				break;
			}
			axis = vector2 - vector;
		}
		while (ConstructOrdering(axis, num4));
		if (CompareAnyLessThan(vector3, BestError))
		{
			Span<byte> unordered = stackalloc byte[16];
			Span<byte> span = Order.AsSpan(16 * num, count);
			int j;
			for (j = 0; j < num2; j++)
			{
				unordered[span[j]] = 0;
			}
			for (; j < num3; j++)
			{
				unordered[span[j]] = 2;
			}
			for (; j < count; j++)
			{
				unordered[span[j]] = 1;
			}
			Span<byte> indices = stackalloc byte[16];
			Colours.RemapIndices(unordered, indices);
			ColourBlock.WriteColourBlock3(To565(vector), To565(vector2), indices, block);
			BestError = vector3;
		}
	}

	protected override void Compress4(Span<byte> block)
	{
		int count = Colours.Count;
		ConstructOrdering(Principle);
		Simd4 vector = default;
		Simd4 vector2 = default;
		Simd4 vector3 = BestError;
		Simd4 vector4 = default;
		Simd4 vector5 = default;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		Simd4 axis;
		do
		{
			vector4 = default;
			for (int i = 0; i < count; i++)
			{
				vector5 = default;
				int num6 = i;
				while (true)
				{
					Simd4 vector7;
					int num7;
					if (num6 == 0)
					{
						vector7 = PointsWeights[0];
						num7 = 1;
					}
					else
					{
						vector7 = default;
						num7 = num6;
					}
					int num8 = num7;
					while (true)
					{
						Simd4 vector8 = XsumWsum - vector7 - vector5 - vector4;
						Simd4 vector9 = vector5 * TwothirdsTwothirds2 + V1Rcp * vector7 + vector4;
						float w = vector9.W;
						Simd4 vector10 = V1Rcp * vector5 + vector7 * TwothirdsTwothirds2 + vector8;
						float w2 = vector10.W;
						float num9 = (vector5.W + vector7.W) * 2f / 9f;
						float num10 = w2 * w - num9 * num9;
						float num10Rcp = 1f / num10;
						Simd4 vector11 = num10Rcp * (w2 * vector9 - num9 * vector10);
						Simd4 vector12 = num10Rcp * (w * vector10 - num9 * vector9);
						vector11 = GridRcp * HalfAdjust(Grid * Clamp01(vector11));
						vector12 = GridRcp * HalfAdjust(Grid * Clamp01(vector12));
						Simd4 vector13 = w * vector11 * vector11 + w2 * vector12 * vector12;
						Simd4 vector14 = num9 * vector11 * vector12 - vector11 * vector9 - vector12 * vector10;
						Simd4 vector15 = (2f * vector14 + vector13) * Metric;
						Simd4 vector16 = new Simd4(vector15.X + vector15.Y + vector15.Z);
						if (CompareAnyLessThan(vector16, vector3))
						{
							vector = vector11;
							vector2 = vector12;
							vector3 = vector16;
							num2 = i;
							num3 = num6;
							num4 = num8;
							num = num5;
						}
						if (num8 == count)
						{
							break;
						}
						vector7 += PointsWeights[num8];
						num8++;
					}
					if (num6 == count)
					{
						break;
					}
					vector5 += PointsWeights[num6];
					num6++;
				}
				vector4 += PointsWeights[i];
			}
			if (num != num5)
			{
				break;
			}
			num5++;
			if (num5 == IterationCount)
			{
				break;
			}
			axis = vector2 - vector;
		}
		while (ConstructOrdering(axis, num5));
		if (CompareAnyLessThan(vector3, BestError))
		{
			Span<byte> unordered = stackalloc byte[16];
			Span<byte> span = Order.AsSpan(16 * num, count);
			int j;
			for (j = 0; j < num2; j++)
			{
				unordered[span[j]] = 0;
			}
			for (; j < num3; j++)
			{
				unordered[span[j]] = 2;
			}
			for (; j < num4; j++)
			{
				unordered[span[j]] = 3;
			}
			for (; j < count; j++)
			{
				unordered[span[j]] = 1;
			}
			Span<byte> indices = stackalloc byte[16];
			Colours.RemapIndices(unordered, indices);
			ColourBlock.WriteColourBlock4(To565(vector), To565(vector2), indices, block);
			BestError = vector3;
		}
	}

	public override void Dispose()
	{
		Pool.Return(Order);
	}
}
