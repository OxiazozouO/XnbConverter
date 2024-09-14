using System;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class FmtChunkReader : BaseReader, IReaderFileUtil<FmtChunk>
{
	public void Save(FmtChunk fmtChunk)
	{
		bufferWriter.WriteAsciiString(fmtChunk.FmtID);
		bufferWriter.WriteUInt32(fmtChunk.FmtSize);
		bufferWriter.WriteInt16((short)fmtChunk.FmtTag);
		bufferWriter.WriteUInt16(fmtChunk.NumChannels);
		bufferWriter.WriteUInt32(fmtChunk.SampleRate);
		bufferWriter.WriteUInt32(fmtChunk.ByteRate);
		bufferWriter.WriteUInt16(fmtChunk.BlockAlign);
		bufferWriter.WriteUInt16(fmtChunk.BitsPerSample);
		if ((fmtChunk.CbSize ?? 0) != 0)
		{
			bufferWriter.WriteUInt16(fmtChunk.CbSize.Value);
			bufferWriter.Write(fmtChunk.ExtensionChunkData);
		}
	}

	public FmtChunk Load()
	{
		FmtChunk fmtChunk = new FmtChunk();
		fmtChunk.FmtID = bufferReader.ReadString(4);
		fmtChunk.FmtSize = bufferReader.ReadUInt32();
		fmtChunk.FmtTag = (FmtChunk.AudioFormats)bufferReader.ReadInt16();
		fmtChunk.NumChannels = bufferReader.ReadUInt16();
		fmtChunk.SampleRate = bufferReader.ReadUInt32();
		fmtChunk.ByteRate = bufferReader.ReadUInt32();
		fmtChunk.BlockAlign = bufferReader.ReadUInt16();
		fmtChunk.BitsPerSample = bufferReader.ReadUInt16();
		if (fmtChunk.FmtID != "fmt ")
		{
			throw new XnbError(Error.FmtChunkReader_1, fmtChunk.FmtID);
		}
		if (fmtChunk.FmtSize < 16)
		{
			throw new Exception("fmt chunk size is too small");
		}
		switch (bufferReader.PeekUInt16())
		{
		case 0:
			fmtChunk.CbSize = bufferReader.ReadUInt16();
			break;
		case 32:
			fmtChunk.CbSize = bufferReader.ReadUInt16();
			fmtChunk.ExtensionChunkData = bufferReader.Read(fmtChunk.CbSize.Value);
			break;
		}
		return fmtChunk;
	}

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		FmtChunk fmtChunk = new FmtChunk();
		fmtChunk.FmtSize = bufferReader.ReadUInt32();
		fmtChunk.FmtTag = (FmtChunk.AudioFormats)bufferReader.ReadInt16();
		fmtChunk.NumChannels = bufferReader.ReadUInt16();
		fmtChunk.SampleRate = bufferReader.ReadUInt32();
		fmtChunk.ByteRate = bufferReader.ReadUInt32();
		fmtChunk.BlockAlign = bufferReader.ReadUInt16();
		fmtChunk.BitsPerSample = bufferReader.ReadUInt16();
		if (bufferReader.PeekUInt16() == 0)
		{
			fmtChunk.CbSize = bufferReader.ReadUInt16();
		}
		return fmtChunk;
	}

	public override void Write(object input)
	{
		FmtChunk fmtChunk = (FmtChunk)input;
		bufferWriter.WriteUInt32(fmtChunk.FmtSize);
		bufferWriter.WriteInt16((short)fmtChunk.FmtTag);
		bufferWriter.WriteUInt16(fmtChunk.NumChannels);
		bufferWriter.WriteUInt32(fmtChunk.SampleRate);
		bufferWriter.WriteUInt32(fmtChunk.ByteRate);
		bufferWriter.WriteUInt16(fmtChunk.BlockAlign);
		bufferWriter.WriteUInt16(fmtChunk.BitsPerSample);
		if (fmtChunk.CbSize == 0)
		{
			bufferWriter.WriteUInt16(fmtChunk.CbSize.Value);
		}
	}
}
