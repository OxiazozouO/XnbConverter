using Android.Views;
using XnbConverter.Android.Holders;

namespace XnbConverter.Android.ViewModel;

public class FileViewItemHolder(View root) : Java.Lang.Object
{
    public readonly View Root = root;
    public LinearLayout MainLayout = root.FindViewById<LinearLayout>(Resource.Id.id_item_layout);
    public readonly ImageView Icon = root.FindViewById<ImageView>(Resource.Id.id_item_icon);
    public readonly TextView Title = root.FindViewById<TextView>(Resource.Id.id_item_title);
    public LinearLayout DateAndSize = root.FindViewById<LinearLayout>(Resource.Id.id_item_date_and_size);
    public readonly TextView FileUpdateTime = root.FindViewById<TextView>(Resource.Id.id_item_date);
    public readonly TextView FileSize = root.FindViewById<TextView>(Resource.Id.id_item_size);
    public readonly CheckBox CheckBox = root.FindViewById<CheckBox>(Resource.Id.id_item_checkbox);

    public void UpdateConfig(FileViewItemConfig config)
    {
        MainLayout.Orientation = config.orientation;

        var p = MainLayout.LayoutParameters;
        p.Width = config.layoutWidth;
        MainLayout.LayoutParameters = p;

        p = Icon.LayoutParameters;
        p.Width = config.IconWAndH;
        p.Height = config.IconWAndH;
        Icon.LayoutParameters = p;

        // CheckBox.Visibility = config.Visibility;
    }
}