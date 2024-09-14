using AndroidX.ViewPager.Widget;

namespace XnbConverter.Android.ViewModel;

public class ImageViewerPageHolder(FrameLayout root)
{
    public ViewPager ViewPager = root.FindViewById<ViewPager>(Resource.Id.id_page_viewpager);
    public LinearLayout ToolBar = root.FindViewById<LinearLayout>(Resource.Id.id_image_viewer_toolbar);
    public ImageView Back = root.FindViewById<ImageView>(Resource.Id.id_image_viewer_back);
    public TextView Title = root.FindViewById<TextView>(Resource.Id.id_image_page_title);
    public TextView Count = root.FindViewById<TextView>(Resource.Id.id_image_page_count);
    public TextView Desc = root.FindViewById<TextView>(Resource.Id.id_image_page_desc);
    public ImageView More = root.FindViewById<ImageView>(Resource.Id.id_image_viewer_more);
}