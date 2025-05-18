using System.Reflection;
using Android.Views;

namespace XnbConverter.Android.Attribute;


public abstract class ViewHolder<T> : Java.Lang.Object where T : View
{
    public readonly T Root;

    private int typeId = -1;

    public ViewHolder(global::Android.App.Activity activity)
    {
        InitViewBindRoot();
        Root = (T)activity.LayoutInflater.Inflate(typeId, null);
        InitViewBind();
        Init();
    }

    protected ViewHolder(T root)
    {
        Root = root;
        InitViewBind();
        InitViewBindRoot();
        Init();
    }

    private void InitViewBindRoot()
    {
        var attribute = GetType().GetCustomAttribute<ViewClassBindAttribute>();
        if (attribute != null) typeId = attribute.Id;
    }

    private void InitViewBind()
    {
        // 在这里实现查找属性和处理特性的逻辑
        var properties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var fields = GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);

        SetAttributes<PropertyInfo, ViewBindAttribute>(properties,
            (property, attribute) => { property.SetValue(this, Root.FindViewById(attribute.Id)); });

        SetAttributes<FieldInfo, ViewBindAttribute>(fields,
            (field, attribute) => { field.SetValue(this, Root.FindViewById(attribute.Id)); });

        SetAttributes<PropertyInfo, ViewHolderBindAttribute>(properties, (property, attribute) =>
        {
            if (CreateHolder(Root, attribute.Id, property.PropertyType, out var holder))
                property.SetValue(this, holder);
        });

        SetAttributes<FieldInfo, ViewHolderBindAttribute>(fields, (field, attribute) =>
        {
            if (CreateHolder(Root, attribute.Id, field.FieldType, out var holder))
                field.SetValue(this, holder);
        });

        SetAttributes<PropertyInfo, ViewHolderArrayBindAttribute>(properties, (property, attribute) =>
        {
            var type = property.PropertyType.GetGenericArguments()[0];
            var list = Activator.CreateInstance(property.PropertyType);
            MethodInfo addMethod = property.PropertyType.GetMethod("Add");

            foreach (var i in attribute.Id)
            {
                if (CreateHolder(Root, i, type, out var holder))
                    addMethod.Invoke(list, [holder]);
            }

            property.SetValue(this, list);
        });

        SetAttributes<FieldInfo, ViewHolderArrayBindAttribute>(fields, (field, attribute) =>
        {
            var type = field.FieldType.GetGenericArguments()[0];
            var list = Activator.CreateInstance(field.FieldType);
            MethodInfo addMethod = field.FieldType.GetMethod("Add");

            foreach (var i in attribute.Id)
            {
                if (CreateHolder(Root, i, type, out var holder))
                    addMethod.Invoke(list, [holder]);
            }

            field.SetValue(this, list);
        });
    }

    public static bool CreateHolder(View Root, int id, Type type, out object holder)
    {
        holder = null;
        var view = Root.FindViewById(id);
        var constructor = type.GetConstructor([typeof(View)]);
        if (view == null || constructor == null) return false;
        holder = constructor.Invoke([view]);
        return true;
    }

    protected abstract void Init();

    private static void SetAttributes<TK, TV>(TK[] fields, Action<TK, TV> action)
        where TV : System.Attribute
        where TK : MemberInfo
    {
        foreach (var field in fields)
        {
            var viewBindAttribute = field.GetCustomAttribute<TV>();
            if (viewBindAttribute != null)
            {
                action(field, viewBindAttribute);
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ViewBindAttribute(int id) : System.Attribute
{
    public int Id { set; get; } = id;
}

[AttributeUsage(AttributeTargets.Class)]
public class ViewClassBindAttribute(int id) : System.Attribute
{
    public int Id { set; get; } = id;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ViewHolderBindAttribute(int id) : System.Attribute
{
    public int Id { set; get; } = id;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ViewHolderArrayBindAttribute(int[] id) : System.Attribute
{
    public int[] Id { set; get; } = id;
}