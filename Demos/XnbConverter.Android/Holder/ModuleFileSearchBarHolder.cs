using _Microsoft.Android.Resource.Designer;
using Android.Text;
using Android.Views;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Helper;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;
using Color = Android.Graphics.Color;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Holder;

[ViewClassBind(ResourceConstant.Layout.module_file_search_bar)]
public class ModuleFileSearchBarHolder(View view) : ViewHolder<View>(view)
{
    [ViewBind(Id.id_file_search_bar_content)]
    public LinearLayout Content;

    [ViewBind(Id.id_file_search_bar_sub)] public ImageButton Sub;

    public global::Android.App.Activity Activity;

    protected override void Init()
    {
    }

    public void InitActivity(global::Android.App.Activity activity)
    {
        Activity = activity;
        Sub.CallClick(() =>
        {
            MsgBoxHelper
                .Builder(activity, "请输入路径", "")
                .AddEditText(Uri, InputTypes.ClassText, 100)
                .Show(list =>
                {
                    var tmp = $"{list[0]}";
                    if (Uri != tmp)
                    {
                        bind(tmp);
                        Bind(bind, tmp);
                    }
                });
        });
    }

    public string Uri = "";
    public Action<string>? bind;

    public void Bind(Action<string> bind, string uri)
    {
        this.bind = bind;
        Uri = uri.Replace(FileInfo.RootPath, "");
        var arr = Uri.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToList();
        arr.Insert(0, "");
        Content.RemoveAllViews();

        string path = "";
        ItemFileSearchBarHolder? holder = null;
        foreach (var se in arr)
        {
            holder = new ItemFileSearchBarHolder(Activity);
            string tmp = path + "/" + se;
            path = tmp;
            holder.Bind(se);
            holder.Root.CallClick(() =>
            {
                if (uri != tmp)
                {
                    bind(tmp);
                    Bind(bind, tmp);
                }
            });
            Content.AddView(holder.Root);
        }

        holder?.Arrow.SetTextColor(Color.Orange);
        holder?.Text.SetBackgroundResource(Drawable.bg_filepath_select);
    }
}