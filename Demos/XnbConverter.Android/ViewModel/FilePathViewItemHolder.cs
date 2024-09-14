using Android.Views;

namespace XnbConverter.Android.ViewModel;

public class FilePathViewItemHolder(View root): Java.Lang.Object
{
    public readonly TextView FileSearchBarRightArrow = root.FindViewById<TextView>(Resource.Id.id_file_search_bar_right_arrow);

    public readonly TextView FileSearchBarText = root.FindViewById<TextView>(Resource.Id.id_file_search_bar_text);
}