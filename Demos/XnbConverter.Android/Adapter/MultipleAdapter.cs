using Android.Util;
using Android.Views;

namespace XnbConverter.Android.Adapter;

public enum MultipleFlag
{
    Open,
    Close,
    Empty,
    Add
}

public abstract class MultipleAdapter<T, TV> : BaseAdapter<T> where TV : Java.Lang.Object
{
    public List<T> Models { get; set; } = [];

    private ListView root;

    protected readonly SparseBooleanArray _checkedMap = new();
    public readonly List<T> SelectedList = new();


    public bool LongMultiple = false;
    private bool _isMultiple = false;

    public MultipleFlag Flag;

    public EventHandler<bool>? OnSelect;
    public EventHandler<T>? OnItemClick;

    public bool IsMultiple
    {
        set
        {
            _isMultiple = value;
            if (!value)
            {
                _checkedMap.Clear();
                SelectedList.Clear();
                Flag = MultipleFlag.Close;
                OnSelect?.Invoke(this, true);
            }

            NotifyDataSetChanged();
        }
        get => _isMultiple;
    }

    protected MultipleAdapter(ListView root)
    {
        this.root = root;
        Init();
    }

    public void ReSet(List<T>? models = null)
    {
        if (models is not null) Models = models;
        _checkedMap.Clear();
        SelectedList.Clear();
        Flag = MultipleFlag.Empty;
        OnSelect?.Invoke(this, true);
        NotifyDataSetChanged();
    }

    protected void UpdateCheckBoxStatus(int position)
    {
        if (!_isMultiple) return;
        var check = !_checkedMap.Get(position);
        _checkedMap.Put(position, check);

        if (check) SelectedList.Add(Models[position]);
        else SelectedList.Remove(Models[position]);
        Flag = SelectedList.Count == 0 ? MultipleFlag.Empty : MultipleFlag.Add;
        OnSelect?.Invoke(this, true);

        NotifyDataSetChanged();
    }

    public void SelectAll()
    {
        if (!_isMultiple) return;
        for (var i = Models.Count - 1; i >= 0; i--)
            _checkedMap.Put(i, true);
        SelectedList.AddRange(Models);
        Flag = MultipleFlag.Add;
        OnSelect?.Invoke(this, true);
        NotifyDataSetChanged();
    }

    public void RemoveAll()
    {
        _checkedMap.Clear();
        SelectedList.Clear();
        Flag = MultipleFlag.Empty;
        OnSelect?.Invoke(this, true);
        NotifyDataSetChanged();
    }

    private void Init()
    {
        ReSet();
        root.Adapter = this;
        root.ItemClick += (sender, args) =>
        {
            if (_isMultiple)
            {
                UpdateCheckBoxStatus(args.Position);
            }
            else
            {
                OnItemClick?.Invoke(null, Models[args.Position]);
            }
        };
        root.ItemLongClick += (sender, args) =>
        {
            if (!LongMultiple) return;
            if (!IsMultiple)
            {
                IsMultiple = true;
                Flag = MultipleFlag.Add;
                OnSelect?.Invoke(this, true);
            }

            UpdateCheckBoxStatus(args.Position);
        };
        NotifyDataSetChanged();
    }

    public override View? GetView(int position, View? convertView, ViewGroup? parent)
    {
        var item = Models[position];
        TV holder;
        if (convertView is null)
        {
            CreateItem(out holder, out convertView);
            convertView.Tag = holder;
        }
        else
        {
            holder = (TV)convertView.Tag;
        }

        Bind(position, item, holder);

        return convertView;
    }

    public abstract void CreateItem(out TV holder, out View root);

    public abstract void Bind(int pos, T item, TV holder);

    public override long GetItemId(int position) => position;

    public override int Count => Models.Count;

    public override T this[int position] => Models[position];
}