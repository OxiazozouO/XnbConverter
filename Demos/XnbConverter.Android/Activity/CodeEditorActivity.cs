using XnbConverter.Android.Components;
using XnbConverter.Android.Helper;
using XnbConverter.Android.Holder;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Activity;

[Activity]
public class CodeEditorActivity : global::Android.App.Activity
{
    private ComponentCodeEditorHolder _holder;
    private string code;
    private string path;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try
        {
            if (Intent?.Extras is not { } b) goto ret;
            path = b.GetString("path");
            if (string.IsNullOrEmpty(path)) goto ret;
            var info = new FileInfo(path);
            code = File.ReadAllText(path);
            _holder = new ComponentCodeEditorHolder(this);
            SetContentView(_holder.Root);
            if (info.Extension == ".xml")
            {
                _holder.Bind(code, CodeMode.Xml);
            }
            else if (info.Extension == ".js")
            {
                _holder.Bind(code, CodeMode.Js);
            }
            else if (info.Extension == ".java")
            {
                _holder.Bind(code, CodeMode.Java);
            }
            else
            {
                _holder.Bind(code, CodeMode.Text);
            }

            _holder.Editor.TextChanged += TextChanged;
            return;
        }
        catch (Exception)
        {
            // ignored
        }

        ret:
        Finish();
    }

    private bool IsBackPressed = false;

    public override void OnBackPressed()
    {
        IsBackPressed = true;
        var newCode = _holder.Editor.Code;
    }

    private void TextChanged(object? sender, string e)
    {
        if (IsBackPressed)
        {
            IsBackPressed = false;
            if (code == e)
            {
                base.OnBackPressed();
            }
            else
            {
                MsgBoxHelper
                    .Builder(this, "是否保存？")
                    .OkCancel(() =>
                    {
                        try
                        {
                            File.WriteAllText(path, e);
                            base.OnBackPressed();
                        }
                        catch (Exception e)
                        {
                            MsgBoxHelper.Error(this, e.Message);
                        }
                    }, base.OnBackPressed);
            }
        }
    }
}