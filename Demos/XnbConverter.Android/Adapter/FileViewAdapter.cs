using Android.Content;
using Android.Views;
using Android.Views.Animations;
using Bumptech.Glide;
using Bumptech.Glide.Load.Engine;
using XnbConverter.Android.Helper;
using XnbConverter.Android.Holders;
using XnbConverter.Android.ViewModel;
using XnbConverter.Configurations;
using XnbConverter.Utilities;
using File = Java.IO.File;
using FileInfo = XnbConverter.Android.Model.FileInfo;

namespace XnbConverter.Android.Adapter;

public class FileViewAdapter : BaseAdapter<FileInfo>
{
    private bool _selectMode;

    public bool SelectMode
    {
        get => _selectMode;
        set
        {
            bool b = _selectMode != value;
            _selectMode = value;
            if (!_selectMode) selectedFiles.Clear();
            ((MainActivity)mContext).OnSelected(this);
            if (b) NotifyDataSetChanged();
        }
    }

    private List<FileInfo> _fileLists;

    public HashSet<int> selectedFiles = new HashSet<int>();

    public Stack<File> stack = new Stack<File>();

    private readonly Dictionary<String, int> _fileTypeIconMap;

    private float myx;
    private bool Yes = true;
    private int State = 0;

    private const int DefPosY = -160;
    private const int PosY = 10;
    private const int MaxPosY = 160;

    private Context mContext { get; set; }

    public TouchEventHelper TouchEventHelper = new TouchEventHelper();

    public FileViewAdapter(Context context, string path, FileViewHelper helper)
    {
        mContext = context;
        if (string.IsNullOrEmpty(path)) path = ConfigHelper.BasePath;
        var file = new Java.IO.File(path);
        stack.Push(file);
        _fileLists = JavaFileHelper.GetFileList(file).OrderBy(f => f.FileName).ToList();

        helper.FileList.Adapter = this;

        helper.FileList.Touch += (sender, e) =>
        {
            switch (e?.Event?.Action)
            {
                case MotionEventActions.Down:
                    TouchEventHelper.Init(e);
                    break;
                case MotionEventActions.Move:
                    if (TouchEventHelper.IsEmpty())
                        TouchEventHelper.Init(e.Event, true);
                    break;
            }

            switch (e?.Event?.Action)
            {
                case MotionEventActions.Down:
                    helper.L1.Top = DefPosY;
                    break;
                case MotionEventActions.Move:
                    if (TouchEventHelper.IsUpSwipe(e.Event))
                    {
                        helper.L1.Top = DefPosY;
                        helper.Bar.Visibility = ViewStates.Invisible;
                        return;
                    }

                    if (TouchEventHelper.IsDownSwipe(e.Event, 60) && helper.FileList.FirstVisiblePosition == 0)
                    {
                        helper.L1.Top = DefPosY;
                        helper.L1.ClearAnimation();
                        helper.Bar.ClearAnimation();
                        if (helper.Bar.Visibility == ViewStates.Invisible) helper.Bar.Visibility = ViewStates.Visible;
                        helper.L1.Top = Math.Min(MaxPosY, (int)(TouchEventHelper.GetCurrentY(e.Event) + DefPosY - 60));
                    }

                    break;
                case MotionEventActions.Up:
                case MotionEventActions.HoverExit:
                case MotionEventActions.Mask:
                case MotionEventActions.Cancel:
                case MotionEventActions.Outside:
                    if (!TouchEventHelper.IsDownSwipe(e.Event, 60)) return;
                    TouchEventHelper.Clear();
                    if (helper.L1.Top >= PosY && helper.L1.Top > DefPosY)
                    {
                        var dy = PosY - helper.L1.Top;
                        TranslateAnimation animation =
                            new TranslateAnimation(0, 0, 0, dy)
                            {
                                Duration = 200 * (MaxPosY - PosY) / -dy
                            };
                        animation.AnimationEnd += (_, _) =>
                        {
                            helper.L1.Top = PosY;
                            _fileLists = JavaFileHelper.GetFileList(stack.Peek()).OrderBy(f => f.FileName).ToList();
                            NotifyDataSetChanged();
                            Anime2();
                        };
                        helper.L1.StartAnimation(animation);
                    }
                    else
                    {
                        Anime2();
                    }

                    break;
                default:
                    break;

                    void Anime2()
                    {
                        AnimationSet set = new AnimationSet(true);

                        var dy = helper.L1.Top + DefPosY; //负数
                        set.AddAnimation(new ScaleAnimation(1.0f, 0f, 1.0f, 0f, Dimension.RelativeToSelf, 0.5f,
                            Dimension.RelativeToSelf, 0f)
                        {
                            Interpolator = new AccelerateInterpolator(),
                            Duration = dy == 0 ? 0 : 400 * (PosY - DefPosY) / -dy,
                            FillAfter = true
                        });
                        set.AddAnimation(new TranslateAnimation(0, 0, 0, dy)
                        {
                            Duration = dy == 0 ? 0 : 500 * (PosY - DefPosY) / -dy
                        });

                        set.AnimationEnd += (_, _) =>
                        {
                            helper.L1.Top = DefPosY;
                            helper.Bar.Visibility = ViewStates.Invisible;
                        };
                        helper.L1.StartAnimation(set);
                    }
            }
        };
    }

    public override FileInfo this[int position] => _fileLists[position];

    public override int Count => _fileLists.Count;

    public override long GetItemId(int position) => position;

    public override View? GetView(int position, View? convertView, ViewGroup? parent)
    {
        var fileModel = _fileLists[position];
        FileViewItemHolder? holder;
        if (convertView is null)
        {
            convertView = LayoutInflater.From(mContext).Inflate(Resource.Layout.file_view_item, null);
            holder = new FileViewItemHolder(convertView);
            convertView.Tag = holder;

            holder.Root.LongClick += (_, _) =>
            {
                holder.CheckBox.Tag ??= position;
                var pos = (int)holder.CheckBox.Tag;
                var model = _fileLists[pos];

                if (SelectMode)
                {
                    if (selectedFiles.Count == 0)
                    {
                        selectedFiles.Add(pos);
                        SelectChangeUi(holder, model, true);
                    }
                    ((Activity)mContext).OpenContextMenu(holder.Root);
                }
                else
                {
                    selectedFiles.Clear();
                    selectedFiles.Add(pos);
                    SelectChangeUi(holder, model, true);
                    SelectMode = true;
                }
            };

            holder.Root.Click += (sender, args) =>
            {
                holder.CheckBox.Tag ??= position;
                var pos = (int)holder.CheckBox.Tag;
                var model = _fileLists[pos];
                if (SelectMode)
                {
                    if (selectedFiles.Add(pos))
                    {
                        SelectChangeUi(holder, model, true);
                    }
                    else
                    {
                        selectedFiles.Remove(pos);
                        SelectChangeUi(holder, model, false);
                    }

                    return;
                }

                if (model.IsFolder)
                {
                    stack.Push(model.Root);
                    ChangeFileList(model.Root);
                    return;
                }

                if (JavaFileHelper.ImageTypes.Contains(model.FileType))
                {
                    var images = _fileLists
                        .Where(f => JavaFileHelper.ImageTypes.Contains(f.FileType))
                        .Select(f => f.Root)
                        .ToList();
                    ((MainActivity)mContext).ImageViewerHelper.Init(images, model.Root);
                    return;
                }
            };

            holder.Root.Touch += (sender, args) =>
            {
                var root = holder.Root.FindAncestorById<LinearLayout>(Resource.Id.id_left);
                root ??= holder.Root.FindAncestorById<LinearLayout>(Resource.Id.id_right);

                if (root is not null)
                    ((MainActivity)mContext).LayoutOnClick(root);
                args.Handled = false;
            };
        }
        else
        {
            holder = (FileViewItemHolder)convertView.Tag;
        }

        holder.CheckBox.Tag = position;


        #region 写入模型数据

        // 设置文件标题
        holder.Title.Text = fileModel.FileName;

        // 设置文件日期和大小
        holder.FileUpdateTime.Text = fileModel.UpdateTime();
        holder.FileSize.Text = fileModel.FileSize();

        #endregion

        #region 调整UI

        // 设置多选模式下的复选框
        holder.CheckBox.Visibility = SelectMode ? ViewStates.Visible : ViewStates.Gone;
        SelectChangeUi(holder, fileModel, selectedFiles.Contains(position));

        #endregion

        return convertView;
    }

    private void SelectChangeUi(FileViewItemHolder holder, FileInfo fileModel, bool b)
    {
        holder.CheckBox.Checked = b;
        if (b)
        {
            if (fileModel.IsFolder)
            {
                holder.Icon.SetImageResource(Resource.Drawable.ic_folder_select);
                return;
            }

            if (JavaFileHelper.FileTypes.TryGetValue(fileModel.FileType, out var resource))
            {
                // holder.Icon.SetImageResource(resource.Item2);
                if (JavaFileHelper.ImageTypes.Contains(fileModel.FileType))
                {
                    Glide.With(mContext)
                        .Load(fileModel.Root)
                        .SetDiskCacheStrategy(DiskCacheStrategy.All)
                        .DontAnimate()
                        .CenterCrop()
                        .Placeholder(resource.Item2)
                        .Into(holder.Icon);
                }
                else if (fileModel.FileType == ".xnb")
                {
                    if (_xnbCache.TryGetValue(fileModel.Root.AbsolutePath, out var info))
                    {
                        var file = new File(info.Item1);
                        if (file.Exists())
                        {
                            if (info.Item2 == fileModel.Root.LastModified())
                            {
                                Glide.With(mContext)
                                    .Load(file)
                                    .SetDiskCacheStrategy(DiskCacheStrategy.All)
                                    .DontAnimate()
                                    .CenterCrop()
                                    .Placeholder(resource.Item2)
                                    .Into(holder.Icon);
                                return;
                            }
                        }
                    }

                    var xnb = new XNB();
                    xnb.Decode(fileModel.Root.AbsolutePath);
                    // 保存文件
                    var ex = new File(ConfigHelper.XnbCachePath, FileUtils.RandomName + ".xnb");
                    if (xnb.ExportFiles(ex.AbsolutePath + ".config"))
                    {
                        _xnbCache[fileModel.Root.AbsolutePath] =
                            new Tuple<string, long>(ex.AbsolutePath, fileModel.Root.LastModified());
                    }

                    Glide.With(mContext)
                        .Load(ex)
                        .SetDiskCacheStrategy(DiskCacheStrategy.All)
                        .DontAnimate()
                        .CenterCrop()
                        .Placeholder(resource.Item2)
                        .Into(holder.Icon);
                }
                else
                {
                    holder.Icon.SetImageResource(resource.Item2);
                }
            }
            else
            {
                holder.Icon.SetImageResource(Resource.Drawable.ic_file__select);
            }
        }
        else
        {
            if (fileModel.IsFolder)
            {
                holder.Icon.SetImageResource(Resource.Drawable.ic_folder);
            }
            else if (JavaFileHelper.FileTypes.TryGetValue(fileModel.FileType, out var resource))
            {
                if (JavaFileHelper.ImageTypes.Contains(fileModel.FileType))
                {
                    Glide.With(mContext)
                        .Load(fileModel.Root)
                        .SetDiskCacheStrategy(DiskCacheStrategy.All)
                        .DontAnimate()
                        .CenterCrop()
                        .Placeholder(resource.Item1)
                        .Into(holder.Icon);
                }
                else
                {
                    holder.Icon.SetImageResource(resource.Item1);
                }
            }
            else
            {
                holder.Icon.SetImageResource(Resource.Drawable.ic_file_);
            }
        }
    }

    private Dictionary<string, Tuple<string, long>> _xnbCache = new();

    public void ChangeFileList(Java.IO.File file)
    {
        var fileList = JavaFileHelper.GetFileList(file);
        fileList = fileList.OrderBy(f => f.FileName).ToList();
        _fileLists = fileList;
        selectedFiles.Clear();
        NotifyDataSetChanged();
        OnFileListChanged?.Invoke(this, file);
    }

    public event EventHandler<File> OnFileListChanged;
}