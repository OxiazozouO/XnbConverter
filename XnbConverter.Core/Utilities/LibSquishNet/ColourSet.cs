﻿using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class ColourSet : IDisposable
{
    private readonly bool IsDxt1;

    private readonly int[] Remap = Pool.RentNewInt(16);
    private readonly bool WeightByAlpha;
    public int Count;

    public bool IsTransparent;

    public Vector3[] Points = Pool.RentVector3(16);

    public float[] Weights = Pool.RentFloat(16);

    public ColourSet(bool isDxt1, bool weightByAlpha)
    {
        IsDxt1 = isDxt1;
        WeightByAlpha = weightByAlpha;
    }

    public void Dispose()
    {
        Pool.Return(Weights);
        Pool.Return(Remap);
        Pool.Return(Points);
    }

    public void Init(ReadOnlySpan<byte> rgba, int mask)
    {
        Weights.AsSpan().Fill(0.0f);
        Remap.AsSpan().Fill(0);
        Count = 0;
        IsTransparent = false;
        // create the minimal set
        for (int i = 0, i4; i < 16; ++i)
        {
            i4 = i * 4;
            // check this pixel is enabled
            var bit = 1 << i;

            if ((mask & bit) == 0)
            {
                Remap[i] = -1;
                continue;
            }

            // check for transparent pixels when using dxt1
            if (IsDxt1 && rgba[i4 + 3] < 128)
            {
                Remap[i] = -1;
                IsTransparent = true;
                continue;
            }

            // loop over previous points for a match
            for (int j = 0, j4;; ++j)
            {
                j4 = j * 4;
                // allocate a new point
                if (j == i)
                {
                    // normalise coordinates to [0,1]
                    Points[Count].X = rgba[i4 + 0] / 255.0f;
                    Points[Count].Y = rgba[i4 + 1] / 255.0f;
                    Points[Count].Z = rgba[i4 + 2] / 255.0f;
                    // ensure there is always non-zero weight even for zero alpha
                    var w = (rgba[i4 + 3] + 1) / 256.0f;

                    // add the point
                    Weights[Count] = WeightByAlpha ? w : 1.0f;
                    Remap[i] = Count;
                    ++Count;

                    break;
                }

                // check for a match
                var oldBit = 1 << j;
                var match = (mask & oldBit) != 0
                            && rgba[i4] == rgba[j4]
                            && rgba[i4 + 1] == rgba[j4 + 1]
                            && rgba[i4 + 2] == rgba[j4 + 2]
                            && !(rgba[j4 + 3] < 128 && IsDxt1);

                if (!match) continue;
                // get the index of the match
                var index = Remap[j];

                // ensure there is always non-zero weight even for zero alpha
                var f = WeightByAlpha ? (rgba[i4 + 3] + 1) / 256.0f : 1.0f;

                // map to this point and increase the weight
                Weights[index] += f;
                Remap[i] = index;
                break;
            }
        }

        // square root the weights
        for (var i = 0; i < Count; ++i) Weights[i] = (float)Math.Sqrt(Weights[i]);

        for (var i = Count; i < 16; i++) Points[i].Clear();
    }

    public byte[] RemapIndices(byte[] source)
    {
        var target = Pool.RentByte(16);
        for (var i = 0; i < 16; ++i)
        {
            var j = Remap[i];
            target[i] = j == -1 ? (byte)3 : source[j];
        }

        return target;
    }

    public byte[] RemapIndices(byte source)
    {
        var target = Pool.RentByte(16);
        for (var i = 0; i < 16; ++i)
            target[i] = Remap[i] == -1 ? (byte)3 : source;

        return target;
    }
}