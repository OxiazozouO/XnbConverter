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
// ReSharper disable TooWideLocalVariableScope
// ReSharper disable JoinDeclarationAndInitializer

namespace XnbConverter.Utilities.LZ4
{
    internal static partial class LZ4Codec
    {
        #region LZ4_compressCtx_32

        /// <summary>
        /// LZ4 快速压缩档（fast compressor）的主入口，32 位平台实现 —— 移植自 lz4 r93。
        ///
        /// 与 HC（高压缩率）档不同，这里**不做前瞻**：扫到第一个可用匹配就立刻编出去，
        /// 用压缩率换速度。整体节奏是：
        /// <list type="number">
        ///   <item><c>src_anchor</c> 记住「上一段还没输出的字面量（literal）起点」。</item>
        ///   <item>从 <c>src_p</c> 找匹配；找到就把 <c>[src_anchor, src_p)</c> 当字面量写出，
        ///         再写「2 字节偏移 + 匹配长度」，然后把 <c>src_anchor</c> 推到匹配之后。</item>
        ///   <item>重复，直到逼近块尾，剩下的字节整段当字面量收尾。</item>
        /// </list>
        ///
        /// 找匹配用的是**跳步**搜索，不是逐字节：每轮把检查点往前推
        /// <c>findMatchAttempts++ &gt;&gt; SKIPSTRENGTH</c> 字节。开头步长是 1（近似逐字节），
        /// 连续落空后步长按 2 的幂逐级拉大，于是遇到不可压缩数据能迅速跨过去 —— 这就是
        /// <c>SKIPSTRENGTH</c>（默认 6）的作用，也是「快速档」快的原因。
        ///
        /// 哈希用 Knuth 乘法散列：乘 2654435761（≈ 2^32 / 黄金比例 φ）再取高位，
        /// 对应表达式 <c>* 2654435761u &gt;&gt; HASH_ADJUST</c>。桶里存的是位置相对 <c>src_base</c>
        /// 的偏移（不是指针）；本函数里 <c>src_base</c> 恒为 0，所以其实是被截成 32 位的地址 ——
        /// 这正是「32 位实现」的由来：表更小、缓存更友好，代价是输入不能超过 4GB。
        ///
        /// <para>快速档每个桶只记**最近一次**出现的位置，没有链；一次比对不中就继续跳步。</para>
        /// </summary>
        /// <param name="hash_table">由调用方分配并清零的哈希桶数组。</param>
        /// <returns>写入 <paramref name="dst"/> 的字节数；输出缓冲放不下时返回 0，
        /// 调用方会退回「整块不压缩」。</returns>
        private static unsafe int LZ4_compressCtx_32(
            byte** hash_table,
            byte* src,
            byte* dst,
            int src_len,
            int dst_maxlen)
        {
            byte* _p;

            fixed (int* debruijn32 = &DEBRUIJN_TABLE_32[0])
            {
                // 以下变量沿用 lz4 r93 版源码的命名
                var src_p = src;                     // 当前扫描/查找位置
                const int src_base = 0;              // 哈希桶里存的偏移以此为基准（此处即 0）
                var src_anchor = src_p;              // 尚未输出的字面量起点
                var src_end = src_p + src_len;       // 输入末尾（不含）
                var src_mflimit = src_end - MFLIMIT; // 主循环终点：越过它就不可能再安全地放下匹配

                var dst_p = dst;
                var dst_end = dst_p + dst_maxlen;

                // 各种「末尾保留区」起点：LZ4 规定块尾最后 LASTLITERALS(5) 字节必须是字面量，
                // 匹配不许碰到它们。这些常量都是预先算好、循环里只做指针比较的边界。
                var src_LASTLITERALS = src_end - LASTLITERALS;      // 匹配可触及的输入上界（不含）
                var src_LASTLITERALS_1 = src_LASTLITERALS - 1;      // 还要再留 1 字节

                var src_LASTLITERALS_STEPSIZE_1 = src_LASTLITERALS - (STEPSIZE_32 - 1); // 整块 4 字节比较的上界
                var dst_LASTLITERALS_1 = dst_end - (1 + LASTLITERALS);                  // 预留 1+5 字节
                var dst_LASTLITERALS_3 = dst_end - (2 + 1 + LASTLITERALS);              // 预留 2+1+5 字节

                int length;

                uint h, h_fwd; // 当前位置 / 下一位置的哈希桶下标

                // Init：太短的输入（&lt; MINLENGTH = MFLIMIT+1 = 13）不可能编出匹配，整块当字面量
                if (src_len < MINLENGTH) goto _last_literals;

                // First Byte：首字节没有前文可匹配，先登记进哈希表；再预取「下一个位置」的哈希
                // 到 h_fwd，主循环里就不必重算。
                hash_table[((((*(uint*)(src_p))) * 2654435761u) >> HASH_ADJUST)] = (src_p - src_base);
                src_p++;
                h_fwd = ((((*(uint*)(src_p))) * 2654435761u) >> HASH_ADJUST);

                // 主循环：每轮编出「一段字面量 + 一个匹配」，一直跑到块尾
                while (true)
                {
                    // 跳步搜索的进度计。初值里的 +3 是原始实现的微调，让开头几十次尝试
                    // 的步长仍保持为 1，不至于一开始就跳着找而漏掉近处的匹配。
                    var findMatchAttempts = (1 << SKIPSTRENGTH) + 3;
                    var src_p_fwd = src_p; // 「下一个检查位置」，随跳步前进
                    byte* xxx_ref;         // 找到的匹配在输入里的位置
                    byte* xxx_token;       // 本序列的 token 字节，稍后回填高低两个半字节

                    // Find a match：跳步扫描，直到找到一个「距离合法且首 4 字节吻合」的匹配
                    do
                    {
                        h = h_fwd;
                        // 步长 = 尝试次数 >> SKIPSTRENGTH。落空越久步长越大，遇到不可压缩数据
                        // 就能一次跨过一大片 —— 这是快速档「快」的关键。
                        var step = findMatchAttempts++ >> SKIPSTRENGTH;
                        src_p = src_p_fwd;
                        src_p_fwd = src_p + step;

                        // 越过 mflimit 说明剩余字节已经不够安全地收尾，直接跳去写尾字面量
                        if (src_p_fwd > src_mflimit) goto _last_literals;

                        h_fwd = ((((*(uint*)(src_p_fwd))) * 2654435761u) >> HASH_ADJUST);
                        xxx_ref = src_base + hash_table[h];
                        hash_table[h] = (src_p - src_base); // 桶只保留最近一次出现的位置
                    // 候选要同时满足：落在 MAX_DISTANCE(65535) 回溯窗口内，且首 4 字节真的相等
                    //（哈希只是散列、会碰撞，必须复核）。从未写过的空桶值为 0，会天然落进前半条被否掉。
                    } while ((xxx_ref < src_p - MAX_DISTANCE) || ((*(uint*)(xxx_ref)) != (*(uint*)(src_p))));

                    // Catch up：匹配点已定，再往前逐字节回退，把扫描时已跳过、但同样相同的字节
                    // 也吃进匹配里 —— 字面量更短、匹配更长，两头都省。
                    while ((src_p > src_anchor) && (xxx_ref > src) && (src_p[-1] == xxx_ref[-1]))
                    {
                        src_p--;
                        xxx_ref--;
                    }

                    // Encode Literal length：先把 token 字节占住（内容稍后回填），
                    // 字面量长度就是 src_anchor 到 src_p 之间的字节数。
                    length = (int)(src_p - src_anchor);
                    xxx_token = dst_p++;

                    // 输出上限预检：字面量数据 length 字节 + 约 length/256 个长度续字节，
                    // 再加 2(偏移)+1(匹配长度)+LASTLITERALS(5) 的固定余量。
                    // dst_LASTLITERALS_3 正是 dst_end 扣掉这个余量后的结果。放不下就整块放弃（返回 0）。
                    if (dst_p + length + (length >> 8) > dst_LASTLITERALS_3) return 0; // 检查输出上限

                    // 长度 < RUN_MASK(15)：直接塞进 token 的高 4 位就算完。
                    // 长度 ≥ 15：高 4 位写满 15，后面跟一串 255，最后一个 <255 的字节收尾
                    //（RUN_MASK 就是 token 高 4 位能表示的最大值）。
                    if (length >= RUN_MASK)
                    {
                        var len = length - RUN_MASK;
                        *xxx_token = (RUN_MASK << ML_BITS);
                        if (len > 254)
                        {
                            // 长字面量：先吐 255 续字节；这段数据量大，随后的搬运改用 BlockCopy
                            do
                            {
                                *dst_p++ = 255;
                                len -= 255;
                            } while (len > 254);

                            *dst_p++ = (byte)len;
                            BlockCopy(src_anchor, dst_p, (length));
                            dst_p += length;
                            goto _next_match; // 字面量已经搬完，跳过下面那段手写拷贝循环
                        }

                        *dst_p++ = (byte)len;
                    }
                    else
                    {
                        // 高 4 位放字面量长度，低 4 位先置 0，等会儿再补匹配长度
                        *xxx_token = (byte)(length << ML_BITS);
                    }

                    // Copy Literals：按 32 位一次搬 8 字节（两轮 4 字节），比逐字节快得多。
                    // 末尾可能多拷几个字节，无害 —— 循环后 dst_p = _p 会把多出来的裁掉。
                    _p = dst_p + (length);
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

                    _next_match:

                    // Encode Offset：小端 2 字节的「往回多少」。MAX_DISTANCE 恰好是 65535，
                    // 一个 ushort 就装得下，所以偏移永远不需要续字节。
                    *(ushort*)dst_p = (ushort)(src_p - xxx_ref);
                    dst_p += 2;

                    // Start Counting：首 4 字节在查找阶段已经验证相等（MINMATCH），这里直接跳过，
                    // 匹配指针同步前进。src_anchor 跟着挪到匹配之后，作为下一段字面量的起点。
                    src_p += MINMATCH;
                    xxx_ref += MINMATCH; // MINMATCH 已在查找阶段验证过
                    src_anchor = src_p;

                    // 统计匹配长度：一次比 4 字节，相等就继续往前推
                    while (src_p < src_LASTLITERALS_STEPSIZE_1)
                    {
                        var diff = (*(int*)(xxx_ref)) ^ (*(int*)(src_p));
                        if (diff == 0)
                        {
                            src_p += STEPSIZE_32;
                            xxx_ref += STEPSIZE_32;
                            continue;
                        }

                        // 不等时：x & -x 隔离出最低的那个 1 位，再乘 De Bruijn 魔数 0x077CB531、
                        // 右移 27 位得到 0..31 的表下标，查 DEBRUIJN_TABLE_32 便直接得到
                        //「末尾有几个零」= 这 4 字节里前面连续相同的字节数。于是省掉了逐字节循环。
                        src_p += debruijn32[(((uint)((diff) & -(diff)) * 0x077CB531u)) >> 27];
                        goto _endCount;
                    }

                    // 收尾退回 2 字节、1 字节的小步长比较：整块 4 字节会越过 src_LASTLITERALS，
                    // 而块尾最后 LASTLITERALS(5) 字节不得算进匹配。
                    if ((src_p < src_LASTLITERALS_1) && ((*(ushort*)(xxx_ref)) == (*(ushort*)(src_p))))
                    {
                        src_p += 2;
                        xxx_ref += 2;
                    }

                    if ((src_p < src_LASTLITERALS) && (*xxx_ref == *src_p)) src_p++;

                    _endCount:

                    // Encode MatchLength：写进块里的是「匹配长度 - MINMATCH」（匹配最短 4 字节，
                    // 那 4 字节是双方共识，不必花字节表示）。length 小则直接累加进 token 低 4 位。
                    length = (int)(src_p - src_anchor);

                    // 出口检查：这段长度最坏再写 (length >> 8) 个续字节，外加 1 + LASTLITERALS 的余量
                    if (dst_p + (length >> 8) > dst_LASTLITERALS_1) return 0; // 检查输出上限

                    if (length >= ML_MASK)
                    {
                        // token 低 4 位写满，余下的用续字节；一次吐两个 255（510 = 2×255）比逐个写快
                        *xxx_token += ML_MASK;
                        length -= ML_MASK;
                        for (; length > 509; length -= 510)
                        {
                            *dst_p++ = 255;
                            *dst_p++ = 255;
                        }

                        if (length > 254)
                        {
                            length -= 255;
                            *dst_p++ = 255;
                        }

                        *dst_p++ = (byte)length;
                    }
                    else
                    {
                        *xxx_token += (byte)length;
                    }

                    // Test end of chunk：已经逼近块尾，记下 anchor 就退出主循环，剩余交给收尾
                    if (src_p > src_mflimit)
                    {
                        src_anchor = src_p;
                        break;
                    }

                    // Fill table：顺手登记 src_p-2 处的哈希。刚结束的匹配后面这两个字节很可能
                    // 还会再出现，提前登记能让下一轮更容易命中（原始 lz4 的经验优化）。
                    hash_table[((((*(uint*)(src_p - 2))) * 2654435761u) >> HASH_ADJUST)] = (src_p - 2 - src_base);

                    // Test next position：立刻在当前新位置再试一次匹配。

                    h = ((((*(uint*)(src_p))) * 2654435761u) >> HASH_ADJUST);
                    xxx_ref = src_base + hash_table[h];
                    hash_table[h] = (src_p - src_base);

                    // 命中就写一个「零字面量」的 token（高 4 位为 0），直接跳去编匹配，
                    // 省掉一轮主循环的开销。注意这里的距离判据是 MAX_DISTANCE + 1，比主循环严一格。
                    if ((xxx_ref > src_p - (MAX_DISTANCE + 1)) && ((*(uint*)(xxx_ref)) == (*(uint*)(src_p))))
                    {
                        xxx_token = dst_p++;
                        *xxx_token = 0;
                        goto _next_match;
                    }

                    // Prepare next loop：没命中，当前位置并进字面量，前进 1 字节并预取新位置的哈希
                    src_anchor = src_p++;
                    h_fwd = ((((*(uint*)(src_p))) * 2654435761u) >> HASH_ADJUST);
                }

                _last_literals:

                // Encode Last Literals：把 src_anchor 之后剩下的字节整段当字面量写出
                //（token 低 4 位为 0，表示这一块不再有匹配）
                {
                    var lastRun = (int)(src_end - src_anchor);

                    // 上限检查：lastRun 个数据字节 + 1 个 token + 续字节个数。
                    // 续字节向上取整写成 (lastRun + 255 - RUN_MASK) / 255，是保守但安全的估法。
                    if (dst_p + lastRun + 1 + ((lastRun + 255 - RUN_MASK) / 255) > dst_end) return 0;

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

                // End：返回实际写出的字节数
                return (int)((dst_p) - dst);
            }
        }

        #endregion

        #region LZ4_compress64kCtx_32

        /// <summary>
        /// 小输入专用的压缩档：当输入长度 &lt; <c>LZ4_64KLIMIT</c>（约 64KB）时改走这里。
        ///
        /// 为什么要单独开一套？因为整块不超过 64KB，任何位置到块首的距离都装得进一个
        /// <c>ushort</c>，于是哈希表元素可以用 16 位偏移（<c>ushort*</c>）来代替指针 / 32 位值：
        /// <list type="bullet">
        ///   <item>表小一半，缓存更友好。</item>
        ///   <item>表尺寸再翻一倍（<c>HASH64K_LOG = HASH_LOG + 1</c>），桶更多、冲突更少，
        ///         结果压缩率反而比「大表但桶少」的普通档还好一点。</item>
        ///   <item>既然回溯距离天然 ≤ 64K，查找循环里就**省掉了 MAX_DISTANCE 检查** ——
        ///         这是本函数与 <c>LZ4_compressCtx_32</c> 最实质的差别。</item>
        /// </list>
        ///
        /// 除此之外的流程（跳步搜索、Knuth 乘法散列、token/续字节编码）与快速档完全一致。
        /// </summary>
        /// <returns>写入 <paramref name="dst"/> 的字节数；输出缓冲放不下时返回 0。</returns>
        private static unsafe int LZ4_compress64kCtx_32(
            ushort* hash_table,
            byte* src,
            byte* dst,
            int src_len,
            int dst_maxlen)
        {
            byte* _p;

            fixed (int* debruijn32 = &DEBRUIJN_TABLE_32[0])
            {
                // 以下变量沿用 lz4 r93 版源码的命名
                var src_p = src;                     // 当前扫描/查找位置
                var src_anchor = src_p;              // 尚未输出的字面量起点
                var src_base = src_p;                // 块首：既是 ushort 偏移的基准，也是回溯下界
                var src_end = src_p + src_len;       // 输入末尾（不含）
                var src_mflimit = src_end - MFLIMIT; // 主循环终点

                var dst_p = dst;
                var dst_end = dst_p + dst_maxlen;

                var src_LASTLITERALS = src_end - LASTLITERALS; // 匹配不得触及的最后 5 字节起点
                var src_LASTLITERALS_1 = src_LASTLITERALS - 1;

                var src_LASTLITERALS_STEPSIZE_1 = src_LASTLITERALS - (STEPSIZE_32 - 1);
                var dst_LASTLITERALS_1 = dst_end - (1 + LASTLITERALS);
                var dst_LASTLITERALS_3 = dst_end - (2 + 1 + LASTLITERALS);

                int len, length;

                uint h, h_fwd; // 当前位置 / 下一位置的哈希桶下标

                // Init：太短就整块当字面量
                if (src_len < MINLENGTH) goto _last_literals;

                // First Byte：跳过首字节（没有前文可匹配）。
                // 这里不像 32 位档那样先登记块首 —— 桶里 0 表示偏移 0，指向 src_base 本身，
                // 未写过的桶也只是把块首当作候选，最终仍要复核首 4 字节，不会出错。
                src_p++;
                h_fwd = ((((*(uint*)(src_p))) * 2654435761u) >> HASH64K_ADJUST);

                // 主循环：每轮编出「一段字面量 + 一个匹配」
                while (true)
                {
                    // 跳步搜索的进度计，含义同 32 位档（初值 +3 是原始实现的微调）
                    var findMatchAttempts = (1 << SKIPSTRENGTH) + 3;
                    var src_p_fwd = src_p; // 「下一个检查位置」
                    byte* xxx_ref;         // 匹配所在位置
                    byte* xxx_token;       // 本序列的 token 字节

                    // Find a match：跳步扫描，直到找到首 4 字节吻合的候选
                    do
                    {
                        h = h_fwd;
                        var step = findMatchAttempts++ >> SKIPSTRENGTH;
                        src_p = src_p_fwd;
                        src_p_fwd = src_p + step;

                        if (src_p_fwd > src_mflimit) goto _last_literals;

                        h_fwd = ((((*(uint*)(src_p_fwd))) * 2654435761u) >> HASH64K_ADJUST);
                        xxx_ref = src_base + hash_table[h];
                        hash_table[h] = (ushort)(src_p - src_base); // 桶只记最近一次，覆盖旧值
                    // 只需复核首 4 字节：偏移是 16 位、以块首为基准，回溯距离天然 ≤ 64K，
                    // 不可能越过 MAX_DISTANCE，所以这里没有（也不需要）距离判定。
                    } while ((*(uint*)(xxx_ref)) != (*(uint*)(src_p)));

                    // Catch up：往前逐字节回退，把同样相同的已扫字节也吃进匹配，两端都省
                    while ((src_p > src_anchor) && (xxx_ref > src) && (src_p[-1] == xxx_ref[-1]))
                    {
                        src_p--;
                        xxx_ref--;
                    }

                    // Encode Literal length：先占住 token 字节，字面量长度 = src_anchor 到 src_p
                    length = (int)(src_p - src_anchor);
                    xxx_token = dst_p++;

                    // 输出上限预检，算式同 32 位档：数据 + 约 length/256 个续字节 + 2+1+5 余量
                    if (dst_p + length + (length >> 8) > dst_LASTLITERALS_3) return 0; // 检查输出上限

                    // 长度编码规则同 32 位档：< 15 只写 token 高 4 位；≥ 15 则高 4 位写满、
                    // 后面跟一串 255 续字节。长字面量走 BlockCopy 并直接跳到 _next_match。
                    if (length >= RUN_MASK)
                    {
                        len = length - RUN_MASK;
                        *xxx_token = (RUN_MASK << ML_BITS);
                        if (len > 254)
                        {
                            do
                            {
                                *dst_p++ = 255;
                                len -= 255;
                            } while (len > 254);

                            *dst_p++ = (byte)len;
                            BlockCopy(src_anchor, dst_p, (length));
                            dst_p += length;
                            goto _next_match;
                        }

                        *dst_p++ = (byte)len;
                    }
                    else
                    {
                        *xxx_token = (byte)(length << ML_BITS);
                    }

                    // Copy Literals：32 位一次搬 8 字节；末尾多拷的字节由 dst_p = _p 裁掉
                    _p = dst_p + (length);
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

                    _next_match:

                    // Encode Offset：小端 2 字节的回溯距离（≤ 65535，天然装得下）
                    *(ushort*)dst_p = (ushort)(src_p - xxx_ref);
                    dst_p += 2;

                    // Start Counting：首 4 字节已验证相等，跳过；src_anchor 移到匹配之后
                    src_p += MINMATCH;
                    xxx_ref += MINMATCH; // MINMATCH 已验证
                    src_anchor = src_p;

                    // 统计匹配长度：一次比 4 字节；不等时用 De Bruijn 表求尾零个数
                    //（x & -x 隔离最低置位，乘魔数 0x077CB531 后取高 5 位查表，见 32 位档的说明）
                    while (src_p < src_LASTLITERALS_STEPSIZE_1)
                    {
                        var diff = (*(int*)(xxx_ref)) ^ (*(int*)(src_p));
                        if (diff == 0)
                        {
                            src_p += STEPSIZE_32;
                            xxx_ref += STEPSIZE_32;
                            continue;
                        }

                        src_p += debruijn32[(((uint)((diff) & -(diff)) * 0x077CB531u)) >> 27];
                        goto _endCount;
                    }

                    // 收尾改用 2 字节 / 1 字节比较，避免越过 src_LASTLITERALS（块尾 5 字节不算匹配）
                    if ((src_p < src_LASTLITERALS_1) && ((*(ushort*)(xxx_ref)) == (*(ushort*)(src_p))))
                    {
                        src_p += 2;
                        xxx_ref += 2;
                    }

                    if ((src_p < src_LASTLITERALS) && (*xxx_ref == *src_p)) src_p++;

                    _endCount:

                    // Encode MatchLength：len = 匹配长度 - MINMATCH，编码规则同 32 位档
                    len = (int)(src_p - src_anchor);

                    // 出口检查：最坏 (len >> 8) 个续字节 + 1 + LASTLITERALS 余量
                    if (dst_p + (len >> 8) > dst_LASTLITERALS_1) return 0; // 检查输出上限

                    if (len >= ML_MASK)
                    {
                        // token 低 4 位写满，余下用续字节；一次吐两个 255（510 = 2×255）更快
                        *xxx_token += ML_MASK;
                        len -= ML_MASK;
                        for (; len > 509; len -= 510)
                        {
                            *dst_p++ = 255;
                            *dst_p++ = 255;
                        }

                        if (len > 254)
                        {
                            len -= 255;
                            *dst_p++ = 255;
                        }

                        *dst_p++ = (byte)len;
                    }
                    else *xxx_token += (byte)len;

                    // Test end of chunk：逼近块尾，记下 anchor 退出主循环
                    if (src_p > src_mflimit)
                    {
                        src_anchor = src_p;
                        break;
                    }

                    // Fill table：顺手登记 src_p-2 处的哈希，让下一轮更容易命中
                    hash_table[((((*(uint*)(src_p - 2))) * 2654435761u) >> HASH64K_ADJUST)] =
                        (ushort)(src_p - 2 - src_base);

                    // Test next position：立刻在新位置再试一次

                    h = ((((*(uint*)(src_p))) * 2654435761u) >> HASH64K_ADJUST);
                    xxx_ref = src_base + hash_table[h];
                    hash_table[h] = (ushort)(src_p - src_base);

                    // 命中就写一个「零字面量」token 直接跳去编匹配（距离天然合法，无需判定）
                    if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p)))
                    {
                        xxx_token = dst_p++;
                        *xxx_token = 0;
                        goto _next_match;
                    }

                    // Prepare next loop：没命中，当前位置并进字面量，前进 1 字节并预取新哈希
                    src_anchor = src_p++;
                    h_fwd = ((((*(uint*)(src_p))) * 2654435761u) >> HASH64K_ADJUST);
                }

                _last_literals:

                // Encode Last Literals：剩余字节整段作为收尾字面量写出
                {
                    var lastRun = (int)(src_end - src_anchor);
                    // 上限检查：这里是同一公式的另一种写法 —— (lastRun - RUN_MASK + 255) / 255
                    // 与 32 位档的 (lastRun + 255 - RUN_MASK) / 255 完全等价，只是操作数顺序不同。
                    if (dst_p + lastRun + 1 + (lastRun - RUN_MASK + 255) / 255 > dst_end) return 0;
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

                // End：返回实际写出的字节数
                return (int)((dst_p) - dst);
            }
        }

        #endregion

        #region LZ4_uncompress_32

        /// <summary>
        /// 解压 LZ4 块到**大小已知**的目标缓冲（<paramref name="dst_len"/> 字节）。
        ///
        /// 解析的就是压缩端写出的那套格式：
        /// <list type="bullet">
        ///   <item>token 1 字节：高 4 位 = 字面量长度，低 4 位 = 匹配长度（真实匹配长度还要 +MINMATCH）。</item>
        ///   <item>哪个长度字段等于 15（RUN_MASK / ML_MASK 都是 15），就说明后面还跟着一串 255 续字节，
        ///         直到出现一个 &lt;255 的字节为止。</item>
        ///   <item>偏移是 2 字节**小端**，指向目标缓冲里**已经解出**的数据；允许与写指针重叠，
        ///         重叠拷贝本身就会生成重复片段 —— 这正是 LZ4 天然支持 RLE 的原因。</item>
        /// </list>
        ///
        /// <c>DECODER_TABLE_32</c> 只服务于「偏移 &lt; 4」这种源与目标重叠的情形：此时不能直接用
        /// 4 字节拷贝（会读到刚写入的数据），得先用单字节把前 4 字节铺好，再按表把源指针校正到
        /// 正确的重叠起点。
        /// </summary>
        /// <returns>成功：消耗的输入字节数。失败：返回**负数**，绝对值为已消耗的输入字节数
        /// （见 <c>_output_error</c> 处的 <c>-((src_p) - src)</c>），便于上层定位坏在哪。</returns>
        private static unsafe int LZ4_uncompress_32(
            byte* src,
            byte* dst,
            int dst_len)
        {
            fixed (int* dec32table = &DECODER_TABLE_32[0])
            {
                // 以下变量沿用 lz4 r93 版源码的命名
                var src_p = src;
                byte* xxx_ref; // 匹配的源位置（指向 dst 缓冲里已解出的数据）

                var dst_p = dst;
                var dst_end = dst_p + dst_len;
                byte* dst_cpy; // 本段字面量 / 匹配的目标终点

                // 边界常量：末尾留出「最后一段字面量」的安全区
                var dst_LASTLITERALS = dst_end - LASTLITERALS;
                var dst_COPYLENGTH = dst_end - COPYLENGTH;
                var dst_COPYLENGTH_STEPSIZE_4 = dst_end - COPYLENGTH - (STEPSIZE_32 - 4);

                uint xxx_token; // 当前序列的 token

                // 主循环
                while (true)
                {
                    int length;

                    // get runlength：token 高 4 位就是字面量长度；等于 15 表示后面还有 255 续字节
                    xxx_token = *src_p++;
                    if ((length = (int)(xxx_token >> ML_BITS)) == RUN_MASK)
                    {
                        int len;
                        for (; (len = *src_p++) == 255; length += 255)
                        {
                            /* do nothing */ // 255 表示「再加 255」，累加已经写在循环条件里
                        }

                        length += len; // 补上最后一个 <255 的收尾字节
                    }

                    // copy literals：先算出本段终点 dst_cpy
                    dst_cpy = dst_p + length;

                    if (dst_cpy > dst_COPYLENGTH)
                    {
                        // 越过 COPYLENGTH 安全线，只可能是最后一段字面量；此时终点必须正好是 dst_end，
                        // 否则数据非法 —— 后面已经放不下「最小 4 字节匹配 + 5 字节尾字面量」了
                        if (dst_cpy != dst_end)
                            goto _output_error; // 错误：剩余空间放不下「下一个匹配(最少 4 字节) + 5 字节字面量」
                        BlockCopy(src_p, dst_p, (length));
                        src_p += length;
                        break; // 输入结束
                    }

                    // 普通情形：32 位批量拷贝字面量
                    do
                    {
                        *(uint*)dst_p = *(uint*)src_p;
                        dst_p += 4;
                        src_p += 4;
                        *(uint*)dst_p = *(uint*)src_p;
                        dst_p += 4;
                        src_p += 4;
                    } while (dst_p < dst_cpy);

                    // 循环会多走几个字节（dst_p 冲过了 dst_cpy），把多读的输入拨回去
                    src_p -= (dst_p - dst_cpy);
                    dst_p = dst_cpy;

                    // get offset：小端 2 字节的回溯距离，源指针指向已解出的数据
                    xxx_ref = (dst_cpy) - (*(ushort*)(src_p));
                    src_p += 2;
                    if (xxx_ref < dst) goto _output_error; // 错误：偏移落在目标缓冲之外

                    // get matchlength：低 4 位是「匹配长度 - MINMATCH」；等于 15 同样跟 255 续字节
                    if ((length = (int)(xxx_token & ML_MASK)) == ML_MASK)
                    {
                        for (; *src_p == 255; length += 255) src_p++;
                        length += *src_p++;
                    }

                    // copy repeated sequence：偏移小于 4 时源和目标重叠，不能直接整块拷
                    if ((dst_p - xxx_ref) < STEPSIZE_32)
                    {
                        // dec64 在 32 位实现里恒为 0，保留只是为了与 64 位版本共用同一套代码结构
                        const int dec64 = 0;

                        // 先老老实实写 4 个单字节，保证后面读到的重叠数据都已写好
                        dst_p[0] = xxx_ref[0];
                        dst_p[1] = xxx_ref[1];
                        dst_p[2] = xxx_ref[2];
                        dst_p[3] = xxx_ref[3];
                        dst_p += 4;
                        xxx_ref += 4;
                        // 用 DECODER_TABLE_32 把源指针校正到正确的重叠起点，随后的批量拷贝就会
                        // 自动把已写的数据一遍遍复制出去（RLE 效果）
                        xxx_ref -= dec32table[dst_p - xxx_ref];
                        (*(uint*)(dst_p)) = (*(uint*)(xxx_ref));
                        dst_p += STEPSIZE_32 - 4;
                        xxx_ref -= dec64;
                    }
                    else
                    {
                        // 距离足够远，源与目标不重叠，可以放心 4 字节一次拷
                        *(uint*)dst_p = *(uint*)xxx_ref;
                        dst_p += 4;
                        xxx_ref += 4;
                    }

                    dst_cpy = dst_p + length - (STEPSIZE_32 - 4);

                    if (dst_cpy > dst_COPYLENGTH_STEPSIZE_4)
                    {
                        // 要超出批量拷贝的安全区了：先按 4 字节块把安全区内拷完，再逐字节补尾巴。
                        // 但无论如何不能碰到 dst_LASTLITERALS —— 规范要求最后 5 字节必须是字面量。
                        if (dst_cpy > dst_LASTLITERALS) goto _output_error; // 错误：最后 5 字节必须是字面量
                        {
                            do
                            {
                                *(uint*)dst_p = *(uint*)xxx_ref;
                                dst_p += 4;
                                xxx_ref += 4;
                                *(uint*)dst_p = *(uint*)xxx_ref;
                                dst_p += 4;
                                xxx_ref += 4;
                            } while (dst_p < dst_COPYLENGTH);
                        }

                        while (dst_p < dst_cpy) *dst_p++ = *xxx_ref++;
                        dst_p = dst_cpy;
                        continue;
                    }

                    // 常见情形：整段匹配都在安全区内，直接 32 位批量拷
                    do
                    {
                        *(uint*)dst_p = *(uint*)xxx_ref;
                        dst_p += 4;
                        xxx_ref += 4;
                        *(uint*)dst_p = *(uint*)xxx_ref;
                        dst_p += 4;
                        xxx_ref += 4;
                    } while (dst_p < dst_cpy);

                    dst_p = dst_cpy; // correction：裁掉批量拷贝多写出来的几个字节
                }

                // end of decoding：正常结束，返回消耗的输入字节数
                return (int)((src_p) - src);

                // write overflow error detected：出错，返回负的已消耗输入字节数
                _output_error:
                return (int)(-((src_p) - src));
            }
        }

        #endregion

        #region LZ4_uncompress_unknownOutputSize_32

        /// <summary>
        /// 解压 LZ4 块到**大小未知**的目标缓冲，只给出上限 <paramref name="dst_maxlen"/>。
        /// 因此这里必须自己盯紧输入长度：源侧不能读越界、目标侧不能写越界，任何一处不合法都要
        /// 立刻返回错误，而不能像已知大小版那样「相信」调用方给的尺寸。
        ///
        /// 与 <c>LZ4_uncompress_32</c> 的主要差别：
        /// <list type="bullet">
        ///   <item>多出 <c>src_end</c>，读续字节时用 <c>src_p &lt; src_end</c> 兜底防读穿。</item>
        ///   <item>字面量段的收尾判定更严：既要求 <c>dst_cpy</c> 不越界，又要求输入**正好**耗尽。</item>
        ///   <item>空输入直接判错 —— LZ4 规定即使压缩的是空块，也至少要有 1 字节 token(=0)。</item>
        ///   <item>返回值是**解出的字节数**（<c>dst_p - dst</c>），与已知大小版「返回消耗的输入数」不同。</item>
        /// </list>
        /// 出错时统一返回负数，绝对值仍是已消耗的输入字节数。
        /// </summary>
        private static unsafe int LZ4_uncompress_unknownOutputSize_32(
            byte* src,
            byte* dst,
            int src_len,
            int dst_maxlen)
        {
            fixed (int* dec32table = &DECODER_TABLE_32[0])
            {
                // 以下变量沿用 lz4 r93 版源码的命名
                var src_p = src;
                var src_end = src_p + src_len;
                byte* xxx_ref;

                var dst_p = dst;
                var dst_end = dst_p + dst_maxlen;
                byte* dst_cpy;

                // 输入侧的收尾线（字面量之后还要留够「偏移 + 匹配长度 + 尾字面量」）
                var src_LASTLITERALS_3 = (src_end - (2 + 1 + LASTLITERALS));
                var src_LASTLITERALS_1 = (src_end - (LASTLITERALS + 1));
                // 输出侧的安全线
                var dst_COPYLENGTH = (dst_end - COPYLENGTH);
                var dst_COPYLENGTH_STEPSIZE_4 = (dst_end - (COPYLENGTH + (STEPSIZE_32 - 4)));
                var dst_LASTLITERALS = (dst_end - LASTLITERALS);
                var dst_MFLIMIT = (dst_end - MFLIMIT);

                // Special case：空输入不是合法的 LZ4 块 —— 规范的「空块」也必须写成 1 字节 token(=0)
                if (src_p == src_end)
                    goto _output_error; // 合法的「空 LZ4 块」也至少要有 1 字节（token=0）

                // 主循环
                while (true)
                {
                    uint xxx_token;
                    int length;

                    // get runlength：高 4 位；为 15 时继续读 255 续字节，并要用 src_p < src_end 防读越界
                    xxx_token = *src_p++;
                    if ((length = (int)(xxx_token >> ML_BITS)) == RUN_MASK)
                    {
                        var s = 255;
                        // 循环条件里带上 src_p < src_end，避免把输入读穿
                        while ((src_p < src_end) && (s == 255))
                        {
                            s = *src_p++;
                            length += s;
                        }
                    }

                    // 拷贝字面量
                    dst_cpy = dst_p + length;

                    // 只要逼近任意一侧的尾巴，就只可能是最后一段字面量
                    if ((dst_cpy > dst_MFLIMIT) || (src_p + length > src_LASTLITERALS_3))
                    {
                        if (dst_cpy > dst_end) goto _output_error; // 错误：写出了目标缓冲之外
                        // 收尾阶段必须把输入**正好**用完：最后 11 字节内不允许再有匹配，
                        // 而且还要再留 8 字节（4 字节匹配 + 5 字节尾字面量）才够编下一段
                        if (src_p + length != src_end)
                            goto
                                _output_error; // 错误：此处 LZ4 格式要求把输入正好读完（最后 11 字节内不允许再有匹配，且要再留 8 字节才够编「匹配+字面量」）
                        BlockCopy(src_p, dst_p, (length));
                        dst_p += length;
                        break; // 受解析规则所限，此处必然是输入结束
                    }

                    // 32 位批量拷贝字面量；循环会多读几个字节，下面把 src_p 拨回
                    do
                    {
                        *(uint*)dst_p = *(uint*)src_p;
                        dst_p += 4;
                        src_p += 4;
                        *(uint*)dst_p = *(uint*)src_p;
                        dst_p += 4;
                        src_p += 4;
                    } while (dst_p < dst_cpy);

                    src_p -= (dst_p - dst_cpy);
                    dst_p = dst_cpy;

                    // get offset：小端 2 字节，指向已解出的数据；越界即数据损坏
                    xxx_ref = (dst_cpy) - (*(ushort*)(src_p));
                    src_p += 2;
                    if (xxx_ref < dst) goto _output_error; // 错误：偏移落在目标缓冲之外

                    // get matchlength：低 4 位；等于 15 时继续读续字节。
                    // 这里必须保证输入还留得下「尾字面量 + 下一个 token」，所以要卡在 src_LASTLITERALS_1。
                    if ((length = (int)(xxx_token & ML_MASK)) == ML_MASK)
                    {
                        while
                            (src_p <
                             src_LASTLITERALS_1) // 错误：必须给「尾字面量 + 下一个 token」留足输入字节
                        {
                            int s = *src_p++;
                            length += s;
                            if (s == 255) continue;
                            break;
                        }
                    }

                    // copy repeated sequence：偏移 < 4 的源目标重叠情形，处理方式同 LZ4_uncompress_32
                    if (dst_p - xxx_ref < STEPSIZE_32)
                    {
                        // dec64 在 32 位实现里恒为 0，仅为与 64 位版本保持同一代码结构
                        const int dec64 = 0;

                        dst_p[0] = xxx_ref[0];
                        dst_p[1] = xxx_ref[1];
                        dst_p[2] = xxx_ref[2];
                        dst_p[3] = xxx_ref[3];
                        dst_p += 4;
                        xxx_ref += 4;
                        xxx_ref -= dec32table[dst_p - xxx_ref];
                        (*(uint*)(dst_p)) = (*(uint*)(xxx_ref));
                        dst_p += STEPSIZE_32 - 4;
                        xxx_ref -= dec64;
                    }
                    else
                    {
                        *(uint*)dst_p = *(uint*)xxx_ref;
                        dst_p += 4;
                        xxx_ref += 4;
                    }

                    dst_cpy = dst_p + length - (STEPSIZE_32 - 4);

                    if (dst_cpy > dst_COPYLENGTH_STEPSIZE_4)
                    {
                        // 超出批量拷贝安全区：先 4 字节块拷到安全线，再逐字节补尾巴；
                        // 但绝不能碰 dst_LASTLITERALS —— 最后 5 字节必须是字面量
                        if (dst_cpy > dst_LASTLITERALS) goto _output_error; // 错误：最后 5 字节必须是字面量
                        {
                            do
                            {
                                *(uint*)dst_p = *(uint*)xxx_ref;
                                dst_p += 4;
                                xxx_ref += 4;
                                *(uint*)dst_p = *(uint*)xxx_ref;
                                dst_p += 4;
                                xxx_ref += 4;
                            } while (dst_p < dst_COPYLENGTH);
                        }

                        while (dst_p < dst_cpy) *dst_p++ = *xxx_ref++;
                        dst_p = dst_cpy;
                        continue;
                    }

                    // 常见情形：整段匹配都在安全区内，直接 32 位批量拷
                    do
                    {
                        *(uint*)dst_p = *(uint*)xxx_ref;
                        dst_p += 4;
                        xxx_ref += 4;
                        *(uint*)dst_p = *(uint*)xxx_ref;
                        dst_p += 4;
                        xxx_ref += 4;
                    } while (dst_p < dst_cpy);

                    dst_p = dst_cpy; // correction：裁掉批量拷贝多写出来的几个字节
                }

                // end of decoding：正常结束，返回**解出的字节数**（注意与已知大小版不同）
                return (int)((dst_p) - dst);

                // write overflow error detected：出错，返回负的已消耗输入字节数
                _output_error:
                return (int)(-((src_p) - src));
            }
        }

        #endregion
    }
}

// ReSharper restore JoinDeclarationAndInitializer
// ReSharper restore TooWideLocalVariableScope
// ReSharper restore InconsistentNaming