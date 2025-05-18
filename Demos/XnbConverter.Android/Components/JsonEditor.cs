using Android.Content;
using Android.Util;
using Android.Webkit;

namespace XnbConverter.Android.Components;

public class JsonEditor : MyWebView
{
    protected override void Init(string setupName = "", string jsName = "")
    {
        VerticalScrollBarEnabled = false;
        HorizontalScrollBarEnabled = false;
        Settings.JavaScriptEnabled = true;
        Settings.CacheMode = CacheModes.CacheElseNetwork;

        Focusable = true;
        FocusableInTouchMode = true;
        base.Init("json_editor", "JE");
    }

    public string Json
    {
        get
        {
            Exec("Text()");

            _contents ??= "";

            return _contents;
        }
        set
        {
            try
            {
                Exec($"Init(`{value.Replace(@"\\", @"\\\\")}`)");
            }
            catch (Exception)
            {
                // ignored
            }

            _contents = value;
        }
    }

    public JsonEditor(Context context, IAttributeSet? attrs, int defStyleAttr, bool privateBrowsing) : base(context,
        attrs, defStyleAttr, privateBrowsing)
    {
    }

    public JsonEditor(Context context, IAttributeSet? attrs, int defStyleAttr, int defStyleRes) : base(context, attrs,
        defStyleAttr, defStyleRes)
    {
    }

    public JsonEditor(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
    }

    public JsonEditor(Context context, IAttributeSet? attrs) : base(context, attrs)
    {
    }

    public JsonEditor(Context context) : base(context)
    {
    }
}