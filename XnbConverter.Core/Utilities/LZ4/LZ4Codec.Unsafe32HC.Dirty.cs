#region LZ4 original

/*
   LZ4 - Fast LZ compression algorithm
   Copyright (C) 2011-2012, Yann Collet.
   BSD 2-Clause License (http://www.opensource.org/licenses/bsd-license.php)

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions are
   met:

       * Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
       * Redistributions in binary form must reproduce the above
   copyright notice, this list of conditions and the following disclaimer
   in the documentation and/or other materials provided with the
   distribution.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
   OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
   SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
   LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
   DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
   THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
   (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
   OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

   You can contact the author at :
   - LZ4 homepage : http://fastcompression.blogspot.com/p/lz4.html
   - LZ4 source repository : http://code.google.com/p/lz4/
*/

#endregion

#pragma warning disable

#region LZ4 port

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

// ReSharper disable InconsistentNaming

namespace XnbConverter.Utilities.LZ4
{
    internal static partial class LZ4Codec
    {
        /// <summary>
        /// 把哈希链推进到 <paramref name="src_p"/>（不含），也就是为 <c>[nextToUpdate, src_p)</c>
        /// 这段新扫过的字节补登链条目。
        ///
        /// 两张表的分工：
        /// <list type="bullet">
        ///   <item><c>hashTable</c>：哈希桶 → 该哈希最近一次出现的位置（相对 <c>src_base</c>）。</item>
        ///   <item><c>chainTable</c>：按 <c>位置 &amp; MAXD_MASK</c> 索引的**环形**表，存的是
        ///         「跳到上一个同哈希位置的距离」。因为只保留低 16 位，环形表正好覆盖
        ///         LZ4 允许的最大回溯距离 65535，不会串味。</item>
        /// </list>
        /// 哈希用的是 Knuth 乘法散列：2654435761 = 2^32 / φ，乘完取高 HASHHC_LOG 位。
        /// </summary>
        private static unsafe void LZ4HC_Insert_32(LZ4HC_Data_Structure hc4, byte* src_p)
        {
            fixed (ushort* chainTable = hc4.chainTable)
            fixed (int* hashTable = hc4.hashTable)
            {
                var src_base = hc4.src_base;

                while (hc4.nextToUpdate < src_p)
                {
                    var p = hc4.nextToUpdate;
                    var delta = (int)((p) - (hashTable[((((*(uint*)(p))) * 2654435761u) >> HASHHC_ADJUST)] + src_base));
                    if (delta > MAX_DISTANCE) delta = MAX_DISTANCE;
                    chainTable[((int)p) & MAXD_MASK] = (ushort)delta;
                    hashTable[((((*(uint*)(p))) * 2654435761u) >> HASHHC_ADJUST)] = (int)(p - src_base);
                    hc4.nextToUpdate++;
                }
            }
        }

        /// <summary>
        /// 从 <paramref name="p1"/>/<paramref name="p2"/> 开始逐字节比对，返回相同前缀的长度。
        ///
        /// 主体按 32 位一块比：相等就前进 4 字节。不等时用 <c>x &amp; -x</c> 隔离出最低的
        /// 那个 1 位，再乘 De Bruijn 魔数 0x077CB531 取高 5 位查表，直接得到「末尾有几个
        /// 零」——也就是这 4 字节里第一处不同的下标。省掉了逐字节循环。
        ///
        /// 收尾处必须留够 <c>src_LASTLITERALS</c>（末端 5 字节不能参与匹配），所以尾部退回
        /// 2 字节、1 字节的逐字节比较。返回值可以直接当匹配长度用。
        /// </summary>
        private static unsafe int LZ4HC_CommonLength_32(byte* p1, byte* p2, byte* src_LASTLITERALS)
        {
            fixed (int* debruijn32 = DEBRUIJN_TABLE_32)
            {
                var p1t = p1;

                while (p1t < src_LASTLITERALS - (STEPSIZE_32 - 1))
                {
                    var diff = (*(int*)(p2)) ^ (*(int*)(p1t));
                    if (diff == 0)
                    {
                        p1t += STEPSIZE_32;
                        p2 += STEPSIZE_32;
                        continue;
                    }

                    p1t += debruijn32[(((uint)((diff) & -(diff)) * 0x077CB531u)) >> 27];
                    return (int)(p1t - p1);
                }

                if ((p1t < (src_LASTLITERALS - 1)) && ((*(ushort*)(p2)) == (*(ushort*)(p1t))))
                {
                    p1t += 2;
                    p2 += 2;
                }

                if ((p1t < src_LASTLITERALS) && (*p2 == *p1t)) p1t++;
                return (int)(p1t - p1);
            }
        }

        /// <summary>
        /// HC4 的「最佳匹配」查找：在 <paramref name="src_p"/> 处沿哈希链往回追，返回最长匹配
        /// 长度，并把匹配位置写进 <paramref name="matchpos"/>。
        ///
        /// 两个提速点：
        /// <list type="number">
        ///   <item>先单独判一次「长度 ≤ 4 的重复串」：这种短重复在贴图/文本里极常见，
        ///         命中后可以一次性把中间那些位置的 chainTable / hashTable 全部预填好
        ///         （下面 <c>// Complete table</c> 那段），后续位置就不用再逐个 Insert。</item>
        ///   <item>链上每个候选先只看 <c>第 ml 字节</c> 是否相同作为廉价预筛，过了才去比 4 字节。</item>
        /// </list>
        /// <c>nbAttempts</c>（MAX_NB_ATTEMPTS = 256）是硬上限：链上太多了直接放弃，
        /// 属于拿压缩率换速度的旋钮。
        /// </summary>
        private static unsafe int LZ4HC_InsertAndFindBestMatch_32(
            LZ4HC_Data_Structure hc4, byte* src_p, byte* src_LASTLITERALS, ref byte* matchpos)
        {
            fixed (ushort* chainTable = hc4.chainTable)
            fixed (int* hashTable = hc4.hashTable)
            {
                var src_base = hc4.src_base;
                var nbAttempts = MAX_NB_ATTEMPTS;
                int repl = 0, ml = 0;
                ushort delta = 0;

                // 先把哈希链推到当前位置，再取桶里的头节点作为第一个候选
                LZ4HC_Insert_32(hc4, src_p);
                var xxx_ref = (hashTable[((((*(uint*)(src_p))) * 2654435761u) >> HASHHC_ADJUST)] + src_base);

                // 优先处理「长度 ≤ 4 的重复串」——贴图里的连续同色、文本里的重复字符都属于这类
                if (xxx_ref >= src_p - 4) // 距离 ≤ 4，值得单独判一次
                {
                    if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p))) // 确实是 4 字节重复
                    {
                        delta = (ushort)(src_p - xxx_ref);
                        repl = ml = LZ4HC_CommonLength_32(src_p + MINMATCH, xxx_ref + MINMATCH, src_LASTLITERALS) +
                                    MINMATCH;
                        matchpos = xxx_ref;
                    }

                    xxx_ref = ((xxx_ref) - chainTable[((int)xxx_ref) & MAXD_MASK]);
                }

                while ((xxx_ref >= src_p - MAX_DISTANCE) && (nbAttempts != 0))
                {
                    nbAttempts--;
                    if (*(xxx_ref + ml) == *(src_p + ml))
                        if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p)))
                        {
                            var mlt = LZ4HC_CommonLength_32(src_p + MINMATCH, xxx_ref + MINMATCH, src_LASTLITERALS) +
                                      MINMATCH;
                            if (mlt > ml)
                            {
                                ml = mlt;
                                matchpos = xxx_ref;
                            }
                        }

                    xxx_ref = ((xxx_ref) - chainTable[((int)xxx_ref) & MAXD_MASK]);
                }

                // 把这段重复串覆盖到的位置一次性补进两张表，
                // 后面主循环扫到这里时就不必再逐个 Insert 了（省下的正是最热的那条路径）
                if (repl != 0)
                {
                    var src_ptr = src_p;

                    var src_end = src_p + repl - (MINMATCH - 1);
                    // 前半段只填 chainTable：这些位置的 hashTable 头还是别的东西，不能覆盖
                    while (src_ptr < src_end - delta)
                    {
                        chainTable[((int)src_ptr) & MAXD_MASK] = delta;
                        src_ptr++;
                    }

                    // 后半段才开始接管哈希桶的头节点
                    do
                    {
                        chainTable[((int)src_ptr) & MAXD_MASK] = delta;
                        hashTable[((((*(uint*)(src_ptr))) * 2654435761u) >> HASHHC_ADJUST)] =
                            (int)(src_ptr - src_base);
                        src_ptr++;
                    } while (src_ptr < src_end);

                    hc4.nextToUpdate = src_end;
                }

                return ml;
            }
        }

        /// <summary>
        /// 在 <paramref name="src_p"/> 处找**比 <paramref name="longest"/> 更长**的匹配，
        /// 用于 HC 的「懒匹配」前瞻：宁可当前位置少编一点，也要吃到后面更长的匹配。
        ///
        /// 和前一个方法的区别是它会**向前后双向扩展**：
        /// <list type="bullet">
        ///   <item>向前：往 <paramref name="startLimit"/> 方向逐字节回退（<c>startt[-1] == reft[-1]</c>），
        ///         把匹配的起点往左挪，从而变长。</item>
        ///   <item><paramref name="delta"/> = <c>src_p - startLimit</c>，用来把「当前扫描起点」
        ///         换算回「真实比较位置」，否则回退对齐就错了。</item>
        /// </list>
        /// 命中条件里 <c>*(startLimit + longest) == *(xxx_ref - delta + longest)</c> 是廉价预筛：
        /// 只比「比现有最长还多一位」的那个字节，不相等就整条跳过。
        /// </summary>
        private static unsafe int LZ4HC_InsertAndGetWiderMatch_32(
            LZ4HC_Data_Structure hc4, byte* src_p, byte* startLimit, byte* src_LASTLITERALS, int longest,
            ref byte* matchpos, ref byte* startpos)
        {
            fixed (ushort* chainTable = hc4.chainTable)
            fixed (int* hashTable = hc4.hashTable)
            fixed (int* debruijn32 = DEBRUIJN_TABLE_32)
            {
                var src_base = hc4.src_base;
                var nbAttempts = MAX_NB_ATTEMPTS;
                var delta = (int)(src_p - startLimit);

                // 沿链找第一个候选就开比
                LZ4HC_Insert_32(hc4, src_p);
                var xxx_ref = (hashTable[((((*(uint*)(src_p))) * 2654435761u) >> HASHHC_ADJUST)] + src_base);

                while ((xxx_ref >= src_p - MAX_DISTANCE) && (nbAttempts != 0))
                {
                    nbAttempts--;
                    if (*(startLimit + longest) == *(xxx_ref - delta + longest))
                    {
                        if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p)))
                        {
                            var reft = xxx_ref + MINMATCH;
                            var ipt = src_p + MINMATCH;
                            var startt = src_p;

                            while (ipt < src_LASTLITERALS - (STEPSIZE_32 - 1))
                            {
                                var diff = (*(int*)(reft)) ^ (*(int*)(ipt));
                                if (diff == 0)
                                {
                                    ipt += STEPSIZE_32;
                                    reft += STEPSIZE_32;
                                    continue;
                                }

                                ipt += debruijn32[(((uint)((diff) & -(diff)) * 0x077CB531u)) >> 27];
                                goto _endCount;
                            }

                            if ((ipt < (src_LASTLITERALS - 1)) && ((*(ushort*)(reft)) == (*(ushort*)(ipt))))
                            {
                                ipt += 2;
                                reft += 2;
                            }

                            if ((ipt < src_LASTLITERALS) && (*reft == *ipt)) ipt++;
                            _endCount:
                            reft = xxx_ref;

                            while ((startt > startLimit) && (reft > hc4.src_base) && (startt[-1] == reft[-1]))
                            {
                                startt--;
                                reft--;
                            }

                            if ((ipt - startt) > longest)
                            {
                                longest = (int)(ipt - startt);
                                matchpos = reft;
                                startpos = startt;
                            }
                        }
                    }

                    xxx_ref = ((xxx_ref) - chainTable[((int)xxx_ref) & MAXD_MASK]);
                }

                return longest;
            }
        }

        /// <summary>
        /// 编出「一段字面量 + 一个匹配」这个标准序列，是 LZ4 块格式的唯一写出点。
        ///
        /// 序列长这样（token 是 1 字节，高 4 位存字面量长度、低 4 位存匹配长度）：
        /// <code>
        /// [token][额外的字面量长度字节…][字面量…][2 字节偏移][额外的匹配长度字节…]
        /// </code>
        /// 长度字段的规则：值 &lt; 15 就只写在 token 的半字节里；等于 15 表示「还有续字节」，
        /// 后续每个 255 表示 +255，最后一个 &lt;255 的字节收尾。
        ///
        /// <paramref name="src_anchor"/> 指向尚未输出的字面量起点，函数返回后会被推进到
        /// <c>src_p + matchLength</c>，即下一个未处理位置。
        ///
        /// 返回 1 表示输出缓冲不够（调用方据此放弃本块）；正常返回 0。
        /// </summary>
        private static unsafe int LZ4_encodeSequence_32(
            ref byte* src_p, ref byte* dst_p, ref byte* src_anchor, int matchLength, byte* xxx_ref, byte* dst_end)
        {
            int len;

            // 1) 字面量长度：先占住 token 字节，稍后再回填它的高 4 位
            var length = (int)(src_p - src_anchor);
            var xxx_token = (dst_p)++;
            // 预检缓冲够不够：字面量本身 + 2 字节偏移 + 1 字节匹配长度 + 5 字节收尾，
            // 再加 length/256 个续字节。不够就让调用方放弃这块。
            if ((dst_p + length + (2 + 1 + LASTLITERALS) + (length >> 8)) > dst_end) return 1;
            if (length >= RUN_MASK)
            {
                *xxx_token = (RUN_MASK << ML_BITS);
                len = length - RUN_MASK;
                for (; len > 254; len -= 255) *(dst_p)++ = 255;
                *(dst_p)++ = (byte)len;
            }
            else
            {
                *xxx_token = (byte)(length << ML_BITS);
            }

            // 2) 拷字面量：按 32 位一次搬 8 字节（两轮），比逐字节快得多。
            //    末尾多写的几字节落点会被下面 dst_p = _p 裁掉，不影响输出
            //（上面那次上限检查已经为这几字节留了余量）。
            var _p = dst_p + (length);
            do
            {
                *(uint*)dst_p = *(uint*)src_anchor;
                dst_p += 4;
                src_anchor += 4;
                *(uint*)dst_p = *(uint*)src_anchor;
                dst_p += 4;
                src_anchor += 4;
            } while (dst_p < _p);

            dst_p = _p;

            // 3) 偏移：小端 2 字节，值就是「往回多少」。
            //    不必判 0 —— 匹配距离至少 1，0 在 LZ4 里非法。
            *(ushort*)dst_p = (ushort)(src_p - xxx_ref);
            dst_p += 2;

            // 4) 匹配长度：存的是 matchLength - MINMATCH（最少 4 字节的匹配才编码，省掉一个常量）
            len = (matchLength - MINMATCH);
            if (dst_p + (1 + LASTLITERALS) + (length >> 8) > dst_end) return 1;
            if (len >= ML_MASK)
            {
                *xxx_token += ML_MASK;
                len -= ML_MASK;
                for (; len > 509; len -= 510)
                {
                    *(dst_p)++ = 255;
                    *(dst_p)++ = 255;
                }

                if (len > 254)
                {
                    len -= 255;
                    *(dst_p)++ = 255;
                }

                *(dst_p)++ = (byte)len;
            }
            else
            {
                *xxx_token += (byte)len;
            }

            // 5) 推进到匹配之后：这一段已经输出了，下一个序列的字面量从这里重新攒
            src_p += matchLength;
            src_anchor = src_p;

            return 0;
        }

        /// <summary>
        /// LZ4-HC 的主压缩循环，按 32 位平台实现。
        ///
        /// 与快速档最大的差别是**前瞻**：先找当前位置的最佳匹配 <c>ml</c>，再往后探一层
        /// （<c>_Search2</c>）甚至两层（<c>_Search3</c>），比较「早编一个短的」和
        /// 「晚编一个长的」哪个更省，从而牺牲速度换压缩率。
        /// <c>start0/ref0/ml0</c> 保存的是第一层结果，因为第二层的搜索点
        /// （<c>src_p + ml - 2</c>）可能把更优的起点漏掉。
        ///
        /// 几个边界常量：
        /// <list type="bullet">
        ///   <item><c>src_mflimit = src_end - MFLIMIT</c>：主循环的终点。块尾最后 13 字节
        ///         不再参与匹配，否则编码器可能来不及写收尾。</item>
        ///   <item><c>src_LASTLITERALS = src_end - LASTLITERALS</c>：任何匹配都不许碰到
        ///         最后 5 字节，这是 LZ4 规范给解压器留的安全边界。</item>
        ///   <item><c>OPTIMAL_ML = 18</c>：匹配长度超过它以后，每多 255 个字节才多 1 个
        ///         长度字节，收益递减，所以裁到 18 以便腾出位置给后面的匹配。</item>
        /// </list>
        /// 中途任何一次写出检测到缓冲不足都返回 0，调用方会退回「整块不压缩」。
        /// 返回值为写完的字节数。
        /// </summary>
        private static unsafe int LZ4_compressHCCtx_32(
            LZ4HC_Data_Structure ctx,
            byte* src,
            byte* dst,
            int src_len,
            int dst_maxlen)
        {
            var src_p = src;
            var src_anchor = src_p;
            var src_end = src_p + src_len;
            var src_mflimit = src_end - MFLIMIT;
            var src_LASTLITERALS = (src_end - LASTLITERALS);

            var dst_p = dst;
            var dst_end = dst_p + dst_maxlen;

            byte* xxx_ref = null;
            byte* start2 = null;
            byte* ref2 = null;
            byte* start3 = null;
            byte* ref3 = null;

            // 首字节永远进不了匹配（没有可比的前文），直接跳过
            src_p++;

            // 主循环：每个位置决定了「从这里往后要编几段」
            while (src_p < src_mflimit)
            {
                var ml = LZ4HC_InsertAndFindBestMatch_32(ctx, src_p, src_LASTLITERALS, ref xxx_ref);
                if (ml == 0)
                {
                    // 没匹配，当前位置并进字面量，继续往后找
                    src_p++;
                    continue;
                }

                // 存下第一层的结果：_Search2 的搜索点会往前挪，可能反而漏掉更优的起点
                var start0 = src_p;
                var ref0 = xxx_ref;
                var ml0 = ml;

                // 第二层前瞻：从「第一个匹配的倒数第 2 字节」再找一次，看有没有更长的
                _Search2:
                var ml2 = src_p + ml < src_mflimit
                    ? LZ4HC_InsertAndGetWiderMatch_32(ctx, src_p + ml - 2, src_p + 1, src_LASTLITERALS, ml, ref ref2,
                        ref start2)
                    : ml;

                if (ml2 == ml) // 没有更长的：直接编这一段的匹配
                {
                    if (LZ4_encodeSequence_32(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0)
                        return 0;
                    continue;
                }

                if (start0 < src_p)
                {
                    // 经验阈值：新匹配的起点挪得太靠前就不值当，回退到第一层的结果
                    if (start2 < src_p + ml0)
                    {
                        src_p = start0;
                        xxx_ref = ref0;
                        ml = ml0;
                    }
                }

                // 走到这里 start0 == src_p
                if ((start2 - src_p) < 3) // 前一个匹配太短，丢掉，改用后找到的这个
                {
                    ml = ml2;
                    src_p = start2;
                    xxx_ref = ref2;
                    goto _Search2;
                }

                // 第三层前瞻。此刻已知 ml2 > ml，且两段匹配的起点相距 ≥ 3（上面刚保证过）
                _Search3:
                // 把第一段的长度裁到刚好接上第二段：太长会吃掉第二段的开头，反而变差。
                // OPTIMAL_ML = 18 是经验上限，超过这个长度再多编基本不省字节。
                if ((start2 - src_p) < OPTIMAL_ML)
                {
                    var new_ml = ml;
                    if (new_ml > OPTIMAL_ML) new_ml = OPTIMAL_ML;
                    if (src_p + new_ml > start2 + ml2 - MINMATCH) new_ml = (int)(start2 - src_p) + ml2 - MINMATCH;
                    var correction = new_ml - (int)(start2 - src_p);
                    if (correction > 0)
                    {
                        // 第一段让出来的字节补给第二段，总量不变
                        start2 += correction;
                        ref2 += correction;
                        ml2 -= correction;
                    }
                }
                // 到这里 start2 = ip + new_ml，其中 new_ml = min(ml, OPTIMAL_ML)

                var ml3 = start2 + ml2 < src_mflimit
                    ? LZ4HC_InsertAndGetWiderMatch_32(ctx, start2 + ml2 - 3, start2, src_LASTLITERALS, ml2, ref ref3,
                        ref start3)
                    : ml2;

                if (ml3 == ml2) // 没有更长的了：这一段只能编两个序列，收工
                {
                    // 第一段的长度不能越过第二段的起点
                    if (start2 < src_p + ml) ml = (int)(start2 - src_p);
                    if (LZ4_encodeSequence_32(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0)
                        return 0;
                    src_p = start2;
                    if (LZ4_encodeSequence_32(ref src_p, ref dst_p, ref src_anchor, ml2, ref2, dst_end) != 0) return 0;
                    continue;
                }

                if (start3 < src_p + ml + 3) // 第三段起点离得太近，中间塞不下第二段，放弃第二段
                {
                    // 如果第三段起点在第一段结束之后，就可以立刻写出第一段；
                    // 第二段被丢掉，第三段顶上来当新的第二段
                    if (start3 >= (src_p + ml))
                    {
                        if (start2 < src_p + ml)
                        {
                            var correction = (int)(src_p + ml - start2);
                            start2 += correction;
                            ref2 += correction;
                            ml2 -= correction;
                            if (ml2 < MINMATCH)
                            {
                                start2 = start3;
                                ref2 = ref3;
                                ml2 = ml3;
                            }
                        }

                        if (LZ4_encodeSequence_32(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0)
                            return 0;
                        src_p = start3;
                        xxx_ref = ref3;
                        ml = ml3;

                        start0 = start2;
                        ref0 = ref2;
                        ml0 = ml2;
                        goto _Search2;
                    }

                    start2 = start3;
                    ref2 = ref3;
                    ml2 = ml3;
                    goto _Search3;
                }

                // 三段匹配依次递增，至少要把第一段写出去。
                // 同样地，第一段不能越到第二段起点之后
                if (start2 < src_p + ml)
                {
                    if ((start2 - src_p) < ML_MASK)
                    {
                        if (ml > OPTIMAL_ML) ml = OPTIMAL_ML;
                        if (src_p + ml > start2 + ml2 - MINMATCH) ml = (int)(start2 - src_p) + ml2 - MINMATCH;
                        var correction = ml - (int)(start2 - src_p);
                        if (correction > 0)
                        {
                            start2 += correction;
                            ref2 += correction;
                            ml2 -= correction;
                        }
                    }
                    else
                    {
                        ml = (int)(start2 - src_p);
                    }
                }

                if (LZ4_encodeSequence_32(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0) return 0;

                src_p = start2;
                xxx_ref = ref2;
                ml = ml2;

                start2 = start3;
                ref2 = ref3;
                ml2 = ml3;

                goto _Search3;
            }

            // 收尾：把剩下没编的字面量整段写出，形式和不带匹配的 token 一样（低 4 位为 0）。
            // LZ4 规范要求块尾至少留 LASTLITERALS(5) 个字节的字面量，这里天然满足。
            {
                var lastRun = (int)(src_end - src_anchor);
                // 这里用整数除法算出续字节个数，是规范里唯一允许的估法
                if ((dst_p - dst) + lastRun + 1 + ((lastRun + 255 - RUN_MASK) / 255) > (uint)dst_maxlen)
                    return 0;
                if (lastRun >= RUN_MASK)
                {
                    *dst_p++ = (RUN_MASK << ML_BITS);
                    lastRun -= RUN_MASK;
                    for (; lastRun > 254; lastRun -= 255) *dst_p++ = 255;
                    *dst_p++ = (byte)lastRun;
                }
                else *dst_p++ = (byte)(lastRun << ML_BITS);

                BlockCopy(src_anchor, dst_p, (int)(src_end - src_anchor));
                dst_p += src_end - src_anchor;
            }

            // 返回实际写出的字节数
            return (int)((dst_p) - dst);
        }
    }
}

// ReSharper restore InconsistentNaming