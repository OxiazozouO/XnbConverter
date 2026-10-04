namespace XnbConverter.Xact.SoundBank.Entity;

public class XactSound
{
	internal uint[] AudioEngineFileOffsets;

	public ushort CategoryId;

	public SoundBank.SoundCue.CueVariation CueVariation;

	public ushort ExtraDataLen;

	/// <summary>
	/// 该 Sound 在 .xsb 中的起始偏移。尾部存在未被解析的 ExtraData 区间，
	/// 因此写回时只在原位置就地修补已知字段，不整段重建。
	/// </summary>
	public uint FileOffset;

	public ushort Filter;

	public byte Flags;

	public int NumClips;

	public float Pitch;

	public byte Priority;

	public int[] RpcCurves;

	public XactClip[] SoundClips;

	public ushort TrackIndex;

	public byte VolumeFlag;

	public byte WaveBankIndex;
}
