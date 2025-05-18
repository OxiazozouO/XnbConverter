using XnbConverter.Android.Helper;
using XnbConverter.Android.Holder;

namespace XnbConverter.Android.Activity;

[Activity]
public class JsonEditorActivity : global::Android.App.Activity
{
    private ComponentJsonEditorHolder _holder;
    private string json;
    private string path;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try
        {
            if (Intent?.Extras is not { } b) goto ret;
            path = b.GetString("path");
            if (string.IsNullOrEmpty(path)) goto ret;
            json = File.ReadAllText(path);
            _holder = new ComponentJsonEditorHolder(this);
            SetContentView(_holder.Root);
            _holder.Bind(json);

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
        var newJson = _holder.Editor.Json;
    }

    private void TextChanged(object? sender, string e)
    {
        if (IsBackPressed)
        {
            IsBackPressed = false;
            if (json == e)
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