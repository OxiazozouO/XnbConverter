using AndroidX.ViewPager.Widget;
using XnbConverter.Android.Activities;

namespace XnbConverter.Android.ViewModel;

public class ActivityImageViewerHolder(ImageViewerActivity root)
{
    public RelativeLayout MainLayout = root.FindViewById<RelativeLayout>(Resource.Id.id_page);
    public ViewPager ViewPager = root.FindViewById<ViewPager>(Resource.Id.id_page_viewpager);
}