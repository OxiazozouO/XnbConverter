using XnbConverter.Android.Components;
using XnbConverter.Android.Holder;
using XnbConverter.Utilities;

namespace XnbConverter.Android.Activity;

[Activity]
public class FontViewerActivity : global::Android.App.Activity
{
    private ComponentFontViewerHolder _holder;
    private List<string> paths;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try
        {
            if (Intent?.Extras is not { } b) goto ret;
            if (b.GetString("paths") is not { } path) goto ret;
            paths = path.ToEntity<List<string>>();
            var pp = paths.Select(f => new Model.FileInfo(f))
                .Select(f =>
                    new FontInfo
                    {
                        Name = f.FileName,
                        Type = f.Extension[1..],
                        Base64 = Convert.ToBase64String(File.ReadAllBytes(f.Uri))
                    }).ToList();
            _holder = new ComponentFontViewerHolder(this);
            SetContentView(_holder.Root);
            _holder.Bind(pp);
            return;
        }
        catch (Exception)
        {
            // ignored
        }

        ret:
        Finish();
    }
}