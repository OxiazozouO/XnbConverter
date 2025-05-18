using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.module_file_view_two)]
public class ModuleFileViewTwoHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewHolderBind(Id.id_two_l)]
    public ModuleFileViewHolder LHolder;
    [ViewHolderBind(Id.id_two_r)]
    public ModuleFileViewHolder RHolder;

    protected override void Init()
    {
        LHolder.InitActivity(activity);
        RHolder.InitActivity(activity);
    }

    public void Bind(string luri, string ruri)
    {
        LHolder.Bind(luri);
        RHolder.Bind(ruri);
    }
}