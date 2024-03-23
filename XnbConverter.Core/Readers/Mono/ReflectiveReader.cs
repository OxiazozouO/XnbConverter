using System.Reflection;
using Microsoft.Xna.Framework.Content;
using XnbConverter.Utilities;

namespace XnbConverter.Readers.Mono;

public class ReflectiveReader<TV> : BaseReader where TV : new()
{
    private const BindingFlags attrs = BindingFlags.NonPublic | BindingFlags.Public |
                                       BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private readonly List<object> _allPro = new();
    private readonly List<Type> _types = new();
    private int[] _readIndex;
    private int _baseReadIndex = -1;

    public override bool IsValueType()
    {
        return false;
    }

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        var type = typeof(TV);

        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            _baseReadIndex = readerResolver.GetIndex(type.BaseType);
        }

        var properties = GetAllProperties(type); // 获取所有属性
        var fields = GetAllFields(type); // 获取所有字段

        // 设置属性的值
        _allPro.AddRange(properties);

        foreach (var p in properties)
        {
            _types.Add(p.PropertyType);
        }

        // 设置字段的值
        _allPro.AddRange(fields);
        foreach (var f in fields) _types.Add(f.FieldType);

        _readIndex = new int[_types.Count];
        for (var i = 0; i < _types.Count; i++)
        {
            var info = TypeReadHelper.GetReaderInfo(_types[i].FullName);
            _types[i] = info.Entity;
            _readIndex[i] = readerResolver.GetIndex(info.Reader);
        }
    }

    public override object Read()
    {
        TV result = new TV();
        if (_baseReadIndex != -1)
        {
            object baseObj = readerResolver.ReadValue(_baseReadIndex);
            CopyParent(result, baseObj);
        }

        for (var i = 0; i < _allPro.Count; i++)
        {
            object? v = _types[i].IsValueType
                ? readerResolver.ReadValue(_readIndex[i])
                : readerResolver.Read_Null(_readIndex[i]);

            SetValue(_allPro[i], result, v);
        }

        return result;
    }

    private void SetValue(object info, object result, object? v) //v设置到result
    {
        object? put;
        Type tmp = null;
        if (info is PropertyInfo p)
        {
            tmp = p.PropertyType;
        }
        else if (info is FieldInfo f)
        {
            tmp = f.FieldType;
        }

        if (v == null || tmp == v.GetType())
        {
            put = v;
        }
        else
        {
            put = tmp.Name switch
            {
                "Nullable`1" => Activator.CreateInstance(tmp.GenericTypeArguments[0]),
                _ => Activator.CreateInstance(tmp)
            };

            CopyFields(v, ref put);
        }

        // Console.WriteLine(((MemberInfo)info).Name + "  " + put);
        switch (info)
        {
            case PropertyInfo pp:
                pp.SetValue(result, put);
                break;
            case FieldInfo ff:
                ff.SetValue(result, put);
                break;
        }
    }

    public override void Write(object content)
    {
        TV input = (TV)content;
        if (_baseReadIndex != -1)
        {
            readerResolver.WriteValue(_baseReadIndex, input);
        }

        for (var i = 0; i < _allPro.Count; i++)
        {
            var value = _allPro[i] switch
            {
                PropertyInfo p =>
                    p.GetValue(input),
                FieldInfo f =>
                    f.GetValue(input),
                _ => throw new ArgumentOutOfRangeException()
            };

            object? result = GetValue(_allPro[i], value, _types[i]);
            if (_types[i].IsValueType)
                readerResolver.WriteValue(_readIndex[i], result);
            else
                readerResolver.Write_Null(_readIndex[i], result);
        }
    }

    private object? GetValue(object info, object? inp, Type resultType)
    {
        if (inp is null) return null;
        object? put;
        Type? tmp = info switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => null
        };

        if (tmp == resultType)
        {
            return inp;
        }

        put = resultType.Name switch
        {
            "Nullable`1" => Activator.CreateInstance(resultType.GenericTypeArguments[0]),
            _ => Activator.CreateInstance(resultType)
        };

        CopyFields(inp, ref put);
        return put;
    }

    private static void CopyParent(object child, object parent)
    {
        Type p = parent.GetType();
        var pp = p.GetProperties();
        foreach (var propertyInfo in pp)
        {
            propertyInfo.SetValue(child, propertyInfo.GetValue(parent));
        }
    }

    /// <summary>
    /// copy l->r
    /// </summary>
    /// <param name="l"></param>
    /// <param name="r">result object</param>
    /// <exception cref="NotImplementedException"></exception>
    private static void CopyFields(object l, ref object? r)
    {
        // 获取源对象的类型信息
        Type lType = l.GetType();
        if (r == null)
        {
            r = l;
            return;
        }

        Type rType = r.GetType();

        var lfs = GetAllFields(lType); // 获取所有字段
        var rfs = GetAllFields(rType); // 获取所有字段

        // 遍历源对象的属性
        foreach (FieldInfo lf in lfs)
        {
            // 找到对应的目标属性
            FieldInfo rf = rfs.FirstOrDefault(rf => rf.Name == lf.Name);

            if (rf != null && lf.FieldType == rf.FieldType)
            {
                // 获取源对象属性的值
                object value = lf.GetValue(l);
                // 将值赋给目标对象的属性
                rf.SetValue(r, value);
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }

    private static List<PropertyInfo> GetAllProperties(Type type)
    {
        List<PropertyInfo> list = new();
        var properties = type.GetProperties(attrs);
        foreach (var p in properties)
            if (IsProperties(p))
                list.Add(p);
        return list;
    }

    private static bool IsProperties(PropertyInfo p)
    {
        var b = p.GetGetMethod(true);
        if (b == null || b != b.GetBaseDefinition()) return false;
        if (!p.CanRead) return false;
        foreach (var parameter in p.GetIndexParameters()) return false;
        // 如果成员被标记为忽略，则返回 null
        if (Attribute.GetCustomAttribute(p, typeof(ContentSerializerIgnoreAttribute)) is
            ContentSerializerIgnoreAttribute)
            return false;
        // 如果 ContentSerializerAttribute 属性为空：
        if (Attribute.GetCustomAttribute(p, typeof(ContentSerializerAttribute)) is not ContentSerializerAttribute)
        {
            b = p.GetGetMethod();
            if (b == null || !b.IsPublic) return false;
            // 如果属性不可写
            if (!p.CanWrite) throw new NotImplementedException();
        }

        return true;
    }

    public static List<FieldInfo> GetAllFields(Type type)
    {
        List<FieldInfo> list = new();
        var fields = type.GetFields(attrs);
        foreach (var f in fields)
            if (IsField(f))
                list.Add(f);

        return list;
    }

    private static bool IsField(FieldInfo p)
    {
        if (Attribute.GetCustomAttribute(p, typeof(ContentSerializerIgnoreAttribute)) is
            ContentSerializerIgnoreAttribute)
            return false;
        if (!(Attribute.GetCustomAttribute(p, typeof(ContentSerializerAttribute)) is ContentSerializerAttribute))
            // 如果字段不是公共或者是只读的，则不要
            if (!p.IsPublic || p.IsInitOnly)
                return false;

        return true;
    }
}