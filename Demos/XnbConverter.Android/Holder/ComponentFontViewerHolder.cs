using Android.Views;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Components;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.component_font_viewer)]
public class ComponentFontViewerHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_font_viewer)] public FontViewer Viewer;

    protected override void Init()
    {
    }

    public void Bind(List<FontInfo> paths)
    {
        Viewer.Fonts = paths;
    }
}