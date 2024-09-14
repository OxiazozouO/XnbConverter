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
		string[] array = ((string)Value).Split(' ');
		foreach (string text in array)
		{
			double result2;
			if (int.TryParse(text, out var result))
			{
				parsedData.Add(result);
			}
			else if (double.TryParse(text, out result2))
			{
				parsedData.Add(result2);
			}
			else
			{
				parsedData.Add(text);
			}
		}
	}
}
