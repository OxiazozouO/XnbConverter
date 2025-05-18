using Android.Views;

namespace XnbConverter.Android.Helper;

public class TouchEventHelper
{
    private float? downPosX;
    private float? downPosY;


    public float GetCurrentY(MotionEvent ev) => ev.GetY() - downPosY ?? 0;
    public float GetCurrentX(MotionEvent ev) => ev.GetX() - downPosX ?? 0;


    public void Init(View.TouchEventArgs? e)
    {
        Init(e?.Event);
    }

    public void Init(MotionEvent? ev, bool isInit = false)
    {
        if (isInit)
        {
            downPosX ??= ev?.GetX();
            downPosY ??= ev?.GetY();
            return;
        }

        if (ev is null || ev.Action != MotionEventActions.Down) return;
        downPosX = ev?.GetX();
        downPosY = ev?.GetY();
    }


    public void Clear()
    {
        downPosX = null;
        downPosY = null;
    }

    public bool IsEmpty() => downPosX == null || downPosY == null;

    /// <summary>
    /// 判断是否左右滑动（如果左右滑动距离大于上下滑动距离则认为是左右滑动）
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public bool IsHorizontalSwipe(MotionEvent ev) =>
        downPosX != null && downPosY != null &&
        Math.Abs((float)(ev.GetX() - downPosX)) > Math.Abs((float)(ev.GetY() - downPosY));

    /// <summary>
    /// 判断是否上下滑动（如果上下滑动距离大于左右滑动距离则认为是上下滑动）
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public bool IsVerticalSwipe(MotionEvent ev) => downPosX != null && downPosY != null &&
                                                   Math.Abs((float)(ev.GetY() - downPosY)) >
                                                   Math.Abs((float)(ev.GetX() - downPosX));

    //上划
    public bool IsUpSwipe(MotionEvent ev, float height = 0)
    {
        if (downPosY != null)
        {
            float dy = downPosY - ev.GetY() ?? 0;
            if (dy > height)
            {
                OnUpSwipe?.Invoke(this, dy);
                return true;
            }
        }

        return false;
    }


    //下划
    public bool IsDownSwipe(MotionEvent ev, float height = 0)
    {
        if (downPosY != null)
        {
            float dy = ev.GetY() - downPosY ?? 0;
            if (dy > height)
            {
                OnDownSwipe?.Invoke(this, dy);
                return true;
            }
        }
        return false;
    }

    //左划
    public bool IsLeftSwipe(MotionEvent ev) =>
        downPosY != null && ev.GetX() - downPosX < 0;

    //右划
    public bool IsRightSwipe(MotionEvent ev) =>
        downPosY != null && ev.GetX() - downPosX > 0;


    public event EventHandler<float?>? OnUpSwipe;
    public event EventHandler<float?>? OnDownSwipe;
}