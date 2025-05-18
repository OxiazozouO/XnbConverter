using Android.Content;
using Android.Text;
using Android.Views;
using XnbConverter.Android.Holder;

namespace XnbConverter.Android.Helper;

public class MsgBoxHelper(global::Android.App.Activity activity)
{
    public static void ShowToast(global::Android.App.Activity activity,
        string message = "", ToastLength length = ToastLength.Short) =>
        Toast.MakeText(activity, message, length).Show();

    public static AlertDialog ShowProgressBar(
        global::Android.App.Activity activity,
        out MsgProgressBarHolder holder,
        string message = "")
    {
        holder = new MsgProgressBarHolder(activity);
        var helper = Builder(activity, message, "")
            .TryAddView(holder.Root);
        return helper.ShowDialog();
    }

    public static void Error(global::Android.App.Activity activity, string? message) =>
        Builder(activity).TryError(message);

    public static MsgBoxHelper Builder(global::Android.App.Activity activity, string message = "", string caption = "")
    {
        return new MsgBoxHelper(activity).Create(message, caption);
    }

    private AlertDialog.Builder? builder = null;

    private MsgBoxHelper Create(string message, string caption)
    {
        try
        {
            builder = new AlertDialog.Builder(activity)
                ?.SetTitle(caption)
                ?.SetMessage(message);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return this;
    }

    private static void Handler(object? sender, DialogClickEventArgs e)
    {
    }

    public List<Func<object>> Funcs = new List<Func<object>>();
    private LinearLayout Layout;

    public MsgBoxHelper AddEditText(string? text, InputTypes inputType, int maxLen, string tip = "")
    {
        var holder = new ItemLayoutMsgEditHolder(activity);
        holder.MsgText.Text = tip;
        holder.MsgEdit.Text = text;
        holder.MsgEdit.InputType = inputType | InputTypes.TextFlagMultiLine;
        holder.MsgEdit.SetFilters([new InputFilterLengthFilter(maxLen)]);
        Funcs.Add(() => holder.MsgEdit.Text);
        return TryAddView(holder.Root);
    }

    private MsgBoxHelper TryAddView(View view)
    {
        if (Layout == null)
        {
            Layout = new LinearLayout(activity);
            Layout.Orientation = Orientation.Vertical;
            builder?.SetView(Layout);
        }

        Layout.AddView(view);
        return this;
    }

    public MsgBoxHelper AddLisView(List<MsgItem> items, int pos)
    {
        var msgHolder = new LayoutMsgHolder(activity);
        if (items.Count > 7)
        {
            msgHolder.Root.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1000);
        }

        msgHolder.Bind(items);
        msgHolder.CallClick(pos);
        Funcs.Add(() => msgHolder.SelectedItem);

        return TryAddView(msgHolder.Root);
    }

    public void OkCancel(Action action, Action? cancel = null)
    {
        builder
            ?.SetPositiveButton("确定", (_, _) => { action(); })
            ?.SetNegativeButton("取消", (_, _) => { cancel?.Invoke(); })
            ?.Create()
            ?.Show();
    }

    public AlertDialog ShowDialog()
    {
        var dialog = builder
            ?.SetPositiveButton("确定", Handler)
            ?.Create();
        dialog?.Show();
        return dialog;
    }

    public void Show(Action<List<object>> action)
    {
        builder
            ?.SetPositiveButton("确认", (_, _) =>
            {
                try
                {
                    action.Invoke(Funcs.Select(func => func.Invoke()).ToList());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            })
            ?.SetNegativeButton("取消", Handler)
            ?.Create()
            ?.Show();
    }

    // 尝试显示错误类型对话框，如果消息为空则返回false
    public bool TryError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        builder
            ?.SetMessage(message)
            ?.SetPositiveButton("确定", Handler)
            ?.Create()
            ?.Show();
        return true;
    }

    public void Close()
    {
        builder?.Dispose();
    }
}