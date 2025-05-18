using Android.Views;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Components;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.component_json_editor)]
public class ComponentJsonEditorHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_json_editor)] public JsonEditor Editor;

    protected override void Init()
    {
    }

    public void Bind(string json)
    {
        if (Editor.IsReady)
        {
            Editor.Json = json;
        }
        else
        {
            Editor.AfterInitialLoad += (sender, b) =>
            {
                Editor.Json = json;
            };
        }
    }
}