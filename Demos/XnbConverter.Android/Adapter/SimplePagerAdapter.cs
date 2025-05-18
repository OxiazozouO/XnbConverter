using Android.Views;
using AndroidX.ViewPager.Widget;
using Object = Java.Lang.Object;

namespace XnbConverter.Android.Adapter;

public abstract class SimplePagerAdapter<T>(List<T> models) : PagerAdapter
{
    public List<T> Models { get; set; } = models;
    public override int Count => Models.Count;

    public override bool IsViewFromObject(View view, Object obj)
    {
        return view == obj;
    }

    public override void DestroyItem(ViewGroup container, int position, Object obj)
    {
        container.RemoveView((View)obj);
    }

    public override int GetItemPosition(Object obj) => PositionNone;
}