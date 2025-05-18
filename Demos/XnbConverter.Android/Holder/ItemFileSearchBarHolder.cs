using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.item_file_search_bar)]
public class ItemFileSearchBarHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_file_search_bar_right_arrow)]
    public TextView Arrow;

    [ViewBind(Id.id_file_search_bar_text)] public TextView Text;

    protected override void Init()
    {
    }

    public void Bind(string name)
    {
        if (name == "")
        {
            Arrow.Visibility = ViewStates.Gone;
        }
        else
        {
            Text.Text = name;
        }
    }
}