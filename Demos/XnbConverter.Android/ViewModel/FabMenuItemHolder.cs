using Android.Views;

namespace XnbConverter.Android.ViewModel;

public class FabMenuItemHolder(View root)
{
    public TextView TextView = root.FindViewById<TextView>(Resource.Id.fab_menu_item_text);
    public ImageView ImageView = root.FindViewById<ImageView>(Resource.Id.fab_menu_item_icon);
}