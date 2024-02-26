using System.Reflection;
using Microsoft.Xna.Framework.Content;

namespace XnbConverter.Readers.Mono;

public class ReflectiveReader<TV> : BaseReader where TV : new()
{
    private int[] _readIndex;
    private List<Type> _types = new();
    private List<object> _allPro = new();

    public override bool IsValueType()
    {
        return false;
    }

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        var type = typeof(TV);
        List<PropertyInfo> properties = GetAllProperties(type); // 获取所有属性
        List<FieldInfo> fields = GetAllFields(type); // 获取所有字段

        // 设置属性的值
        _allPro.AddRange(properties);
        foreach (var p in properties) _types.Add(p.PropertyType);

        // 设置字段的值
        _allPro.AddRange(fields);
        foreach (var f in fields) _types.Add(f.FieldType);

        _readIndex = new int[_types.Count];
        for (var i = 0; i < _types.Count; i++) _readIndex[i] = readerResolver.GetIndex(_types[i]);
    }

    public override object Read()
    {
        var result = new TV();

        for (var i = 0; i < _types.Count; i++)
        {
            var v = _types[i].IsValueType switch
            {
                true => readerResolver.ReadValue(_readIndex[i]),
                _ => readerResolver.Read_Null(_readIndex[i])
            };
            switch (_allPro[i])
            {
                case PropertyInfo p:
                    p.SetValue(result, v);
                    break;
                case FieldInfo f:
                    f.SetValue(result, v);
                    break;
            }
        }

        return result;
    }

    public override void Write(object input)
    {
        for (var i = 0; i < _types.Count; i++)
        {
            var value = _allPro[i] switch
            {
                PropertyInfo p =>
                    p.GetValue(_types[i]),
                FieldInfo f =>
                    f.GetValue(_types[i])
            };
            if (_types[i].IsValueType)
                readerResolver.Write(_readIndex[i], value);
            else
                readerResolver.Write_Null(_readIndex[i], value);
        }
    }

    public static List<PropertyInfo> GetAllProperties(Type type)
    {
        List<PropertyInfo> list = new();
        var properties = type.GetProperties(attrs);
        foreach (var p in properties)
            if (getPro(p))
                list.Add(p);

        return list;
    }

    private static bool getPro(PropertyInfo p)
    {
        var b = p.GetGetMethod(true);
        if (b == null || b != b.GetBaseDefinition()) return false;
        if (!p.CanRead) return false;
        if (p.GetIndexParameters().Any()) return false;
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

    private const BindingFlags attrs = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance |
                                       BindingFlags.DeclaredOnly;

    public static List<FieldInfo> GetAllFields(Type type)
    {
        List<FieldInfo> list = new();
        var fields = type.GetFields(attrs);
        foreach (var f in fields)
            if (getFi(f))
                list.Add(f);

        return list;
    }

    private static bool getFi(FieldInfo p)
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