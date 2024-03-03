namespace XnbConverter.Xact.WaveBank.Entity;

public static class WaveMarks
{
    public const int Size = 4;

    //读取出来的数据仍然与Ascii对应
    public const string fmt = "fmt "; //fmt
    public const string data = "data"; //data
    public const string seek = "seek"; //seek
    public const string RIFF = "RIFF"; //RIFF
    public const string WAVE = "WAVE"; //WAVE
    public const string fact = "fact"; //face
    public const string LIST = "LIST"; //LIST
}