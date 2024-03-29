﻿using static XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue;

namespace XnbConverter.Xact.SoundBank.Entity;

public class XactSound
{
    internal uint[] AudioEngineFileOffsets; //AudioEngine文件偏移量
    public ushort CategoryId; //声音类别的ID

    public CueVariation CueVariation;
    public ushort ExtraDataLen; //额外数据的长度
    public ushort Filter; //滤波器
    public byte Flags; //声音的标志
    public int NumClips; //剪辑的数量
    public float Pitch; //音高
    public byte Priority; //优先级
    public int[] RpcCurves; //RPC曲线数组

    public XactClip[] SoundClips; //剪辑集合，包含了声音关联的剪辑数据。
    public ushort TrackIndex; //索引编号，表示声音所在的轨道的索引。
    public byte VolumeFlag; //音量标志
    public byte WaveBankIndex; //Wave Bank索引
}