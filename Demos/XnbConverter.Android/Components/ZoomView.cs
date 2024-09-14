using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Java.Lang;
using Exception = System.Exception;
using Math = System.Math;

namespace XnbConverter.Android.Components;

/// <summary>
/// 可根据点击/多指触控 放大,放小的ImageVIew
/// </summary>
public class ZoomImageView : ImageView, ViewTreeObserver.IOnGlobalLayoutListener
    , View.IOnTouchListener, ScaleGestureDetector.IOnScaleGestureListener
{
    private readonly Context? _context;

    private bool _isInit;

    //缩放工具
    private readonly Matrix _matrix = new Matrix();

    //默认状态的缩放比率
    private float _defScale;

    //缩放的最小值
    private float _minScale;

    //缩放的中间值
    private float _fillScale;

    //缩放的最大值
    private float _maxScale;

    //多点手势触 控缩放比率分析器
    private ScaleGestureDetector _scaleGestureDetector;
    public new EventHandler<MotionEvent>? Click;

    // 记录上一次多点触控的数量
    private int _lastPointereCount;
    private int _touchSlop;
    private bool _isCheckLeftAndRight;
    private bool _isCheckTopAndBottom;


    //----双击放大与缩小
    private GestureDetector _gestureDetector;

    //是否在缩放中
    private bool _isScaleing;

    private int _araeImgId = -1;
    private const float TOLERANCE = 0.001f;


    public bool IsDeselect { get; set; } = false;
    public EventHandler<MotionEvent>? ScaleBegin;

    #region Init

    protected ZoomImageView(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public ZoomImageView(Context? context, IAttributeSet? attrs, int defStyleAttr, int defStyleRes) : base(context,
        attrs, defStyleAttr, defStyleRes)
    {
        _context = context;
        Init();
    }

    public ZoomImageView(Context? context, IAttributeSet? attrs) : base(context, attrs)
    {
        _context = context;
        Init();
    }

    public ZoomImageView(Context? context) : base(context)
    {
        _context = context;
        Init();
    }

    public ZoomImageView(Context? context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        _context = context;
        Init();
    }

    #endregion


    private void Init()
    {
        if (_context is null) return;
        _scaleGestureDetector = new ScaleGestureDetector(_context, this);
        _gestureDetector = new GestureDetector(_context, new MGestureDetector(this));
        _touchSlop = (int)(ViewConfiguration.Get(_context)?.ScaledTouchSlop * 0.2f ?? 0);
        SetOnTouchListener(this);
        // _events = new List<MotionEvent>();
    }

    private Action? _updateAction;

    public Action? UpdateAction
    {
        set
        {
            _updateAction = value;
            value?.Invoke();
        }
    }

    public override Drawable? Drawable
    {
        get
        {
            if (base.Drawable is not null)
                base.Drawable.SetFilterBitmap(false);

            return base.Drawable;
        }
    }

    private class MGestureDetector(ZoomImageView root) : GestureDetector.SimpleOnGestureListener
    {
        /// <summary>
        ///  双击
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public override bool OnDoubleTap(MotionEvent e)
        {
            if (root._isScaleing)
                return true;
            root._isScaleing = true;
            float x = e.GetX();
            float y = e.GetY();
            float scale = root.GetScale();
            if (scale < root._defScale * 0.7f) //默认状态（在_baseScale附近）
            {
                root.PostDelayed(new AutoScaleRunnable(root, root._defScale, x, y), 16);
            }
            else if (scale < root._fillScale) //填充全屏状态  在_baseScale之上
            {
                root.PostDelayed(new AutoScaleRunnable(root, root._fillScale, x, y), 16);
            }
            else if (scale <= root._maxScale) //非常大的状态 在midScale之上
            {
                root.PostDelayed(new AutoScaleRunnable(root, root._defScale, x, y), 16);
            }

            return true;
        }


        public override bool OnSingleTapConfirmed(MotionEvent e)
        {
            if (root.Click == null) return false;
            if (e.DownTime < 200) return false;
            var recf = root.GetScaledImageRectF();
            float x = e.GetX();
            float y = e.GetY();
            if (x >= recf.Left && x <= recf.Right && y >= recf.Top && y <= recf.Bottom)
            {
                root.IsDeselect = false;
            }
            else
            {
                root.IsDeselect = true;
            }

            root.Click.Invoke(root, e);
            return true;
        }
    }


    /// <summary>
    /// 缩放动画
    /// </summary>
    /// <param name="root"></param>
    /// <param name="sEnd">要缩放的目标值</param>
    /// <param name="x">缩放的中心点</param>
    /// <param name="y">缩放的中心点</param>
    private class AutoScaleRunnable(ZoomImageView root, float sEnd, float x, float y) : Java.Lang.Object, IRunnable
    {
        private const float V = 0.07f;

        // 临时缩放比例，用于逐步接近目标缩放值
        private readonly float _ds = root.GetScale() < sEnd ? 1 + V : 1 - V;

        public void Run()
        {
            root._matrix.PostScale(_ds, _ds, x, y);
            root.CheckViewIsSubsetOfImageWhenTranslate(false);
            root.CheckImageIsSubsetOfViewWhenTranslate(false);
            root.CheckBorderAndCenterWhenScale(1, false);
            root.ImageMatrix = root._matrix;

            float s = root.GetScale();

            if ((_ds > 1.0f && s < sEnd) || (_ds < 1.0f && s > sEnd))
            {
                root.PostDelayed(this, 16);
            }
            else // 缩放结束
            {
                float scale = sEnd / s;
                root._matrix.PostScale(scale, scale, x, y);
                root.CheckBorderAndCenterWhenScale();
                root._isScaleing = false;
            }
        }
    }

    // 在View被添加到窗口的时候调用
    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        ViewTreeObserver?.AddOnGlobalLayoutListener(this);
    }

    // 移除监听
    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        ViewTreeObserver?.RemoveOnGlobalLayoutListener(this);
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        CheckBorderAndCenterWhenScale();
    }

    // 当布局完成后，获取图片的宽高
    public void OnGlobalLayout()
    {
        if (_araeImgId != -1)
        {
            _araeImgId = -1;
            return;
        }

        SetScaleType(ScaleType.Matrix);
        if (Drawable == null || Width == 0 || Height == 0) return;

        if (_isInit) return;
        float screenWeight = Height * 1.0f / Width;

        int imageH = Drawable.IntrinsicHeight; // 图片高度
        int imageW = Drawable.IntrinsicWidth; // 图片宽度
        float imageWeight = imageH * 1.0f / imageW;
        //如果当前屏幕高宽比 大于等于 图片高宽比,就缩放图片

        _defScale = Math.Min(Width * 1.0f / imageW, Height * 1.0f / imageH);

        if (screenWeight >= imageWeight)
        {
            //一个屏幕8个像素
            _minScale = 8f / imageW;
            _fillScale = Math.Max(Width * 1.0f / imageW, Height * 1.0f / imageH) + 0.1f;
            _maxScale = _defScale * 100f;

            //把图片移动到中心点去
            int dx = Width / 2 - imageW / 2;
            int dy = Height / 2 - imageH / 2;

            //设置缩放(全图浏览模式,用最小的缩放比率去查看图片就好了)/移动位置
            _matrix.PostTranslate(dx, dy);
            //根据
            _matrix.PostScale(_defScale, _defScale, Width * 1.0f / 2, Height * 1.0f / 2);
        }
        else
        {
            //将宽度缩放至屏幕比例缩放(长图,全图预览)
            float scale = Math.Max(Width * 1.0f / imageW, Height * 1.0f / imageH);

            //设置缩放比率
            _maxScale = scale;
            _fillScale = _maxScale / 2;
            _minScale = _maxScale / 4;
            _minScale = 0.1f;

            //因为是长图浏览,所以用最大的缩放比率去加载长图
            //_matrix.postTranslate(0, 0);
            _matrix.PostScale(_maxScale, _maxScale, 0, 0);
        }

        ImageMatrix = _matrix;
        _isInit = true;
    }

    private float _lastX;
    private float _lastY;
    private bool _isCanDrag;

    public bool OnTouch(View? view, MotionEvent? motionEvent)
    {
        if (_gestureDetector.OnTouchEvent(motionEvent))
            return true;

        //将触摸事件传递给ScaleGestureDetector
        if (motionEvent.PointerCount > 1)
            _scaleGestureDetector.OnTouchEvent(motionEvent);

        float x = 0;
        float y = 0;

        int pointerCount = motionEvent.PointerCount;

        for (int i = 0; i < pointerCount; i++)
        {
            x += motionEvent.GetX(i);
            y += motionEvent.GetY(i);
        }

        x /= pointerCount;
        y /= pointerCount;

        if (_lastPointereCount != pointerCount)
        {
            _isCanDrag = false;
            _lastX = x;
            _lastY = y;
        }

        _lastPointereCount = pointerCount;

        switch (motionEvent.Action)
        {
            case MotionEventActions.Down:
            {
                RectF rectF = GetScaledImageRectF();
                if (rectF.Width() > Width || rectF.Height() > Height)
                {
                    if (Math.Abs(rectF.Right - Width) < TOLERANCE || rectF.Left == 0) break;
                    try
                    {
                        Parent.RequestDisallowInterceptTouchEvent(true);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }

                break;
            }
            case MotionEventActions.Move:
            {
                float dx = x - _lastX;
                float dy = y - _lastY;

                if (!_isCanDrag)
                {
                    _isCanDrag = IsMoveAction(dx, dy);
                }

                //判断是否可以拖动
                if (_isCanDrag)
                {
                    //获取图片放大缩小后的宽高/top/left/right/bottom
                    RectF rectF = GetScaledImageRectF();

                    if (Drawable != null)
                    {
                        _isCheckLeftAndRight = rectF.Width() > Width;
                        _isCheckTopAndBottom = rectF.Height() > Height;

                        _matrix.PostTranslate(dx * 2, dy * 2);
                        //画布被困在里面
                        CheckViewIsSubsetOfImageWhenTranslate(false);
                        //画布框柱里面
                        CheckBorderAndCenterWhenScale();
                    }
                }

                _lastX = x;
                _lastY = y;

                //获取图片放大缩小后的宽高/top/left/right/bottom
                RectF rect = GetScaledImageRectF();
                if (rect.Width() > Width + 0.01f || rect.Height() > Height + 0.01f)
                {
                    if (Math.Abs(rect.Right - Width) < TOLERANCE || rect.Left == 0)
                        break;
                    try
                    {
                        Parent.RequestDisallowInterceptTouchEvent(true);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }

                break;
            }
            case MotionEventActions.Cancel:
            case MotionEventActions.Up:
            {
                _lastPointereCount = 0;
                break;
            }
        }

        return true;
    }

    //缩放后的图片比较大
    private void CheckViewIsSubsetOfImageWhenTranslate(bool isSet = true)
    {
        RectF rectF = GetScaledImageRectF();

        float deltaX = 0;
        float deltaY = 0;

        if (rectF.Top > 0 && _isCheckTopAndBottom)
        {
            deltaY = -rectF.Top;
        }

        if (rectF.Bottom < Height && _isCheckTopAndBottom)
        {
            deltaY = Height - rectF.Bottom;
        }


        if (rectF.Left > 0 && _isCheckLeftAndRight)
        {
            deltaX = -rectF.Left;
        }

        if (rectF.Right < Width && _isCheckLeftAndRight)
        {
            deltaX = Width - rectF.Right;
        }

        if (deltaY == 0 && deltaX == 0)
        {
            return;
        }

        _matrix.PostTranslate(deltaX, deltaY);
        if (isSet) ImageMatrix = _matrix;
    }

    //画板比较大
    private void CheckImageIsSubsetOfViewWhenTranslate(bool isSet = true)
    {
        RectF image = GetScaledImageRectF();

        float deltaX = 0;
        float deltaY = 0;
        if (image.Width() <= Width && image.Height() <= Height)
        {
            if (image.Left < 0) deltaX = -image.Left;

            if (image.Right > Width) deltaX = Width - image.Right;

            if (image.Top < 0) deltaY = -image.Top;

            if (image.Bottom > Height) deltaY = Height - image.Bottom;

            _matrix.PostTranslate(deltaX, deltaY);
            if (isSet) ImageMatrix = _matrix;
        }
    }

    // 判断是否足以触发移动事件
    private bool IsMoveAction(float dx, float dy) => Math.Sqrt(dx * dx + dy * dy) > _touchSlop;

    // 缩放
    public bool OnScale(ScaleGestureDetector detector)
    {
        float scaleFactor = detector.ScaleFactor; //获取用户手势判断出来的缩放值

        //没有图片
        if (Drawable == null) return true;

        //缩放范围控制
        float scale = GetScale();
        if ((scale < _maxScale && scaleFactor > 1.0f) || (scale > _minScale && scaleFactor < 1.0f))
        {
            // 如果缩放后小于最小值，则取最小值
            if (scaleFactor * scale < _minScale)
            {
                scaleFactor = _minScale / scale;
            }

            // 如果缩放后大于最大值，则取最大值
            if (scale * scaleFactor > _maxScale)
            {
                scaleFactor = _maxScale / scale;
            }

            _matrix.PostScale(scaleFactor, scaleFactor, detector.FocusX, detector.FocusY);
            CheckBorderAndCenterWhenScale();
        }

        return true;
    }

    /// <summary>
    /// 在缩放的时候进行边界,位置 检查
    /// </summary>
    /// <param name="v">缩放速度</param>
    private void CheckBorderAndCenterWhenScale(float v = 1f, bool isSet = true)
    {
        //获取图片放大缩小后的宽高/top/left/right/bottom
        RectF rectF = GetScaledImageRectF();

        //x和y方向上 需要平移的距离
        float deltaX = 0;
        float deltaY = 0;

        if (rectF.Width() >= Width)
        {
            if (rectF.Left > 0)
                deltaX = -rectF.Left;
            if (rectF.Right < Width)
                deltaX = Width - rectF.Right;
        }
        else
        {
            deltaX = Width / 2f - rectF.Right + rectF.Width() / 2;
        }

        if (rectF.Height() >= Height)
        {
            if (rectF.Top > 0)
                deltaY = 0;
            if (rectF.Bottom < Height)
                deltaY = Height - rectF.Bottom;
        }
        else
        {
            deltaY = Height / 2f - rectF.Bottom + rectF.Height() / 2;
        }

        _matrix.PostTranslate(deltaX * v, deltaY * v);
        if (isSet) ImageMatrix = _matrix;
    }

    // 获取图片放大缩小后的宽高/top/left/right/bottom
    private RectF GetScaledImageRectF()
    {
        RectF rectF = new RectF();

        if (Drawable != null)
        {
            rectF.Set(0, 0, Drawable.IntrinsicWidth, Drawable.IntrinsicHeight);
            _matrix.MapRect(rectF);
        }

        return rectF;
    }

    public bool OnScaleBegin(ScaleGestureDetector scaleGestureDetector)
    {
        ScaleBegin?.Invoke(this, null);
        return true; //缩放开始,返回true 用于接收后续时间
    }

    public void OnScaleEnd(ScaleGestureDetector scaleGestureDetector)
    {
    }

    // 获取当前的缩放比率
    private float GetScale()
    {
        float[] values = new float[9];
        _matrix.GetValues(values);
        return values[Matrix.MscaleX];
    }

    public override void SetImageBitmap(Bitmap? bm)
    {
        ReSetState();
        base.SetImageBitmap(bm);
    }

    public override void SetImageResource(int resId)
    {
        ReSetState();
        base.SetImageResource(resId);
    }

    public override void SetImageDrawable(Drawable? drawable)
    {
        ReSetState();
        base.SetImageDrawable(drawable);
    }

    // 设置初始化状态为false
    private void ReSetState()
    {
        _isInit = false;
        _matrix.Reset();
    }
}