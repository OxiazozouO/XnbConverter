using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.msg_main)]
public class LayoutMsgHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_msg_main)] public LinearLayout MainMsg;

    public int SelectedItem = 0;

    private List<View> views;

    protected override void Init()
    {
    }

    public void Bind(List<MsgItem> items)
    {
        views = [];
        SelectedItem = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var holder = new ItemLayoutMsgHolder(activity);
            holder.Bind(items[i]);
            holder.Root.Tag = i;
            MainMsg.AddView(holder.Root);
            views.Add(holder.Root);
            holder.Root.Click += Fun;
        }

        return;

        void Fun(object? sender, EventArgs e)
        {
            if (sender is not View text) return;
            views[SelectedItem].SetBackgroundResource(Drawable.shape_label7_bg);
            SelectedItem = (int)text.Tag;
            views[SelectedItem].SetBackgroundResource(Drawable.shape_label8_bg);
        }
    }

    public void CallClick(int pos = 0)
    {
        views?[pos]?.CallOnClick();
    }
}