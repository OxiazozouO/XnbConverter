using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class Sym3x3
{
	public static Vector3 ExtractIndicesFromPackedBytes(int n, Vector3[] points, float[] weights)
	{
		float num = 0f;
		float[] array = Pool.RentFloat(16);
		Span<float> span = array.AsSpan();
		Span<float> span2 = span.Slice(0, 3);
		span2.Fill(0f);
		for (int i = 0; i < n; i++)
		{
			num += weights[i];
			span2[0] += weights[i] * points[i].X;
			span2[1] += weights[i] * points[i].Y;
			span2[2] += weights[i] * points[i].Z;
		}
		if (num > float.Epsilon)
		{
			span2[0] /= num;
			span2[1] /= num;
			span2[2] /= num;
		}
		Span<float> span3 = span.Slice(3, 3);
		Span<float> span4 = span.Slice(6, 3);
		Span<float> span5 = span.Slice(9, span.Length - 9);
		for (int j = 0; j < n; j++)
		{
			span3[0] = points[j].X - span2[0];
			span3[1] = points[j].Y - span2[1];
			span3[2] = points[j].Z - span2[2];
			span4[0] = weights[j] * span3[0];
			span4[1] = weights[j] * span3[1];
			span4[2] = weights[j] * span3[2];
			span5[0] += span3[0] * span4[0];
			span5[1] += span3[0] * span4[1];
			span5[2] += span3[0] * span4[2];
			span5[3] += span3[1] * span4[1];
			span5[4] += span3[1] * span4[2];
			span5[5] += span3[2] * span4[2];
		}
		Span<float> span6 = span2;
		Span<float> span7 = span3;
		span7.Fill(1f);
		for (int k = 0; k < 8; k++)
		{
			span6[0] = span7[0] * span5[0] + span7[1] * span5[1] + span7[2] * span5[2];
			span6[1] = span7[0] * span5[1] + span7[1] * span5[3] + span7[2] * span5[4];
			span6[2] = span7[0] * span5[2] + span7[1] * span5[4] + span7[2] * span5[5];
			float num2 = Math.Max(span6[0], Math.Max(span6[1], span6[2]));
			span7[0] = span6[0] / num2;
			span7[1] = span6[1] / num2;
			span7[2] = span6[2] / num2;
		}
		Pool.Return(array);
		return new Vector3(span7[0], span7[1], span7[2]);
	}
}
