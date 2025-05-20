using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

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
			m_start = m_end = points[0];
			float num;
			float num2 = num = points[0].Dot(v);
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
		Vector3[] array = new Vector3[3]
		{
			m_start,
			m_end,
			0.5f * (m_start + m_end)
		};
		byte[] array2 = new byte[16];
		float num = 0f;
		for (int i = 0; i < count; i++)
		{
			float num2 = float.MaxValue;
			int num3 = 0;
			for (int j = 0; j < 3; j++)
			{
				float num4 = (m_metric * (points[i] - array[j])).LengthSquared();
				if (num4 < num2)
				{
					num2 = num4;
					num3 = j;
				}
			}
			array2[i] = (byte)num3;
			num += num2;
		}
		if (num < m_besterror)
		{
			byte[] array3 = Colours.RemapIndices(array2);
			ColourBlock.WriteColourBlock3(m_start.To565(), m_end.To565(), array3, block);
			Pool.Return(array3);
			m_besterror = num;
		}
	}

	protected override void Compress4(Span<byte> block)
	{
		throw new NotImplementedException("RangeFit.Compress4");
	}

	public override void Dispose()
	{
	}
}
