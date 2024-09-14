using Android.Content;
using Android.Views;
using AndroidX.ViewPager.Widget;
using Bumptech.Glide;
using XnbConverter.Android.Components;
using Object = Java.Lang.Object;

namespace XnbConverter.Android.Adapter;

public class BannerAdapter(Context context, List<Java.IO.File> files) : PagerAdapter
{
    private List<Java.IO.File> _files = files;


    public override int Count => _files.Count;

    public override Object InstantiateItem(ViewGroup container, int position)
    {
        ZoomImageView v = new ZoomImageView(context);
        v.UpdateAction = () =>
        {
            int pos = position;
            Glide.With(context).Load(_files[pos]).Into(v);
        };

        v.Click += (_, _) => { OnItemClick?.Invoke(this, v); };
        v.ScaleBegin += (_, _) => { OnItemScaleBegin?.Invoke(this, v); };
        v.Id = position;
        container.AddView(v); //添加到父控件
        return v;
    }


    public override bool IsViewFromObject(View view, Object @object)
    {
        return view == @object;
    }

    public override void DestroyItem(ViewGroup container, int position, Object @object)
    {
        container.RemoveView((View)@object);
    }

    public EventHandler<View>? OnItemClick;
    public EventHandler<View>? OnItemScaleBegin;
}