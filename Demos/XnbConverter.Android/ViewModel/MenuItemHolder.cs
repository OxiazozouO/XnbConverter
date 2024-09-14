using Android.Views;

namespace XnbConverter.Android.ViewModel;

public class MenuItemHolder(View root) : Java.Lang.Object
{
    public ImageView Icon = root.FindViewById<ImageView>(Resource.Id.menu_item_icon);
    public TextView Text = root.FindViewById<TextView>(Resource.Id.menu_item_text);
}