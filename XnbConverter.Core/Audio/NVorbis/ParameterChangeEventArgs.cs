using System;

namespace NVorbis;

/// <summary>
/// Event data for when a logical stream has a parameter change.
/// </summary>
[Serializable]
public class ParameterChangeEventArgs : EventArgs
{
	/// <summary>
	/// Gets the first packet after the parameter change.  This would typically be the parameters packet.
	/// </summary>
	public DataPacket FirstPacket { get; private set; }

	/// <summary>
	/// Creates a new instance of <see cref="T:NVorbis.ParameterChangeEventArgs" />.
	/// </summary>
	/// <param name="firstPacket">The first packet after the parameter change.</param>
	public ParameterChangeEventArgs(DataPacket firstPacket)
	{
		FirstPacket = firstPacket;
	}
}
