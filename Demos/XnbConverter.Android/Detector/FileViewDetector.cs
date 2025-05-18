using Android.Content;
using Android.Views;
using Bumptech.Glide;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Detector;

public class FileViewDetector : Java.Lang.Object, View.IOnTouchListener, GestureDetector.IOnGestureListener
{
    private readonly MainActivity activity;
    private GestureDetector gestureDetector;
    private List<FileInfo> _files;
    private readonly View _root;

    public float startX, startY;
    public int pos;

    public FileViewDetector(Context context, View root, List<FileInfo> files, FileInfo file)
    {
        this.activity = (MainActivity)context;
        _files = files;
        _root = root;
        pos = files.IndexOf(file);
        gestureDetector = new GestureDetector(context, this);
    }

    public bool OnTouch(View v, MotionEvent e)
    {
        return gestureDetector.OnTouchEvent(e);
    }

    public bool OnDown(MotionEvent e)
    {
        startX = e.GetX();
        startY = e.GetY();
        return true;
    }

    public void OnShowPress(MotionEvent e)
    {
    }

    public bool OnSingleTapUp(MotionEvent e)
    {
        return false;
    }

    public bool OnScroll(MotionEvent e1, MotionEvent e2, float distanceX, float distanceY)
    {
        return false;
    }

    public void OnLongPress(MotionEvent e)
    {
    }

    public bool OnFling(MotionEvent e1, MotionEvent e2, float velocityX, float velocityY)
    {
        float diffX = e2.GetX() - e1.GetX();
        float diffY = e2.GetY() - e1.GetY();

        if (Math.Abs(diffX) > Math.Abs(diffY))
        {
            // 判断左右滑动
            if (diffX > 20)
            {
                // 向右滑动，切换到下一张图片的逻辑
                pos--;
                if (pos == -1)
                {
                    ++pos;
                    return true;
                }
            }
            else if (diffX < -20)
            {
                // 向左滑动，切换到上一张图片的逻辑
                pos++;
                if (pos == _files.Count)
                {
                    pos--;
                    return true;
                }
            }

            Glide.With(activity)
                .Load(_files[pos].Root)
                .Into(_root);
        }
        else
        {
            // 判断上下滑动
            if (diffY > 0)
            {
                // 向下滑动，执行退出逻辑
                // activity.Finish();
            }
        }

        return false;
    }
}