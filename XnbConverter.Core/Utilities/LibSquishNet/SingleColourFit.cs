using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class SingleColourFit : ColourFit
{
    private readonly SourceBlock[] _sources;
    private readonly byte[] m_colour = Pool.RentByte(3);
    private Vector3 m_end;
    private Vector3 m_start;

    // initialise the best error
    private int m_besterror;

    // check each index combination (endpoint or intermediate)
    private int m_error;

    private byte m_index;

    public SingleColourFit(ColourSet colours, bool isDxt1) : base(colours, isDxt1)
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
        // grab the single colour
        var values = Colours.Points[0];
        m_colour[0] = values.X.ScaleToByte(255);
        m_colour[1] = values.Y.ScaleToByte(255);
        m_colour[2] = values.Z.ScaleToByte(255);
        m_start.Clear();
        m_end.Clear();
    }

    private bool ComputeEndPoints(SourceBlock[][][] lookups)
    {
        // check the error for this codebook index
        for (int index = 0, error = 0; index < 2; ++index)
        {
            for (var channel = 0; channel < 3; ++channel)
            {
                // grab the lookup table and index for this channel
                // store a pointer to the source for this channel
                _sources[channel] = lookups[channel][m_colour[channel]][index];

                // accumulate the error
                int diff = _sources[channel].Error;
                error += diff * diff;
            }

            // keep it if the error is lower
            if (error >= m_error) continue;
            m_start.X = _sources[0].Start;
            m_start.Y = _sources[1].Start;
            m_start.Z = _sources[2].Start;

            m_end.X = _sources[0].End;
            m_end.Y = _sources[1].End;
            m_end.Z = _sources[2].End;

            m_index = (byte)(2 * index);
            m_error = error;
        }

        m_start.X /= 31.0f;
        m_start.Y /= 63.0f;
        m_start.Z /= 31.0f;

        m_end.X /= 31.0f;
        m_end.Y /= 63.0f;
        m_end.Z /= 31.0f;
        // build the block if we win
        return m_error >= m_besterror;
    }

    protected override void Compress3(Span<byte> block)
    {
        // find the best end-points and index 
        if (ComputeEndPoints(SingleColourLookup.Lookups_53_63_53)) return;

        // remap the indices
        var indices = Colours.RemapIndices(m_index);
        // save the block
        ColourBlock.WriteColourBlock3(m_start.To565(), m_end.To565(), indices, block);
        Pool.Return(indices);

        // save the error
        m_besterror = m_error;
    }

    protected override void Compress4(Span<byte> block)
    {
        // find the best end-points and index
        if (ComputeEndPoints(SingleColourLookup.Lookups_54_64_54)) return;

        // remap the indices
        var indices = Colours.RemapIndices(m_index);
        // save the block                 // get the packed values
        ColourBlock.WriteColourBlock4(m_start.To565(), m_end.To565(), indices, block);
        Pool.Return(indices);

        // save the error
        m_besterror = m_error;
    }

    public override void Dispose()
    {
        Pool.Return(m_colour);
    }
}