namespace XnbConverter.Readers;

public interface IReaderFileUtil<T>
{
    public void Save(T input);
    
    public T Load();
}