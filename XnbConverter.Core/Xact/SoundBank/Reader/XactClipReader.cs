using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.SoundBank.Entity;
using static XnbConverter.Xact.SoundBank.Entity.XactClip;

namespace XnbConverter.Xact.SoundBank.Reader;

public class XactClipReader : BaseReader
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override object Read()
    {
        var result = new XactClip();

        result.VolumeDb = bufferReader.ReadByte();
        result.ClipOffset = bufferReader.ReadUInt32();
        result.FilterQAndFlags = bufferReader.ReadUInt16();
        result.FilterFrequency = bufferReader.ReadUInt16();
        Log.Debug(Helpers.I18N["XactClipReader.1"], result.ClipOffset);


        var oldPosition = bufferReader.BytePosition;
        bufferReader.BytePosition = (int)result.ClipOffset;

        var numEvents = bufferReader.ReadByte();
        result.WaveIndexs = new WaveIndex[numEvents];
        for (var i = 0; i < numEvents; i++)
        {
            var w = new WaveIndex();
            w.EventInfo = bufferReader.ReadUInt32();
            w.RandomOffset = bufferReader.ReadUInt16() * 0.001f;

            switch (w.EventInfo & 0x1F)
            {
                case 0:
                    // Stop Event
                    throw new NotImplementedException("Stop event");
                case 1:
                    w.unkn.Add(bufferReader.Read(1));
                    w.EventFlags = bufferReader.ReadByte();

                    w.TrackIndex = bufferReader.ReadUInt16();
                    w.WaveBankIndex = bufferReader.ReadByte();
                    w.LoopCount = bufferReader.ReadByte();
                    w.PanAngle = bufferReader.ReadUInt16() / 100.0f;
                    w.PanArc = bufferReader.ReadUInt16() / 100.0f;

                    // bufferReader.Skip(5);

                    Log.Debug(Helpers.I18N["XactClipReader.2"], w.TrackIndex);
                    break;
                case 3:
                    w.unkn.Add(bufferReader.Read(1));
                    w.EventFlags = bufferReader.ReadByte();

                    w.LoopCount = bufferReader.ReadByte();
                    w.PanAngle = bufferReader.ReadUInt16() / 100.0f;
                    w.PanArc = bufferReader.ReadUInt16() / 100.0f;

                    // The number of tracks for the variations.
                    w.NumTracks = bufferReader.ReadUInt16();

                    // Not sure what most of this is.
                    w.MoreFlags = bufferReader.ReadByte();

                    // Unknown!
                    w.unkn.Add(bufferReader.Read(5));

                    // Read in the variation playlist.
                    w.WaveBanks = new int[w.NumTracks];
                    w.Tracks = new int[w.NumTracks];
                    w.Weights = new byte[w.NumTracks][];
                    var totalWeights = 0;
                    for (var j = 0; j < w.NumTracks; j++)
                    {
                        w.Tracks[j] = bufferReader.ReadUInt16();
                        w.WaveBanks[j] = bufferReader.ReadByte();
                        var minWeight = bufferReader.ReadByte();
                        var maxWeight = bufferReader.ReadByte();
                        w.Weights[j] = new byte[]
                        {
                            minWeight,
                            maxWeight
                        };
                        totalWeights += maxWeight - minWeight;
                    }

                    break;

                case 4:
                    w.unkn.Add(bufferReader.Read(1));
                    w.EventFlags = bufferReader.ReadByte();

                    w.TrackIndex = bufferReader.ReadUInt16();
                    w.WaveBankIndex = bufferReader.ReadByte();
                    w.LoopCount = bufferReader.ReadByte();
                    w.PanAngle = bufferReader.ReadUInt16();
                    w.PanArc = bufferReader.ReadUInt16();

                    // Pitch variation range
                    w.MinPitch = bufferReader.ReadInt16() / 1000.0f;
                    w.MaxPitch = bufferReader.ReadInt16() / 1000.0f;

                    // Volume variation range
                    w.MinVolumeDecibels = bufferReader.ReadByte();
                    w.MaxVolumeDecibels = bufferReader.ReadByte();

                    // Filter variation
                    w.MinFrequency = bufferReader.ReadSingle();
                    w.MaxFrequency = bufferReader.ReadSingle();
                    w.MinQ = bufferReader.ReadSingle();
                    w.MaxQ = bufferReader.ReadSingle();

                    // Unknown!
                    w.unkn.Add(bufferReader.Read(1));

                    w.VariationFlags = bufferReader.ReadByte();

                    Log.Debug(Helpers.I18N["XactClipReader.3"]);
                    break;

                case 6:
                    // Unknown!
                    w.unkn.Add(bufferReader.Read(1));

                    // Event flags
                    w.EventFlags = bufferReader.ReadByte();

                    w.LoopCount = bufferReader.ReadByte();
                    w.PanAngle = bufferReader.ReadUInt16() / 100.0f;
                    w.PanArc = bufferReader.ReadUInt16() / 100.0f;

                    // Pitch variation range
                    w.MinPitch = bufferReader.ReadInt16() / 1000.0f;
                    w.MaxPitch = bufferReader.ReadInt16() / 1000.0f;

                    // Volume variation range
                    w.MinVolumeDecibels = bufferReader.ReadByte();
                    w.MaxVolumeDecibels = bufferReader.ReadByte();

                    // Filter variation range
                    w.MinFrequency = bufferReader.ReadSingle();
                    w.MaxFrequency = bufferReader.ReadSingle();
                    w.MinQ = bufferReader.ReadSingle();
                    w.MaxQ = bufferReader.ReadSingle();

                    // Unknown!
                    w.unkn.Add(bufferReader.Read(1));

                    // TODO: Still has unknown bits!
                    w.VariationFlags = bufferReader.ReadByte();
                    // The number of tracks for the variations.
                    w.NumTracks = bufferReader.ReadUInt16();

                    // Not sure what most of this is.
                    w.MoreFlags = bufferReader.ReadByte();

                    // Unknown!
                    w.unkn.Add(bufferReader.Read(5));

                    // Read in the variation playlist.
                    totalWeights = 0;
                    for (var j = 0; j < w.NumTracks; j++)
                    {
                        w.Tracks[j] = bufferReader.ReadUInt16();
                        w.WaveBanks[j] = bufferReader.ReadByte();
                        var minWeight = bufferReader.ReadByte();
                        var maxWeight = bufferReader.ReadByte();
                        w.Weights[j] = new byte[]
                        {
                            minWeight,
                            maxWeight
                        };

                        totalWeights += maxWeight - minWeight;
                    }

                    break;

                case 7:
                    // Pitch Event
                    throw new NotImplementedException("Pitch event");

                case 8:
                    // Unknown!
                    w.unkn.Add(bufferReader.Read(2));

                    // Event flags
                    var eventFlags = bufferReader.ReadByte();

                    // The replacement or additive volume.
                    throw new NotImplementedException();
                    // Unknown!
                    bufferReader.Read(9);
                    break;

                case 17:
                    // Volume Repeat Event
                    throw new NotImplementedException("Volume repeat event");

                case 9:
                    // Marker Event
                    throw new NotImplementedException("Marker event");

                default:
                    throw new Exception();
            }

            result.WaveIndexs[i] = w;
        }

        bufferReader.BytePosition = oldPosition; //??????
        return result;
    }

    public override void Write(object input)
    {
        throw new NotImplementedException();
    }
}