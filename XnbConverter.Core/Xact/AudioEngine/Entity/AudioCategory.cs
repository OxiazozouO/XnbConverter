using XnbConverter.Xact.SoundBank.Entity;

namespace XnbConverter.Xact.AudioEngine.Entity;

public class AudioCategory
{
    public string name;
    public AudioEngine engine;
    public List<XactSound> Sounds;
    
    // This is a bit gross, but we use an array here
    // instead of a field since AudioCategory is a struct
    // This allows us to save _volume when the user
    // holds onto a reference of AudioCategory, or when a cue
    // is created/loaded after the volume's already been set.
    public float[] _volume;
    public bool isBackgroundMusic;
    public bool isPublic;
    public bool instanceLimit;
    public int maxInstances;
    public MaxInstanceBehavior InstanceBehavior;

    public CrossfadeType fadeType;
    public float fadeIn;
    public float fadeOut;
    public byte instanceFlags;
    public byte volumeDecibels;
    public byte visibilityFlags;
    public ushort unkn;
    
    
    public enum MaxInstanceBehavior 
    {
        FailToPlay,
        Queue,
        ReplaceOldest,
        ReplaceQuietest,
        ReplaceLowestPriority,
    }

    public enum CrossfadeType 
    {
        Linear,
        Logarithmic,
        EqualPower,
    }
}