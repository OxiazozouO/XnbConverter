using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.module_main_bottom_menu)]
public class ModuleMainBottomMenuHolder(View view) : ViewHolder<View>(view)
{
    [ViewBind(Id.id_main_bottom_menu_shear)]
    public ImageButton Shear;

    [ViewBind(Id.id_bottom_menu_copy)] public ImageButton Copy;
    [ViewBind(Id.id_bottom_menu_del)] public ImageButton Del;
    [ViewBind(Id.id_bottom_menu_info)] public ImageButton Info;
    [ViewBind(Id.id_bottom_menu_more)] public ImageButton More;

    protected override void Init()
    {
    }
}