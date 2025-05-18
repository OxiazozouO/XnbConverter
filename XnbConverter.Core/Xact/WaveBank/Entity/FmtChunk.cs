using XnbConverter.Exceptions;

namespace XnbConverter.Xact.WaveBank.Entity;

public class FmtChunk
{
	public enum AudioFormats : short
	{
		Pcm = 1,
		AdpcmMs = 2,
		Ieee = 3,
		Ima4 = 17
	}

	public static class Tags
	{
		private const short Bit_DEPTH8 = 0;

		private const short BIT_DEPTH16 = 1;
	}

	public const uint defSize = 16u;

	public const ushort AdpcmMsCbSize = 32;

	private static readonly byte[] AdpcmMsBuff = new byte[32]
	{
		0, 0, 7, 0, 0, 1, 0, 0, 0, 2,
		0, 255, 0, 0, 0, 0, 192, 0, 64, 0,
		240, 0, 0, 0, 204, 1, 48, 255, 136, 1,
		24, 255
	};

	public ushort BitsPerSample;

	public ushort BlockAlign;

	public uint ByteRate;

	public ushort? CbSize;

	public byte[] ExtensionChunkData;

	public string FmtID = "fmt ";

	public uint FmtSize = 16u;

	public AudioFormats FmtTag;

	public ushort NumChannels;

	public uint SampleRate;

	public uint ExtensionChunkSize => (uint)(ExtensionChunkData.Length + 2);

	public bool CheckFmtID(string typeName)
	{
		bool flag2;
		switch (typeName)
		{
		case "SoundEffect":
			flag2 = FmtTag == AudioFormats.Pcm;
			if (!flag2)
			{
				throw new XnbError(Error.FmtChunk_1, FmtTag.ToString());
			}
			break;
		case "Xact":
		{
			AudioFormats fmtTag = FmtTag;
			bool flag = (uint)(fmtTag - 1) <= 1u;
			flag2 = flag;
			if (!flag2)
			{
				throw new XnbError(Error.FmtChunk_2, FmtTag.ToString());
			}
			break;
		}
		case "All":
		{
			AudioFormats fmtTag = FmtTag;
			bool flag = (uint)(fmtTag - 1) <= 2u || fmtTag == AudioFormats.Ima4 ? true : false;
			flag2 = flag;
			if (!flag2)
			{
				throw new XnbError(Error.FmtChunk_3);
			}
			break;
		}
		default:
			throw new NotImplementedException();
		}
		return flag2;
	}

	public void SetAdpcmMsExtensionChunk(ushort value)
	{
		CbSize = 32;
		ExtensionChunkData = new byte[AdpcmMsBuff.Length];
		Array.Copy(AdpcmMsBuff, ExtensionChunkData, AdpcmMsBuff.Length);
		BitConverter.GetBytes(value).CopyTo(ExtensionChunkData, 0);
	}
}
