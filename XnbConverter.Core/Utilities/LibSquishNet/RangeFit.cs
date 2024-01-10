using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class RangeFit : ColourFit
{
    Vector3 m_metric = new Vector3();
    Vector3 m_start;
    Vector3 m_end;
    float m_besterror;

    public RangeFit(ColourSet colours, bool isDxt1) : base(colours, isDxt1){}

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
        int count = Colours.Count;
        Vector3[] values = Colours.Points;
        
        // get the covariance matrix, compute the principle component
        Vector3 principle = Sym3x3.ExtractIndicesFromPackedBytes(count, values, Colours.Weights);

        // get the min and max range as the codebook endpoints
        if (count > 0)
        {
            float min, max;

            // compute the range
            m_start = m_end = values[0];
            min = max = values[0].Dot(principle);
            for (int i = 1; i < count; ++i)
            {
                float val = values[i].Dot(principle);
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
        m_end   = (Vector3.Grid * m_end  .Clamp(0.0f, 1.0f)).HalfAdjust() / Vector3.Grid;
    }

    protected override void Compress3(Span<byte> block)
    {
        // cache some values
        int count = Colours.Count;
        Vector3[] values = Colours.Points;

        // create a codebook
        Vector3[] codes = new Vector3[3];
        codes[0] = m_start;
        codes[1] = m_end;
        codes[2] = 0.5f * (m_start + m_end);

        // match each point to the closest code
        byte[] closest = new byte[16];
        float error = 0.0f;
        for (int i = 0; i < count; ++i)
        {
            // find the closest code
            float dist = float.MaxValue;
            int idx = 0;
            for (int j = 0; j < 3; ++j)
            {
                float d = (m_metric * (values[i] - codes[j])).LengthSquared();
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
        byte[] indices = Colours.RemapIndices(closest);

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