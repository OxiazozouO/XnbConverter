using XnbConverter.Entity.Mono;

namespace Squish;

public class SingleColourFit : ColourFit
{
	private readonly SourceBlock[] _sources;

	private readonly byte[] m_colour = new byte[3];

	private Vector3 m_end;

	private Vector3 m_start;

	private int m_besterror;

	private int m_error;

	private byte m_index;

	public SingleColourFit(ColourSet colours, bool isDxt1)
		: base(colours, isDxt1)
	{
		m_start = new Vector3();
		m_end = new Vector3();
		_sources = new SourceBlock[3];
	}

	public override void Init()
	{
		m_besterror = int.MaxValue;
		m_index = 0;
		Vector3 vector = Colours.Points[0];
		m_colour[0] = vector.X.ScaleToByte(255);
		m_colour[1] = vector.Y.ScaleToByte(255);
		m_colour[2] = vector.Z.ScaleToByte(255);
		m_start.Clear();
		m_end.Clear();
	}

	private bool ComputeEndPoints(SourceBlock[][][] lookups)
	{
		// 每次进来都要重置：原移植版沿用上一轮（Compress3）的 m_error，
		// 而且累加变量写在了循环外，导致第二个候选点永远赢不了。
		m_error = int.MaxValue;
		Vector3 start = default;
		Vector3 end = default;
		for (int i = 0; i < 2; i++)
		{
			int error = 0;
			for (int j = 0; j < 3; j++)
			{
				_sources[j] = lookups[j][m_colour[j]][i];
				int diff = _sources[j].Error;
				error += diff * diff;
			}

			if (error < m_error)
			{
				start.X = _sources[0].Start / 31f;
				start.Y = _sources[1].Start / 63f;
				start.Z = _sources[2].Start / 31f;
				end.X = _sources[0].End / 31f;
				end.Y = _sources[1].End / 63f;
				end.Z = _sources[2].End / 31f;
				m_index = (byte)(2 * i);
				m_error = error;
			}
		}

		m_start = start;
		m_end = end;
		return m_error >= m_besterror;
	}

	protected override void Compress3(Span<byte> block)
	{
		if (!ComputeEndPoints(SingleColourLookup.Lookups_53_63_53))
		{
			Span<byte> indices = stackalloc byte[16];
			Colours.RemapIndices(m_index, indices);
			ColourBlock.WriteColourBlock3(m_start.To565(), m_end.To565(), indices, block);
			m_besterror = m_error;
		}
	}

	protected override void Compress4(Span<byte> block)
	{
		if (!ComputeEndPoints(SingleColourLookup.Lookups_54_64_54))
		{
			Span<byte> indices = stackalloc byte[16];
			Colours.RemapIndices(m_index, indices);
			ColourBlock.WriteColourBlock4(m_start.To565(), m_end.To565(), indices, block);
			m_besterror = m_error;
		}
	}

	public override void Dispose()
	{
	}
}
