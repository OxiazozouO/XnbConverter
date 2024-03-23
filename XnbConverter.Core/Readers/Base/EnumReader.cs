using XnbConverter.Utilities;

namespace XnbConverter.Readers.Base;

public class EnumReader<T> : BaseReader where T : Enum
{
    private bool b;
    private int reader;
    private Type _buildType;
    private bool _isFlags;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        reader = readerResolver.GetIndex(Enum.GetUnderlyingType(typeof(T)));
        _buildType = typeof(T);
        _isFlags = _buildType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;
    }

    public override bool IsValueType()
    {
        return true;
    }
    public override object Read()
    {
        var value = readerResolver.ReadValue(reader);
        if (Enum.IsDefined(_buildType, value))
        {
            return (T)value;
        }
        if (_isFlags)
        {
            return (T)Enum.Parse(_buildType, value.ToString());
        }
        
        throw new XnbError(Helpers.I18N["EnumReader.1"], value);
    }

    public override void Write(object content)
    {
        var input = (T)content;

        if (Enum.IsDefined(typeof(T), input) || _isFlags)
            readerResolver.WriteValue(reader, input);
        else
            throw new XnbError(Helpers.I18N["EnumReader.1"], input);
    }
}