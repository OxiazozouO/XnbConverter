using XnbConverter.Entity.Mono;

namespace Squish;

/// <summary>
/// 按主轴两端做区间拟合。每个块只需 O(n)，比 ClusterFit 快一两个数量级，质量略低。
/// </summary>
public class RangeFit : ColourFit
{
	private readonly Vector3 m_metric = new Vector3();

	private float m_besterror;

	private Vector3 m_end;

	private Vector3 m_start;

	public RangeFit(ColourSet colours, bool isDxt1)
		: base(colours, isDxt1)
	{
	}

	public override void Init()
	{
		m_metric.Fill(1f);
		m_start = Vector3.Zero;
		m_end = Vector3.Zero;
		m_besterror = float.MaxValue;
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Vector3 v = Sym3x3.ExtractIndicesFromPackedBytes(count, points, Colours.Weights);
		if (count > 0)
		{
			m_start = (m_end = points[0]);
			float num;
			float num2 = (num = points[0].Dot(v));
			for (int i = 1; i < count; i++)
			{
				float num3 = points[i].Dot(v);
				if (num3 < num2)
				{
					m_start = points[i];
					num2 = num3;
				}
				else if (num3 > num)
				{
					m_end = points[i];
					num = num3;
				}
			}
		}

		m_start = (Vector3.Grid * m_start.Clamp(0f, 1f)).HalfAdjust() / Vector3.Grid;
		m_end = (Vector3.Grid * m_end.Clamp(0f, 1f)).HalfAdjust() / Vector3.Grid;
	}

	protected override void Compress3(Span<byte> block)
	{
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Span<Vector3> codes = stackalloc Vector3[3];
		codes[0] = m_start;
		codes[1] = m_end;
		codes[2] = 0.5f * (m_start + m_end);
		Span<byte> closest = stackalloc byte[16];
		Span<byte> indices = stackalloc byte[16];
		float num = 0f;
		for (int i = 0; i < count; i++)
		{
			float num2 = float.MaxValue;
			int num3 = 0;
			for (int j = 0; j < 3; j++)
			{
				float num4 = (m_metric * (points[i] - codes[j])).LengthSquared();
				if (num4 < num2)
				{
					num2 = num4;
					num3 = j;
				}
			}

			closest[i] = (byte)num3;
			num += num2;
		}

		if (num < m_besterror)
		{
			Colours.RemapIndices(closest, indices);
			ColourBlock.WriteColourBlock3(m_start.To565(), m_end.To565(), indices, block);
			m_besterror = num;
		}
	}

	protected override void Compress4(Span<byte> block)
	{
		int count = Colours.Count;
		Vector3[] points = Colours.Points;
		Span<Vector3> codes = stackalloc Vector3[4];
		codes[0] = m_start;
		codes[1] = m_end;
		codes[2] = (2f / 3f) * m_start + (1f / 3f) * m_end;
		codes[3] = (1f / 3f) * m_start + (2f / 3f) * m_end;
		Span<byte> closest = stackalloc byte[16];
		Span<byte> indices = stackalloc byte[16];
		float num = 0f;
		for (int i = 0; i < count; i++)
		{
			float num2 = float.MaxValue;
			int num3 = 0;
			for (int j = 0; j < 4; j++)
			{
				float num4 = (m_metric * (points[i] - codes[j])).LengthSquared();
				if (num4 < num2)
				{
					num2 = num4;
					num3 = j;
				}
			}

			closest[i] = (byte)num3;
			num += num2;
		}

		if (num < m_besterror)
		{
			Colours.RemapIndices(closest, indices);
			ColourBlock.WriteColourBlock4(m_start.To565(), m_end.To565(), indices, block);
			m_besterror = num;
		}
	}

	public override void Dispose()
	{
	}
}
