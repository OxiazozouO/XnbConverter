namespace XnbConverter.Tbin.Entity;

public class Propertie
{
    public string Key { get; set; }
    public byte Type { get; set; }
    public object Value { get; set; }
    
    public List<object> Parse()
    {
        List<object> parsedData = new List<object>();

        string[] elements = ((string)Value).Split(' ');

        foreach (string element in elements)
        {

            if (int.TryParse(element, out int intValue))
            {
                parsedData.Add(intValue);
            }
            else if (double.TryParse(element, out double floatValue))
            {
                parsedData.Add(floatValue);
            }
            else
            {
                parsedData.Add(element);
            }
        }

        return parsedData;
    }
}