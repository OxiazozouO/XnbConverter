using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.item_layout_msg_edit)]
public class ItemLayoutMsgEditHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_msg_edit_text)] public TextView MsgText;
    [ViewBind(Id.id_msg_edit)] public EditText MsgEdit;

    protected override void Init()
    {
    }
}