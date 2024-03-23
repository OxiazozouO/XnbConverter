using Android.Content;
using Android;
using Android.Database;
using Android.Provider;
using Environment = Android.OS.Environment;
using Uri = Android.Net.Uri;
using PMPermission = Android.Content.PM.Permission;

namespace XnbConverter.Android
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private ListView _listView;
        private List<string> _fileList;
        private ArrayAdapter<string> _adapter;
        private const int READ_EXTERNAL_STORAGE_REQUEST_CODE = 123;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            _listView = FindViewById<ListView>(Resource.Id.listView);
            _listView.ItemClick += ListView_ItemClick;

            Button browseButton = FindViewById<Button>(Resource.Id.browseButton);
            browseButton.Click += BrowseButton_Click;

            LoadFileList();
        }
        
        private void LoadFileList()
        {
            _fileList = new List<string>();
            string[] files = Directory.GetFiles(Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDownloads).AbsolutePath);
            foreach (string file in files)
            {
                _fileList.Add(System.IO.Path.GetFileName(file));
            }

            _adapter = new ArrayAdapter<string>(this, Resource.Layout.custom_list_item, _fileList);
            _listView.Adapter = _adapter;
        }


        private void ListView_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            string selectedFile = _fileList[e.Position];
            OpenFile(selectedFile);
        }

        private void OpenFile(string fileName)
        {
            string filePath = System.IO.Path.Combine(Environment.ExternalStorageDirectory.ToString(), fileName);
            Intent intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(Uri.Parse("file://" + filePath), "application/*");
            intent.SetFlags(ActivityFlags.ClearTop);
            StartActivity(intent);
        }
        
        private void BrowseButton_Click(object sender, EventArgs e)
        {
            if (CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == PMPermission.Granted)
            {
                Intent intent = new Intent(Intent.ActionGetContent);
                intent.SetType("*/*");
                StartActivityForResult(intent, 0);
            }
            else
            {
                RequestPermissions(new string[] { Manifest.Permission.ReadExternalStorage }, READ_EXTERNAL_STORAGE_REQUEST_CODE);
            }
        }
        
        

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            if (requestCode == 0 && resultCode == Result.Ok)
            {
                Uri uri = data.Data;
                string filePath = GetRealPathFromURI(uri);
                if (!string.IsNullOrEmpty(filePath))
                {
                    // Do something with the selected file
                }
            }
        }

        private string GetRealPathFromURI(Uri contentUri)
        {
            string[] proj = { MediaStore.Images.ImageColumns.Data };
            ICursor cursor = ContentResolver.Query(contentUri, proj, null, null, null);
            if (cursor == null)
                return null;
    
            try
            {
                int columnIndex = cursor.GetColumnIndexOrThrow(MediaStore.Images.ImageColumns.Data);
                cursor.MoveToFirst();
                return cursor.GetString(columnIndex);
            }
            finally
            {
                cursor.Close();
            }
        }
    }
}