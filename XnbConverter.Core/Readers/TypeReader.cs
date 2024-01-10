using XnbConverter.Entity.Mono;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Readers.Mono;
using XnbConverter.StardewValley.Entity;
using XnbConverter.StardewValley.Entity.Crafting;
using XnbConverter.StardewValley.Entity.FishPond;
using XnbConverter.StardewValley.Entity.HomeRenovations;
using XnbConverter.StardewValley.Entity.Movies;
using XnbConverter.StardewValley.Readers.Crafting;
using XnbConverter.StardewValley.Readers.FishPond;
using XnbConverter.StardewValley.Readers.HomeRenovations;
using XnbConverter.StardewValley.Readers.Mod;
using XnbConverter.StardewValley.Readers.Movies;
using XnbConverter.StardewValley.Readers.RandomBundle;
using XnbConverter.StardewValley.Readers.SpecialOrder;
using XnbConverter.Utilities;
using XnbConverter.Xact;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Readers;

public static class TypeReader
{
    public record struct Ext
    {
        public const string DEF         = ".bin";
        public const string JSON        = ".json";
        public const string TEXTURE_2D  = ".png";
        public const string EFFECT      = ".cso";
        public const string TBIN        = ".tbin";
        public const string BM_FONT     = ".xml";
        public const string SPRITE_FONT = ".json .png";
        public const string SoundEffect = ".json .wav";
    }
    public struct Types
    {
        public readonly string FullNameStr;
        public readonly Type ReaderType;
        public readonly string? Extension;
        public Types(string fullNameStr, string extension, Type readerType)
        {
            FullNameStr = fullNameStr;
            Extension = extension;
            ReaderType = readerType;
        }
    }

    public static readonly Types[] W = {
        /*  2  */new("DictionaryReader`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[StardewValley.GameData.Movies.MovieData"               , Ext.JSON     , typeof(DictionaryReader<StringReader , ReflectiveReader<MovieDataReader>, string, MovieData>)                ),
        /*  1  */new("DictionaryReader`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[StardewValley.GameData.SpecialOrderData"               , Ext.JSON     , typeof(DictionaryReader<StringReader , ReflectiveReader<SpecialOrderDataReader>, string, SpecialOrderData>)  ),
        /*  1  */new("DictionaryReader`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[StardewValley.GameData.HomeRenovations.HomeRenovation" , Ext.JSON     , typeof(DictionaryReader<StringReader , ReflectiveReader<HomeRenovationReader>, string, HomeRenovation>)      ),
        /*  1  */new("DictionaryReader`2[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Collections.Generic.List`1[[System.String"       , Ext.JSON     , typeof(DictionaryReader<Int32Reader  , ListReader<StringReader, string>, int, List<string>>)),
        
        /* 630 */new("Texture2DReader"                                , Ext.TEXTURE_2D  , typeof( Texture2DReader           )),
        /* 415 */new("StringReader"                                   , Ext.DEF        , typeof( StringReader )),
        /* 374 */new("DictionaryReader`2[[System.String"              , Ext.JSON       , typeof( DictionaryReader<StringReader, StringReader, string, string> )),
        /* 212 */new("xTile.Pipeline.TideReader"                      , Ext.TBIN       , typeof( TBinReader                )),
        /* 26  */new("DictionaryReader`2[[System.Int32"               , Ext.JSON       , typeof( DictionaryReader<Int32Reader , StringReader, int, string> )),
        /* 36  */new("Content.Int32Reader"                            , Ext.DEF        , typeof( Int32Reader               )),
        /* 11  */new("ListReader`1[[System.String"                    , Ext.JSON       , typeof( ListReader<StringReader, string> )),
        
        /* 11  */new("Content.SoundEffectReader"                    , Ext.SoundEffect       , typeof( SoundEffectReader)),

        /*  6  */new("Content.SpriteFontReader"                       , Ext.SPRITE_FONT , typeof( SpriteFontReader                   )),
        /*  6  */new("ListReader`1[[Microsoft.Xna.Framework.Rectangle", Ext.DEF        , typeof( ListReader<RectangleReader, Rect>  )),
        /*  6  */new("ListReader`1[[System.Char"                      , Ext.DEF        , typeof( ListReader<CharReader, char>       )),
        /*  6  */new("ListReader`1[[Microsoft.Xna.Framework.Vector3"  , Ext.DEF        , typeof( ListReader<Vector3Reader, Vector3> )),
        
        /*  6  */new("Content.RectangleReader"                        , Ext.DEF        , typeof( RectangleReader )),
        /*  6  */new("Content.Vector3Reader"                          , Ext.DEF        , typeof( Vector3Reader   )),
        /*  6  */new("Content.BooleanReader"                          , Ext.DEF        , typeof( BooleanReader   )),
        /*  6  */new("Content.CharReader"                             , Ext.DEF        , typeof( CharReader      )),
        /*  4  */new("BmFont.XmlSourceReader"                         , Ext.BM_FONT     , typeof( BmFontReader    )),
        /*  3  */new("Content.EffectReader"                           , Ext.EFFECT     , typeof( EffectReader    )),
        /*  2  */new("Content.SingleReader"                           , Ext.DEF        , typeof( SingleReader    )),

        /*  2  */new("ListReader`1[[StardewValley.GameData.Movies.ConcessionItemData"      , Ext.JSON, typeof( ListReader<ReflectiveReader<ConcessionItemDataReader>, ConcessionItemData> )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.ConcessionItemData", Ext.DEF , typeof( ReflectiveReader<ConcessionItemDataReader> )),
        
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.MovieData" , Ext.DEF , typeof( ReflectiveReader<MovieDataReader>  )),
        /*  2  */new("ListReader`1[[StardewValley.GameData.Movies.MovieScene"      , Ext.JSON, typeof( ListReader<ReflectiveReader<MovieSceneReader>, MovieScene> )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.MovieScene", Ext.DEF , typeof( ReflectiveReader<MovieSceneReader> )),
        
        /*  2  */new("ListReader`1[[StardewValley.GameData.Movies.MovieCharacterReaction"      , Ext.JSON, typeof( ListReader<ReflectiveReader<MovieCharacterReactionReader>, MovieCharacterReaction> )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.MovieCharacterReaction", Ext.DEF , typeof( ReflectiveReader<MovieCharacterReactionReader>                   )),
        /*  2  */new("ListReader`1[[StardewValley.GameData.Movies.MovieReaction"               , Ext.JSON, typeof( ListReader<ReflectiveReader<MovieReactionReader>, MovieReaction> )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.MovieReaction"         , Ext.DEF , typeof( ReflectiveReader<MovieReactionReader>                            )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.SpecialResponses"      , Ext.DEF , typeof( ReflectiveReader<SpecialResponsesReader>                         )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.CharacterResponse"     , Ext.DEF , typeof( ReflectiveReader<CharacterResponseReader>                        )),
        
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.SpecialOrderData"         , Ext.DEF , typeof( ReflectiveReader<SpecialOrderDataReader>          )),
        /*  2  */new("ListReader`1[[StardewValley.GameData.RandomizedElement,"              , Ext.JSON, typeof( ListReader<ReflectiveReader<RandomizedElementReader>, RandomizedElement>                 )),
        /*  2  */new("ReflectiveReader`1[[StardewValley.GameData.RandomizedElement,"        , Ext.DEF , typeof( ReflectiveReader<RandomizedElementReader>         )),
        /*  0  */new("ListReader`1[[StardewValley.GameData.RandomizedElementItem"          , Ext.JSON, typeof( ListReader<ReflectiveReader<RandomizedElementItemReader>, RandomizedElementItem>         )),
        /*  0  */new("ReflectiveReader`1[[StardewValley.GameData.RandomizedElementItem"    , Ext.DEF , typeof( ReflectiveReader<RandomizedElementItemReader>     )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.SpecialOrderObjectiveData"      , Ext.JSON, typeof( ListReader<ReflectiveReader<SpecialOrderObjectiveDataReader>, SpecialOrderObjectiveData> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.SpecialOrderObjectiveData", Ext.DEF , typeof( ReflectiveReader<SpecialOrderObjectiveDataReader> )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.SpecialOrderRewardData"         , Ext.JSON, typeof( ListReader<ReflectiveReader<SpecialOrderRewardDataReader>, SpecialOrderRewardData>       )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.SpecialOrderRewardData"   , Ext.DEF , typeof( ReflectiveReader<SpecialOrderRewardDataReader>    )),
        
        /*  1  */new("ListReader`1[[StardewValley.GameData.Crafting.TailorItemRecipe"      , Ext.JSON, typeof( ListReader<ReflectiveReader<TailorItemRecipeReader>, TailorItemRecipe> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.Crafting.TailorItemRecipe", Ext.DEF , typeof( ReflectiveReader<TailorItemRecipeReader>             )),
                
        /*  1  */new("ListReader`1[[StardewValley.GameData.ModFarmType"                    , Ext.JSON, typeof( ListReader<ReflectiveReader<ModFarmTypeReader>, ModFarmType> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.ModFarmType"              , Ext.DEF , typeof( ReflectiveReader<ModFarmTypeReader> )),
                      
        /*  1  */new("ListReader`1[[StardewValley.GameData.ModLanguage"                    , Ext.JSON, typeof( ListReader<ReflectiveReader<ModLanguageReader>, ModLanguage> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.ModLanguage"              , Ext.DEF , typeof( ReflectiveReader<ModLanguageReader> )),
                 
        /*  1  */new("ListReader`1[[StardewValley.GameData.ModWallpaperOrFlooring"         , Ext.JSON, typeof( ListReader<ReflectiveReader<ModWallpaperOrFlooringReader>, ModWallpaperOrFlooring> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.ModWallpaperOrFlooring"   , Ext.DEF , typeof( ReflectiveReader<ModWallpaperOrFlooringReader> )),
           
        /*  1  */new("ListReader`1[[StardewValley.GameData.Movies.ConcessionTaste"         , Ext.JSON, typeof( ListReader<ReflectiveReader<ConcessionTasteReader>, ConcessionTaste> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.Movies.ConcessionTaste"   , Ext.DEF , typeof( ReflectiveReader<ConcessionTasteReader> )),
   
        /*  1  */new("ListReader`1[[StardewValley.GameData.FishPond.FishPondData"          , Ext.JSON, typeof( ListReader<ReflectiveReader<FishPondDataReader>, FishPondData>     )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.FishPond.FishPondData"    , Ext.DEF , typeof( ReflectiveReader<FishPondDataReader>    )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.FishPond.FishPondReward"        , Ext.JSON, typeof( ListReader<ReflectiveReader<FishPondRewardReader>, FishPondReward> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.FishPond.FishPondReward"  , Ext.DEF , typeof( ReflectiveReader<FishPondRewardReader>  )),
        
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.HomeRenovations.HomeRenovation" , Ext.DEF , typeof( ReflectiveReader<HomeRenovationReader> )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.HomeRenovations.RenovationValue"      , Ext.JSON, typeof( ListReader<ReflectiveReader<RenovationValueReader>, RenovationValue>)),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.HomeRenovations.RenovationValue", Ext.DEF , typeof( ReflectiveReader<RenovationValueReader> )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.HomeRenovations.RectGroup"            , Ext.JSON, typeof( ListReader<ReflectiveReader<RectGroupReader>, RectGroup> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.HomeRenovations.RectGroup"      , Ext.DEF , typeof( ReflectiveReader<RectGroupReader>       )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.HomeRenovations.Rect"                 , Ext.JSON, typeof( ListReader<ReflectiveReader<RectReader>, Rect>           )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.HomeRenovations.Rect"           , Ext.DEF , typeof( ReflectiveReader<RectReader>            )),
        
        /*  1  */new("ListReader`1[[StardewValley.GameData.RandomBundleData"      , Ext.JSON, typeof( ListReader<ReflectiveReader<RandomBundleDataReader>, RandomBundleData> )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.RandomBundleData", Ext.DEF , typeof( ReflectiveReader<RandomBundleDataReader> )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.BundleSetData"         , Ext.JSON, typeof( ListReader<ReflectiveReader<BundleSetDataReader>, BundleSetData>       )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.BundleSetData"   , Ext.DEF , typeof( ReflectiveReader<BundleSetDataReader>    )),
        /*  1  */new("ListReader`1[[StardewValley.GameData.BundleData"            , Ext.JSON, typeof( ListReader<ReflectiveReader<BundleDataReader>, BundleData>             )),
        /*  1  */new("ReflectiveReader`1[[StardewValley.GameData.BundleData"      , Ext.DEF , typeof( ReflectiveReader<BundleDataReader>       )),
        
        /*  0  */new("Graphics.Effect"      , Ext.EFFECT, typeof( EffectReader    )),
        /*  0  */new("Framework.Rectangle"  , Ext.DEF   , typeof( RectangleReader )),
        /*  0  */new("Content.Vector2Reader", Ext.DEF   , typeof( Vector2Reader   )),
        /*  0  */new("Framework.Vector2"    , Ext.DEF   , typeof( Vector2Reader   )),
        /*  0  */new("Framework.Vector3"    , Ext.DEF   , typeof( Vector3Reader   )),
        /*  0  */new("Content.Vector4Reader", Ext.DEF   , typeof( Vector4Reader   )),
        /*  0  */new("Framework.Vector4"    , Ext.DEF   , typeof( Vector4Reader   )),
        /*  0  */new("Content.ArrayReader"  , Ext.DEF   , typeof( ArrayReader<>   ))
    };

    private static readonly string[] FullNames = W.Select(y => y.FullNameStr).ToArray();
    
    public static int[] MA = new int[W.Length];
    public static BaseReader GetReader(string fullName)
    {
        ReadOnlySpan<char> span = fullName.AsSpan();
        Span<string> arr = FullNames.AsSpan();
        for (int i = 0,j = 0; i < arr.Length; ++i,++j)
        {
            if(span.IndexOf(arr[i]) < 0) continue;
            MA[j]++;
            return (BaseReader)Activator.CreateInstance(W[i].ReaderType)
                   ?? throw new XnbError(Helpers.TextTip.UnrealizedTypes, fullName);
        }
        throw new XnbError(Helpers.TextTip.UnrealizedTypes, fullName);
    }
    
    public static Types GetTypes(BaseReader baseReader)
    {
        var type = baseReader.GetType();
        foreach (var v in W)
        {
            if (v.ReaderType == type)
                return v;
        }
        throw new XnbError(Helpers.TextTip.InvalidReaderType, baseReader.GetType().FullName);
    }

    public static void Print()
    {
        int i = -1;
        foreach (var v in W)
        {
            Console.WriteLine(MA[++i]+ "     " + v.ReaderType );
        }
    }
}