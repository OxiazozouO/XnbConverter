using Android.Views;
using AndroidX.ViewPager.Widget;
using Google.Android.Material.Tabs;
using XnbConverter.Android.Adapter;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Helper;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.activity_main)]
public class ActivityMainHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewHolderBind(Id.id_main_file_search_bar)]
    public ModuleFileSearchBarHolder FileSearchBarHolder;

    [ViewBind(Id.id_main_tab)] public TabLayout Tab;

    [ViewBind(Id.id_main_view_pager)] public ViewPager ViewPager;

    [ViewHolderBind(Id.id_main_bottom_menu)]
    public ModuleMainBottomMenuHolder BottomMenuHolder;

    [ViewBind(Id.id_more_menu_bnt)] public ImageButton MoreMenuBnt;

    [ViewHolderBind(Id.id_main_image_viewer)]
    public ModuleImageViewerHolder ImageViewerHolder;

    [ViewBind(Id.id_main_menu)] public ImageView MainMenu;
    [ViewBind(Id.id_main_menu_img)] public ImageView MenuImg;

    public MainPagerAdapter adapter;

    protected override void Init()
    {
        FileSearchBarHolder.InitActivity(activity);
        adapter = new MainPagerAdapter(FileSearchBarHolder.Bind, activity);
        adapter.Models = [[new MainPager("")], [new MainPager(""), new MainPager("")], [new MainPager("")]];
        ViewPager.Adapter = adapter;
        ViewPager.PageSelected += (sender, args) =>
        {
            if (adapter.Dir.TryGetValue(args.Position, out var holder))
            {
                adapter.OnPathChanged(args.Position, 0, holder, holder.Path);
            }
        };

        Tab.SetupWithViewPager(ViewPager);

        MainMenu.CallClick(() => activity.OpenContextMenu(MainMenu));

        MultipleFlag fg = MultipleFlag.Close;

        MoreMenuBnt.CallClick(() =>
        {
            var fileHolder = adapter.SelectFile.Holder;
            if (fileHolder == null) return;
        });

        adapter.OnOpenMenu += (_, flag) =>
        {
            var fileHolder = adapter.SelectFile.Holder;
            fg = flag;
            switch (flag)
            {
                case MultipleFlag.Open:
                    BottomMenuHolder.Root.Visibility = ViewStates.Visible;
                    MoreMenuBnt.Visibility = ViewStates.Gone;
                    break;
                case MultipleFlag.Close:
                    BottomMenuHolder.Root.Visibility = ViewStates.Gone;
                    break;
                case MultipleFlag.Empty:
                    BottomMenuHolder.Root.Visibility = ViewStates.Visible;
                    MoreMenuBnt.Visibility = ViewStates.Gone;
                    break;
                case MultipleFlag.Add:
                    BottomMenuHolder.Root.Visibility = ViewStates.Visible;
                    MoreMenuBnt.Visibility = ViewStates.Gone;
                    break;
            }
        };

        adapter.OnOpen += (_, b) =>
        {
            if (fg == MultipleFlag.Close)
            {
                if (b)
                {
                    MoreMenuBnt.Visibility = ViewStates.Visible;
                }
                else
                {
                    MoreMenuBnt.Visibility = ViewStates.Gone;
                }
            }
            else
            {
                MoreMenuBnt.Visibility = ViewStates.Gone;
            }
        };

        adapter.OnSelectFile += (sender, b) =>
        {
            var id = adapter.SelectFile.Holder.adapter.config switch
            {
                0 => Drawable.ic_menu_list1,
                1 => Drawable.ic_menu_list2,
                2 => Drawable.ic_menu_list3,
                3 => Drawable.ic_menu_list,
                _ => -1
            };
            if (id != -1) MenuImg.SetBackgroundResource(id);
        };

        MenuImg.CallClick(() =>
        {
            if (adapter.SelectFile is null) return;
            var file = adapter.SelectFile;
            var holder = file.Holder;
            var viewAdapter = holder.adapter;
            var page = file.Page;
            var pos = file.Pos;

            var config = viewAdapter.config;
            config++;
            config %= 7;
            viewAdapter.config = config;
            adapter.SelectFile?.Holder.ListView.Invalidate();
            viewAdapter.NotifyDataSetChanged();
            var id = viewAdapter.config switch
            {
                0 => Drawable.ic_menu_max,
                1 => Drawable.ic_menu_list1,
                2 => Drawable.ic_menu_list1,
                3 => Drawable.ic_menu_list2,
                4 => Drawable.ic_menu_list2,
                5 => Drawable.ic_menu_list3,
                6 => Drawable.ic_menu_list3,
                _ => Drawable.ic_menu_list
            };
            if (id != -1) MenuImg.SetBackgroundResource(id);
            adapter.Models[page][pos].Id = viewAdapter.config;
        });

        adapter.OnOpenFile += (sender, s) =>
        {
            if (sender is not Model.FileInfo info) return;
            if (adapter.SelectFile is not { } sf) return;

            activity.OpenFile(info, sf, ImageViewerHolder);
        };
    }

    public void OnContextItemSelected(int id)
    {
        var position = Tab.SelectedTabPosition;
        var b = position == adapter.Models.Count - 1;
        var model = adapter.Models[position]
            .Select(m => new MainPager(m.Path))
            .ToList();
        switch (id)
        {
            case Id.id_menu_add:
                if (b) adapter.Models.Add([new MainPager("")]);
                else adapter.Models.Insert(position, [new MainPager("")]);
                break;
            case Id.id_menu_copy:
                if (b) adapter.Models.Add(model);
                else adapter.Models.Insert(position, model);
                break;
            case Id.id_menu_merge:
                model.Add(new MainPager(""));
                if (b) adapter.Models.Add(model);
                else adapter.Models.Insert(position, model);
                break;
            case Id.id_menu_del:
                adapter.Models.RemoveAt(position);
                break;
        }

        adapter.NotifyDataSetChanged();
    }
}