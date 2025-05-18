using Android.Views;
using XnbConverter.Android.Holder;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Adapter;

public class FileViewAdapter(global::Android.App.Activity activity, ListView root)
    : MultipleAdapter<FileInfo, ItemFileViewHolder>(root)
{
    public ItemFileViewHolder.FileViewItemConfig _cfg = ItemFileViewHolder.DefaultConfig;

    public ItemFileViewHolder.FileViewItemConfig Config
    {
        set
        {
            _cfg = value;
            ConfigChanged?.Invoke(null, value);
        }
    }

    public int _config = 0;

    public int config
    {
        set
        {
            _config = value;
            Config = value switch
            {
                0 => ItemFileViewHolder.DefaultConfig,
                1 => ItemFileViewHolder.Config1,
                2 => ItemFileViewHolder.Config2,
                3 => ItemFileViewHolder.Config3,
                4 => ItemFileViewHolder.Config4,
                5 => ItemFileViewHolder.Config5,
                6 => ItemFileViewHolder.Config6,
                _ => ItemFileViewHolder.DefaultConfig
            };
        }
        get => _config;
    }

    public EventHandler<ItemFileViewHolder.FileViewItemConfig>? ConfigChanged;

    public override void CreateItem(out ItemFileViewHolder holder, out View root)
    {
        holder = new ItemFileViewHolder(activity);
        var h = holder;
        root = holder.Root;
        ConfigChanged += (_, i) => { h.UpdateConfig(i); };
        holder.UpdateConfig(_cfg);
    }

    public override void Bind(int pos, FileInfo item, ItemFileViewHolder holder)
    {
        // new Thread(() =>
        // {
        // activity.RunOnUiThread(() =>
        // {
        holder.Check.Visibility = IsMultiple
            ? ViewStates.Visible
            : ViewStates.Gone;

        holder.Check.Checked = _checkedMap.Get(pos);
        holder.Bind(item);
        // });
        // }).Start();
    }
}