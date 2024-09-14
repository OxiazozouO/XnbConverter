using Android.Views;

namespace XnbConverter.Android.Holders;

public class FileViewItemConfig
{
    public int IconWAndH { get; set; }
    public int TitleFontSize { get; set; }
    public int DateAndSizeFontSize { get; set; }
    public Orientation orientation { get; set; }
    public int layoutWidth = ViewGroup.LayoutParams.MatchParent;

    public static FileViewItemConfig DefaultConfig => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels / 8),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal,
        layoutWidth = ViewGroup.LayoutParams.MatchParent
    };

    public static FileViewItemConfig Config2 => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels * 0.9 / 6),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal,
        layoutWidth = ViewGroup.LayoutParams.MatchParent
    };

    public static FileViewItemConfig Config21 => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels * 0.9 / 5),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal,
        layoutWidth = ViewGroup.LayoutParams.MatchParent
    };

    public static FileViewItemConfig Config3 => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels * 0.9 / 4),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical,
        layoutWidth = ViewGroup.LayoutParams.WrapContent
    };

    public static FileViewItemConfig Config31 => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels * 0.9 / 3),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal,
        layoutWidth = ViewGroup.LayoutParams.WrapContent
    };public static FileViewItemConfig Config4 => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels * 0.9 / 2),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical,
        layoutWidth = ViewGroup.LayoutParams.WrapContent
    };

    public static FileViewItemConfig Config41 => new FileViewItemConfig
    {
        IconWAndH = (int)(MainActivity.WidthPixels * 0.9 / 1),
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical,
        layoutWidth = ViewGroup.LayoutParams.WrapContent
    };
}