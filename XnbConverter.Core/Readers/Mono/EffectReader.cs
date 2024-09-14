using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Mono;

public class EffectReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		Effect effect = new Effect();
		uint count = bufferReader.ReadUInt32();
		effect.Data = bufferReader.Read((int)count);
		return effect;
	}

	public override void Write(object content)
	{
		Effect effect = (Effect)content;
		bufferWriter.WriteUInt32((uint)effect.Data.Length);
		bufferWriter.Write(effect.Data);
	}

	public override bool IsValueType()
	{
		return false;
	}
}
