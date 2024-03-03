using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class Sym3x3
{
    public static Vector3 ExtractIndicesFromPackedBytes(int n, Vector3[] points, float[] weights)
    {
        // compute the centroid
        var total = 0.0f;
        var floats = Pool.RentFloat(16);
        var fSpan = floats.AsSpan();
        var centroid = fSpan[..3];
        centroid.Fill(0.0f);
        // Vector3 centroid = new Vector3();

        for (var i = 0; i < n; ++i)
        {
            total += weights[i];
            // centroid += weights[i] * points[i];
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

        // Vector3 a;
        var a = fSpan[3..6];
        var b = fSpan[6..9];
        // accumulate the covariance matrix
        var matrix = fSpan[9..];
        // Vector3 b;
        for (var i = 0; i < n; ++i)
        {
            a[0] = points[i].X - centroid[0];
            a[1] = points[i].Y - centroid[1];
            a[2] = points[i].Z - centroid[2];

            b[0] = weights[i] * a[0];
            b[1] = weights[i] * a[1];
            b[2] = weights[i] * a[2];

            matrix[0] += a[0] * b[0];
            matrix[1] += a[0] * b[1];
            matrix[2] += a[0] * b[2];
            matrix[3] += a[1] * b[1];
            matrix[4] += a[1] * b[2];
            matrix[5] += a[2] * b[2];
        }

        var w = centroid;
        var v = a;
        v.Fill(1.0f);
        float f;
        for (var i = 0; i < 8; ++i)
        {
            // matrix multiply
            w[0] = v[0] * matrix[0] + v[1] * matrix[1] + v[2] * matrix[2];
            w[1] = v[0] * matrix[1] + v[1] * matrix[3] + v[2] * matrix[4];
            w[2] = v[0] * matrix[2] + v[1] * matrix[4] + v[2] * matrix[5];
            // get max component from xyz in all channels
            f = Math.Max(w[0], Math.Max(w[1], w[2]));

            // divide through and advance
            // v = ww / f;
            v[0] = w[0] / f;
            v[1] = w[1] / f;
            v[2] = w[2] / f;
        }

        Pool.Return(floats);
        return new Vector3(v[0], v[1], v[2]);
    }
}