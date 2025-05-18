using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.module_file_view_one)]
public class ModuleFileViewOneHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewHolderBind(Id.id_one_view)] public ModuleFileViewHolder OneViewHolder;

    protected override void Init()
    {
        OneViewHolder.InitActivity(activity);
    }

    public void Bind(string uri)
    {
        OneViewHolder.Bind(uri);
    }
}