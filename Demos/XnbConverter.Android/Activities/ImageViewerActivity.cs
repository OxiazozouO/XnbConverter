using XnbConverter.Android.Adapter;
using XnbConverter.Android.Helper;
using XnbConverter.Android.Model;
using XnbConverter.Android.ViewModel;

namespace XnbConverter.Android.Activities;

public class ImageViewerActivity : Activity
{
    private ActivityImageViewerHolder holder;


    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_image_viewer);
        holder = new ActivityImageViewerHolder(this);
        if (Intent.GetSerializableExtra("files") is ImageViewerModel model)
        {
            holder.ViewPager.Adapter = new BannerAdapter(this, model.Files);
            //Pager之间的间距
            holder.ViewPager.PageMargin = 20;

            //预加载
            holder.ViewPager.OffscreenPageLimit = 3;

            //默认图
            holder.ViewPager.CurrentItem = model.CurrentIndex;
        }
    }
}