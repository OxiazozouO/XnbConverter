using Android.Views;
using XnbConverter.Android.Activity;
using XnbConverter.Android.Holder;

namespace XnbConverter.Android;

[Activity(MainLauncher = true)]
public class MainActivity : BaseActivity
{
    private ActivityMainHolder _holder;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _holder = new ActivityMainHolder(this);
        SetContentView(_holder.Root);

        RegisterForContextMenu(_holder.MainMenu);
    }

    public DateTime BackTime = DateTime.Now;

    public override void OnBackPressed()
    {
        var holder = _holder.adapter.SelectFile.Holder;
        if (holder == null)
        {
            base.OnBackPressed();
            return;
        }

        if (_holder.ImageViewerHolder.Root.Visibility == ViewStates.Visible)
        {
            _holder.ImageViewerHolder.Root.Visibility = ViewStates.Gone;
            return;
        }

        if (holder.OnBack())
        {
            if ((DateTime.Now - BackTime).Seconds > 1)
            {
                BackTime = DateTime.Now;
                Toast.MakeText(this, "再按一次返回键退出", ToastLength.Short).Show();
                return;
            }

            base.OnBackPressed();
            return;
        }
    }

    public override void OnCreateContextMenu(IContextMenu? menu, View? v, IContextMenuContextMenuInfo? menuInfo)
    {
        if (v.Id == _holder.MainMenu.Id)
        {
            MenuInflater.Inflate(Resource.Menu.tab_more_menu, menu);
            var model = _holder.adapter.Models[_holder.Tab.SelectedTabPosition];
            if (model.Count > 1)
            {
                menu.FindItem(Resource.Id.id_menu_merge).SetVisible(false);
            }
        }

        base.OnCreateContextMenu(menu, v, menuInfo);
    }

    public override bool OnContextItemSelected(IMenuItem item)
    {
        _holder.OnContextItemSelected(item.ItemId);

        return base.OnContextItemSelected(item);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _holder?.adapter?.SelectFile?.Holder.adapter.NotifyDataSetChanged();
    }
}