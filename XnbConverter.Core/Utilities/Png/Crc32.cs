namespace XnbConverter.Utilities.Png;

/// <summary>PNG 每个 chunk 尾部要带的标准 CRC-32（多项式 0xEDB88320）。</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = crc ^ 0xFFFFFFFFu;
        for (int i = 0; i < data.Length; i++)
        {
            c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }
}
