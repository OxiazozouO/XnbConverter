namespace XnbConverter.Xact.AudioEngine.Entity;

public class ReverbSettings
{
	public DspParameter[] Parameters = new DspParameter[22];

	public float this[int index] => Parameters[index].Value;

	public float ReflectionsDelayMs => Parameters[0].Value;

	public float ReverbDelayMs => Parameters[1].Value;

	public float PositionLeft => Parameters[2].Value;

	public float PositionRight => Parameters[3].Value;

	public float PositionLeftMatrix => Parameters[4].Value;

	public float PositionRightMatrix => Parameters[5].Value;

	public float EarlyDiffusion => Parameters[6].Value;

	public float LateDiffusion => Parameters[7].Value;

	public float LowEqGain => Parameters[8].Value;

	public float LowEqCutoff => Parameters[9].Value;

	public float HighEqGain => Parameters[10].Value;

	public float HighEqCutoff => Parameters[11].Value;

	public float RearDelayMs => Parameters[12].Value;

	public float RoomFilterFrequencyHz => Parameters[13].Value;

	public float RoomFilterMainDb => Parameters[14].Value;

	public float RoomFilterHighFrequencyDb => Parameters[15].Value;

	public float ReflectionsGainDb => Parameters[16].Value;

	public float ReverbGainDb => Parameters[17].Value;

	public float DecayTimeSec => Parameters[18].Value;

	public float DensityPct => Parameters[19].Value;

	public float RoomSizeFeet => Parameters[20].Value;

	public float WetDryMixPct => Parameters[21].Value;
}
