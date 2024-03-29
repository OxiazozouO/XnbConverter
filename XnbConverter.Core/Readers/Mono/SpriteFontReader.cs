﻿using System;
using System.Collections.Generic;
using XnbConverter.Entity.Mono;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using Rectangle = XnbConverter.Entity.Mono.Rectangle;

namespace XnbConverter.Readers.Mono;

public class SpriteFontReader : BaseReader
{
    private readonly NullableReader<CharReader, char> nullableReader = new();
    private int charListReader;
    private int rectangleListReader;
    private int texture2DReader;
    private int vector3ListReader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        nullableReader.Init(readerResolver);
        texture2DReader = readerResolver.GetIndex(typeof(Texture2D));
        rectangleListReader = readerResolver.GetIndex(typeof(List<Rectangle>));
        charListReader = readerResolver.GetIndex(typeof(List<char>));
        vector3ListReader = readerResolver.GetIndex(typeof(List<Vector3>));
    }

    public override SpriteFont Read()
    {
        var result = new SpriteFont();

        result.Texture = (Texture2D)readerResolver.Read(texture2DReader);
        result.Glyphs = (List<Rectangle>)readerResolver.Read(rectangleListReader);
        result.Cropping = (List<Rectangle>)readerResolver.Read(rectangleListReader);
        result.CharacterMap = (List<char>)readerResolver.Read(charListReader);
        result.VerticalLineSpacing = bufferReader.ReadInt32();
        result.HorizontalSpacing = bufferReader.ReadSingle();
        result.Kerning = (List<Vector3>)readerResolver.Read(vector3ListReader);
        result.DefaultCharacter = nullableReader.Read() as char?;

        return result;
    }

    public override void Write(object content)
    {
        var input = (SpriteFont)content;
        readerResolver.Write(texture2DReader, input.Texture);
        readerResolver.Write(rectangleListReader, input.Glyphs);
        readerResolver.Write(rectangleListReader, input.Cropping);
        readerResolver.Write(charListReader, input.CharacterMap);
        bufferWriter.WriteInt32(input.VerticalLineSpacing);
        bufferWriter.WriteSingle(input.HorizontalSpacing);
        readerResolver.Write(vector3ListReader, input.Kerning);
        nullableReader.Write(input.DefaultCharacter);
    }

    public override bool IsValueType()
    {
        return false;
    }
}