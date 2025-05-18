using Android.Views;
using AndroidX.SwipeRefreshLayout.Widget;
using XnbConverter.Android.Adapter;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Helper;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.module_file_view)]
public class ModuleFileViewHolder(View view) : ViewHolder<View>(view)
{
    [ViewBind(Id.id_file_view_swipe)] public SwipeRefreshLayout SwipeRefresh;
    [ViewBind(Id.id_file_view_list_view)] public ListView ListView;

    public FileViewAdapter adapter;

    private DateTime lastUpdate = DateTime.Now;

    protected override void Init()
    {
        SwipeRefresh.BindRefresh(UpdateUi);
        ListView.Touch += (_, args) =>
        {
            if ((DateTime.Now - lastUpdate).Seconds > 1)
            {
                lastUpdate = DateTime.Now;
                OnPathChanged?.Invoke(null, Path);
            }

            args.Handled = false;
        };
    }

    public void InitActivity(global::Android.App.Activity activity)
    {
        adapter = new FileViewAdapter(activity, ListView)
        {
            LongMultiple = true,
            IsMultiple = false
        };

        adapter.OnItemClick += (_, info) =>
        {
            var type = info.TypeInfo.Type;
            if (type == FileType.Empty) return;
            if (type == FileType.Folder)
            {
                Bind(info.Uri);
            }
            else
            {
                OnOpenFile?.Invoke(info, info.Uri);
            }
        };
    }

    public string Path;

    public EventHandler<string>? OnPathChanged;
    public EventHandler<string>? OnOpenFile;

    public Stack<string> Paths = new Stack<string>();

    public void Bind(string uri)
    {
        if (Paths.Count == 0 || Paths.Peek() != uri)
            Paths.Push(uri);
        Path = uri;
        OnPathChanged?.Invoke(this, uri);
        UpdateUi();
    }


    public void UpdateUi()
    {
        adapter.Models = new FileInfo(Path).ListFiles();
        adapter.NotifyDataSetChanged();
    }

    public bool OnBack()
    {
        if (adapter.IsMultiple)
        {
            adapter.IsMultiple = false;
            return false;
        }

        if (Paths.Count <= 1) return true;
        Paths.Pop();
        var peek = Paths.Peek();
        Bind(peek);
        return Paths.Count == 0;
    }
}