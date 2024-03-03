#region HEADER

/*
 * Author: Maxx Wyndham (https://github.com/MaxxWyndham)
 * Project URL: https://github.com/MaxxWyndham/LibSquishNet
 */

#region MIT License

/*
 * Copyright (c) 2021 Maxx Wyndham
 * Copyright (c) 2006 Simon Brown                          si@sjbrown.co.uk
 */

#endregion

/*
 * 对于原代码进行了一定适配，并使用了Span、ArrayPool等优化、
 * 减少了一些不必要的创建对象、极大提升了源代码的性能。
 */

#endregion

using System;
using XnbConverter.Utilities;
using static Squish.SquishFlags;

namespace Squish;

[Flags]
public enum SquishFlags
{
    //! Use DXT1 compression.
    kDxt1 = 1,

    //! Use DXT3 compression.
    kDxt3 = 2,

    //! Use DXT5 compression.
    kDxt5 = 4,

    //! Use a very slow but very high quality colour compressor.
    kColourIterativeClusterFit = 256,

    //! Use a slow but high quality colour compressor (the default).
    kColourClusterFit = 8,

    //! Use a fast but low quality colour compressor.
    kColourRangeFit = 16,

    //! Weight the colour by alpha during cluster fit (disabled by default).
    kWeightColourByAlpha = 128
}

public class BoolFlag
{
    public bool isColourClusterFit;
    public bool isColourIterativeClusterFit;
    public bool isColourRangeFit;
    public bool isDxt1;
    public bool isDxt3;
    public bool isDxt5;
    public bool isWeightColourByAlpha;

    public BoolFlag(SquishFlags flags)
    {
        isDxt1 = (flags & kDxt1) != 0;
        isDxt3 = (flags & kDxt3) != 0;
        isDxt5 = (flags & kDxt5) != 0;
        isColourIterativeClusterFit = (flags & kColourIterativeClusterFit) != 0;
        isColourClusterFit = (flags & kColourClusterFit) != 0;
        isColourRangeFit = (flags & kColourRangeFit) != 0;
        isWeightColourByAlpha = (flags & kWeightColourByAlpha) != 0;
    }
}

public class Squish : IDisposable
{
    private readonly BoolFlag boolFlag;
    private readonly ClusterFit clusterFit;
    private readonly ColourSet Colours;
    private readonly int Height;
    private readonly RangeFit rangeFit;
    private readonly SingleColourFit singleColourFit;
    private readonly int Width;

    public Squish(SquishFlags flags, int width, int height)
    {
        boolFlag = new BoolFlag(flags);
        if (boolFlag is { isDxt3: false, isDxt5: false })
            boolFlag.isDxt1 = true;

        if (boolFlag is { isColourRangeFit: false, isColourIterativeClusterFit: false })
            boolFlag.isColourClusterFit = true;

        Colours = new ColourSet(boolFlag.isDxt1, boolFlag.isWeightColourByAlpha);
        singleColourFit = new SingleColourFit(Colours, boolFlag.isDxt1);
        rangeFit = new RangeFit(Colours, boolFlag.isDxt1);
        clusterFit = new ClusterFit(Colours, boolFlag.isDxt1, boolFlag.isColourIterativeClusterFit);

        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        Colours.Dispose();
        singleColourFit.Dispose();
        clusterFit.Dispose();
        rangeFit.Dispose();
    }

    private static void FixRange(int min, int max, int steps)
    {
        if (max - min < steps) max = Math.Min(min + steps, 255);

        if (max - min < steps) min = Math.Max(0, max - steps);
    }

    private static int FitCodes(ReadOnlySpan<byte> rgba, int mask, byte[] codes, byte[] indices)
    {
        // fit each alpha value to the codebook
        var err = 0;

        for (var i = 0; i < 16; ++i)
        {
            // check this pixel is valid
            var bit = 1 << i;

            if ((mask & bit) == 0)
            {
                // use the first code
                indices[i] = 0;
                continue;
            }

            // find the least error and corresponding index
            int value = rgba[4 * i + 3];
            var least = int.MaxValue;
            var index = 0;

            for (var j = 0; j < 8; ++j)
            {
                // get the squared error from this code
                var dist = value - codes[j];
                dist *= dist;

                // compare with the best so far
                if (dist < least)
                {
                    least = dist;
                    index = j;
                }
            }

            // save this index and accumulate the error
            indices[i] = (byte)index;
            err += least;
        }

        // return the total error
        return err;
    }

    public int GetStorageRequirements()
    {
        // compute the storage requirements
        var blockCount = (Width + 3) / 4 * ((Height + 3) / 4);
        var blockSize = boolFlag.isDxt1 ? 8 : 16;

        return blockCount * blockSize;
    }

    public void DecompressImage(Span<byte> rgba, ReadOnlySpan<byte> blocks)
    {
        // initialise the block input
        var sourceBlock = 0;
        var bytesPerBlock = boolFlag.isDxt1 ? 8 : 16;
        var bytes = Pool.RentByte(4 * 16);
        var targetRgba = bytes.AsSpan();
        // loop over blocks
        for (var y = 0; y < Height; y += 4)
        for (var x = 0; x < Width; x += 4)
        {
            // decompress the block
            Decompress(targetRgba, blocks, sourceBlock);

            // write the decompressed pixels to the correct image locations
            var sourcePixel = 0;

            for (var py = 0; py < 4; ++py)
            for (var px = 0; px < 4; ++px)
            {
                // get the target location
                var sx = x + px;
                var sy = y + py;

                if (sx < Width && sy < Height)
                    // copy the rgba value
                    targetRgba.Slice(sourcePixel, 4)
                        .CopyTo(rgba.Slice(4 * (Width * sy + sx), 4));
                sourcePixel += 4;
            }

            // advance
            sourceBlock += bytesPerBlock;
        }

        Pool.Return(bytes);
    }

    private void Decompress(Span<byte> rgba, ReadOnlySpan<byte> block, int offset)
    {
        // get the block locations
        var colourBlock = offset;
        if (boolFlag.isDxt3 || boolFlag.isDxt5)
            colourBlock += 8;

        // decompress colour
        ColourBlock.DecompressColour(rgba, block.Slice(colourBlock, 8), boolFlag.isDxt1);

        // decompress alpha separately if necessary
        if (boolFlag.isDxt3)
            DecompressAlphaDxt3(rgba, block.Slice(offset, 8));
        else if (boolFlag.isDxt5) DecompressAlphaDxt5(rgba, block.Slice(offset, 8));
    }

    private static void DecompressAlphaDxt3(Span<byte> rgba, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < 8; ++i)
        {
            var lo = (byte)(block[i] & 0x0f);
            var hi = (byte)(block[i] & 0xf0);
            rgba[8 * i + 3] = (byte)(lo | (lo << 4));
            rgba[8 * i + 7] = (byte)(hi | (hi >> 4));
        }
    }

    private static void DecompressAlphaDxt5(Span<byte> rgba, ReadOnlySpan<byte> block)
    {
        // get the two alpha values
        int alpha0 = block[0];
        int alpha1 = block[1];

        // compare the values to build the codebook
        var codes = new byte[8];
        codes[0] = (byte)alpha0;
        codes[1] = (byte)alpha1;

        if (alpha0 > alpha1)
        {
            // use 7-alpha codebook
            for (var i = 1; i < 7; ++i) codes[1 + i] = (byte)(i * (alpha1 - alpha0) / 7 + alpha0);
        }
        else
        {
            // use 5-alpha codebook
            for (var i = 1; i < 5; ++i) codes[1 + i] = (byte)(i * (alpha1 - alpha0) / 5 + alpha0);

            codes[6] = 0;
            codes[7] = 255;
        }

        // decode the indices
        var indices = new byte[16];
        var src = 2;
        var dest = 0;

        for (var i = 0; i < 2; ++i)
        {
            // grab 3 bytes
            var value = 0;
            for (var j = 0; j < 3; ++j) value |= block[src++] << (8 * j);

            // unpack 8 3-bit values from it
            for (var j = 0; j < 8; ++j)
            {
                var index = (value >> (3 * j)) & 0x7;
                indices[dest++] = (byte)index;
            }
        }

        // write out the indexed codebook values
        for (var i = 0; i < 16; ++i) rgba[4 * i + 3] = codes[indices[i]];
    }

    public void CompressImage(ReadOnlySpan<byte> rgba, Span<byte> blocks)
    {
        // initialise the block output
        var targetBlock = 0;
        var bytesPerBlock = boolFlag.isDxt1 ? 8 : 16;

        // build the 4x4 block of pixels
        var bytes = Pool.RentByte(16 * 4);
        var sourceRgba = bytes.AsSpan();

        // loop over blocks
        for (var y = 0; y < Height; y += 4)
        for (var x = 0; x < Width; x += 4)
        {
            var targetPixel = 0;
            var mask = 0;
            for (var py = 0; py < 4; ++py)
            for (var px = 0; px < 4; ++px)
            {
                // get the source pixel in the image
                var sx = x + px;
                var sy = y + py;
                var span = sourceRgba.Slice(targetPixel, 4);
                // enable if we're in the image
                if (sx < Width && sy < Height)
                {
                    // copy the rgba value
                    rgba.Slice(4 * (Width * sy + sx), 4).CopyTo(span);
                    // enable this pixel
                    mask |= 1 << (4 * py + px);
                }
                else
                {
                    // skip this pixel as its outside the image
                    span.Fill(0);
                }

                targetPixel += 4;
            }

            // compress it into the output
            CompressMasked(sourceRgba, mask, blocks, targetBlock);
            // advance
            targetBlock += bytesPerBlock;
        }

        Pool.Return(bytes);
    }

    // private ColourSet colours = new ColourSet(rgba, mask, boolFlag.isDxt1, boolFlag.isWeightColourByAlpha);
    private void CompressMasked(ReadOnlySpan<byte> rgba, int mask, Span<byte> block, int offset)
    {
        // get the block locations
        var colourBlock = offset;
        var alphaBlock = offset;
        if (boolFlag.isDxt3 || boolFlag.isDxt5) colourBlock += 8;

        // create the minimal point set
        Colours.Init(rgba, mask);
        // ColourSet colours = new ColourSet(rgba, mask, boolFlag.isDxt1, boolFlag.isWeightColourByAlpha);
        ColourFit fit;
        // check the compression type and compress colour
        if (Colours.Count == 1)
            // always do a single colour fit
            fit = singleColourFit;
        else if (boolFlag.isColourRangeFit || Colours.Count == 0)
            // do a range fit
            fit = rangeFit;
        else
            // default to a cluster fit (could be iterative or not)
            fit = clusterFit;
        fit.Init();
        fit.Compress(block[colourBlock..]);

        // compress alpha separately if necessary
        if (boolFlag.isDxt3)
            CompressAlphaDxt3(rgba, mask, block[alphaBlock..]);
        else if (boolFlag.isDxt5) CompressAlphaDxt5(rgba, mask, block[alphaBlock..]);
    }

    private static void CompressAlphaDxt3(ReadOnlySpan<byte> rgba, int mask, Span<byte> block)
    {
        // quantise and pack the alpha values pairwise
        const float _ = 15.0f / 255.0f;
        for (var i = 0; i < 8; ++i)
        {
            // quantise down to 4 bits
            var alpha1 = rgba[8 * i + 3] * _;
            var alpha2 = rgba[8 * i + 7] * _;
            var q1 = alpha1.ToInt(15);
            var q2 = alpha2.ToInt(15);
            // set alpha to zero where masked
            var bit1 = 1 << (2 * i);
            var bit2 = 1 << (2 * i + 1);
            if ((mask & bit1) == 0) q1 = 0;

            if ((mask & bit2) == 0) q2 = 0;

            // pack into the byte
            block[i] = (byte)(q1 | (q2 << 4));
        }
    }

    private static void CompressAlphaDxt5(ReadOnlySpan<byte> rgba, int mask, Span<byte> block)
    {
        // get the range for 5-alpha and 7-alpha interpolation
        var min5 = 255;
        var max5 = 0;
        var min7 = 255;
        var max7 = 0;

        for (var i = 0; i < 16; ++i)
        {
            // check this pixel is valid
            var bit = 1 << i;

            if ((mask & bit) == 0) continue;

            // incorporate into the min/max
            int value = rgba[4 * i + 3];
            min7 = Math.Min(min7, value);
            max7 = Math.Max(max7, value);

            if (value != 0)
                min5 = Math.Min(min5, value);
            if (value != 255)
                max5 = Math.Max(max5, value);
        }

        // handle the case that no valid range was found
        min5 = Math.Min(min5, max5);
        min7 = Math.Min(min7, max7);

        // set up the 5-alpha code book
        var codes5 = new byte[8];

        codes5[0] = (byte)min5;
        codes5[1] = (byte)max5;

        for (var i = 1; i < 5; ++i) codes5[1 + i] = (byte)(((5 - i) * min5 + i * max5) / 5);

        codes5[6] = 0;
        codes5[7] = 255;

        // set up the 7-alpha code book
        var codes7 = new byte[8];

        codes7[0] = (byte)min7;
        codes7[1] = (byte)max7;

        for (var i = 1; i < 7; ++i) codes7[1 + i] = (byte)(((7 - i) * min7 + i * max7) / 7);

        // fit the data to both code books
        var indices5 = Pool.RentByte(16);
        var indices7 = Pool.RentByte(16);
        var err5 = FitCodes(rgba, mask, codes5, indices5);
        var err7 = FitCodes(rgba, mask, codes7, indices7);

        // save the block with least error
        if (err5 <= err7)
            WriteAlphaBlock5(min5, max5, indices5, block);
        else
            WriteAlphaBlock7(min7, max7, indices7, block);

        Pool.Return(indices5);
        Pool.Return(indices7);
    }

    private static void WriteAlphaBlock(int alpha0, int alpha1, byte[] indices, Span<byte> block)
    {
        // write the first two bytes
        block[0] = (byte)alpha0;
        block[1] = (byte)alpha1;

        // pack the indices with 3 bits each
        var dest = 2;
        var src = 0;

        for (var i = 0; i < 2; ++i)
        {
            // pack 8 3-bit values
            var value = 0;

            for (var j = 0; j < 8; ++j)
            {
                int index = indices[src];

                value |= index << (3 * j);
                src++;
            }

            // store in 3 bytes
            for (var j = 0; j < 3; ++j) block[dest++] = (byte)((value >> (8 * j)) & 0xff);
        }
    }

    private static void WriteAlphaBlock5(int alpha0, int alpha1, byte[] indices, Span<byte> block)
    {
        // check the relative values of the endpoints
        if (alpha0 > alpha1)
        {
            (alpha0, alpha1) = (alpha1, alpha0);
            // swap the indices
            for (var i = 0; i < 16; ++i)
                indices[i] = indices[i] switch
                {
                    0 => 1,
                    1 => 0,
                    <= 5 => (byte)(7 - indices[i]),
                    _ => indices[i]
                };
        }

        // write the block
        WriteAlphaBlock(alpha0, alpha1, indices, block);
    }

    private static void WriteAlphaBlock7(int alpha0, int alpha1, byte[] indices, Span<byte> block)
    {
        // check the relative values of the endpoints
        if (alpha0 < alpha1)
        {
            (alpha0, alpha1) = (alpha1, alpha0);
            // swap the indices
            for (var i = 0; i < 16; ++i)
                indices[i] = indices[i] switch
                {
                    0 => 1,
                    1 => 0,
                    _ => (byte)(9 - indices[i])
                };
        }

        // write the block
        WriteAlphaBlock(alpha0, alpha1, indices, block);
    }
}