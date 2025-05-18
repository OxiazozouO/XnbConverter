using Android.Views;

namespace XnbConverter.Android.Helper;

public static class ViewGroupHelper
{
    public static T? FindAncestorById<T>(this View view, int id) where T : View
    {
        while (true)
        {
            if (view.Parent is not View parent) return default;
            if (parent.Id == id)
            {
                return (T?)parent;
            }

            view = parent;
        }
    }
}