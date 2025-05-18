using Android.Views;

namespace XnbConverter.Android.Adapter;

public abstract class SimpleAdapter<T> : BaseAdapter<T>
{
    private readonly global::Android.App.Activity _activity;
    public List<T> Models { get; set; } = [];

    protected SimpleAdapter(global::Android.App.Activity activity)
    {
        _activity = activity;
        Init();
    }

    public virtual void Init()
    {
    }

    public override long GetItemId(int position)
    {
        return position;
    }

    public override View? GetView(int position, View? convertView, ViewGroup? parent)
    {
        var item = Models[position];
        Java.Lang.Object holder = null;
        if (convertView is null)
        {
            if (!CreateItem(_activity, position, out holder, out convertView)) return null;
            convertView.Tag = holder;
        }
        else
        {
            holder = convertView.Tag;
        }

        Bind(item, holder);

        return convertView;
    }

    public abstract bool CreateItem(global::Android.App.Activity activity, int pos, out Java.Lang.Object holder, out View root);

    public abstract void Bind(T item, Java.Lang.Object holder);

    public override int Count => Models.Count;

    public override T this[int position] => Models[position];
}