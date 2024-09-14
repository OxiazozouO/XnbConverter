using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using XnbConverter.Android.Adapter;
using XnbConverter.Android.Components;
using XnbConverter.Android.ViewModel;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Helper;

public class ImageViewerHelper(Context context, FrameLayout root)
{
    public ImageViewerPageHolder Holder = new ImageViewerPageHolder(root);

    public void Init(List<Java.IO.File> files, Java.IO.File file)
    {
        ((Activity?)context)?.ActionBar?.Hide();
        var adapter = new BannerAdapter(context, files);
        //设置适配器
        Holder.ViewPager.Adapter = adapter;

        //Pager之间的间距
        Holder.ViewPager.PageMargin = 20;

        //预加载
        Holder.ViewPager.OffscreenPageLimit = 3;


        root.Visibility = ViewStates.Visible;

        Holder.ViewPager.PageSelected += (sender, e) =>
        {
            var file = new FileInfo(files[e.Position]);
            Holder.Count.Text = $"{e.Position + 1}/{files.Count}";
            Holder.Title.Text = file.FileName;
            var view = Holder.ViewPager.FindViewById<ZoomImageView>(e.Position);
            Holder.Desc.Text = $"{file.UpdateTime()} " + ((view?.Drawable?.IntrinsicWidth ?? 0) > 0
                ? $"w:{view?.Drawable?.IntrinsicWidth ?? 0} h:{view?.Drawable?.IntrinsicHeight ?? 0}"
                : "");
        };

        int i = 0;
        adapter.OnItemClick += (_, args) =>
        {
            ColorDrawable? c = i switch
            {
                0 => new ColorDrawable(Color.Black),
                1 => new ColorDrawable(Color.White),
                2 => new ColorDrawable(Color.Gray),
                3 => new ColorDrawable(Color.Transparent),
                _ => null
            };
            if (args is not ZoomImageView z) return;
            if (z.IsDeselect)
            {
                if (c is not null)
                {
                    Holder.ViewPager.Background = c;
                }
                i = (i + 1) % 4;
            }
            else
            {
                Holder.ToolBar.Visibility = Holder.ToolBar.Visibility == ViewStates.Visible
                    ? ViewStates.Gone
                    : ViewStates.Visible;
            }
        };
        adapter.OnItemScaleBegin += (_, _) =>
        {
            Holder.ToolBar.Visibility = ViewStates.Gone;
        };
        
        Holder.Back.Click += (sender, args) =>
        {
            ((Activity?)context)?.ActionBar?.Show();
            root.Visibility = ViewStates.Gone;
        };


        //默认图
        Holder.ViewPager.CurrentItem = files.IndexOf(file);
        root.Visibility = ViewStates.Visible;
        Holder.ViewPager.Tag = root;
    }
}