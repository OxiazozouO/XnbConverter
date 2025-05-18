using Android.Views;
using XnbConverter.Android.Attribute;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Components;

[ViewClassBind(Layout.component_progress_bar)]
public class ProgressBarHolder(View view) : ViewHolder<View>(view)
{
    [ViewBind(Id.id_chart_progress_view)] public View ProgressView;

    public int Width => Root.LayoutParameters.Width;
    public int Height => Root.LayoutParameters.Height;

    protected override void Init()
    {
    }

    public int CalculationProgress(decimal pos, decimal max = 100)
    {
        var p = Root.LayoutParameters;
        int w = p.Width;
        int h = p.Height;
        if (max < 0.001m) return 0;
        return w > h
            ? Math.Min((int)(pos * w / max), w)
            : Math.Min((int)(pos * p.Height / max), h);
    }

    public void SetProgress(decimal pos, decimal max = 100)
    {
        decimal p = CalculationProgress(pos, max);
        var parameters = ProgressView.LayoutParameters;
        var layoutParams = Root.LayoutParameters;
        if (layoutParams.Width > layoutParams.Height)
        {
            parameters.Width = (int)p;
        }
        else
        {
            parameters.Height = (int)p;
        }

        ProgressView.LayoutParameters = parameters;
    }
}