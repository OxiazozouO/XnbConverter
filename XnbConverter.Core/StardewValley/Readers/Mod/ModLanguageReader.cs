using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.Mod;

public class ModLanguageReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override ModLanguage Read()
    {
        ModLanguage result = new ModLanguage();
        result.ID = readerResolver.Read_Null<string>();
        result.LanguageCode = readerResolver.Read_Null<string>();
        result.ButtonTexture = readerResolver.Read_Null<string>();
        result.UseLatinFont = bufferReader.ReadBoolean();
        result.FontFile = readerResolver.Read_Null<string>();
        result.FontPixelZoom = bufferReader.ReadSingle();
        result.FontApplyYOffset = bufferReader.ReadBoolean();
        result.SmallFontLineSpacing = bufferReader.ReadInt32();
        result.UseGenderedCharacterTranslations = bufferReader.ReadBoolean();
        result.NumberComma = readerResolver.Read_Null<string>();
        result.TimeFormat = readerResolver.Read_Null<string>();
        result.ClockTimeFormat = readerResolver.Read_Null<string>();
        result.ClockDateFormat = readerResolver.Read_Null<string>();
        return result;
    }

    public override void Write(object content)
    {
        var input = (ModLanguage)content;
        
        readerResolver.Write_Null(stringReader, input.ID);
        readerResolver.Write_Null(stringReader, input.LanguageCode);
        readerResolver.Write_Null(stringReader, input.ButtonTexture);
        bufferWriter.WriteBoolean(input.UseLatinFont);
        readerResolver.Write_Null(stringReader, input.FontFile);
        bufferWriter.WriteSingle(input.FontPixelZoom);
        bufferWriter.WriteBoolean(input.FontApplyYOffset);
        bufferWriter.WriteInt32(input.SmallFontLineSpacing);
        bufferWriter.WriteBoolean(input.UseGenderedCharacterTranslations);
        readerResolver.Write_Null(stringReader, input.NumberComma);
        readerResolver.Write_Null(stringReader, input.TimeFormat);
        readerResolver.Write_Null(stringReader, input.ClockTimeFormat);
        readerResolver.Write_Null(stringReader, input.ClockDateFormat);
    }

    public override bool IsValueType()
    {
        return false;
    }
}