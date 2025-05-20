using System.Reflection;
using Microsoft.Xna.Framework.Content;

namespace XnbConverter.Readers.Mono;

public class ReflectiveReader<TV> : BaseReader where TV : new()
{
    private readonly List<object> _allPro = new List<object>();

    private readonly List<Type> _types = new List<Type>();

    private int[] _readIndex;

    private int _baseReadIndex = -1;

    public override bool IsValueType()
    {
        return false;
    }

    public override void Init(ReaderResolver resolver)
    {
        base.Init(resolver);
        Type typeFromHandle = typeof(TV);
        if (typeFromHandle.BaseType != null && typeFromHandle.BaseType != typeof(object))
        {
            _baseReadIndex = resolver.GetIndex(typeFromHandle.BaseType);
        }

        List<PropertyInfo> allProperties = GetAllProperties(typeFromHandle);
        List<FieldInfo> allFields = GetAllFields(typeFromHandle);
        _allPro.AddRange(allProperties);
        foreach (PropertyInfo item in allProperties)
        {
            _types.Add(item.PropertyType);
        }

        _allPro.AddRange(allFields);
        foreach (FieldInfo item2 in allFields)
        {
            _types.Add(item2.FieldType);
        }

        _readIndex = new int[_types.Count];
        for (int i = 0; i < _types.Count; i++)
        {
            TypeReadHelper.ReaderInfo readerInfo = TypeReadHelper.GetReaderInfo(_types[i].FullName);
            _types[i] = readerInfo.Entity;
            _readIndex[i] = resolver.GetIndex(readerInfo.Reader);
        }
    }

    public override object Read()
    {
        TV val = new TV();
        if (_baseReadIndex != -1)
        {
            object parent = readerResolver.ReadValue(_baseReadIndex);
            CopyParent(val, parent);
        }

        for (int i = 0; i < _allPro.Count; i++)
        {
            object v = _types[i].IsValueType
                ? readerResolver.ReadValue(_readIndex[i])
                : readerResolver.Read_Null(_readIndex[i]);

            SetValue(_allPro[i], val, v);
        }

        return val;
    }

    private void SetValue(object info, object result, object? v)
    {
        Type type = null;
        if (info is PropertyInfo propertyInfo)
        {
            type = propertyInfo.PropertyType;
        }
        else if (info is FieldInfo fieldInfo)
        {
            type = fieldInfo.FieldType;
        }

        object r;
        if (v == null || type == v.GetType())
        {
            r = v;
        }
        else
        {
            object obj = !(type.Name == "Nullable`1")
                ? Activator.CreateInstance(type)
                : Activator.CreateInstance(type.GenericTypeArguments[0]);
            r = obj;
            CopyFields(v, ref r);
        }

        if (!(info is PropertyInfo propertyInfo2))
        {
            if (info is FieldInfo fieldInfo2)
            {
                fieldInfo2.SetValue(result, r);
            }
        }
        else
        {
            propertyInfo2.SetValue(result, r);
        }
    }

    public override void Write(object content)
    {
        TV val = (TV)content;
        if (_baseReadIndex != -1)
        {
            readerResolver.WriteValue(_baseReadIndex, val);
        }

        for (int i = 0; i < _allPro.Count; i++)
        {
            object obj = _allPro[i];
            object value;
            if (!(obj is PropertyInfo propertyInfo))
            {
                if (!(obj is FieldInfo fieldInfo))
                {
                    throw new ArgumentOutOfRangeException();
                }

                value = fieldInfo.GetValue(val);
            }
            else
            {
                value = propertyInfo.GetValue(val);
            }

            object inp = value;
            object value2 = GetValue(_allPro[i], inp, _types[i]);
            if (_types[i].IsValueType)
            {
                readerResolver.WriteValue(_readIndex[i], value2);
            }
            else
            {
                readerResolver.Write_Null(_readIndex[i], value2);
            }
        }
    }

    private object? GetValue(object info, object? inp, Type resultType)
    {
        if (inp == null)
        {
            return null;
        }

        Type type = info is PropertyInfo propertyInfo ? propertyInfo.PropertyType :
            !(info is FieldInfo fieldInfo) ? null : fieldInfo.FieldType;
        if (type == resultType)
        {
            return inp;
        }

        object obj = !(resultType.Name == "Nullable`1")
            ? Activator.CreateInstance(resultType)
            : Activator.CreateInstance(resultType.GenericTypeArguments[0]);
        object r = obj;
        CopyFields(inp, ref r);
        return r;
    }

    private static void CopyParent(object child, object parent)
    {
        PropertyInfo[] properties = parent.GetType().GetProperties();
        foreach (PropertyInfo propertyInfo in properties)
        {
            propertyInfo.SetValue(child, propertyInfo.GetValue(parent));
        }
    }

    private static void CopyFields(object l, ref object? r)
    {
        Type type = l.GetType();
        if (r == null)
        {
            r = l;
            return;
        }

        Type type2 = r.GetType();

        if (type.IsValueType && type == type2)
        {
            r = l;
            return;
        }


        List<FieldInfo> allFields = GetAllFields(type);
        List<FieldInfo> allFields2 = GetAllFields(type2);
        foreach (FieldInfo lf in allFields)
        {
            FieldInfo fieldInfo = allFields2.FirstOrDefault((FieldInfo rf) => rf.Name == lf.Name);
            if (fieldInfo != null && lf.FieldType == fieldInfo.FieldType)
            {
                object value = lf.GetValue(l);
                fieldInfo.SetValue(r, value);
                continue;
            }

            throw new NotImplementedException();
        }
    }

    private static List<PropertyInfo> GetAllProperties(Type type)
    {
        List<PropertyInfo> list = new List<PropertyInfo>();
        PropertyInfo[] properties = type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance |
                                                       BindingFlags.Public | BindingFlags.NonPublic);
        foreach (PropertyInfo propertyInfo in properties)
        {
            if (IsProperties(propertyInfo))
            {
                list.Add(propertyInfo);
            }
        }

        return list;
    }

    private static bool IsProperties(PropertyInfo p)
    {
        MethodInfo getMethod = p.GetGetMethod(nonPublic: true);
        if (getMethod == null || getMethod != getMethod.GetBaseDefinition())
        {
            return false;
        }

        if (!p.CanRead)
        {
            return false;
        }

        ParameterInfo[] indexParameters = p.GetIndexParameters();
        int num = 0;
        if (num < indexParameters.Length)
        {
            _ = indexParameters[num];
            return false;
        }

        if (Attribute.GetCustomAttribute(p, typeof(ContentSerializerIgnoreAttribute)) is
            ContentSerializerIgnoreAttribute)
        {
            return false;
        }

        if (!(Attribute.GetCustomAttribute(p, typeof(ContentSerializerAttribute)) is ContentSerializerAttribute))
        {
            getMethod = p.GetGetMethod();
            if (getMethod == null || !getMethod.IsPublic)
            {
                return false;
            }

            if (!p.CanWrite)
            {
                throw new NotImplementedException();
            }
        }

        return true;
    }

    public static List<FieldInfo> GetAllFields(Type type)
    {
        List<FieldInfo> list = new List<FieldInfo>();
        FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic);
        foreach (FieldInfo fieldInfo in fields)
        {
            if (IsField(fieldInfo))
            {
                list.Add(fieldInfo);
            }
        }

        return list;
    }

    private static bool IsField(FieldInfo p)
    {
        if (Attribute.GetCustomAttribute(p, typeof(ContentSerializerIgnoreAttribute)) is
            ContentSerializerIgnoreAttribute)
        {
            return false;
        }

        if (!(Attribute.GetCustomAttribute(p, typeof(ContentSerializerAttribute)) is ContentSerializerAttribute) &&
            (!p.IsPublic || p.IsInitOnly))
        {
            return false;
        }

        return true;
    }
}