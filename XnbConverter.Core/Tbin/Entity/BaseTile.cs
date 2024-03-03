using System.Collections.Generic;

namespace XnbConverter.Tbin.Entity;

//一层中的单个瓷砖
public class BaseTile
{
    /// <summary>
    ///     此磁贴的属性。
    /// </summary>
    public List<Propertie> Properties { get; set; }
}