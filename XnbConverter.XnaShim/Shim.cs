using System;

// MonoGame.Framework 的替身。
//
// 为什么需要它：游戏的数据程序集（如 StardewValley.GameData.dll）在成员签名里引用了
// Microsoft.Xna.Framework.*。少了被引用的程序集，运行时连 Assembly.GetType("...LocationData")
// 都会失败 —— 不是"某些类型读不到"，是整个程序集一个类型都建不起来。
//
// 为什么不能用工具自己的程序集顶替（哪怕类型同名同命名空间）：运行时是按
// **程序集简单名 + 命名空间 + 类型名** 认类型的，借出去的程序集必须真的叫 MonoGame.Framework。
// 名字不符（包括 Reflection.Emit 现场造的）一律被拒：FileLoadException 0x80131509。
//
// 所以这个程序集只干两件事：
//   1. 承载运行时要认的那个「名字」；
//   2. 装下 Point / Rectangle / Vector2 —— 它们本来就是 MonoGame 也有的纯字段结构体，
//      放这里两边共用一份，不用再各写一遍。
//      之所以能共用：形状和真 MonoGame 完全一致，所以玩家往 .config/custom dll 放真 MonoGame
//      覆盖时也不会出问题（见 RootPath.InitDll 的解析兜底）。
//
// 注意：工具的其它模型类型（Texture2D / SpriteFont / Effect / XmlSource / DynamicSpriteFont /
// IntVector2）以及 Vector3 / Vector4 **不能**搬进来 —— 它们带着 MonoGame 没有的成员
// （例如 Vector3.HalfAdjust、Vector4.CompareAnyLessThan）。搬进来之后一旦有人放真 MonoGame，
// 这些名字会被真货顶掉，调用就变成 MissingMethodException。
namespace Microsoft.Xna.Framework.Content
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ContentSerializerAttribute : Attribute
    {
        private string _collectionItemName;

        public ContentSerializerAttribute()
        {
            AllowNull = true;
        }

        public bool AllowNull { get; set; }

        public string CollectionItemName
        {
            get { return string.IsNullOrEmpty(_collectionItemName) ? "Item" : _collectionItemName; }
            set { _collectionItemName = value; }
        }

        public string ElementName { get; set; }

        public bool FlattenContent { get; set; }

        public bool HasCollectionItemName
        {
            get { return !string.IsNullOrEmpty(_collectionItemName); }
        }

        public bool Optional { get; set; }

        public bool SharedResource { get; set; }

        public ContentSerializerAttribute Clone()
        {
            return new ContentSerializerAttribute
            {
                AllowNull = AllowNull,
                _collectionItemName = _collectionItemName,
                ElementName = ElementName,
                FlattenContent = FlattenContent,
                Optional = Optional,
                SharedResource = SharedResource
            };
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ContentSerializerIgnoreAttribute : Attribute
    {
    }
}
