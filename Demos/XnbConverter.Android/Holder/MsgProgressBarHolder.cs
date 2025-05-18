using Android.Views;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Components;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Resource.Layout.msg_progress_bar)]
public class MsgProgressBarHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewHolderBind(Resource.Id.id_msg_progress_bar)]
    public ProgressBarHolder ProgressBarHolder;

    [ViewBind(Resource.Id.id_msg_progress_text)]
    public TextView Text;

    protected override void Init()
    {
        var parameters = ProgressBarHolder.Root.LayoutParameters;
        parameters.Height = 6;
    }

    public void Bind(float max, float value, string text)
    {
        activity.RunOnUiThread(() =>
        {
            ProgressBarHolder.SetProgress((decimal)value, (decimal)max);
            Text.Text = text;
        });
    }
}