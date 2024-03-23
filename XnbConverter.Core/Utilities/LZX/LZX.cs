#region HEADER

/* This file was derived from libmspack
 * (C) 2003-2004 Stuart Caie.
 * (C) 2011 Ali Scissons.
 *
 * The LZX method was created by Jonathan Forbes and Tomi Poutanen, adapted
 * by Microsoft Corporation.
 *
 * This source file is Dual licensed; meaning the end-user of this source file
 * may redistribute/modify it under the LGPL 2.1 or MS-PL licenses.
 */

#region LGPL License

/* GNU LESSER GENERAL PUBLIC LICENSE version 2.1
 * LzxDecoder is free software; you can redistribute it and/or modify it under
 * the terms of the GNU Lesser General Public License (LGPL) version 2.1
 */

#endregion

#region MS-PL License

/*
 * MICROSOFT PUBLIC LICENSE
 * This source code is subject to the terms of the Microsoft Public License (Ms-PL).
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * is permitted provided that redistributions of the source code retain the above
 * copyright notices and this file header.
 *
 * Additional copyright notices should be appended to the list above.
 *
 * For details, see <http://www.opensource.org/licenses/ms-pl.html>.
 */

#endregion

/*
 * This derived work is recognized by Stuart Caie and is authorized to adapt
 * any changes made to lzxd.c in his libmspack library and will still retain
 * this dual licensing scheme. Big thanks to Stuart Caie!
 *
 * DETAILS
 * This file is a pure C# port of the lzxd.c file from libmspack, with minor
 * changes towards the decompression of XNB files. The original decompression
 * software of LZX encoded data was written by Suart Caie in his
 * libmspack/cabextract projects, which can be located at
 * http://http://www.cabextract.org.uk/
 */
/*
 * 资源：
 * cabextract/libmspack - https://www.cabextract.org.uk/libmspack/
 * MonoGame LzxDecoder.cs - https://github.com/MonoGame/MonoGame/blob/master/MonoGame.Framework/Content/LzxDecoder.cs
 *
 */

#endregion

using System;
// using Newtonsoft.Json;
using XnbConverter.Readers;

namespace XnbConverter.Utilities.LZX;
/*
 * LZX静态数据表
 *
 * LZX使用“位置槽”来表示匹配偏移量。对于每个匹配，
 * 编码的是一个小的“位置槽”号码和相对于该槽的小偏移量，而不是一个大的偏移量。
 *
 * Lzx.position_base是一个指向位置槽基址的索引
 * Lzx.extra_bits指明需要多少位的相对于基址的偏移数据。
 */

/*
 * 用于压缩和解压缩LZX格式的缓冲区。
 * @class
 * @public
 */
public class LZX : IDisposable
{
    private const ushort MIN_MATCH = 2; // 最小允许的匹配长度
    private const ushort MAX_MATCH = 257; // 最大允许的匹配长度
    private const ushort NUM_CHARS = 256; // 未压缩字符类型的数量

    private const uint INVALID = 0;
    private const uint VERBATIM = 1;
    private const uint ALIGNED = 2;
    private const uint UNCOMPRESSED = 3;

    private const ushort PRETREE_NUM_ELEMENTS = 20;
    private const ushort ALIGNED_NUM_ELEMENTS = 8; // 对齐偏移树元素
    private const ushort NUM_PRIMARY_LENGTHS = 7;

    private const ushort NUM_SECONDARY_LENGTHS = 249; // 长度树中的元素数量

    // LZX哈夫曼常量
    private const ushort PRETREE_MAXSYMBOLS = PRETREE_NUM_ELEMENTS;
    private const ushort PRETREE_TABLEBITS = 6;
    private const ushort MAINTREE_MAXSYMBOLS = NUM_CHARS + 50 * 8;
    private const ushort MAINTREE_TABLEBITS = 12;
    private const ushort LENGTH_MAXSYMBOLS = NUM_SECONDARY_LENGTHS + 1;
    private const ushort LENGTH_TABLEBITS = 12;
    private const ushort ALIGNED_MAXSYMBOLS = ALIGNED_NUM_ELEMENTS;
    private const ushort ALIGNED_TABLEBITS = 7;
    private const ushort LENTABLE_SAFETY = 64; // 允许表解码超出的安全范围

    private static readonly uint[] position_base;
    private static readonly byte[] extra_bits;
    private LzxState m_state;

    static LZX() // 初始化静态表
    {
        extra_bits = new byte[52];
        for (int i = 0, j = 0; i < 52; i += 2)
        {
            extra_bits[i] = extra_bits[i + 1] = (byte)j;
            if (i != 0 && j < 17)
                j++;
        }

        position_base = new uint[51];
        for (int i = 0, j = 0; i < 51; i++)
        {
            position_base[i] = (uint)j;
            j += 1 << extra_bits[i];
        }

        Log.Debug(Helpers.I18N["LZX.13"], extra_bits.ToJoinStr());
        Log.Debug(Helpers.I18N["LZX.14"],  position_base.ToJoinStr());
    }

    /**
     * 创建具有给定窗口大小的LZX实例。
     * @constructor
     * @param {Number} window_bits
     */
    private LZX(int window)
    {
        // LZX支持2^15（32 KB）到2^21（2 MB）的窗口大小
        if (window < 15 || window > 21)
            throw new XnbError(Helpers.I18N["LZX.1"]);
        m_state.window_size = (uint)(1 << window);

        /*
         * 计算所需的位置槽
         *
         * 窗口位数：   15 16 17 18 19 20 21
         * 位置槽数：   30 32 34 36 38 42 50
         */
        var posn_slots = window switch
        {
            21 => 50,
            20 => 42,
            _ => window << 1
        };
        // 重复的偏移量
        m_state.R0 = m_state.R1 = m_state.R2 = 1;
        // 设置主要元素的数量
        m_state.main_elements = (ushort)(NUM_CHARS + (posn_slots << 3));
        // 用于循环处理多个块时，用于标记是否已读取头部
        m_state.header_read = false;
        // 设置剩余块大小
        m_state.block_remaining = 0;
        // 设置默认的块类型
        m_state.block_type = INVALID;
        // 窗口位置
        m_state.window_posn = 0;

        // 经常使用的表
        m_state.pretree_len = new byte[20];

        m_state.aligned_len = new byte[8];

        // 初始化主树和长度树，以供进行增量操作
        m_state.length_len = new byte[NUM_SECONDARY_LENGTHS];

        m_state.maintree_len = new byte[MAINTREE_MAXSYMBOLS];

        m_state.window = Pool.RentByte((int)m_state.window_size);
    }

    public void Dispose()
    {
        Pool.Return(m_state.window);
    }

    /*
     * 使用给定的帧大小和块大小对缓冲区进行解压缩。
     * @param {BufferReader} buffer
     * @param {Number} frame_size
     * @param {Number} block_size
     * @returns {Number[]}
     */
    private void Decompress(BufferReader bufferReader, int frame_size, int block_size, byte[] decompressed,
        int decompressed_index)
    {
        // 如果还没有读取头部，则读取头部
        if (!m_state.header_read)
        {
            // 读取Intel调用

            var intel = bufferReader.ReadLzxBits(1);

            Log.Debug(Helpers.I18N["LZX.15"], intel.B(), intel);

            // 不关心Intel E8调用
            if (intel != 0)
                throw new XnbError(Helpers.I18N["LZX.2"]);

            // 头部已读取
            m_state.header_read = true;
        }

        int this_run,
            main_element,
            match_length,
            match_offset,
            length_footer,
            extra,
            verbatim_bits;
        int rundest,
            runsrc,
            copy_length,
            aligned_bits;
        // 设置剩余的帧大小
        var togo = frame_size;

        // 遍历帧中剩余的部分
        while (togo > 0)
        {
            // 这是一个新的块
            if (m_state.block_remaining == 0)
            {
                // 读取块类型
                m_state.block_type = bufferReader.ReadLzxBits(3);
                Log.Debug(Helpers.I18N["LZX.16"], m_state.block_type.B(), m_state.block_type);

                // 读取24位无压缩字节数
                var hi = bufferReader.ReadLzxBits(16);
                var lo = bufferReader.ReadLzxBits(8);

                m_state.block_remaining = (hi << 8) | lo;

                Log.Debug(Helpers.I18N["LZX.17"], m_state.block_remaining);
                // 根据有效的块类型进行切换
                switch (m_state.block_type)
                {
                    case ALIGNED:
                        // 对齐偏移树
                        for (var i = 0; i < 8; i++)
                            m_state.aligned_len[i] = (byte)bufferReader.ReadLzxBits(3);
                        // 对齐树的解码表
                        m_state.aligned_table = DecodeTable(
                            ALIGNED_MAXSYMBOLS,
                            ALIGNED_TABLEBITS,
                            m_state.aligned_len,
                            Pool.Len512
                        );

                        goto case VERBATIM;
                    // 注意：其余的对齐块类型与直接块类型相同
                    case VERBATIM:
                        // 读取主树的前256个元素
                        ReadLengths(bufferReader, m_state.maintree_len, 0, 256);
                        // 读取主树的其余元素
                        ReadLengths(bufferReader, m_state.maintree_len, 256, m_state.main_elements);
                        // 解码主树为表格
                        m_state.maintree_table = DecodeTable(
                            MAINTREE_MAXSYMBOLS,
                            MAINTREE_TABLEBITS,
                            m_state.maintree_len,
                            Pool.Len8192
                        );

                        // 读取长度树的路径长度
                        ReadLengths(bufferReader, m_state.length_len, 0, NUM_SECONDARY_LENGTHS);

                        // 解码长度树
                        m_state.length_table = DecodeTable(
                            LENGTH_MAXSYMBOLS,
                            LENGTH_TABLEBITS,
                            m_state.length_len,
                            Pool.Len8192
                        );

                        break;
                    case UNCOMPRESSED:
                        // 将位缓冲器对齐到字节范围
                        bufferReader.Align();
                        // 读取偏移量
                        m_state.R0 = bufferReader.ReadUInt32();
                        m_state.R1 = bufferReader.ReadUInt32();
                        m_state.R2 = bufferReader.ReadUInt32();
                        break;
                    default:
                        throw new XnbError(Helpers.I18N["LZX.3"], m_state.block_type);
                }
            }

            // 迭代剩余的块
            // 循环遍历缓冲区中剩余的字节，以输出我们的结果
            while ((this_run = (int)m_state.block_remaining) > 0 && togo > 0)
            {
                // 如果此次运行超过了 togo，则将其限制在 togo 之内
                if (this_run > togo)
                    this_run = togo;

                // 减少 togo 和 block_remaining 的值
                togo -= this_run;
                m_state.block_remaining -= (uint)this_run;

                // 应用 2^x-1 的掩码
                m_state.window_posn &= m_state.window_size - 1;
                // 运行长度不能超过窗口大小
                if (m_state.window_posn + this_run > m_state.window_size)
                    throw new XnbError(Helpers.I18N["LZX.4"]);

                switch (m_state.block_type)
                {
                    case ALIGNED:
                        while (this_run > 0)
                        {
                            // get the element of this run
                            main_element = (int)ReadHuffSymbol(
                                bufferReader,
                                m_state.maintree_table,
                                m_state.maintree_len,
                                MAINTREE_MAXSYMBOLS,
                                MAINTREE_TABLEBITS
                            );

                            // main element is an unmatched character
                            if (main_element < NUM_CHARS)
                            {
                                m_state.window[m_state.window_posn++] = (byte)main_element;
                                this_run--;
                                continue;
                            }

                            main_element -= NUM_CHARS;

                            match_length = main_element & NUM_PRIMARY_LENGTHS;
                            if (match_length == NUM_PRIMARY_LENGTHS)
                            {
                                length_footer = (int)ReadHuffSymbol(
                                    bufferReader,
                                    m_state.length_table,
                                    m_state.length_len,
                                    LENGTH_MAXSYMBOLS,
                                    LENGTH_TABLEBITS
                                );
                                // increase match length by the footer
                                match_length += length_footer;
                            }

                            match_length += MIN_MATCH;

                            match_offset = main_element >> 3;
                            if (match_offset > 2)
                            {
                                // not repeated offset
                                extra = extra_bits[match_offset];
                                match_offset = (int)position_base[match_offset] - 2;
                                if (extra > 3)
                                {
                                    // verbatim and aligned bits
                                    extra -= 3;
                                    verbatim_bits = (int)bufferReader.ReadLzxBits((byte)extra);
                                    match_offset += verbatim_bits << 3;
                                    aligned_bits = (int)ReadHuffSymbol(
                                        bufferReader,
                                        m_state.aligned_table,
                                        m_state.aligned_len,
                                        ALIGNED_MAXSYMBOLS,
                                        ALIGNED_TABLEBITS
                                    );
                                    match_offset += aligned_bits;
                                }
                                else if (extra == 3)
                                {
                                    // aligned bits only
                                    match_offset += (int)ReadHuffSymbol(
                                        bufferReader,
                                        m_state.aligned_table,
                                        m_state.aligned_len,
                                        ALIGNED_MAXSYMBOLS,
                                        ALIGNED_TABLEBITS
                                    );
                                }
                                else if (extra > 0) /* extra==1, extra==2 */
                                {
                                    // verbatim bits only
                                    match_offset += (int)bufferReader.ReadLzxBits((byte)extra);
                                }
                                else /* extra == 0 */
                                {
                                    match_offset = 1;
                                }

                                /* update repeated offset LRU queue */
                                m_state.R2 = m_state.R1;
                                m_state.R1 = m_state.R0;
                                m_state.R0 = (uint)match_offset;
                            }
                            else if (match_offset == 0)
                            {
                                match_offset = (int)m_state.R0;
                            }
                            else if (match_offset == 1)
                            {
                                match_offset = (int)m_state.R1;
                                m_state.R1 = m_state.R0;
                                m_state.R0 = (uint)match_offset;
                            }
                            else /* match_offset == 2 */
                            {
                                match_offset = (int)m_state.R2;
                                m_state.R2 = m_state.R0;
                                m_state.R0 = (uint)match_offset;
                            }

                            rundest = (int)m_state.window_posn;
                            this_run -= match_length;
                            /* copy any wrapped around source data */
                            if (m_state.window_posn >= match_offset)
                            {
                                /* no wrap */
                                runsrc = rundest - match_offset;
                            }
                            else
                            {
                                runsrc = rundest + ((int)m_state.window_size - match_offset);

                                copy_length = match_offset - (int)m_state.window_posn;

                                if (copy_length < match_length)
                                {
                                    match_length -= copy_length;
                                    m_state.window_posn += (uint)copy_length;
                                    while (copy_length-- > 0)
                                        m_state.window[rundest++] = m_state.window[runsrc++];
                                    runsrc = 0;
                                }
                            }

                            m_state.window_posn += (uint)match_length;
                            // copy match data - no worrries about destination wraps
                            while (match_length-- > 0)
                                m_state.window[rundest++] = m_state.window[runsrc++];
                        }

                        break;

                    case VERBATIM:
                        while (this_run > 0)
                        {
                            // get the element of this run
                            main_element = (int)ReadHuffSymbol(bufferReader,
                                m_state.maintree_table,
                                m_state.maintree_len,
                                MAINTREE_MAXSYMBOLS,
                                MAINTREE_TABLEBITS
                            );
                            // main element is an unmatched character
                            if (main_element < NUM_CHARS)
                            {
                                /* literal: 0 to NUM_CHARS-1 */
                                m_state.window[m_state.window_posn++] = (byte)main_element;
                                this_run--;
                                continue;
                            }

                            // match: NUM_CHARS + ((slot << 3) | length_header (3 bits))
                            main_element -= NUM_CHARS;

                            match_length = main_element & NUM_PRIMARY_LENGTHS;
                            if (match_length == NUM_PRIMARY_LENGTHS)
                            {
                                length_footer = (int)ReadHuffSymbol(
                                    bufferReader,
                                    m_state.length_table,
                                    m_state.length_len,
                                    LENGTH_MAXSYMBOLS,
                                    LENGTH_TABLEBITS
                                );
                                match_length += length_footer;
                            }

                            match_length += MIN_MATCH;

                            match_offset = main_element >> 3;

                            if (match_offset > 2)
                            {
                                // not repeated offset
                                if (match_offset != 3)
                                {
                                    extra = extra_bits[match_offset];
                                    verbatim_bits = (int)bufferReader.ReadLzxBits((byte)extra);
                                    match_offset = (int)position_base[match_offset] - 2 + verbatim_bits;
                                }
                                else
                                {
                                    match_offset = 1;
                                }

                                // update repeated offset LRU queue
                                m_state.R2 = m_state.R1;
                                m_state.R1 = m_state.R0;
                                m_state.R0 = (uint)match_offset;
                            }
                            else if (match_offset == 0)
                            {
                                match_offset = (int)m_state.R0;
                            }
                            else if (match_offset == 1)
                            {
                                match_offset = (int)m_state.R1;
                                m_state.R1 = m_state.R0;
                                m_state.R0 = (uint)match_offset;
                            }
                            else /* match_offset == 2 */
                            {
                                match_offset = (int)m_state.R2;
                                m_state.R2 = m_state.R0;
                                m_state.R0 = (uint)match_offset;
                            }

                            rundest = (int)m_state.window_posn;
                            this_run -= match_length;
                            // copy any wrapped around source data
                            if (m_state.window_posn >= match_offset)
                            {
                                runsrc = rundest - match_offset; // no wrap
                            }
                            else
                            {
                                runsrc = rundest + ((int)m_state.window_size - match_offset);
                                copy_length = match_offset - (int)m_state.window_posn;
                                if (copy_length < match_length)
                                {
                                    match_length -= copy_length;
                                    m_state.window_posn += (uint)copy_length;
                                    while (copy_length-- > 0)
                                        m_state.window[rundest++] = m_state.window[runsrc++];
                                    runsrc = 0;
                                }
                            }

                            m_state.window_posn += (uint)match_length;

                            // copy match data - no worries about destination wraps
                            while (match_length-- > 0)
                                m_state.window[rundest++] = m_state.window[runsrc++];
                        }

                        break;
                    case UNCOMPRESSED:
                        if (bufferReader.BytePosition + this_run > block_size)
                            throw new XnbError(Helpers.I18N["LZX.5"], block_size, bufferReader.BytePosition, this_run);
                        bufferReader.Buffer.AsSpan(bufferReader.BytePosition, this_run).CopyTo(
                            m_state.window.AsSpan((int)m_state.window_posn, this_run)
                        );
                        bufferReader.BytePosition += this_run;
                        m_state.window_posn += (uint)this_run;
                        break;
                    default:
                        throw new XnbError(Helpers.I18N["LZX.6"]);
                }
            }
        }

        // there is still more left
        if (togo != 0)
            throw new XnbError(Helpers.I18N["LZX.7"]);

        // ensure the buffer is aligned
        bufferReader.Align();

        // get the start window position
        var start_window_pos = (int)(m_state.window_posn is 0 ? m_state.window_size : m_state.window_posn);
        start_window_pos -= frame_size;

        // return the window
        m_state.window.AsSpan(start_window_pos, frame_size)
            .CopyTo(decompressed.AsSpan(decompressed_index, frame_size));
    }

    /*
     * 以LZX方式读取给定表中从first到last的符号的码长
     * 码长以特殊的方式存储
     * @public
     * @method readLengths
     * @param {BufferReader} buffer
     * @param {Array} table
     * @param {Number} first
     * @param {Number} last
     * @returns {Array}
     */
    private void ReadLengths(BufferReader buffer, Span<byte> table, int first, uint last)
    {
        int i;
        uint zeros;

        // 读取4位的预树增量
        for (i = 0; i < 20; i++) m_state.pretree_len[i] = (byte)buffer.ReadLzxBits(4);
        // 从长度创建预树表
        m_state.pretree_table = DecodeTable(
            PRETREE_MAXSYMBOLS,
            PRETREE_TABLEBITS,
            m_state.pretree_len,
            Pool.Len128
        );
        // 从first到last循环遍历长度
        for (i = first; i < last;)
        {
            // 读取哈夫曼符号
            var symbol = (int)ReadHuffSymbol(
                buffer,
                m_state.pretree_table,
                m_state.pretree_len,
                PRETREE_MAXSYMBOLS,
                PRETREE_TABLEBITS
            );
            switch (symbol)
            {
                // code = 17, 连续 ([读取4位] + 4) 个零
                case 17:
                {
                    // 以4位数字+4的形式读取零的数量
                    zeros = buffer.ReadLzxBits(4) + 4;
                    // 迭代零计数器，并将它们添加到表中
                    table.Slice(i, (int)zeros).Fill(0);
                    i += (int)zeros;
                    break;
                }
                // code = 18, 连续 ([读取5位] + 20) 个零
                case 18:
                {
                    // 以5位数字+20的形式读取零的数量
                    zeros = buffer.ReadLzxBits(5) + 20;
                    // 将零的数量添加到表数组中
                    table.Slice(i, (int)zeros).Fill(0);
                    i += (int)zeros;
                    break;
                }
                // code = 19 连续 ([读取1位] + 4) [读取哈夫曼符号]
                case 19:
                {
                    // 读取重复相同哈夫曼符号的次数
                    var same = buffer.ReadLzxBits(1) + 4;
                    symbol = (int)ReadHuffSymbol(
                        buffer,
                        m_state.pretree_table,
                        m_state.pretree_len,
                        PRETREE_MAXSYMBOLS,
                        PRETREE_TABLEBITS
                    );
                    symbol = table[i] - symbol;
                    if (symbol < 0) symbol += 17;
                    table.Slice(i, (int)same).Fill((byte)symbol);
                    i += (int)same;
                    break;
                }
                // code 0 -> 16, 对当前长度条目进行增量计算
                default:
                {
                    symbol = table[i] - symbol;
                    if (symbol < 0) symbol += 17;
                    table[i++] = (byte)symbol;
                    break;
                }
            }
        }
    }

    /**
     * 从规范的哈夫曼长度表构建解码表
     * @public
     * @method makeDecodeTable
     * @param {Number} symbols 树中的总符号数。
     * @param {Number} bits 任何小于此值的符号可以在查找表中进行一次解码。
     * @param {Number[]} length 给定表的长度表以进行解码。
     * @returns {Number[]} 解码表, 长度应为 ((1
     * <
     * <nbits) + ( nsyms
     * *
     * 2
     * )
     * )
     */
    private ushort[] DecodeTable(uint symbols, uint bits, byte[] length, int tableMaxLen)
    {
        var table = Pool.RentUShort(tableMaxLen);
        var table_index = 0;

        uint pos = 0;
        /* the current position in the decode table */
        var table_mask = 1u << (int)bits;
        /* don't do 0 length codes */
        var bit_mask = table_mask >> 1;

        uint index = 0;

        // 循环遍历所有位位置
        for (var bit_num = 1; bit_num <= bits; bit_num++)
        {
            // 循环遍历要解码的符号
            for (var s = 0; s < symbols; s++)
            {
                // 如果符号不在此次迭代的长度中，则忽略
                if (length.Length == s) continue;
                if (length[s] != bit_num) continue;
                var leaf = pos;
                pos += bit_mask;
                // 如果位置超过了表掩码，则表示溢出
                if (pos > table_mask)
                {
                    Log.Debug(length[s].ToString());
                    Log.Debug(Helpers.I18N["LZX.18"], pos, bit_mask, table_mask);
                    Log.Debug(Helpers.I18N["LZX.19"], bit_num, bits);
                    Log.Debug(Helpers.I18N["LZX.20"], s, symbols);
                    throw new XnbError(Helpers.I18N["LZX.8"]);
                }

                // 将此符号的所有可能查找填充为该符号本身
                var fill = bit_mask;
                table_index += (int)fill;
                while (fill-- > 0)
                {
                    table[(int)leaf] = (ushort)s;
                    leaf++;
                }
            }

            // 将位掩码向下移动位位置
            bit_mask >>= 1;
        }

        // 如果表格完整，则成功退出
        if (pos == table_mask)
        {
            var result = table[..table_index];
            Pool.Return(table);
            return result;
        }


        // 将所有剩余的表格条目标记为未使用
        table_index = Math.Max(table_index, (int)table_mask);
        for (var symbol = pos; symbol < table_mask; symbol++) table[(int)symbol] = 0xFFFF;


        // next_symbol = 长码分配的基地址
        var next_symbol = table_mask >> 1 < symbols ? symbols : table_mask >> 1;

        // 为16位值分配空间
        pos <<= 16;
        table_mask <<= 16;
        bit_mask = 1 << 15;
        symbols = (uint)Math.Min(length.Length, symbols);
        // 再次循环遍历位
        for (var bit_num = bits + 1; bit_num <= 16; bit_num++)
        {
            // 循环遍历符号范围
            for (var symbol = 0; symbol < symbols; symbol++)
            {
                // 如果当前长度迭代与位数不匹配，则忽略
                if (length[symbol] != bit_num)
                    continue;

                // 将叶子节点移位去除16位填充
                var leaf = (int)(pos >> 16);

                // 循环填充表格
                for (var fill = 0; fill < bit_num - bits; fill++)
                {
                    // 如果该路径尚未被使用，则“分配”两个条目
                    if (table[leaf] == 0xFFFF)
                    {
                        var off = (int)(next_symbol << 1);
                        table_index = Math.Max(table_index, off + 2);
                        table[off] = table[off + 1] = 0xFFFF;
                        table[leaf] = (ushort)next_symbol++;
                    }

                    // 沿着路径继续，并根据下一位选择左侧或右侧
                    leaf = table[leaf] << 1;
                    if (((pos >> (15 - fill)) & 1) > 0)
                        leaf++;
                }

                table[leaf] = (ushort)symbol;

                // 位位置超过了表格掩码
                if ((pos += bit_mask) > table_mask)
                    throw new XnbError(Helpers.I18N["LZX.9"]);
            }

            bit_mask >>= 1;
        }

        if (pos != table_mask)
            throw new XnbError(Helpers.I18N["LZX.10"]);
        var _result = table[..table_index];
        Pool.Return(table);
        return _result;
    }

    /*
     * 从比特流中解码下一个哈夫曼符号。
     * @public
     * @method readHuffSymbol
     * @param {BufferReader} buffer
     * @param {Number[]} table
     * @param {Number[]} length
     * @param {Number} symbols
     * @param {Number} bits
     * @returns {Number}
     */
    private uint ReadHuffSymbol(BufferReader bufferReader, ushort[] table, byte[] length, uint symbols, uint bits)
    {
        uint j;
        // 提前查看指定的比特数
        // (>>> 0) 允许我们得到一个32位的无符号整数
        var bit = bufferReader.PeekLzxBits(32);
        uint i = table[bufferReader.PeekLzxBits((int)bits)];

        // 如果我们的表正在访问范围之外的符号
        if (i >= symbols)
        {
            j = (uint)(1 << (int)(32 - bits));
            do
            {
                j >>= 1;
                i <<= 1;
                i |= (bit & j) != 0 ? 1u : 0u;
                if (j == 0)
                    return 0;
            } while ((i = table[i]) >= symbols);
        }

        // 跳过这么多比特位
        bufferReader.BitPosition += length[i];
        // 返回符号
        return i;
    }

    /**
     * 解压缩一定数量的字节。
     * @public
     * @static
     * @param {BufferReader} buffer
     * @returns {Buffer}
     */
    public static void Decompress(BufferReader bufferReader, int compressedTodo, int decompressedTodo)
    {
        // 当前缓冲区的索引
        var pos = 0;
        // 分配块大小和帧大小的变量
        int block_size;
        int frame_size;
        // 使用16位窗口帧创建LZX实例
        var lzx = new LZX(16);
        // 完整的解压缩数组
        var decompressed = Pool.RentByte(decompressedTodo);
        var decompressed_index = 0;

        // 循环处理剩余字节
        while (pos < compressedTodo)
        {
            // 用于确定frame_size是固定的还是可变的的标志
            var flag = bufferReader.ReadByte();
            // 如果标志设置为0xFF，表示我们将读取帧大小
            if (flag == 0xFF)
            {
                // 读取帧大小
                frame_size = bufferReader.ReadLzxInt16();
                // 读取块大小
                block_size = bufferReader.ReadLzxInt16();
                // 前进字节位置
                pos += 5;
            }
            else
            {
                // 倒回缓冲区
                bufferReader.Skip(-1);
                // 读取块大小
                block_size = bufferReader.ReadLzxInt16();
                // 设置帧大小
                frame_size = 0x8000;
                // 前进字节位置
                pos += 2;
            }

            // 确保块大小和帧大小不为空
            if (block_size == 0 || frame_size == 0)
                break;
            // 确保块大小和帧大小不超过整数的大小
            if (block_size > 0x10000 || frame_size > 0x10000)
                throw new XnbError(Helpers.I18N["LZX.11"]);

            Log.Debug(Helpers.I18N["LZX.21"], block_size, frame_size);
            // 根据帧大小和块大小解压缩文件
            lzx.Decompress(bufferReader, frame_size, block_size, decompressed, decompressed_index);
            decompressed_index += frame_size;
            // 增加位置计数器
            pos += block_size;
        }

        // 完成文件解压缩
        Log.Info(Helpers.I18N["LZX.12"]);
        // 解压缩后的缓冲区复制到主缓冲区
        decompressed.AsSpan(0, decompressedTodo).CopyTo(
            bufferReader.Buffer.AsSpan(XNB.XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE, decompressedTodo));
        Pool.Return(decompressed);
        lzx.Dispose();
    }

    private struct LzxState
    {
        // 用于最近最少使用（LRU）偏移系统
        public uint R0, R1, R2;

        // 主树元素的数量
        public ushort main_elements;

        // 是否已经开始解码
        public bool header_read;

        // 块的类型
        public uint block_type;

        // 块的未压缩长度
        public uint block_length;

        // 块中剩余待解码的未压缩字节数
        public uint block_remaining;

        // 处理的CFDATA块数量
        public uint frames_read;

        // 用于转换的魔术头值
        public int intel_filesize;

        // 转换空间中的当前偏移量
        public int intel_curpos;

        // 是否已经看到可转换的数据
        public int intel_started;

        // PRETREE表
        public ushort[] pretree_table;

        // PRETREE长度
        public byte[] pretree_len;

        // MAINTREE表
        public ushort[] maintree_table;

        // MAINTREE长度
        public byte[] maintree_len;

        // LENGTH表
        public ushort[] length_table;

        // LENGTH长度
        public byte[] length_len;

        // ALIGNED表
        public ushort[] aligned_table;

        // ALIGNED长度
        public byte[] aligned_len;

        /* 需要的成员 */
        // CAB实际大小
        public uint actual_size;

        // CAB窗口
        public byte[] window;

        // CAB窗口大小
        public uint window_size;

        // CAB窗口位置
        public uint window_posn;
    }
}