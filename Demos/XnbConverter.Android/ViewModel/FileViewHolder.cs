using Android.Views;

namespace XnbConverter.Android.ViewModel;

public class FileViewHolder(View root)
{
    public readonly View Root = root;
    public readonly TextView FileTitle = root.FindViewById<TextView>(Resource.Id.id_file_title);
    public readonly ImageButton ListMenu = root.FindViewById<ImageButton>(Resource.Id.id_list_menu);
    public readonly ImageButton WinMenu = root.FindViewById<ImageButton>(Resource.Id.id_win_menu);
    public readonly ListView FileList = root.FindViewById<ListView>(Resource.Id.id_file_view);
    
    public readonly ProgressBar Bar = root.FindViewById<ProgressBar>(Resource.Id.id_refresh_bar);
    public readonly LinearLayout L1 = root.FindViewById<LinearLayout>(Resource.Id.id_refresh_layout);
}