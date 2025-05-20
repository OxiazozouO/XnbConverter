namespace XnbConverter.Xact.SoundBank.Entity;

public class XactSound
{
	internal uint[] AudioEngineFileOffsets;

	public ushort CategoryId;

	public SoundBank.SoundCue.CueVariation CueVariation;

	public ushort ExtraDataLen;

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
