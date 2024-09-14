using System;

namespace XnbConverter.Entity;

public class Enum<T> where T : Enum
{
	public T Value;
}
