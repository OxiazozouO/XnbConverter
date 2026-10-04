namespace XnbConverter.Utilities.Png;

/// <summary>
/// 转换器自用的 PNG 编解码入口。替代原先的 SixLabors.ImageSharp，
/// 范围只有"RGBA8 ↔ PNG"这一件事，读和写都直接对着
/// <see cref="Entity.Mono.Texture2D.Data"/> 那块内存，不建任何中间图像对象。
///
/// 之所以自己写：ImageSharp 3.x 是 Six Labors Split License（非 OSI 开源），
/// 而我们只要这一个格式；自己写还能把输出参数和热路径都拿在手里，
/// 后面做专门的性能优化（过滤策略、自研 DEFLATE）不用再受制于人。
/// </summary>
public static class Png
{
    /// <summary>把 RGBA8 写成 PNG。像素按 R,G,B,A 逐行排布，长度为 width*height*4。</summary>
    public static void Write(string path, ReadOnlySpan<byte> rgba, int width, int height)
    {
        using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, 1 << 16);
        PngWriter.Write(stream, rgba, width, height);
    }

    /// <summary>读 PNG 成 RGBA8。支持颜色类型 0/2/3/4/6、位深 1/2/4/8/16、tRNS、Adam7。</summary>
    public static byte[] Read(string path, out int width, out int height)
    {
        return PngReader.Read(File.ReadAllBytes(path), out width, out height);
    }
}
