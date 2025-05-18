using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Uri = Android.Net.Uri;
using Environment = Android.OS.Environment;

namespace XnbConverter.Android.Activity;

public abstract class BaseActivity : global::Android.App.Activity
{
    private readonly List<String> NoGranted = new List<string>(); //  未同意的权限

    private const int Step1Code = 114514;
    private const int Step2Code = 1919;
    public const int RequestCodeForDir = 510;

    private readonly string[] permissions =
    [
        //  应用所需权限
        Manifest.Permission.WriteExternalStorage,
        Manifest.Permission.ReadExternalStorage
    ];

    protected override void OnResume()
    {
        base.OnResume();
        CheckPermission();
    }


    private bool Step1Flag = false;
    // private bool Step2Flag = false;

    private void CheckPermission()
    {
        Step1Flag = Step1ManageExternalStorage();
        // Step2Flag = Step2ManageExternalStorage();

        Init();
    }

    private bool Step1ManageExternalStorage()
    {
        if (IsManageExternalStoragePermissionGranted()) return true;
        // 没有权限，需要请求权限
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(Uri.Parse("package:" + PackageName));
            StartActivityForResult(intent, Step1Code);
        }

        return IsManageExternalStoragePermissionGranted();
    }


    private bool IsManageExternalStoragePermissionGranted()
    {
        return Build.VERSION.SdkInt < BuildVersionCodes.R || Environment.IsExternalStorageManager;
    }

    protected bool Step2ManageExternalStorage()
    {
        foreach (var permission in permissions)
        {
            if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
            {
                NoGranted.Add(permission);
            }
        }

        //  未同意权限则请求权限，否则直接执行 init 方法
        if (NoGranted.Count == 0) return true;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            ActivityCompat.RequestPermissions(this, NoGranted.ToArray(), Step2Code);
        }

        return false;
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        switch (requestCode)
        {
            case Step1Code:
                if (IsManageExternalStoragePermissionGranted())
                {
                    // 用户授予了权限，可以执行需要该权限的操作
                    Init();
                }
                else
                {
                    // 用户拒绝了权限，需要提示用户或者采取其他措施
                    Toast.MakeText(this, "权限被拒绝", ToastLength.Short).Show();
                }
                break;
        }
    }

    // 处理权限请求结果
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        try
        {
            switch (requestCode)
            {
                case Step2Code:
                    var req = new List<string>();
                    var ban = new List<string>();
                    for (var i = 0; i < grantResults.Length; i++)
                    {
                        if (grantResults[i] == Permission.Granted) continue;
                        bool ret = ShouldShowRequestPermissionRationale(permissions[i]);
                        if (ret) //重新申请
                        {
                            req.Add(permissions[i]);
                        }
                        else //禁止
                        {
                            ban.Add(permissions[i]);
                        }
                    }

                    if (ban.Count > 0)
                    {
                        Toast.MakeText(this, "权限被禁止,请手动打开", ToastLength.Short).Show();
                        // StartActivity(new Intent(Settings.ActionApplicationDetailsSettings).SetData(Uri.FromParts(
                        // "package", PackageName, null)));
                        return;
                    }

                    if (req.Count > 0)
                    {
                        Toast.MakeText(this, "必要权限被拒绝", ToastLength.Short).Show();
                        CheckPermission();
                    }
                    else
                    {
                        Init();
                    }

                    bool allPermissionsGranted = true;
                    for (int i = 0; i < permissions.Length; i++)
                    {
                        if (grantResults[i] == Permission.Granted) continue;
                        allPermissionsGranted = false;

                        break;
                    }

                    if (allPermissionsGranted)
                    {
                        Init();
                    }
                    else
                    {
                        // 权限被拒绝，提示用户或采取适当的处理措施
                        Toast.MakeText(this, "权限被拒绝", ToastLength.Short).Show();
                        FinishAfterTransition();
                    }

                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    protected virtual bool Init()
    {
        // return Step2Flag && Step1Flag;
        return Step1Flag;
    }
}