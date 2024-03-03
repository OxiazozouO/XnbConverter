using System;

namespace Squish;

public abstract class ColourFit : IDisposable
{
    private readonly bool _isDxt1;
    protected ColourSet Colours;

    protected ColourFit(ColourSet colours, bool isDxt1)
    {
        Colours = colours;
        _isDxt1 = isDxt1;
    }

    public abstract void Dispose();

    public abstract void Init();

    public void Compress(Span<byte> block)
    {
        if (_isDxt1)
        {
            Compress3(block);

            if (!Colours.IsTransparent) Compress4(block);
        }
        else
        {
            Compress4(block);
        }
    }

    protected abstract void Compress3(Span<byte> block);

    protected abstract void Compress4(Span<byte> block);
}