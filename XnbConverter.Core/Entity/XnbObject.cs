using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace XnbConverter.Entity;

public class XnbObject
{
    public HeaderDTO Header;
    public List<ReadersDTO> Readers = new();
    public ContentDTO Content;
    
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TargetTags:byte
    {
        Windows = (byte)'w',
        WindowsPhone7 = (byte)'m',
        Xbox360 = (byte)'x',
        Android = (byte)'a',
        Ios = (byte)'i',
        Linux = (byte)'l',
        MacOSX = (byte)'X'
    }

    [Flags][JsonConverter(typeof(StringEnumConverter))]
    public enum CompressedMasks:byte
    {
        Hidef = 1,
        Lz4 = 64,
        Lzx = 128,
    }
    
    public class HeaderDTO
    {
        public TargetTags Target { get; set; }
        public byte FormatVersion { get; set; }
        public CompressedMasks CompressedFlag { get; set; }
    }
    public class ReadersDTO
    {
        public string? Type { get; set; }
        public uint Version { get; set; }
    }
}

public class ContentDTO
{
    public string Extension { get; set; }
    public int Format { get; set; }
}