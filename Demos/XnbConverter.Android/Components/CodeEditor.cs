using Android.Content;
using Android.Util;
using Android.Webkit;
using Exception = System.Exception;

namespace XnbConverter.Android.Components;

public class CodeEditor : MyWebView
{
    protected override void Init(string setupName = "", string jsName = "")
    {
        VerticalScrollBarEnabled = false;
        HorizontalScrollBarEnabled = false;
        Settings.JavaScriptEnabled = true;
        Settings.CacheMode = CacheModes.CacheElseNetwork;

        Focusable = true;
        FocusableInTouchMode = true;
        base.Init("code_editor", "CE");
    }

    public string Code
    {
        get
        {
            Exec($"GetCode()");
            _contents ??= "";
            return _contents;
        }
        set
        {
            try
            {
                Exec($"SetCode(`{value.Replace(@"\\", @"\\\\")}`)");
            }
            catch (Exception)
            {
                // ignored
            }

            _contents = value;
        }
    }

    public CodeMode Mode
    {
        set
        {
            string mode = "application/text";
            switch (value)
            {
                case CodeMode.Xml:
                    mode = "application/xml";
                    break;
                case CodeMode.Js:
                    mode = "text/x-javascript";
                    break;
                case CodeMode.Java:
                    mode = "text/x-java";
                    break;
            }

            Exec($"SetMode('{mode}')");
        }
    }

    public void Undo() => Exec("Undo()");
    public void Redo() => Exec("Redo()");


    public CodeEditor(Context context, IAttributeSet? attrs, int defStyleAttr, bool privateBrowsing) : base(context,
        attrs, defStyleAttr, privateBrowsing)
    {
    }

    public CodeEditor(Context context, IAttributeSet? attrs, int defStyleAttr, int defStyleRes) : base(context, attrs,
        defStyleAttr, defStyleRes)
    {
    }

    public CodeEditor(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
    }

    public CodeEditor(Context context, IAttributeSet? attrs) : base(context, attrs)
    {
    }

    public CodeEditor(Context context) : base(context)
    {
    }
}

public enum CodeMode
{
    Text,
    Xml,
    Js,
    Java
}