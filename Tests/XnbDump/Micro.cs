using System.Diagnostics;
using MonoVec4 = XnbConverter.Entity.Mono.Vector4;


/// <summary>
/// 对比自研标量 Vector4 与 System.Numerics.Vector4（SIMD）在 ClusterFit 那种
/// 运算组合下的吞吐，用来判断值不值得做 SIMD 重构。
/// </summary>
public static class Micro
{
    public static void Run()
    {
        const int iterations = 20_000_000;
        MonoVec4 a = new MonoVec4(0.3f, 0.7f, 0.1f, 1f);
        MonoVec4 b = new MonoVec4(0.9f, 0.2f, 0.55f, 0.25f);
        MonoVec4 c = new MonoVec4(0.15f, 0.85f, 0.4f, 0.5f);
        MonoVec4 v1 = new MonoVec4(3f, 3f, 3f, 9f);
        MonoVec4 v1rcp = new MonoVec4(1f / 3f, 1f / 3f, 1f / 3f, 1f / 9f);
        MonoVec4 metric = new MonoVec4(1f, 1f, 1f, 1f);

        float sink = 0f;
        // 预热
        for (int i = 0; i < 100_000; i++)
        {
            sink += MonoStep(ref a, ref b, ref c, v1rcp, metric).X;
        }

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            sink += MonoStep(ref a, ref b, ref c, v1rcp, metric).X;
        }

        sw.Stop();
        double monoMs = sw.Elapsed.TotalMilliseconds;

        var sa = new System.Numerics.Vector4(0.3f, 0.7f, 0.1f, 1f);
        var sb = new System.Numerics.Vector4(0.9f, 0.2f, 0.55f, 0.25f);
        var sc = new System.Numerics.Vector4(0.15f, 0.85f, 0.4f, 0.5f);
        var sv1rcp = new System.Numerics.Vector4(1f / 3f, 1f / 3f, 1f / 3f, 1f / 9f);
        var smetric = new System.Numerics.Vector4(1f, 1f, 1f, 1f);

        for (int i = 0; i < 100_000; i++)
        {
            sink += SimdStep(ref sa, ref sb, ref sc, sv1rcp, smetric).X;
        }

        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            sink += SimdStep(ref sa, ref sb, ref sc, sv1rcp, smetric).X;
        }

        sw.Stop();
        double simdMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"\n微基准（{iterations / 1_000_000}M 次迭代，每次约 30 个 vec4 运算）");
        Console.WriteLine($"  标量 Vector4 : {monoMs,8:F0} ms");
        Console.WriteLine($"  SIMD Vector4 : {simdMs,8:F0} ms   加速 {monoMs / simdMs:F2}x   (sink={sink})");
    }

    private static MonoVec4 MonoStep(ref MonoVec4 a, ref MonoVec4 b, ref MonoVec4 c, MonoVec4 v1rcp, MonoVec4 metric)
    {
        MonoVec4 t0 = b * v1rcp + a * v1rcp + c;
        MonoVec4 t1 = v1rcp * c + b * v1rcp + a;
        float w = t0.W;
        float w2 = t1.W;
        float n = (b.W + c.W) * 2f / 9f;
        float d = w2 * w - n * n;
        float dr = 1f / d;
        MonoVec4 u = dr * (w2 * t0 - n * t1);
        MonoVec4 v = dr * (w * t1 - n * t0);
        u = u.Clamp(0f, 1f);
        v = v.Clamp(0f, 1f);
        MonoVec4 e1 = w * u * u + w2 * v * v;
        MonoVec4 e2 = n * u * v - u * t0 - v * t1;
        MonoVec4 e3 = (2f * e2 + e1) * metric;
        MonoVec4 r = new MonoVec4(e3.X + e3.Y + e3.Z);
        return r;
    }

    private static System.Numerics.Vector4 SimdStep(ref System.Numerics.Vector4 a,
        ref System.Numerics.Vector4 b, ref System.Numerics.Vector4 c,
        System.Numerics.Vector4 v1rcp, System.Numerics.Vector4 metric)
    {
        System.Numerics.Vector4 t0 = b * v1rcp + a * v1rcp + c;
        System.Numerics.Vector4 t1 = v1rcp * c + b * v1rcp + a;
        float w = t0.W;
        float w2 = t1.W;
        float n = (b.W + c.W) * 2f / 9f;
        float d = w2 * w - n * n;
        float dr = 1f / d;
        System.Numerics.Vector4 u = dr * (w2 * t0 - n * t1);
        System.Numerics.Vector4 v = dr * (w * t1 - n * t0);
        u = System.Numerics.Vector4.Clamp(u, System.Numerics.Vector4.Zero, System.Numerics.Vector4.One);
        v = System.Numerics.Vector4.Clamp(v, System.Numerics.Vector4.Zero, System.Numerics.Vector4.One);
        System.Numerics.Vector4 e1 = w * u * u + w2 * v * v;
        System.Numerics.Vector4 e2 = n * u * v - u * t0 - v * t1;
        System.Numerics.Vector4 e3 = (2f * e2 + e1) * metric;
        System.Numerics.Vector4 r = new System.Numerics.Vector4(e3.X + e3.Y + e3.Z);
        return r;
    }
}
