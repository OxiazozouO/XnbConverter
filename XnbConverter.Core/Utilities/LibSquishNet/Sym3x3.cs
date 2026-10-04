using XnbConverter.Entity.Mono;

namespace Squish;

public class Sym3x3
{
	/// <summary>
	/// 加权协方差矩阵的主特征向量（幂迭代）。
	/// 缓冲区用栈分配，避免每块向对象池借还。
	/// </summary>
	public static Vector3 ExtractIndicesFromPackedBytes(int n, Vector3[] points, float[] weights)
	{
		Span<float> buffer = stackalloc float[16];
		Span<float> centroid = buffer.Slice(0, 3);
		Span<float> delta = buffer.Slice(3, 3);
		Span<float> weighted = buffer.Slice(6, 3);
		Span<float> covariance = buffer.Slice(9, 6);

		// 协方差必须从 0 开始累加；原移植版漏了这一步，会累加到池里的残留数据上
		covariance.Clear();

		float total = 0f;
		centroid.Clear();
		for (int i = 0; i < n; i++)
		{
			total += weights[i];
			centroid[0] += weights[i] * points[i].X;
			centroid[1] += weights[i] * points[i].Y;
			centroid[2] += weights[i] * points[i].Z;
		}

		if (total > float.Epsilon)
		{
			centroid[0] /= total;
			centroid[1] /= total;
			centroid[2] /= total;
		}

		for (int i = 0; i < n; i++)
		{
			delta[0] = points[i].X - centroid[0];
			delta[1] = points[i].Y - centroid[1];
			delta[2] = points[i].Z - centroid[2];
			weighted[0] = weights[i] * delta[0];
			weighted[1] = weights[i] * delta[1];
			weighted[2] = weights[i] * delta[2];
			covariance[0] += delta[0] * weighted[0];
			covariance[1] += delta[0] * weighted[1];
			covariance[2] += delta[0] * weighted[2];
			covariance[3] += delta[1] * weighted[1];
			covariance[4] += delta[1] * weighted[2];
			covariance[5] += delta[2] * weighted[2];
		}

		// 幂迭代求主特征向量
		Span<float> vector = delta;
		vector.Fill(1f);
		for (int k = 0; k < 8; k++)
		{
			centroid[0] = vector[0] * covariance[0] + vector[1] * covariance[1] + vector[2] * covariance[2];
			centroid[1] = vector[0] * covariance[1] + vector[1] * covariance[3] + vector[2] * covariance[4];
			centroid[2] = vector[0] * covariance[2] + vector[1] * covariance[4] + vector[2] * covariance[5];
			float max = Math.Max(centroid[0], Math.Max(centroid[1], centroid[2]));
			if (max == 0f)
			{
				break;
			}

			vector[0] = centroid[0] / max;
			vector[1] = centroid[1] / max;
			vector[2] = centroid[2] / max;
		}

		return new Vector3(vector[0], vector[1], vector[2]);
	}
}
