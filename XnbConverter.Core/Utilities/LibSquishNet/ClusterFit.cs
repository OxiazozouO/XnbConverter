using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class ClusterFit : ColourFit
{
	private static readonly Vector4 V1 = new Vector4(3f, 3f, 3f, 9f);

	private static readonly Vector4 V2 = new Vector4(2f, 2f, 2f, 4f);

	private static readonly Vector4 TwothirdsTwothirds2 = V2 / V1;

	private readonly int IterationCount;

	private readonly byte[] Order = Pool.RentByte(128);

	private readonly Vector4[] PointsWeights = Pool.RentVector4(16);

	private Vector4 BestError = new Vector4();

	private Vector4 Metric = new Vector4();

	private Vector3 Principle;

	private Vector4 XsumWsum = new Vector4();

	public ClusterFit(ColourSet colours, bool isDxt1, bool isColourIterativeClusterFit)
		: base(colours, isDxt1)
	{
		IterationCount = ((!isColourIterativeClusterFit) ? 1 : 8);
	}

	public override void Init()
	{
		Metric.Fill(1f);
		BestError.Fill(float.MaxValue);
		Order.AsSpan().Fill(0);
		XsumWsum.Clear();
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Principle = Sym3x3.ExtractIndicesFromPackedBytes(count, points, Colours.Weights);
	}

	private void ConstructOrdering(Vector3 axis)
	{
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		float[] array = Pool.RentFloat(16);
		for (int i = 0; i < count; i++)
		{
			array[i] = points[i].Dot(axis);
			Order[i] = (byte)i;
		}
		for (int j = 0; j < count; j++)
		{
			int num = j;
			while (num > 0 && array[num] < array[num - 1])
			{
				ref float reference = ref array[num];
				ref float reference2 = ref array[num - 1];
				float num2 = array[num - 1];
				float num3 = array[num];
				reference = num2;
				reference2 = num3;
				ref byte reference3 = ref Order[num];
				ref byte reference4 = ref Order[num - 1];
				byte b = Order[num - 1];
				byte b2 = Order[num];
				reference3 = b;
				reference4 = b2;
				num--;
			}
		}
		Pool.Return(array);
		Vector3[] points2 = Colours.Points;
		float[] weights = Colours.Weights;
		XsumWsum = new Vector4(0f);
		for (int k = 0; k < count; k++)
		{
			int num4 = Order[k];
			Vector4 vector = new Vector4(points2[num4].X, points2[num4].Y, points2[num4].Z, 1f);
			Vector4 vector2 = weights[num4] * vector;
			PointsWeights[k] = vector2;
			XsumWsum += vector2;
		}
	}

	private bool ConstructOrdering(Vector4 axis, int iteration)
	{
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		float[] array = Pool.RentFloat(16);
		for (int i = 0; i < count; i++)
		{
			array[i] = points[i].Dot(axis);
			Order[16 * iteration + i] = (byte)i;
		}
		for (int j = 0; j < count; j++)
		{
			int num = j;
			while (num > 0 && array[num] < array[num - 1])
			{
				ref float reference = ref array[num];
				ref float reference2 = ref array[num - 1];
				float num2 = array[num - 1];
				float num3 = array[num];
				reference = num2;
				reference2 = num3;
				int num4 = 16 * iteration + num;
				ref byte reference3 = ref Order[num4];
				ref byte reference4 = ref Order[num4 - 1];
				byte b = Order[num4 - 1];
				byte b2 = Order[num4];
				reference3 = b;
				reference4 = b2;
				num--;
			}
		}
		Pool.Return(array);
		for (int k = 0; k < iteration; k++)
		{
			bool flag = true;
			for (int l = 0; l < count; l++)
			{
				if (Order[16 * iteration + l] != Order[16 * k + l])
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
		Vector3[] points2 = Colours.Points;
		float[] weights = Colours.Weights;
		XsumWsum = new Vector4(0f);
		for (int m = 0; m < count; m++)
		{
			int num5 = Order[16 * iteration + m];
			Vector4 vector = new Vector4(points2[num5].X, points2[num5].Y, points2[num5].Z, 1f);
			Vector4 vector2 = weights[num5] * vector;
			PointsWeights[m] = vector2;
			XsumWsum += vector2;
		}
		return true;
	}

	protected override void Compress3(Span<byte> block)
	{
		int count = Colours.Count;
		ConstructOrdering(Principle);
		Vector4 vector = Vector4.Zero;
		Vector4 vector2 = Vector4.Zero;
		Vector4 vector3 = BestError;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		Vector4 axis;
		do
		{
			Vector4 vector4 = new Vector4(0f);
			for (int i = 0; i < count; i++)
			{
				Vector4 vector5 = ((i == 0) ? PointsWeights[0] : new Vector4(0f));
				int num5 = ((i == 0) ? 1 : i);
				while (true)
				{
					Vector4 vector6 = XsumWsum - vector5 - vector4;
					Vector4 vector7 = vector5 * Vector4.HalfHalf2;
					Vector4 vector8 = vector7 + vector4;
					float w = vector8.W;
					Vector4 vector9 = vector7 + vector6;
					float w2 = vector9.W;
					float w3 = vector7.W;
					float num6 = w * w2 - w3 * w3;
					Vector4 vector10 = (w2 * vector8 - w3 * vector9) / num6;
					Vector4 vector11 = (w * vector9 - w3 * vector8) / num6;
					vector10 = (Vector4.Grid * vector10.Clamp(0f, 1f)).HalfAdjust() / Vector4.Grid;
					vector11 = (Vector4.Grid * vector11.Clamp(0f, 1f)).HalfAdjust() / Vector4.Grid;
					Vector4 vector12 = w * vector10 * vector10 + w2 * vector11 * vector11;
					Vector4 vector13 = w3 * vector10 * vector11 - vector10 * vector8 - vector11 * vector9;
					Vector4 vector14 = (2f * vector13 + vector12) * Metric;
					Vector4 vector15 = new Vector4(vector14.X + vector14.Y + vector14.Z);
					if (vector15.CompareAnyLessThan(vector3))
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
		if (vector3.CompareAnyLessThan(BestError))
		{
			byte[] array = Pool.RentNewByte(16);
			Span<byte> span = Order.AsSpan(16 * num, count);
			int j;
			for (j = 0; j < num2; j++)
			{
				array[span[j]] = 0;
			}
			for (; j < num3; j++)
			{
				array[span[j]] = 2;
			}
			for (; j < count; j++)
			{
				array[span[j]] = 1;
			}
			byte[] array2 = Colours.RemapIndices(array);
			ColourBlock.WriteColourBlock3(vector.To565(), vector2.To565(), array2, block);
			Pool.Return(array);
			Pool.Return(array2);
			BestError = vector3;
		}
	}

	protected override void Compress4(Span<byte> block)
	{
		int count = Colours.Count;
		ConstructOrdering(Principle);
		Vector4 vector = Vector4.Zero;
		Vector4 vector2 = Vector4.Zero;
		Vector4 vector3 = BestError;
		Vector4 vector4 = new Vector4();
		Vector4 vector5 = new Vector4();
		Vector4 vector6 = new Vector4();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		Vector4 axis;
		do
		{
			vector4.Clear();
			for (int i = 0; i < count; i++)
			{
				vector5.Clear();
				int num6 = i;
				while (true)
				{
					Vector4 vector7;
					int num7;
					if (num6 == 0)
					{
						vector7 = PointsWeights[0];
						num7 = 1;
					}
					else
					{
						vector6.Clear();
						vector7 = vector6;
						num7 = num6;
					}
					int num8 = num7;
					while (true)
					{
						Vector4 vector8 = XsumWsum - vector7 - vector5 - vector4;
						Vector4 vector9 = vector5 * TwothirdsTwothirds2 + vector7 / V1 + vector4;
						float w = vector9.W;
						Vector4 vector10 = vector5 / V1 + vector7 * TwothirdsTwothirds2 + vector8;
						float w2 = vector10.W;
						float num9 = (vector5.W + vector7.W) * 2f / 9f;
						float num10 = w2 * w - num9 * num9;
						Vector4 vector11 = (w2 * vector9 - num9 * vector10) / num10;
						Vector4 vector12 = (w * vector10 - num9 * vector9) / num10;
						vector11 = (Vector4.Grid * vector11.Clamp(0f, 1f)).HalfAdjust() / Vector4.Grid;
						vector12 = (Vector4.Grid * vector12.Clamp(0f, 1f)).HalfAdjust() / Vector4.Grid;
						Vector4 vector13 = w * vector11 * vector11 + w2 * vector12 * vector12;
						Vector4 vector14 = num9 * vector11 * vector12 - vector11 * vector9 - vector12 * vector10;
						Vector4 vector15 = (2f * vector14 + vector13) * Metric;
						Vector4 vector16 = new Vector4(vector15.X + vector15.Y + vector15.Z);
						if (vector16.CompareAnyLessThan(vector3))
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
		if (vector3.CompareAnyLessThan(BestError))
		{
			byte[] array = Pool.RentNewByte(16);
			Span<byte> span = Order.AsSpan(16 * num, count);
			int j;
			for (j = 0; j < num2; j++)
			{
				array[span[j]] = 0;
			}
			for (; j < num3; j++)
			{
				array[span[j]] = 2;
			}
			for (; j < num4; j++)
			{
				array[span[j]] = 3;
			}
			for (; j < count; j++)
			{
				array[span[j]] = 1;
			}
			byte[] array2 = Colours.RemapIndices(array);
			ColourBlock.WriteColourBlock4(vector.To565(), vector2.To565(), array2, block);
			BestError = vector3;
			Pool.Return(array2);
			Pool.Return(array);
		}
	}

	public override void Dispose()
	{
		Pool.Return(Order);
		Pool.Return(PointsWeights);
	}
}
