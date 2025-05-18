using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Webkit;
using Java.Lang;

namespace XnbConverter.Android.Components;

public abstract class MyWebView : WebView
{
    public EventHandler<bool>? AfterInitialLoad;
    public EventHandler<string>? TextChanged;
    protected string _contents;
    public string _setupHtml;
    public static readonly string CallbackScheme = "callback://";
    private string _jsName;

    protected bool _isReady = false;

    public bool IsReady
    {
        get => _isReady;
        set
        {
            _isReady = value;
            if (value)
            {
                AfterInitialLoad?.Invoke(this, value);
            }
        }
    }

    public virtual void Callback(string text)
    {
        _contents = text
            .Replace(CallbackScheme, "", StringComparison.CurrentCultureIgnoreCase)
            .Replace(@"\\\\", @"\\");
        TextChanged?.Invoke(null, _contents);
    }


    protected virtual void Init(string setupName = "", string jsName = "")
    {
        _setupHtml = $"file:///android_asset/{setupName}.html";
        _jsName = jsName;
        SetWebChromeClient(new WebChromeClient());
        SetWebViewClient(new MyWebViewClient());
        LoadUrl(_setupHtml);
    }

    public MyWebView(Context context, IAttributeSet? attrs, int defStyleAttr, bool privateBrowsing) : base(context,
        attrs, defStyleAttr, privateBrowsing)
    {
    }

    public MyWebView(Context context, IAttributeSet? attrs, int defStyleAttr, int defStyleRes) : base(context, attrs,
        defStyleAttr, defStyleRes)
    {
    }

    public MyWebView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        Init();
        ApplyAttributes(context, attrs);
    }

    public MyWebView(Context context, IAttributeSet? attrs) : this(context, attrs, Resource.Attribute.badgeStyle)
    {
    }

    private MyWebView(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public MyWebView(Context context) : this(context, null)
    {
    }

    protected void ApplyAttributes(Context context, IAttributeSet? attrs)
    {
        context
            .ObtainStyledAttributes(attrs, [Resource.Attribute.buttonGravity])
            .Recycle();
    }

    protected void Exec(string trigger)
    {
        trigger = $"javascript:{_jsName}.{trigger};";
        if (_isReady)
        {
            Load(trigger);
        }
        else
        {
            PostDelayed(new Runnable(() => { Exec(trigger); }), 100);
        }
    }

    [Obsolete("Obsolete")]
    private void Load(string trigger)
    {
        if (Build.VERSION.SdkInt >= Build.VERSION_CODES.Kitkat)
        {
            EvaluateJavascript(trigger, null);
        }
        else
        {
            LoadUrl(trigger);
        }
    }
}

public class MyWebViewClient : WebViewClient
{
    public override void OnPageFinished(WebView? view, string? url)
    {
        if (view is not MyWebView w) return;
        bool b = string.Equals(url, w._setupHtml, StringComparison.OrdinalIgnoreCase);
        w.IsReady = b;
    }

    public override bool ShouldOverrideUrlLoading(WebView? view, string? url)
    {
        if (view is not MyWebView w) return false;
        string unescapeDataString = Uri.UnescapeDataString(url);

        if (url.StartsWith(MyWebView.CallbackScheme, StringComparison.Ordinal))
        {
            w.Callback(unescapeDataString);
            return true;
        }

        return true;
    }

    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        if (view is not MyWebView w) return false;
        string url = request.Url.ToString();
        string unescapeDataString = Uri.UnescapeDataString(url);

        if (url.StartsWith(MyWebView.CallbackScheme, StringComparison.Ordinal))
        {
            w.Callback(unescapeDataString);
            return true;
        }

        return true;
    }
}