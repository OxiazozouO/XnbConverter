#region license

/*
Copyright (c) 2013, Milosz Krajewski
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided
that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this list of conditions
  and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this list of conditions
  and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED
WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN
IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#endregion

#pragma warning disable

using System;

namespace XnbConverter.Utilities.LZ4
{
    /// <summary>
    /// 不安全指针版 LZ4 编解码器。
    /// 本文件是 <c>LZ4Codec</c> 这个 partial class 的共享片段：
    /// 存放快速档（<c>Encode32/Encode64</c> 及其解码）与 HC 档共用的底层工具
    /// （<see cref="BlockCopy"/>、<see cref="BlockFill"/>、HC 上下文结构体），
    /// 以及把安全数组重载接到不安全指针实现上的那层接线代码。
    /// 同一 partial class 的另外两个 <c>*.Dirty.cs</c> 文件才是真正的压缩/解压算法本体。
    /// </summary>
    internal static partial class LZ4Codec
    {
        /// <summary>
        /// 按 8 → 4 → 2 → 1 字节的顺序，手工展开地拷贝一块内存。
        ///
        /// 之所以不用 <c>Buffer.BlockCopy</c>：这里是裸指针，且调用点都在内层热路径上，
        /// 展开后每次循环搬 8 字节（一个 <c>ulong</c>），比逐字节循环快得多。
        /// 先尽量搬大的、再降级到小的，保证剩余不足一个「大块」时也不会漏拷尾部。
        /// </summary>
        /// <param name="src">源地址。</param>
        /// <param name="dst">目标地址。</param>
        /// <param name="len">长度（字节）。</param>
        private static unsafe void BlockCopy(byte* src, byte* dst, int len)
        {
            while (len >= 8)
            {
                *(ulong*)dst = *(ulong*)src;
                dst += 8;
                src += 8;
                len -= 8;
            }

            if (len >= 4)
            {
                *(uint*)dst = *(uint*)src;
                dst += 4;
                src += 4;
                len -= 4;
            }

            if (len >= 2)
            {
                *(ushort*)dst = *(ushort*)src;
                dst += 2;
                src += 2;
                len -= 2;
            }

            if (len >= 1)
            {
                // 只剩 1 字节，直接赋值即可（原版 C 里的 d++/s++/l-- 在后面已无意义，
                // 等价于循环到此结束，故不再展开）
                *dst = *src; /* d++; s++; l--; */
            }
        }

        /// <summary>
        /// 把 <paramref name="len"/> 个字节全部填成 <paramref name="val"/>。
        ///
        /// 先用「移位或」把单字节 <paramref name="val"/> 复制满一个 <c>ulong</c>
        /// （乘出 0xVVVVVVVVVVVVVVVV 这种掩码），再一次性写入 8 字节；
        /// 复制字节这一步只做 3 次移位，比在循环里反复拼装省得多。
        /// 余下不足 8 字节的部分退回逐字节写。
        /// </summary>
        /// <param name="dst">目标地址。</param>
        /// <param name="len">长度（字节）。</param>
        /// <param name="val">要填充的字节值。</param>
        private static unsafe void BlockFill(byte* dst, int len, byte val)
        {
            if (len >= 8)
            {
                // 把 val 的一字节模式复制到 ulong 的 8 个字节上
                ulong mask = val;
                mask |= mask << 8;
                mask |= mask << 16;
                mask |= mask << 32;
                do
                {
                    *(ulong*)dst = mask;
                    dst += 8;
                    len -= 8;
                } while (len >= 8);
            }

            // 尾部不足 8 字节的部分逐字节收尾
            while (len-- > 0) *dst++ = val;
        }

        #region Encode32

        /// <summary>
        /// 按 32 位字长压缩（裸指针入口）。
        ///
        /// 这里按输入长度分成两套算法，区别就在哈希表能表示多远的历史位置：
        /// <list type="bullet">
        ///   <item>短输入（&lt; <c>LZ4_64KLIMIT</c>）：用 <c>ushort</c> 哈希表，只存 16 位偏移。
        ///         因为整个输入都在 64KB 内，相对偏移放得下，表更小、缓存更友好，故更快。</item>
        ///   <item>长输入：换 <c>byte*</c> 哈希表直接存指针，没有 64KB 限制，能覆盖 <c>MAX_DISTANCE</c> 内的全部历史。</item>
        /// </list>
        /// 两套算法产出的都是合法的 LZ4 块，解码端不需要知道用的是哪套。
        /// </summary>
        /// <param name="input">输入数据。</param>
        /// <param name="output">输出缓冲区。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <returns>写出的字节数；出错时（如缓冲区不足）返回负值。</returns>
        public static unsafe int Encode32(
            byte* input,
            byte* output,
            int inputLength,
            int outputLength)
        {
            if (inputLength < LZ4_64KLIMIT)
            {
                var hashTable = new ushort[HASH64K_TABLESIZE];
                fixed (ushort* h = &hashTable[0])
                {
                    return LZ4_compress64kCtx_32(h, input, output, inputLength, outputLength);
                }
            }
            else
            {
                var hashTable = new byte*[HASH_TABLESIZE];
                fixed (byte** h = &hashTable[0])
                {
                    return LZ4_compressCtx_32(h, input, output, inputLength, outputLength);
                }
            }
        }

        /// <summary>
        /// 按 32 位字长压缩（数组重载）。
        /// 负责参数校验、把 <c>byte[]</c> 用 <c>fixed</c> 钉住拿到底层指针，
        /// 真正干活的是上面那个指针重载。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <param name="output">输出数组。</param>
        /// <param name="outputOffset">输出起始偏移。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <returns>写出的字节数。</returns>
        public static unsafe int Encode32(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength)
        {
            CheckArguments(
                input, inputOffset, ref inputLength,
                output, outputOffset, ref outputLength);

            if (outputLength == 0) return 0;

            fixed (byte* inputPtr = &input[inputOffset])
            fixed (byte* outputPtr = &output[outputOffset])
            {
                return Encode32(inputPtr, outputPtr, inputLength, outputLength);
            }
        }

        /// <summary>
        /// 按 32 位字长压缩，自行分配输出缓冲区。
        /// 先按 <see cref="MaximumOutputLength"/> 开足大小的缓冲区，
        /// 压缩完若实际更短，再裁成精确长度返回（这样调用方拿到的数组没有多余尾巴）。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <returns>压缩后的数据。</returns>
        public static byte[] Encode32(byte[] input, int inputOffset, int inputLength)
        {
            if (inputLength < 0) inputLength = input.Length - inputOffset;

            if (input == null) throw new ArgumentNullException("input");
            if (inputOffset < 0 || inputOffset + inputLength > input.Length)
                throw new ArgumentException("inputOffset and inputLength are invalid for given input");

            var result = new byte[MaximumOutputLength(inputLength)];
            var length = Encode32(input, inputOffset, inputLength, result, 0, result.Length);

            if (length != result.Length)
            {
                if (length < 0)
                    throw new InvalidOperationException("Compression has been corrupted");
                // 实际压缩结果更短：拷进精确大小的新数组再返回
                var buffer = new byte[length];
                Buffer.BlockCopy(result, 0, buffer, 0, length);
                return buffer;
            }

            return result;
        }

        #endregion

        #region Decode32

        /// <summary>
        /// 按 32 位字长解压（裸指针入口）。
        ///
        /// 依据 <paramref name="knownOutputLength"/> 走两条不同的解压路径：
        /// <list type="bullet">
        ///   <item><c>true</c>：调用方已知输出总长。用带长度上界的 <c>LZ4_uncompress_32</c>，
        ///         解完后校验「消耗的输入字节数恰好等于 <paramref name="inputLength"/>」，
        ///         不等就说明块被截断或掺了垃圾，抛异常。校验很廉价，能挡住大部分损坏数据。</item>
        ///   <item><c>false</c>：输出总长未知。改用 <c>uncompress_unknownOutputSize</c>，
        ///         它以输入长度为界推进，靠块自身的格式确定何时结束。</item>
        /// </list>
        /// </summary>
        /// <param name="input">输入（压缩数据）。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <param name="output">输出缓冲区。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <param name="knownOutputLength">是否已知输出长度。</param>
        /// <returns>写出的字节数。</returns>
        public static unsafe int Decode32(
            byte* input,
            int inputLength,
            byte* output,
            int outputLength,
            bool knownOutputLength)
        {
            if (knownOutputLength)
            {
                var length = LZ4_uncompress_32(input, output, outputLength);
                // 返回的是消耗掉的输入长度，应当正好等于压缩块长度
                if (length != inputLength)
                    throw new ArgumentException("LZ4 block is corrupted, or invalid length has been given.");
                return outputLength;
            }
            else
            {
                var length = LZ4_uncompress_unknownOutputSize_32(input, output, inputLength, outputLength);
                if (length < 0)
                    throw new ArgumentException("LZ4 block is corrupted, or invalid length has been given.");
                return length;
            }
        }

        /// <summary>
        /// 按 32 位字长解压（数组重载）。
        /// 完成参数校验与指针固定后转交指针重载；<paramref name="knownOutputLength"/> 语义同上。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <param name="output">输出数组。</param>
        /// <param name="outputOffset">输出起始偏移。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <param name="knownOutputLength">是否已知输出长度。</param>
        /// <returns>写出的字节数。</returns>
        public static unsafe int Decode32(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength,
            bool knownOutputLength)
        {
            CheckArguments(
                input, inputOffset, ref inputLength,
                output, outputOffset, ref outputLength);

            if (outputLength == 0) return 0;

            fixed (byte* inputPtr = &input[inputOffset])
            fixed (byte* outputPtr = &output[outputOffset])
            {
                return Decode32(inputPtr, inputLength, outputPtr, outputLength, knownOutputLength);
            }
        }

        /// <summary>
        /// 按 32 位字长解压到指定大小的缓冲区。
        /// 这里强制 <c>knownOutputLength: true</c>，并要求实际写出长度与
        /// <paramref name="outputLength"/> 完全一致，否则说明调用方给的长度是错的。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <param name="outputLength">期望的解压后长度。</param>
        /// <returns>解压后的数据。</returns>
        public static byte[] Decode32(byte[] input, int inputOffset, int inputLength, int outputLength)
        {
            if (inputLength < 0) inputLength = input.Length - inputOffset;

            if (input == null) throw new ArgumentNullException("input");
            if (inputOffset < 0 || inputOffset + inputLength > input.Length)
                throw new ArgumentException("inputOffset and inputLength are invalid for given input");

            var result = new byte[outputLength];
            var length = Decode32(input, inputOffset, inputLength, result, 0, outputLength, true);
            if (length != outputLength)
                throw new ArgumentException("outputLength is not valid");
            return result;
        }

        #endregion

        #region Encode64

        /// <summary>
        /// 按 64 位字长压缩（裸指针入口）。与 <see cref="Encode32(byte*, byte*, int, int)"/> 一一对应，
        /// 只是内层按 64 位（8 字节）为一组做匹配与哈希，哈希表用 <c>uint</c> 存位置。
        /// 同样按 <c>LZ4_64KLIMIT</c> 分成「64KB 短输入用 ushort 表」与「长输入用 uint 表」两套。
        /// </summary>
        /// <param name="input">输入数据。</param>
        /// <param name="output">输出缓冲区。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <returns>写出的字节数；出错时返回负值。</returns>
        public static unsafe int Encode64(
            byte* input,
            byte* output,
            int inputLength,
            int outputLength)
        {
            if (inputLength < LZ4_64KLIMIT)
            {
                var hashTable = new ushort[HASH64K_TABLESIZE];
                fixed (ushort* h = &hashTable[0])
                {
                    return LZ4_compress64kCtx_64(h, input, output, inputLength, outputLength);
                }
            }
            else
            {
                var hashTable = new uint[HASH_TABLESIZE];
                fixed (uint* h = &hashTable[0])
                {
                    return LZ4_compressCtx_64(h, input, output, inputLength, outputLength);
                }
            }
        }

        /// <summary>
        /// 按 64 位字长压缩（数组重载）。
        /// 负责参数校验与指针固定，随后转交指针重载。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <param name="output">输出数组。</param>
        /// <param name="outputOffset">输出起始偏移。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <returns>写出的字节数。</returns>
        public static unsafe int Encode64(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength)
        {
            CheckArguments(
                input, inputOffset, ref inputLength,
                output, outputOffset, ref outputLength);

            if (outputLength == 0) return 0;

            fixed (byte* inputPtr = &input[inputOffset])
            fixed (byte* outputPtr = &output[outputOffset])
            {
                return Encode64(inputPtr, outputPtr, inputLength, outputLength);
            }
        }

        /// <summary>
        /// 按 64 位字长压缩，自行分配输出缓冲区。
        /// 逻辑与 32 位版本一致：先开足量的缓冲区，再按实际压缩长度裁剪。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <returns>压缩后的数据。</returns>
        public static byte[] Encode64(byte[] input, int inputOffset, int inputLength)
        {
            if (inputLength < 0) inputLength = input.Length - inputOffset;

            if (input == null) throw new ArgumentNullException("input");
            if (inputOffset < 0 || inputOffset + inputLength > input.Length)
                throw new ArgumentException("inputOffset and inputLength are invalid for given input");

            var result = new byte[MaximumOutputLength(inputLength)];
            var length = Encode64(input, inputOffset, inputLength, result, 0, result.Length);

            if (length != result.Length)
            {
                if (length < 0)
                    throw new InvalidOperationException("Compression has been corrupted");
                // 实际压缩结果更短：拷进精确大小的新数组再返回
                var buffer = new byte[length];
                Buffer.BlockCopy(result, 0, buffer, 0, length);
                return buffer;
            }

            return result;
        }

        #endregion

        #region Decode64

        /// <summary>
        /// 按 64 位字长解压（裸指针入口）。与 <see cref="Decode32(byte*, int, byte*, int, bool)"/> 对应，
        /// 同样按 <paramref name="knownOutputLength"/> 选择「已知长度、可校验消耗」或
        /// 「长度未知、以输入长度为界」两条路径。
        /// </summary>
        /// <param name="input">输入（压缩数据）。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <param name="output">输出缓冲区。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <param name="knownOutputLength">是否已知输出长度。</param>
        /// <returns>写出的字节数。</returns>
        public static unsafe int Decode64(
            byte* input,
            int inputLength,
            byte* output,
            int outputLength,
            bool knownOutputLength)
        {
            if (knownOutputLength)
            {
                var length = LZ4_uncompress_64(input, output, outputLength);
                // 返回的是消耗掉的输入长度，应当正好等于压缩块长度
                if (length != inputLength)
                    throw new ArgumentException("LZ4 block is corrupted, or invalid length has been given.");
                return outputLength;
            }
            else
            {
                var length = LZ4_uncompress_unknownOutputSize_64(input, output, inputLength, outputLength);
                if (length < 0)
                    throw new ArgumentException("LZ4 block is corrupted, or invalid length has been given.");
                return length;
            }
        }

        /// <summary>
        /// 按 64 位字长解压（数组重载）。
        /// 完成参数校验与指针固定后转交指针重载；<paramref name="knownOutputLength"/> 语义同上。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <param name="output">输出数组。</param>
        /// <param name="outputOffset">输出起始偏移。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <param name="knownOutputLength">是否已知输出长度。</param>
        /// <returns>写出的字节数。</returns>
        public static unsafe int Decode64(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength,
            bool knownOutputLength)
        {
            CheckArguments(
                input, inputOffset, ref inputLength,
                output, outputOffset, ref outputLength);

            if (outputLength == 0) return 0;

            fixed (byte* inputPtr = &input[inputOffset])
            fixed (byte* outputPtr = &output[outputOffset])
            {
                return Decode64(inputPtr, inputLength, outputPtr, outputLength, knownOutputLength);
            }
        }

        /// <summary>
        /// 按 64 位字长解压到指定大小的缓冲区。
        /// 与 32 位版本一样强制已知输出长度，并要求实际写出长度与
        /// <paramref name="outputLength"/> 完全一致。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度；负数表示「到数组末尾」。</param>
        /// <param name="outputLength">期望的解压后长度。</param>
        /// <returns>解压后的数据。</returns>
        public static byte[] Decode64(byte[] input, int inputOffset, int inputLength, int outputLength)
        {
            if (inputLength < 0) inputLength = input.Length - inputOffset;

            if (input == null) throw new ArgumentNullException("input");
            if (inputOffset < 0 || inputOffset + inputLength > input.Length)
                throw new ArgumentException("inputOffset and inputLength are invalid for given input");

            var result = new byte[outputLength];
            var length = Decode64(input, inputOffset, inputLength, result, 0, outputLength, true);
            if (length != outputLength)
                throw new ArgumentException("outputLength is not valid");
            return result;
        }

        #endregion

        #region HC utilities

        // ReSharper disable InconsistentNaming

        /// <summary>
        /// LZ4-HC（高压缩率档）的搜索上下文，被 HC 的压缩主循环反复读写。
        /// <list type="bullet">
        ///   <item><c>src_base</c>：本次压缩输入的起始地址。哈希表里存的是相对它的偏移，
        ///         这样换一块输入时无需重建整张表。</item>
        ///   <item><c>nextToUpdate</c>：哈希链已经推进到的位置。HC 是「边扫边补链」，
        ///         只有 <c>[nextToUpdate, 当前扫描点)</c> 这段才需要补登（见 Dirty 文件里的 LZ4HC_Insert）。</item>
        ///   <item><c>hashTable</c>：哈希桶 → 该哈希最近一次出现位置（相对 <c>src_base</c>）。</item>
        ///   <item><c>chainTable</c>：按 <c>位置 &amp; MAXD_MASK</c> 索引的环形表，存「跳到上一个同哈希位置的距离」，
        ///         用来沿链回溯候选匹配。</item>
        /// </list>
        /// </summary>
        private unsafe class LZ4HC_Data_Structure
        {
            public byte* src_base;
            public byte* nextToUpdate;
            public int[] hashTable;
            public ushort[] chainTable;
        };

        // ReSharper restore InconsistentNaming


        /// <summary>
        /// 为一块新输入创建 HC 上下文。
        /// <c>chainTable</c> 必须整表填成 <c>0xFF</c>（即距离 65535，等于 <c>MAX_DISTANCE</c>），
        /// 作为「链到此为止」的哨兵：回溯时若减出 0 就会停在边界，不会越界读到脏数据。
        /// <c>nextToUpdate</c> 从 <c>src + 1</c> 起，因为首字节没有前文可比，永远进不了匹配。
        /// </summary>
        private static unsafe LZ4HC_Data_Structure LZ4HC_Create(byte* src)
        {
            var hc4 = new LZ4HC_Data_Structure
            {
                hashTable = new int[HASHHC_TABLESIZE],
                chainTable = new ushort[MAXD]
            };

            // 环形链表整表初始化为「到头了」的哨兵值
            fixed (ushort* ct = &hc4.chainTable[0])
            {
                BlockFill((byte*)ct, MAXD * sizeof(ushort), 0xFF);
            }

            hc4.src_base = src;
            hc4.nextToUpdate = src + 1;

            return hc4;
        }

        #endregion

        #region Encode32HC

        /// <summary>
        /// 32 位 HC 压缩的便捷入口：先建好一次性的搜索上下文，再调用真正的主循环
        /// （实现在 <c>*.Dirty.cs</c> 里）。
        /// </summary>
        private static unsafe int LZ4_compressHC_32(byte* input, byte* output, int inputLength, int outputLength)
        {
            return LZ4_compressHCCtx_32(LZ4HC_Create(input), input, output, inputLength, outputLength);
        }

        /// <summary>
        /// 按 32 位字长做 HC（高压缩率）压缩，写入调用方提供的缓冲区。
        /// HC 会做多级前瞻，输出可能比输入还大，所以调用方必须给足缓冲。
        /// 内层返回 <c>&lt;= 0</c> 时统一归一成 <c>-1</c>，作为「缓冲区不够或数据异常」的约定信号。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <param name="output">输出数组。</param>
        /// <param name="outputOffset">输出起始偏移。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <returns>写出的字节数；缓冲区不足时返回负值。</returns>
        public static unsafe int Encode32HC(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength)
        {
            if (inputLength == 0) return 0;

            CheckArguments(
                input, inputOffset, ref inputLength,
                output, outputOffset, ref outputLength);

            fixed (byte* inputPtr = &input[inputOffset])
            fixed (byte* outputPtr = &output[outputOffset])
            {
                var length = LZ4_compressHC_32(inputPtr, outputPtr, inputLength, outputLength);
                return length <= 0 ? -1 : length;
            }
        }

        /// <summary>
        /// 按 32 位字长做 HC 压缩，自行分配输出缓冲区。
        /// 注意 HC 的输出**可能比输入更大**，这里按 <see cref="MaximumOutputLength"/> 预留，
        /// 若最终结果更短再裁剪。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <returns>压缩后的数据。</returns>
        public static byte[] Encode32HC(
            byte[] input, int inputOffset, int inputLength)
        {
            if (inputLength == 0) return new byte[0];
            var outputLength = MaximumOutputLength(inputLength);
            var result = new byte[outputLength];
            var length = Encode32HC(input, inputOffset, inputLength, result, 0, outputLength);

            if (length < 0)
                throw new ArgumentException("Provided data seems to be corrupted.");

            if (length != outputLength)
            {
                var buffer = new byte[length];
                Buffer.BlockCopy(result, 0, buffer, 0, length);
                result = buffer;
            }

            return result;
        }

        #endregion

        #region Encode64HC

        /// <summary>
        /// 64 位 HC 压缩的便捷入口：建好搜索上下文后转交 64 位主循环。
        /// </summary>
        private static unsafe int LZ4_compressHC_64(byte* input, byte* output, int inputLength, int outputLength)
        {
            return LZ4_compressHCCtx_64(LZ4HC_Create(input), input, output, inputLength, outputLength);
        }

        /// <summary>
        /// 按 64 位字长做 HC（高压缩率）压缩，写入调用方提供的缓冲区。
        /// 与 32 位版本约定一致：内层返回 <c>&lt;= 0</c> 时归一成 <c>-1</c>，
        /// 调用方据此判断缓冲区是否给小了。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <param name="output">输出数组。</param>
        /// <param name="outputOffset">输出起始偏移。</param>
        /// <param name="outputLength">输出缓冲区长度。</param>
        /// <returns>写出的字节数；缓冲区不足时返回负值。</returns>
        public static unsafe int Encode64HC(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength)
        {
            if (inputLength == 0) return 0;

            CheckArguments(
                input, inputOffset, ref inputLength,
                output, outputOffset, ref outputLength);

            fixed (byte* inputPtr = &input[inputOffset])
            fixed (byte* outputPtr = &output[outputOffset])
            {
                var length = LZ4_compressHC_64(inputPtr, outputPtr, inputLength, outputLength);
                return length <= 0 ? -1 : length;
            }
        }

        /// <summary>
        /// 按 64 位字长做 HC 压缩，自行分配输出缓冲区。
        /// 同样注意 HC 输出可能大于输入，故按 <see cref="MaximumOutputLength"/> 预留后再裁剪。
        /// </summary>
        /// <param name="input">输入数组。</param>
        /// <param name="inputOffset">输入起始偏移。</param>
        /// <param name="inputLength">输入长度。</param>
        /// <returns>压缩后的数据。</returns>
        public static byte[] Encode64HC(
            byte[] input, int inputOffset, int inputLength)
        {
            if (inputLength == 0) return new byte[0];
            var outputLength = MaximumOutputLength(inputLength);
            var result = new byte[outputLength];
            var length = Encode64HC(input, inputOffset, inputLength, result, 0, outputLength);

            if (length < 0)
                throw new ArgumentException("Provided data seems to be corrupted.");

            if (length != outputLength)
            {
                var buffer = new byte[length];
                Buffer.BlockCopy(result, 0, buffer, 0, length);
                result = buffer;
            }

            return result;
        }

        #endregion
    }
}