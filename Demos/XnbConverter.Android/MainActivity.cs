using Android.Views;
using XnbConverter.Android.Activities;
using XnbConverter.Android.Adapter;
using XnbConverter.Android.Configurations;
using XnbConverter.Android.Helper;
using XnbConverter.Android.Holders;
using XnbConverter.Android.ViewModel;
using XnbConverter.Configurations;
using File = Java.IO.File;

namespace XnbConverter.Android;

[Activity(MainLauncher = true)]
public class MainActivity : BaseActivity
{
    public ActivityMainHolder holder;
    public static int WidthPixels;
    public FilePathViewHelper FilePathViewHelper;
    public ImageViewerHelper ImageViewerHelper;

    protected override bool Init()
    {
        if (!base.Init()) return false;
        LanguageHelper.Build();
        AndroidRootPath.Build();
        AndroidLogger.Build();

        SetContentView(Resource.Layout.activity_main);
        WidthPixels = Resources.DisplayMetrics.WidthPixels;
        ActionBar?.Show();
        holder = new ActivityMainHolder(this);
        InitEvent();
        return true;
    }

    private static List<View> GetAllChild(View parent)
    {
        List<View> views = new List<View>();
        if (parent is not ViewGroup viewGroup) return views;
        for (int i = 0; i < viewGroup.ChildCount; i++)
        {
            var child = viewGroup.GetChildAt(i);
            if (child is null) continue;
            views.Add(child);
            if (child is ListView) continue;
            views.AddRange(GetAllChild(child));
        }

        return views;
    }

    //选了那边
    public FileViewHelper? SelectHelper;

    public void LayoutOnClick(object? sender)
    {
        if (sender is not LinearLayout v) return;
        int[] ind = new int[2];
        if (v == holder.left)
        {
            ind = [Resource.Drawable.bg_card_border_ash, Resource.Drawable.bg_card_border_green];
            SelectHelper = holder._leftHelper;
        }
        else if (v == holder.right)
        {
            ind = [Resource.Drawable.bg_card_border_purple, Resource.Drawable.bg_card_border_ash];
            SelectHelper = holder.RightHelper;
        }
        else
        {
            ind = [Resource.Drawable.bg_card_border_ash, Resource.Drawable.bg_card_border_ash];
            SelectHelper = null;
        }

        if (SelectHelper is not null)
        {
            if (SelectHelper.FileList.Adapter is FileViewAdapter fv)
            {
                OnSelected(fv);
                FilePathViewHelper.Init(fv.stack.Peek().AbsolutePath);
            }
        }

        holder.right.SetBackgroundResource(ind[0]);
        holder.left.SetBackgroundResource(ind[1]);
    }

    public override bool OnCreateOptionsMenu(IMenu menu)
    {
        MenuInflater.Inflate(Resource.Menu.options_menu, menu);
        return base.OnCreateOptionsMenu(menu);
    }

    public void InitEvent()
    {
        foreach (var o in new List<View> { holder.left, holder.right })
        {
            foreach (var view in GetAllChild(o))
            {
                try
                {
                    view.Touch += (_, e) =>
                    {
                        LayoutOnClick(o);
                        e.Handled = false;
                    };
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        holder.FilePathSearchBtn.Click += (_, _) =>
        {
            var lPath = FilePathViewHelper.GetLastPath();
            if (lPath is null) return;
            ShowInputDialog(path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var file = new File(path);
                if (!file.Exists()) return;
                if (SelectHelper?.FileList.Adapter is FileViewAdapter fv)
                {
                    fv.stack.Push(file);
                    fv.ChangeFileList(file);
                }
            }, lPath);
        };

        holder.LeftHelper.FileTitle.Text = "拆包";
        holder.RightHelper.FileTitle.Text = "解包";

        var p = ConfigHelper.BasePath;
        var leftAdapter = new FileViewAdapter(this, p, holder.LeftHelper);
        leftAdapter.OnFileListChanged += OnFileListChanged;
        leftAdapter.TouchEventHelper.OnUpSwipe += FileListOnUpSwipe;
        leftAdapter.TouchEventHelper.OnDownSwipe += FileListOnDownSwipe;

        var rightAdapter = new FileViewAdapter(this, p, holder.RightHelper);
        rightAdapter.OnFileListChanged += OnFileListChanged;
        rightAdapter.TouchEventHelper.OnUpSwipe += FileListOnUpSwipe;
        rightAdapter.TouchEventHelper.OnDownSwipe += FileListOnDownSwipe;

        FilePathViewHelper = new FilePathViewHelper(this, holder.FileSearchBar, OnFilePathItemClick);

        ImageViewerHelper = new ImageViewerHelper(this, holder.ImageViewerPage);

        RegisterForContextMenu(holder.LeftHelper.FileList);
        RegisterForContextMenu(holder.RightHelper.FileList);
    }

    private void FileListOnDownSwipe(object? sender, float? e)
    {
        holder.MoreMenuBnt.Visibility = ViewStates.Visible;
    }

    private void FileListOnUpSwipe(object? sender, float? e)
    {
        holder.MoreMenuBnt.Visibility = ViewStates.Invisible;
    }

    private void OnFileListChanged(object? sender, File e)
    {
        FilePathViewHelper.Init(e.AbsolutePath);
    }

    public void OnFilePathItemClick(string? path)
    {
        if (path is null) return;
        if (SelectHelper?.FileList.Adapter is FileViewAdapter fv)
        {
            var file = new File(path);
            fv.stack.Push(file);
            fv.ChangeFileList(file);
            FilePathViewHelper.Init(path);
        }
    }

    public void OnSelected(FileViewAdapter? adapter)
    {
        if (adapter is null) return;
        if (adapter.SelectMode)
        {
            holder.MoreMenu.Visibility = ViewStates.Gone;
            holder.BottomMenuMain.Visibility = ViewStates.Visible;
        }
        else
        {
            holder.MoreMenu.Visibility = ViewStates.Visible;
            holder.BottomMenuMain.Visibility = ViewStates.Gone;
        }
    }

    public override void OnBackPressed()
    {
        if (holder.ImageViewerPage.Visibility == ViewStates.Visible)
        {
            holder.ImageViewerPage.Visibility = ViewStates.Gone;
            ActionBar?.Show();
        }
        else if (SelectHelper is null)
        {
            Exit();
        }
        else if (SelectHelper.FileList.Adapter is FileViewAdapter adapter)
        {
            if (adapter.SelectMode)
            {
                adapter.SelectMode = false;
            }
            else if (adapter.stack.Count == 1)
            {
                Exit();
            }
            else if (adapter.stack.Count > 1)
            {
                adapter.stack.Pop();
                adapter.ChangeFileList(adapter.stack.Peek());
            }
        }
    }

    private long _exitTime = 0;

    private void Exit()
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if ((now - _exitTime) > 3000)
        {
            Toast.MakeText(this, "再按一次返回键退出", ToastLength.Short).Show();
            _exitTime = now;
        }
        else
        {
            Finish();
        }
    }

    private void ShowInputDialog(Action<string> onConfirmAction, string? ordstr = "")
    {
        var view = LayoutInflater.Inflate(Resource.Layout.custom_input_dialog, null);
        var editText = view.FindViewById<EditText>(Resource.Id.edit_text_dialog);
        editText.Text = ordstr;

        new AlertDialog.Builder(this)?.SetView(view)?
            .SetPositiveButton("确定", (sender, e) =>
            {
                var inputValue = editText?.Text;
                if (string.IsNullOrEmpty(inputValue)) return;
                onConfirmAction(inputValue);
            })?.Create()?.Show();
    }

    public override void OnCreateContextMenu(IContextMenu? menu, View? v, IContextMenuContextMenuInfo? menuInfo)
    {
        // base.OnCreateContextMenu(menu, v, menuInfo);
        View view = LayoutInflater.From(this).Inflate(Resource.Layout.layout_menu, null);

        new AlertDialog.Builder(this)
            .SetView(view)
            .Create()
            .Show();
        // var builder = LayoutMenuHelper.Builder(this, menu, [
        //         Resource.Drawable.ic_filemenu_shear,
        //         Resource.Drawable.ic_filemenu_copy,
        //         Resource.Drawable.ic_del,
        //         Resource.Drawable.ic_info
        //     ],
        //     [
        //         "剪切",
        //         "复制",
        //         "删除",
        //         "属性"
        //     ]);
        // builder.OnContextItemSelected+= (sender, e) =>
        // {
        //     
        // };
    }

    public override bool OnContextItemSelected(IMenuItem item)
    {
        // switch (item.ItemId)
        // {
        // case Resource.Id.menu_item_1:
        // 处理上下文菜单选项 1 的点击事件
        // break;
        // case Resource.Id.menu_item_2:
        // 处理上下文菜单选项 2 的点击事件
        // break;
        // default:
        // return base.OnContextItemSelected(item);
        // }

        return true;
    }
}