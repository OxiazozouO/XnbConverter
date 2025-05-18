using Android.Views;
using Java.Lang;
using XnbConverter.Android.Holder;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;
using Object = Java.Lang.Object;
using String = Java.Lang.String;

namespace XnbConverter.Android.Adapter;

public class MainPager(string path)
{
    public string Path { set; get; } = path;

    public int Id { set; get; } = 0;
}

public class SelectFileData
{
    public ModuleFileViewHolder Holder { set; get; }
    public ModuleFileViewHolder? NextHolder { set; get; }
    public int Page { set; get; }
    public int Pos { set; get; }
}

public class MainPagerAdapter(Action<Action<string>, string> bind, global::Android.App.Activity activity)
    : SimplePagerAdapter<List<MainPager>>([])
{
    public Dictionary<int, ModuleFileViewHolder> Dir = new Dictionary<int, ModuleFileViewHolder>();

    public override Object InstantiateItem(ViewGroup container, int position)
    {
        var model = Models[position];
        View view;
        if (model.Count == 1)
        {
            var viewHolder = new ModuleFileViewOneHolder(activity);
            var holder = viewHolder.OneViewHolder;

            holder.OnOpenFile += OnOpenFile;

            holder.ListView.Touch += ListViewOnScrollChange;
            holder.SwipeRefresh.Touch += SwipeRefreshOnTouch;

            holder.OnPathChanged += (_, s) => OnPathChanged(position, 0, holder, s);
            holder.adapter.OnSelect += (_, _) => OnPathChanged(position, 0, holder, holder.Path);
            viewHolder.Bind(model[0].Path);
            Dir[position] = holder;
            view = viewHolder.Root;
        }
        else
        {
            var viewHolder = new ModuleFileViewTwoHolder(activity);
            var l = viewHolder.LHolder;
            var r = viewHolder.RHolder;
            l.OnOpenFile += OnOpenFile;
            r.OnOpenFile += OnOpenFile;

            l.ListView.Touch += ListViewOnScrollChange;
            r.ListView.Touch += ListViewOnScrollChange;

            l.SwipeRefresh.Touch += SwipeRefreshOnTouch;
            r.SwipeRefresh.Touch += SwipeRefreshOnTouch;

            l.OnPathChanged += (_, s) => OnPathChanged(position, 0, l, s, r);
            r.OnPathChanged += (_, s) => OnPathChanged(position, 1, r, s, l);

            l.adapter.OnSelect += (_, _) => OnPathChanged(position, 0, l, l.Path);
            r.adapter.OnSelect += (_, _) => OnPathChanged(position, 1, r, r.Path);

            viewHolder.Bind(model[0].Path, model[1].Path);
            Dir[position] = viewHolder.LHolder;
            view = viewHolder.Root;
        }

        container.AddView(view);
        return view;
    }

    private void SwipeRefreshOnTouch(object? sender, View.TouchEventArgs e)
    {
        switch (e.Event.Action)
        {
            case MotionEventActions.Down:
                d = e.Event.GetY();
                break;
            case MotionEventActions.Move:
                if (e.Event.GetY() - d > h)
                {
                    OnOpen?.Invoke(sender, true);
                }

                break;
            case MotionEventActions.Up:
                d = 0;
                break;
        }

        e.Handled = false;
    }

    private float d = 0;
    private float h = 20;

    private void ListViewOnScrollChange(object? sender, View.TouchEventArgs e)
    {
        switch (e.Event.Action)
        {
            case MotionEventActions.Down:
                d = e.Event.GetY();
                break;
            case MotionEventActions.Move:
                if (e.Event.GetY() - d > h)
                {
                    OnOpen?.Invoke(sender, true);
                }
                else if (e.Event.GetY() - d < -h)
                {
                    OnOpen?.Invoke(sender, false);
                }

                break;
            case MotionEventActions.Up:
                d = 0;
                break;
        }
    }

    public EventHandler<bool>? OnOpen;
    public EventHandler<string>? OnOpenFile;
    public EventHandler<MultipleFlag>? OnOpenMenu;

    public SelectFileData? SelectFile;
    public EventHandler<bool>? OnSelectFile;

    public void OnPathChanged(int page, int pos, ModuleFileViewHolder sender,
        string e, ModuleFileViewHolder rSender = null)
    {
        SelectFile?.Holder.Root.SetBackgroundResource(Drawable.bg_card_border_ash);
        SelectFile = new SelectFileData { Holder = sender, NextHolder = rSender, Page = page, Pos = pos };
        OnSelectFile?.Invoke(null, true);
        SelectFile?.Holder.Root.SetBackgroundResource(Drawable.bg_card_border_purple);
        OnOpenMenu?.Invoke(null, sender.adapter.Flag);
        bind(sender.Bind, e);
        Models[page][pos].Path = e;
    }

    public override ICharSequence? GetPageTitleFormatted(int position)
    {
        return new String("page " + (position + 1));
    }
}