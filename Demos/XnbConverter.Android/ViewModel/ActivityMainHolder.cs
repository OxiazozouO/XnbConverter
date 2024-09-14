using XnbConverter.Android.Holders;

namespace XnbConverter.Android.ViewModel;

public class ActivityMainHolder(MainActivity root)
{
    public LinearLayout left = root.FindViewById<LinearLayout>(Resource.Id.id_left);
    public LinearLayout right = root.FindViewById<LinearLayout>(Resource.Id.id_right);

    public HorizontalScrollView FileViewList = root.FindViewById<HorizontalScrollView>(Resource.Id.id_gallery);


    public FileViewHelper _leftHelper;

    public FileViewHelper LeftHelper => _leftHelper ??= new FileViewHelper(left);

    public FileViewHelper _rigthHelper;

    public FileViewHelper RightHelper => _rigthHelper ??= new FileViewHelper(right);


    public LinearLayout FileSearchBar = root.FindViewById<LinearLayout>(Resource.Id.id_file_search_bar);
    public ImageButton FilePathSearchBtn = root.FindViewById<ImageButton>(Resource.Id.id_file_path_search_btn);


    public LinearLayout BottomMenuMain = root.FindViewById<LinearLayout>(Resource.Id.id_bottom_menu_main);

    public LinearLayout MoreMenu = root.FindViewById<LinearLayout>(Resource.Id.id_more_menu);
    public LinearLayout MoreMenuList = root.FindViewById<LinearLayout>(Resource.Id.id_more_menu_list);
    public ImageButton MoreMenuBnt = root.FindViewById<ImageButton>(Resource.Id.id_more_menu_bnt);

    public LinearLayout bottonMenuSelect = root.FindViewById<LinearLayout>(Resource.Id.id_botton_menu_select);
    public FrameLayout ImageViewerPage = root.FindViewById<FrameLayout>(Resource.Id.id_image_viewer_page);
}