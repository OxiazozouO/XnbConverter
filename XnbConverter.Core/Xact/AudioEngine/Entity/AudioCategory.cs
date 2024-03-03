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

    // This is a bit gross, but we use an array here
    // instead of a field since AudioCategory is a struct
    // This allows us to save _volume when the user
    // holds onto a reference of AudioCategory, or when a cue
    // is created/loaded after the volume's already been set.
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