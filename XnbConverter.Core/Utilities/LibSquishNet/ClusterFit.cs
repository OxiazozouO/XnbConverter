using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class ClusterFit : ColourFit
{
    private readonly int IterationCount;

    private Vector3 Principle;

    private byte[] Order = Pool.RentByte(16 * 8);

    private Vector4[] PointsWeights = Pool.RentVector4(16);

    private Vector4 XsumWsum = new Vector4();

    private Vector4 Metric = new Vector4();

    private Vector4 BestError = new Vector4();

    public ClusterFit(ColourSet colours, bool isDxt1, bool isColourIterativeClusterFit)
        : base(colours, isDxt1)
    {
        // set the iteration count
        IterationCount = isColourIterativeClusterFit ? 8 : 1;
        // initialise the metric (old perceptual = 0.2126f, 0.7152f, 0.0722f)
        //m_metric = Vec4( metric[0], metric[1], metric[2], 1.0f );
    }

    public override void Init()
    {
        Metric.Fill(1.0f);
        BestError.Fill(float.MaxValue);
        Order.AsSpan().Fill(0);
        XsumWsum.Clear();
        // cache some values
        var count = Colours.Count;
        var values = Colours.Points;
        // get the covariance matrix, compute the principle component
        Principle = Sym3x3.ExtractIndicesFromPackedBytes(count, values, Colours.Weights);
    }

    private void ConstructOrdering(Vector3 axis)
    {
        // cache some values
        var count = Colours.Count;
        var values = Colours.Points;

        // build the list of dot products
        var dps = Pool.RentFloat(16);

        for (var i = 0; i < count; ++i)
        {
            dps[i] = values[i].Dot(axis);
            Order[i] = (byte)i;
        }

        // stable sort using them
        for (var i = 0; i < count; ++i)
        for (var j = i; j > 0 && dps[j] < dps[j - 1]; --j)
        {
            (dps[j], dps[j - 1]) = (dps[j - 1], dps[j]);
            var index = j;
            (Order[j], Order[j - 1]) = (Order[j - 1], Order[j]);
        }

        Pool.Return(dps);
        // copy the ordering and weight all the points
        var unweighted = Colours.Points;
        var weights = Colours.Weights;

        XsumWsum = new Vector4(0.0f);

        for (var i = 0; i < count; ++i)
        {
            int j = Order[i];
            var p = new Vector4(unweighted[j].X, unweighted[j].Y, unweighted[j].Z, 1.0f);
            var x = weights[j] * p;
            PointsWeights[i] = x;
            XsumWsum += x;
        }
    }

    private bool ConstructOrdering(Vector4 axis, int iteration)
    {
        // cache some values
        var count = Colours.Count;
        var values = Colours.Points;

        // build the list of dot products
        var dps = Pool.RentFloat(16);

        for (var i = 0; i < count; ++i)
        {
            dps[i] = values[i].Dot(axis);
            Order[16 * iteration + i] = (byte)i;
        }

        // stable sort using them
        for (var i = 0; i < count; ++i)
        for (var j = i; j > 0 && dps[j] < dps[j - 1]; --j)
        {
            (dps[j], dps[j - 1]) = (dps[j - 1], dps[j]);
            var index = 16 * iteration + j;
            (Order[index], Order[index - 1]) = (Order[index - 1], Order[index]);
        }

        Pool.Return(dps);
        // check this ordering is unique
        for (var it = 0; it < iteration; ++it)
        {
            var same = true;

            for (var i = 0; i < count; ++i)
            {
                if (Order[16 * iteration + i] == Order[16 * it + i]) continue;
                same = false;
                break;
            }

            if (same) return false;
        }
        // copy the ordering and weight all the points
        var unweighted = Colours.Points;
        var weights = Colours.Weights;

        XsumWsum = new Vector4(0.0f);

        for (var i = 0; i < count; ++i)
        {
            int j = Order[16 * iteration + i];
            var p = new Vector4(unweighted[j].X, unweighted[j].Y, unweighted[j].Z, 1.0f);
            var x = weights[j] * p;
            PointsWeights[i] = x;
            XsumWsum += x;
        }
        return true;
    }
    
    private static readonly Vector4 V1 = new(3.0f, 3.0f, 3.0f, 9.0f);
    private static readonly Vector4 V2 = new(2.0f, 2.0f, 2.0f, 4.0f);
    private static readonly Vector4 TwothirdsTwothirds2 = V2 / V1;

    protected override void Compress3(Span<byte> block)
    {
        // declare variables
        var count = Colours.Count;

        // prepare an ordering using the principle axis
        ConstructOrdering(Principle);

        // check all possible clusters and iterate on the total order
        var bestStart = Vector4.Zero;
        var bestEnd   = Vector4.Zero;
        var bestError = BestError;
        var bestIteration = 0;
        int bestI = 0, bestJ = 0;

        // loop over iterations (we avoid the case that all points in first or last cluster)
        for (var iterationIndex = 0;;)
        {
            // first cluster [0,i) is at the start
            var part0 = new Vector4(0.0f);
            for (var i = 0; i < count; ++i)
            {
                // second cluster [i,j) is half along
                var part1 = i == 0 ? PointsWeights[0] : new Vector4(0.0f);
                var jmin = i == 0 ? 1 : i;
                for (var j = jmin;;)
                {
                    // last cluster [j,count) is at the end
                    var part2 = XsumWsum - part1 - part0;

                    // compute least squares terms directly
                    Vector4  _ = part1 * Vector4.HalfHalf2;
                    Vector4 alphax_sum = _ + part0;
                    float alpha2_sum = alphax_sum.W;

                    Vector4  betax_sum = _ + part2;
                    float beta2_sum = betax_sum.W;

                    float alphabeta_sum = _.W;

                    // compute the least-squares optimal points
                    float factor = alpha2_sum * beta2_sum - alphabeta_sum * alphabeta_sum;
                    Vector4 a = (beta2_sum  * alphax_sum - alphabeta_sum * betax_sum ) / factor;
                    Vector4 b = (alpha2_sum * betax_sum  - alphabeta_sum * alphax_sum) / factor;
                    
                    // clamp to the grid
                    a = (Vector4.Grid * a.Clamp(0.0f, 1.0f)).HalfAdjust() / Vector4.Grid;
                    b = (Vector4.Grid * b.Clamp(0.0f, 1.0f)).HalfAdjust() / Vector4.Grid;

                    // compute the error (we skip the constant xxsum)
                    Vector4 e1 = alpha2_sum * a * a + beta2_sum * b * b;
                    Vector4 e2 = alphabeta_sum * a * b - a * alphax_sum;
                    Vector4 e3 = e2 - b * betax_sum;
                    Vector4 e4 = 2.0f * e3 + e1;

                    // apply the metric to the error term
                    Vector4 e5 = e4 * Metric;
                    Vector4 error = new Vector4(e5.X + e5.Y + e5.Z);

                    // keep the solution if it wins
                    if (error.CompareAnyLessThan(bestError))
                    {
                        bestStart = a;
                        bestEnd = b;
                        bestI = i;
                        bestJ = j;
                        bestError = error;
                        bestIteration = iterationIndex;
                    }

                    // advance
                    if (j == count) break;

                    part1 += PointsWeights[j];
                    ++j;
                }

                // advance
                part0 += PointsWeights[i];
            }

            // stop if we didn't improve in this iteration
            if (bestIteration != iterationIndex) break;

            // advance if possible
            ++iterationIndex;

            if (iterationIndex == IterationCount) break;

            // stop if a new iteration is an ordering that has already been tried
            Vector4 axis = bestEnd - bestStart;

            if (!ConstructOrdering(axis, iterationIndex)) break;
        }

        // save the block if necessary
        if (!bestError.CompareAnyLessThan(BestError)) return;
        
        byte[] unordered = Pool.RentNewByte(16);
        Span<byte> span = Order.AsSpan(16 * bestIteration, count);
        int m = 0;
        for (; m < bestI; ++m)
            unordered[span[m]] = 0;
        for (; m < bestJ; ++m)
            unordered[span[m]] = 2;
        for (; m < count; m++)
            unordered[span[m]] = 1;

        byte[] bestIndices = Colours.RemapIndices(unordered);

        // save the block
        ColourBlock.WriteColourBlock3(bestStart.To565(), bestEnd.To565(), bestIndices, block);
        Pool.Return(unordered);
        Pool.Return(bestIndices);
        // save the error
        BestError = bestError;
    }

    protected override void Compress4(Span<byte> block)
    {
        // declare variables
        int count = Colours.Count;

        // prepare an ordering using the principle axis
        ConstructOrdering(Principle);

        // check all possible clusters and iterate on the total order
        Vector4 bestStart = Vector4.Zero;
        Vector4 bestEnd = Vector4.Zero;
        Vector4 bestError = BestError;
        Vector4 part0 = new Vector4();
        Vector4 part1 = new Vector4();
        Vector4 part2Tmp = new Vector4();
        Vector4 part2, part3, alphaxSum, betaxSum, e1, e2, e3, e4, e5, error, a, b;
        int bestIteration = 0, bestI = 0, bestJ = 0, bestK = 0;
        float factor, beta2Sum, alpha2Sum, alphaBetaSum;
        
        // loop over iterations (we avoid the case that all points in first or last cluster)
        for (var iterationIndex = 0;;)
        {
            // first cluster [0,i) is at the start
            part0.Clear();
            for (var i = 0; i < count; ++i)
            {
                // second cluster [i,j) is one third along
                part1.Clear();
                for (int j = i;;)
                {
                    // third cluster [j,k) is two thirds along
                    int minK;
                    if (j == 0)
                    {
                        part2 = PointsWeights[0];
                        minK =  1;
                    }
                    else
                    {
                        part2Tmp.Clear();
                        part2 = part2Tmp;
                        minK = j;
                    }
                    for (int k = minK;;)
                    {
                        // last cluster [k,count) is at the end
                        part3 = XsumWsum - part2 - part1 - part0;

                        // compute least squares terms directly
                        alphaxSum = part1 * TwothirdsTwothirds2 + part2 / V1 + part0;
                        alpha2Sum = alphaxSum.W;

                        betaxSum = part1 / V1 + part2 * TwothirdsTwothirds2 + part3;
                        beta2Sum = betaxSum.W;

                        alphaBetaSum = (part1.W + part2.W) * 2.0f / 9.0f;

                        // compute the least-squares optimal points
                        factor = beta2Sum * alpha2Sum - alphaBetaSum * alphaBetaSum;
                        a = (beta2Sum  * alphaxSum - alphaBetaSum * betaxSum ) / factor;
                        b = (alpha2Sum * betaxSum  - alphaBetaSum * alphaxSum) / factor;

                        // clamp to the grid
                        a = (Vector4.Grid * a.Clamp(0.0f, 1.0f)).HalfAdjust() / Vector4.Grid;
                        b = (Vector4.Grid * b.Clamp(0.0f, 1.0f)).HalfAdjust() / Vector4.Grid;

                        // compute the error (we skip the constant xxsum)
                        e1 = alpha2Sum * a * a + beta2Sum * b * b;
                        e2 = alphaBetaSum * a * b - a * alphaxSum;
                        e3 = e2 - b * betaxSum;
                        e4 = 2.0f * e3 + e1;

                        // apply the metric to the error term
                        e5 = e4 * Metric;
                        error = new Vector4(e5.X + e5.Y + e5.Z);

                        // keep the solution if it wins
                        if (error.CompareAnyLessThan(bestError))
                        {
                            bestStart = a;
                            bestEnd = b;
                            bestError = error;
                            bestI = i;
                            bestJ = j;
                            bestK = k;
                            bestIteration = iterationIndex;
                        }

                        // advance
                        if (k == count) break;

                        part2 += PointsWeights[k];
                        ++k;
                    }

                    // advance
                    if (j == count) break;

                    part1 += PointsWeights[j];
                    ++j;
                }

                // advance
                part0 += PointsWeights[i];
            }

            // stop if we didn't improve in this iteration
            if (bestIteration != iterationIndex) break;

            // advance if possible
            ++iterationIndex;
            if (iterationIndex == IterationCount) break;

            // stop if a new iteration is an ordering that has already been tried
            var axis = bestEnd - bestStart;
            if (!ConstructOrdering(axis, iterationIndex)) break;
        }

        // save the block if necessary
        if (!bestError.CompareAnyLessThan(BestError)) return;
        // remap the indices
        byte[] unordered = Pool.RentNewByte(16);
        
        Span<byte> span = Order.AsSpan(16 * bestIteration, count);
        int m = 0;
        for (; m < bestI; ++m)
            unordered[span[m]] = 0;
        for (; m < bestJ; ++m)
            unordered[span[m]] = 2;
        for (; m < bestK; ++m)
            unordered[span[m]] = 3;
        for (; m < count; ++m)
            unordered[span[m]] = 1;
        
        var bestIndices = Colours.RemapIndices(unordered);

        // save the block                // get the packed values
        ColourBlock.WriteColourBlock4(bestStart.To565(), bestEnd.To565(), bestIndices, block);

        // save the error
        BestError = bestError;

        Pool.Return(bestIndices);
        Pool.Return(unordered);
    }

    public override void Dispose()
    {
        Pool.Return(Order);
        Pool.Return(PointsWeights);
    }
}