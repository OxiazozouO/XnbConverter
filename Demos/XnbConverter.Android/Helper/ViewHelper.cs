using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using AndroidX.SwipeRefreshLayout.Widget;
using Java.Lang;

namespace XnbConverter.Android.Helper;

public static class ViewHelper
{
    public static void BindTo(this TextView view, Action<string?> action)
    {
        view.TextChanged += (_, _) => action(view.Text);
        action(view.Text);
    }

    public static void CallClick(this View view, Action action)
    {
        view.Click += (_, _) => action();
    }

    public static void BindTo(this LinearLayout view, Action action)
    {
        view.Click += (_, _) => action();
    }

    public static void ChangeSize(this View view, int? w, int? h)
    {
        var parameters = view.LayoutParameters;
        if (parameters == null) return;
        if (w is not null) parameters.Width = w.Value;
        if (h is not null) parameters.Height = h.Value;

        view.LayoutParameters = parameters;
    }

    public static void SetTextColorResource(this global::Android.App.Activity activity, TextView view, int id)
    {
        view.SetTextColor((global::Android.Graphics.Color)activity?.Resources?.GetColor(id));
    }

    public static void BindUp(this ScrollView scrollView, Action run)
    {
        scrollView.ScrollChange += (sender, e) =>
        {
            if (scrollView.MeasuredHeight <= scrollView.ScrollY + scrollView.Height)
            {
                run.Invoke();
            }
        };
    }

    public static void BindUp(this ScrollView scrollView, Func<int, bool> run)
    {
        int pos = 1;
        bool isEnd = false;
        scrollView.ScrollChange += (sender, e) =>
        {
            if (scrollView.MeasuredHeight <= scrollView.ScrollY + scrollView.Height)
            {
                if (isEnd) return;
                isEnd = run.Invoke(pos);
                pos++;
            }
        };
    }

    public static void BindRefresh(this SwipeRefreshLayout layout, Action action)
    {
        layout.Refresh += (sender, args) =>
        {
            new Handler().PostDelayed(new Runnable(() =>
            {
                action.Invoke();
                layout.Refreshing = false;
            }), 1000);
        };
    }
    
    public static void SetColors(this View view, int[] colors)
    {
        var gradient = new GradientDrawable();
        gradient.SetCornerRadius(100);
        gradient.SetGradientType(GradientType.LinearGradient);
        gradient.SetOrientation(GradientDrawable.Orientation.BlTr);
        gradient.SetColors(colors);
        view.Background = gradient;
    }
}