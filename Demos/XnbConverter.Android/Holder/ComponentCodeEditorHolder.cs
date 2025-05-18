using Android.Views;
using XnbConverter.Android.Attribute;
using XnbConverter.Android.Components;
using XnbConverter.Android.Helper;
using static _Microsoft.Android.Resource.Designer.ResourceConstant;

namespace XnbConverter.Android.Holder;

[ViewClassBind(Layout.component_code_editor)]
public class ComponentCodeEditorHolder(global::Android.App.Activity activity) : ViewHolder<View>(activity)
{
    [ViewBind(Id.id_code_editor)] public CodeEditor Editor;
    [ViewBind(Id.id_code_mode_text)] public TextView Mode;
    [ViewBind(Id.id_code_editor_undo)] public ImageView Undo;
    [ViewBind(Id.id_code_editor_redo)] public ImageView Redo;

    protected override void Init()
    {
        Undo.CallClick(Editor.Undo);
        Redo.CallClick(Editor.Redo);
        Mode.CallClick(() =>
        {
            var modeText = Mode.Text;
            List<MsgItem> list =
            [
                new MsgItem { Text = "Text" },
                new MsgItem { Text = "Xml" },
                new MsgItem { Text = "JS" },
                new MsgItem { Text = "Java" }
            ];
            var index = list.FindIndex(item => item.Text == modeText);
            if (index == -1) index = 0;
            MsgBoxHelper
                .Builder(activity, "请选择文本的语言")
                .AddLisView(list, index)
                .Show(l =>
                {
                    var msgItem = list[(int)l[0]];
                    Editor.Mode = msgItem.Text switch
                    {
                        "Xml" => CodeMode.Xml,
                        "JS" => CodeMode.Js,
                        "Java" => CodeMode.Java,
                        _ => CodeMode.Text
                    };
                    Mode.Text = msgItem.Text;
                });
        });
    }

    public void Bind(string code, CodeMode mode)
    {
        Editor.Mode = mode;
        Mode.Text = mode switch
        {
            CodeMode.Xml => "Xml",
            CodeMode.Js => "JS",
            CodeMode.Java => "Java",
            _ => "Text"
        };

        if (Editor.IsReady)
        {
            Editor.Code = code;
        }
        else
        {
            Editor.AfterInitialLoad += (sender, b) => { Editor.Code = code; };
        }
    }
}