namespace XnbConverter.Readers;

public interface IReaderFileUtil<T>
{
	void Save(T input);

	T Load();
}
