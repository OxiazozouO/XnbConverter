using Android.Views;
using Bumptech.Glide;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Helper;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.item_file_view)]
public class ItemFileViewHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_file_view_icon)] public ImageView Icon;
    [ViewBind(Id.id_file_view_title)] public TextView Title;

    [ViewBind(Id.id_file_view_date_and_size)]
    public LinearLayout DateAndSize;

    [ViewBind(Id.id_file_view_date)] public TextView Date;
    [ViewBind(Id.id_file_view_size)] public TextView Size;
    [ViewBind(Id.id_file_view_check)] public CheckBox Check;

    [ViewBind(Id.id_file_view_main)] public LinearLayout MainView;

    protected override void Init()
    {
    }

    private FileInfo file;

    public void Bind(FileInfo info)
    {
        this.file = info;
        var typeInfo = file.TypeInfo;
        var type = typeInfo.Type;
        if (type == FileType.Image)
        {
            Glide.With(Root).Load(file.Uri).Into(Icon);
        }
        else
        {
            if (Check.Checked)
            {
                Icon.SetImageResource(typeInfo.ResId2);
            }
            else
            {
                Icon.SetImageResource(typeInfo.ResId1);
            }
        }

        Size.Text = file.FileSize;
        Title.Text = file.FileName;
        Date.Text = file.UpdateTime;
    }

    public void UpdateConfig(FileViewItemConfig config)
    {
        if (Root.Width < 1)
        {
            Root.Post(() => { UpdateConfig(config); });
            return;
        }

        MainView.Orientation = config.orientation;

        var p = MainView.LayoutParameters;
        MainView.LayoutParameters = p;

        p = Icon.LayoutParameters;
        p.Width = (int)(Root.Width * config.IconWAndH);
        p.Height = (int)(Root.Width * config.IconWAndH);
        Icon.LayoutParameters = p;
        if (file.TypeInfo.Type == FileType.Image)
        {
            Glide.With(Root).Clear(Icon);
            Glide.With(Root).Load(file.Uri).Into(Icon);
        }
    }

    public class FileViewItemConfig
    {
        public double IconWAndH { get; set; }
        public int TitleFontSize { get; set; }
        public int DateAndSizeFontSize { get; set; }
        public Orientation orientation { get; set; }
    }

    public static readonly FileViewItemConfig DefaultConfig = new FileViewItemConfig
    {
        IconWAndH = 0.15,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal
    };

    public static readonly FileViewItemConfig Config1 = new FileViewItemConfig
    {
        IconWAndH = 0.25,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal
    };

    public static readonly FileViewItemConfig Config2 = new FileViewItemConfig
    {
        IconWAndH = 0.40,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Horizontal
    };

    public static readonly FileViewItemConfig Config3 = new FileViewItemConfig
    {
        IconWAndH = 0.55,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical
    };

    public static readonly FileViewItemConfig Config4 = new FileViewItemConfig
    {
        IconWAndH = 0.70,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical
    };

    public static readonly FileViewItemConfig Config5 = new FileViewItemConfig
    {
        IconWAndH = 0.85,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical
    };

    public static readonly FileViewItemConfig Config6 = new FileViewItemConfig
    {
        IconWAndH = 0.95,
        TitleFontSize = 20,
        DateAndSizeFontSize = 12,
        orientation = Orientation.Vertical
    };
}