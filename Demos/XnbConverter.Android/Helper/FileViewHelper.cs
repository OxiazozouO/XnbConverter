using Android.Views;
using XnbConverter.Android.ViewModel;

namespace XnbConverter.Android.Holders;

public class FileViewHelper : FileViewHolder
{
    private int _id;
    private bool _flag;

    public FileViewHelper(View root) : base(root)
    {
        InitEvent();
    }

    public void InitEvent()
    {
        InitButtonEvent();
    }

    private void InitButtonEvent()
    {
        var l = Root.LayoutParameters;

        ListMenu.Click += (_, _) =>
        {
            var id = _id switch
            {
                0 => Resource.Drawable.ic_menu_list1,
                1 => Resource.Drawable.ic_menu_list2,
                2 => Resource.Drawable.ic_menu_list3,
                3 => Resource.Drawable.ic_menu_list,
                _ => -1
            };
            if (id != -1) ListMenu.SetBackgroundResource(id);


            _id = (_id + 1) % 4;
            for (int i = 0; i < FileList.ChildCount; i++)
                if (FileList.GetChildAt(i) is RelativeLayout view)
                    UpdateNewViewProperties(view);
        };

        WinMenu.Click += (_, _) =>
        {
            int w2 = MainActivity.WidthPixels;
            int id = -1;
            switch (_flag)
            {
                case false:
                    id = Resource.Drawable.ic_menu_mid;
                    w2 = MainActivity.WidthPixels;
                    break;
                case true:
                    id = Resource.Drawable.ic_menu_max;
                    w2 = MainActivity.WidthPixels / 2;
                    break;
            }

            WinMenu.SetBackgroundResource(id);

            if (l is not null)
            {
                l.Width = w2;
                Root.LayoutParameters = l;
            }

            _flag = !_flag;
            for (int i = 0; i < FileList.ChildCount; i++)
                if (FileList.GetChildAt(i) is RelativeLayout view)
                    UpdateNewViewProperties(view);
        };

        FileList.ChildViewAdded += (_, e) =>
        {
            if (e.Child is not RelativeLayout r) return;
            UpdateNewViewProperties(r);
        };
    }


    public void UpdateNewViewProperties(RelativeLayout r)
    {
        FileViewItemConfig? cfg = _id switch
        {
            0 => FileViewItemConfig.DefaultConfig,
            1 when _flag => FileViewItemConfig.Config21,
            1 => FileViewItemConfig.Config2,
            2 when _flag => FileViewItemConfig.Config31,
            2 => FileViewItemConfig.Config3,
            3 when _flag => FileViewItemConfig.Config41,
            3 => FileViewItemConfig.Config4,
            _ => null
        };
        if (cfg is null) return;

        var holder = new FileViewItemHolder(r);
        holder.UpdateConfig(cfg);
    }
}