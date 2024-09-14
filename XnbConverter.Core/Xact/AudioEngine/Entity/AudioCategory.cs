using System.Collections.Generic;
using XnbConverter.Xact.SoundBank.Entity;

namespace XnbConverter.Xact.AudioEngine.Entity;

public class AudioCategory
{
	public enum CrossfadeType
	{
		Linear,
		Logarithmic,
		EqualPower
	}

	public enum MaxInstanceBehavior
	{
		FailToPlay,
		Queue,
		ReplaceOldest,
		ReplaceQuietest,
		ReplaceLowestPriority
	}

	public float[] _volume;

	public AudioEngine engine;

	public float fadeIn;

	public float fadeOut;

	public CrossfadeType fadeType;

	public MaxInstanceBehavior InstanceBehavior;

	public byte instanceFlags;

	public bool instanceLimit;

	public bool isBackgroundMusic;

	public bool isPublic;

	public int maxInstances;

	public string name;

	public List<XactSound> Sounds;

	public ushort unkn;

	public byte visibilityFlags;

	public byte volumeDecibels;
}
