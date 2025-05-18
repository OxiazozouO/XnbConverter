using Android.Content;
using Android.Util;
using Android.Webkit;
using XnbConverter.Utilities;
using Exception = System.Exception;

namespace XnbConverter.Android.Components;

public class FontInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Base64 { get; set; }
}

public class FontViewer : MyWebView
{
    protected override void Init(string setupName = "", string jsName = "")
    {
        VerticalScrollBarEnabled = false;
        HorizontalScrollBarEnabled = false;
        Settings.JavaScriptEnabled = true;
        Settings.CacheMode = CacheModes.CacheElseNetwork;

        Focusable = true;
        FocusableInTouchMode = true;
        base.Init("font_viewer", "FV");
    }

    public List<FontInfo> Fonts
    {
        set
        {
            try
            {
                Exec($"LoadFontsFromBase64('{value.ToJson()}')");
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    public FontViewer(Context context, IAttributeSet? attrs, int defStyleAttr, bool privateBrowsing) : base(context,
        attrs, defStyleAttr, privateBrowsing)
    {
    }

    public FontViewer(Context context, IAttributeSet? attrs, int defStyleAttr, int defStyleRes) : base(context, attrs,
        defStyleAttr, defStyleRes)
    {
    }

    public FontViewer(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
    }

    public FontViewer(Context context, IAttributeSet? attrs) : base(context, attrs)
    {
    }

    public FontViewer(Context context) : base(context)
    {
    }
}