using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public static class ColourBlock
{
    private const int winSize = 16;

    public static int ToInt(this float a, int limit)
    {
        // use ANSI round-to-zero behaviour to get round-to-nearest
        return Math.Clamp((int)(a + 0.5f), 0, limit);
    }

    public static byte ScaleToByte(this float a, int limit)
    {
        return (byte)Math.Clamp((int)(limit * a + 0.5f), 0, limit);
    }

    public static int To565(this Vector3 colour)
    {
        // get the components in the correct range
        int r = colour.X.ScaleToByte(31);
        int g = colour.Y.ScaleToByte(63);
        int b = colour.Z.ScaleToByte(31);

        // pack into a single value
        return (r << 11) | (g << 5) | b;
    }

    public static int To565(this Vector4 colour)
    {
        // get the components in the correct range
        int r = colour.X.ScaleToByte(31);
        int g = colour.Y.ScaleToByte(63);
        int b = colour.Z.ScaleToByte(31);

        // pack into a single value
        return (r << 11) | (g << 5) | b;
    }

    private static void WriteColourBlock(int a, int b, Span<byte> indices, Span<byte> block)
    {
        // write the endpoints
        block[0] = (byte)(a & 0xff);
        block[1] = (byte)(a >> 8);
        block[2] = (byte)(b & 0xff);
        block[3] = (byte)(b >> 8);
        // write the indices
        block[4] = (byte)((indices[0] << 0) | (indices[1] << 2) | (indices[2] << 4) | (indices[3] << 6));
        block[5] = (byte)((indices[4] << 0) | (indices[5] << 2) | (indices[6] << 4) | (indices[7] << 6));
        block[6] = (byte)((indices[8] << 0) | (indices[9] << 2) | (indices[10] << 4) | (indices[11] << 6));
        block[7] = (byte)((indices[12] << 0) | (indices[13] << 2) | (indices[14] << 4) | (indices[15] << 6));
    }

    public static void WriteColourBlock3(int a565, int b565, Span<byte> indices, Span<byte> block)
    {
        // remap the indices
        if (a565 > b565)
        {
            // swap a and b
            (a565, b565) = (b565, a565);
            for (var i = 0; i < winSize; ++i)
                if (indices[i] == 0)
                    indices[i] = 1;
                else if (indices[i] == 1)
                    indices[i] = 0;
        }

        // write the block
        WriteColourBlock(a565, b565, indices, block);
    }

    public static void WriteColourBlock4(int a565, int b565, Span<byte> indices, Span<byte> block)
    {
        // remap the indices
        if (a565 < b565)
        {
            // swap a and b
            (a565, b565) = (b565, a565);
            for (var i = 0; i < winSize; ++i)
                indices[i] = (byte)((indices[i] ^ 0x1) & 0x3);
        }
        else if (a565 == b565)
        {
            // use index 0
            indices.Fill(0);
        }

        // write the block
        WriteColourBlock(a565, b565, indices, block);
    }

    private static int Unpack565(byte p1, byte p2, Span<byte> colour)
    {
        // build the packed value
        var value = p1 | (p2 << 8);

        // get the components in the stored range
        var r = (byte)((value >> 11) & 0x1f);
        var g = (byte)((value >> 5) & 0x3f);
        var b = (byte)(value & 0x1f);

        // scale up to 8 bits
        colour[0] = (byte)((r << 3) | (r >> 2));
        colour[1] = (byte)((g << 2) | (g >> 4));
        colour[2] = (byte)((b << 3) | (b >> 2));
        colour[3] = 255;

        // return the value
        return value;
    }

    public static void DecompressColour(Span<byte> rgba, ReadOnlySpan<byte> block, bool isDxt1)
    {
        // unpack the endpoints
        var bytes = Pool.RentByte(winSize);
        var codes = bytes.AsSpan();
        codes.Fill(0);
        var a = Unpack565(block[0], block[1], codes);
        var b = Unpack565(block[2], block[3], codes[4..]);

        // generate the midpoints
        if (isDxt1 && a <= b)
            for (var i = 0; i < 3; ++i)
            {
                int c = codes[i];
                int d = codes[4 + i];
                codes[8 + i] = (byte)((c + d) / 2);
                codes[12 + i] = 0;
            }
        else
            for (var i = 0; i < 3; ++i)
            {
                int c = codes[i];
                int d = codes[4 + i];
                codes[8 + i] = (byte)((2 * c + d) / 3);
                codes[12 + i] = (byte)((c + 2 * d) / 3);
            }


        // fill in alpha for the intermediate values
        codes[8 + 3] = 255;
        codes[12 + 3] = (byte)(isDxt1 && a <= b ? 0 : 255);

        // unpack the indices
        var indices = Pool.RentByte(winSize);

        for (int i = 4, index = -1; i < 8; ++i)
        {
            var packed = block[i];

            indices[++index] = (byte)(packed & 0x3);
            indices[++index] = (byte)((packed >> 2) & 0x3);
            indices[++index] = (byte)((packed >> 4) & 0x3);
            indices[++index] = (byte)((packed >> 6) & 0x3);
        }

        // store out the colours
        for (var i = 0; i < winSize; ++i) codes.Slice(4 * indices[i], 4).CopyTo(rgba[(4 * i)..]);

        Pool.Return(indices);
        Pool.Return(bytes);
    }
}