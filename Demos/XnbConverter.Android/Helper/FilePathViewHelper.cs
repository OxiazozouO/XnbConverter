using System.Timers;
using Android.Content;
using Android.Graphics;
using Android.Views;
using XnbConverter.Android.Configurations;
using XnbConverter.Android.Model;
using XnbConverter.Android.ViewModel;
using XnbConverter.Configurations;
using HorizontalScrollView = Android.Widget.HorizontalScrollView;
using Path = System.IO.Path;
using Timer = System.Timers.Timer;

namespace XnbConverter.Android.Helper;

public class FilePathViewHelper(Context context, LinearLayout root, Action<string>? onItemClick)
{
    private List<Tuple<string, string>> _list = new List<Tuple<string, string>>();

    public string? GetLastPath() => _list.Count > 0 ? _list.Last().Item1 : null;

    public void Init(string path)
    {
        if (string.IsNullOrEmpty(path)) path = ConfigHelper.BasePath;
        _list = new List<Tuple<string, string>>();
        while (path != null && path != ConfigHelper.BasePath)
        {
            _list.Add(new Tuple<string, string>(path, Path.GetFileName(path)));
            path = Directory.GetParent(path).FullName;
        }

        _list.Add(new Tuple<string, string>(path, "内部存储"));
        _list.Reverse();

        FilePathList filePathList;
        if (root.Tag is null)
        {
            filePathList = new FilePathList();
            for (int i = 0, ma = root.ChildCount; i < ma; i++) filePathList.Views.Add(root.GetChildAt(i));
            root.Tag = filePathList;
        }
        else
        {
            filePathList = (FilePathList)root.Tag;
        }

        var views = filePathList.Views;

        int sta = 0;


        int newCount = _list.Count;
        int itemCount = views.Count;
        for (int i = 0, ma = Math.Min(newCount, itemCount); i < ma; i++)
        {
            GetView(i, views[i]).Visibility = ViewStates.Visible;
        }

        if (itemCount == newCount)
        {
            for (var i = 0; i < itemCount; i++)
            {
                GetView(i, views[i]).Visibility = ViewStates.Visible;
            }
        }
        else if (itemCount < newCount)
        {
            for (var i = itemCount; i < newCount; i++)
            {
                var view = GetView(i, null);
                view.Visibility = ViewStates.Visible;
                views.Add(view);
                root.AddView(view);
                int ind = i;
                view.Click += (_, _) =>
                {
                    Init(_list[ind].Item1);
                    onItemClick?.Invoke(_list[ind].Item1);
                };
            }
        }
        else if (itemCount > newCount)
        {
            for (var i = newCount; i < itemCount; i++)
            {
                views[i].Visibility = ViewStates.Gone;
            }
        }

        if (root.Parent is HorizontalScrollView h)
        {
            Timer timer = new Timer(100);

            void OnTimerOnElapsed(object? o, ElapsedEventArgs elapsedEventArgs)
            {
                h.FullScroll(FocusSearchDirection.Right);
                timer.Elapsed -= OnTimerOnElapsed;
            }

            timer.Elapsed += OnTimerOnElapsed;
            timer.Start();
        }
    }

    private View GetView(int position, View? convertView)
    {
        var fileModel = _list[position];
        FilePathViewItemHolder? holder;
        if (convertView is null)
        {
            convertView = LayoutInflater.From(context).Inflate(Resource.Layout.file_searchview_item, null);
            holder = new FilePathViewItemHolder(convertView);
            convertView.Tag = holder;
        }
        else
        {
            holder = (FilePathViewItemHolder)convertView.Tag;
        }

        if (position == 0)
        {
            holder.FileSearchBarRightArrow.Visibility = ViewStates.Gone;
        }

        if (position + 1 == _list.Count)
        {
            holder.FileSearchBarRightArrow.SetTextColor(new Color(context.GetColor(Resource.Color.primary2)));
            holder.FileSearchBarText.SetBackgroundResource(Resource.Drawable.bg_filepath_select);
        }
        else
        {
            holder.FileSearchBarRightArrow.SetTextColor(new Color(context.GetColor(Resource.Color.primary1)));
            holder.FileSearchBarText.SetBackgroundResource(Resource.Drawable.bg_filepath);
        }


        #region 写入模型数据

        holder.FileSearchBarText.Text = fileModel.Item2;

        #endregion

        return convertView;
    }
}