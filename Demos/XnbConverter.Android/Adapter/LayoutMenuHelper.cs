using Android.Content;
using Android.Views;
using XnbConverter.Android.ViewModel;

namespace XnbConverter.Android.Adapter;

public class LayoutMenuHelper
{
    public static LayoutMenuHelper Builder(Activity context, IContextMenu menu, List<int> icons, List<string> texts,
        int column = 2)
    {
        if (icons.Count != texts.Count) throw new ArgumentException("icons and texts must be same size");
        var item = menu.Add(0, 1, 0, "");
        var root = context.LayoutInflater.Inflate(Resource.Layout.layout_menu, null);
        var menuLayoutMain = root.FindViewById<LinearLayout>(Resource.Id.menu_layout_main);

        LinearLayout thisLayout = null;
        var helper = new LayoutMenuHelper();
        for (var i = 0; i < icons.Count; i++)
        {
            var icon = icons[i];
            var text = texts[i];
            if (i % column == 0)
            {
                thisLayout = CreateLayout(context);
                menuLayoutMain.AddView(thisLayout);
            }

            var layoutItem = context.LayoutInflater.Inflate(Resource.Layout.menu_item, null);
            thisLayout.AddView(layoutItem);
            var holder = new MenuItemHolder(layoutItem);
            holder.Icon.SetImageResource(icon);
            holder.Text.Text = text;
            layoutItem.Tag = holder;
            holder.Icon.Click += (_, _) =>
            {
                helper.OnContextItemSelected?.Invoke(null, icon);
                // item.SetTitle(ic.ToString());
                context.OnContextItemSelected(item);
            };
        }

        menu.SetHeaderView(root);
        return helper;
    }

    private static LinearLayout CreateLayout(Activity context)
    {
        return new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters =
                new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
    }

    public event EventHandler<int>? OnContextItemSelected;
}