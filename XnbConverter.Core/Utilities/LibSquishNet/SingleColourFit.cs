using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class SingleColourFit : ColourFit
{
	private readonly SourceBlock[] _sources;

	private readonly byte[] m_colour = Pool.RentByte(3);

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
		m_error = int.MaxValue;
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
		int i = 0;
		int num = 0;
		for (; i < 2; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				_sources[j] = lookups[j][m_colour[j]][i];
				int error = _sources[j].Error;
				num += error * error;
			}
			if (num < m_error)
			{
				m_start.X = (int)_sources[0].Start;
				m_start.Y = (int)_sources[1].Start;
				m_start.Z = (int)_sources[2].Start;
				m_end.X = (int)_sources[0].End;
				m_end.Y = (int)_sources[1].End;
				m_end.Z = (int)_sources[2].End;
				m_index = (byte)(2 * i);
				m_error = num;
			}
		}
		m_start.X /= 31f;
		m_start.Y /= 63f;
		m_start.Z /= 31f;
		m_end.X /= 31f;
		m_end.Y /= 63f;
		m_end.Z /= 31f;
		return m_error >= m_besterror;
	}

	protected override void Compress3(Span<byte> block)
	{
		if (!ComputeEndPoints(SingleColourLookup.Lookups_53_63_53))
		{
			byte[] array = Colours.RemapIndices(m_index);
			ColourBlock.WriteColourBlock3(m_start.To565(), m_end.To565(), array, block);
			Pool.Return(array);
			m_besterror = m_error;
		}
	}

	protected override void Compress4(Span<byte> block)
	{
		if (!ComputeEndPoints(SingleColourLookup.Lookups_54_64_54))
		{
			byte[] array = Colours.RemapIndices(m_index);
			ColourBlock.WriteColourBlock4(m_start.To565(), m_end.To565(), array, block);
			Pool.Return(array);
			m_besterror = m_error;
		}
	}

	public override void Dispose()
	{
		Pool.Return(m_colour);
	}
}
