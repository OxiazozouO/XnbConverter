using Android.Views;
using AndroidX.ViewPager.Widget;
using Bumptech.Glide;
using XnbConverter.Android.Adapter;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Components;
using XnbConverter.Android.Helper;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.module_image_viewer)]
public class ModuleImageViewerHolder(View view) : ViewHolder<View>(view)
{
    [ViewBind(Id.id_image_viewer_viewpager)]
    public ViewPager ViewPager;

    [ViewBind(Id.id_image_viewer_toolbar)] public LinearLayout Toolbar;
    [ViewBind(Id.id_image_viewer_back)] public ImageView Back;
    [ViewBind(Id.id_image_viewer_title)] public TextView Title;
    [ViewBind(Id.id_image_viewer_count)] public TextView Count;
    [ViewBind(Id.id_image_viewer_desc)] public TextView Desc;

    [ViewBind(Id.id_image_viewer_more)] public ImageView More;


    private ImageAdapter adapter;

    protected override void Init()
    {
        //预加载
        ViewPager.OffscreenPageLimit = 3;

        Back.CallClick(() => Root.Visibility = ViewStates.Gone);
        ViewPager.PageSelected += (_, a) => ViewPagerOnPageSelected(a.Position);
    }

    private void ViewPagerOnPageSelected(int pos)
    {
        var info = adapter.Models[pos];
        Count.Text = $"{pos + 1}/{adapter.Models.Count}";
        Title.Text = info.FileName;
        Desc.Text = info.FileSize;
    }

    public void Bind(global::Android.App.Activity activity, List<FileInfo> list, int pos)
    {
        Root.Visibility = ViewStates.Visible;
        adapter = new ImageAdapter(activity, list);
        ViewPager.Adapter = adapter;

        int i = 0;
        adapter.OnItemClick += (sender, args) =>
        {
            if (sender is not ZoomImageView z) return;
            if (z.IsDeselect)
            {
                Root.SetBackgroundResource(i switch
                {
                    0 => Color.image_viewer_bg_def,
                    1 => Color.image_viewer_bg_1,
                    2 => Color.image_viewer_bg_2,
                    3 => Color.image_viewer_bg_3,
                    _ => Color.image_viewer_bg_def
                });
                i = (i + 1) % 4;
            }
            else
            {
                var v = Toolbar.Visibility == ViewStates.Visible
                    ? ViewStates.Invisible
                    : ViewStates.Visible;
                Toolbar.Visibility = v;
            }
        };
        adapter.OnItemScaleBegin += (_, _) => { Toolbar.Visibility = ViewStates.Gone; };

        //默认图
        ViewPager.CurrentItem = pos;
        ViewPagerOnPageSelected(pos);
    }
}

public class ImageAdapter(global::Android.App.Activity activity, List<FileInfo> files)
    : SimplePagerAdapter<FileInfo>(files)
{
    public EventHandler<int>? OnItemClick;
    public EventHandler<ZoomImageView>? OnItemScaleBegin;

    public override Java.Lang.Object InstantiateItem(ViewGroup container, int position)
    {
        var url = Models[position];
        ZoomImageView v = new ZoomImageView(activity);
        v.Click += (sender, e) => { OnItemClick?.Invoke(v, position); };
        v.ScaleBegin += (_, _) => { OnItemScaleBegin?.Invoke(this, v); };
        v.Id = position;
        container.AddView(v); //添加到父控件
        Glide.With(activity).Load(url.Uri).Into(v);
        return v;
    }
}