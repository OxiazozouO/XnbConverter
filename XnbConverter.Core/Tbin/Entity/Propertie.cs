using System.Collections.Generic;

namespace XnbConverter.Tbin.Entity;

public class Propertie
{
    public string Key { get; set; }
    public byte Type { get; set; }
    public object Value { get; set; }

    public void Parse(out List<object> parsedData)
    {
        parsedData = new List<object>();
        var elements = ((string)Value).Split(' ');
        foreach (var element in elements)
            if (int.TryParse(element, out var intValue))
                parsedData.Add(intValue);
            else if (double.TryParse(element, out var floatValue))
                parsedData.Add(floatValue);
            else
                parsedData.Add(element);
    }
}