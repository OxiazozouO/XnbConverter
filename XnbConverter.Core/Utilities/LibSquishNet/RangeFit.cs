using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class RangeFit : ColourFit
{
    private readonly Vector3 m_metric = new();
    private float m_besterror;
    private Vector3 m_end;
    private Vector3 m_start;

    public RangeFit(ColourSet colours, bool isDxt1) : base(colours, isDxt1)
    {
    }

    public override void Init()
    {
        // initialise the metric (old perceptual = 0.2126f, 0.7152f, 0.0722f)
        //m_metric = new Vector3( metric[0], metric[1], metric[2] );
        m_metric.Fill(1.0f);
        m_start = Vector3.Zero;
        m_end = Vector3.Zero;
        // initialise the best error
        m_besterror = float.MaxValue;


        // cache some values
        var count = Colours.Count;
        var values = Colours.Points;

        // get the covariance matrix, compute the principle component
        var principle = Sym3x3.ExtractIndicesFromPackedBytes(count, values, Colours.Weights);

        // get the min and max range as the codebook endpoints
        if (count > 0)
        {
            float min, max;

            // compute the range
            m_start = m_end = values[0];
            min = max = values[0].Dot(principle);
            for (var i = 1; i < count; ++i)
            {
                var val = values[i].Dot(principle);
                if (val < min)
                {
                    m_start = values[i];
                    min = val;
                }
                else if (val > max)
                {
                    m_end = values[i];
                    max = val;
                }
            }
        }

        // clamp the output to [0, 1]
        // clamp to the grid and save
        m_start = (Vector3.Grid * m_start.Clamp(0.0f, 1.0f)).HalfAdjust() / Vector3.Grid;
        m_end = (Vector3.Grid * m_end.Clamp(0.0f, 1.0f)).HalfAdjust() / Vector3.Grid;
    }

    protected override void Compress3(Span<byte> block)
    {
        // cache some values
        var count = Colours.Count;
        var values = Colours.Points;

        // create a codebook
        var codes = new Vector3[3];
        codes[0] = m_start;
        codes[1] = m_end;
        codes[2] = 0.5f * (m_start + m_end);

        // match each point to the closest code
        var closest = new byte[16];
        var error = 0.0f;
        for (var i = 0; i < count; ++i)
        {
            // find the closest code
            var dist = float.MaxValue;
            var idx = 0;
            for (var j = 0; j < 3; ++j)
            {
                var d = (m_metric * (values[i] - codes[j])).LengthSquared();
                if (d < dist)
                {
                    dist = d;
                    idx = j;
                }
            }

            // save the index
            closest[i] = (byte)idx;

            // accumulate the error
            error += dist;
        }

        // save this scheme if it wins
        if (!(error < m_besterror)) return;
        // remap the indices
        var indices = Colours.RemapIndices(closest);

        // save the block
        ColourBlock.WriteColourBlock3(m_start.To565(), m_end.To565(), indices, block);
        Pool.Return(indices);

        // save the error
        m_besterror = error;
    }

    protected override void Compress4(Span<byte> block)
    {
        throw new NotImplementedException("RangeFit.Compress4");
    }

    public override void Dispose()
    {
    }
}