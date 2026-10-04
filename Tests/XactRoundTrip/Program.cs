using System.Diagnostics;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

// 波形库往返测试：读 .xwb -> 解包成 wav+清单 -> 重新打包 -> 与原文件逐字节比对
// 用法: XactRoundTrip <a.xwb> [b.xwb ...]
//       XactRoundTrip --codec <a.xwb>      检验 MS-ADPCM 编解码
if (args.Length > 0 && args[0] == "--codec")
{
    Console.WriteLine("MS-ADPCM 编解码检验");
    return XactRoundTrip.CodecCheck.Run(args[1]);
}

if (args.Length > 0 && args[0] == "--replace")
{
    Console.WriteLine("换音频端到端检验");
    return XactRoundTrip.ReplaceCheck.Run(args[1]);
}

if (args.Length > 0 && args[0] == "--triple")
{
    Console.WriteLine("三代往返收敛检验");
    int bad = 0;
    foreach (string file in args.Skip(1))
    {
        Console.WriteLine($"{Path.GetFileName(file)}:");
        bad += XactRoundTrip.TripleCheck.Run(file, Path.Combine("triple", Path.GetFileNameWithoutExtension(file)));
    }

    if (args.Length > 1 && args[1].EndsWith(".xwb", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("ADPCM 三代:");
        bad += XactRoundTrip.TripleCheck.CheckAdpcm(args[1], "triple");
    }

    Console.WriteLine($"\n未收敛/不一致: {bad}");
    return bad;
}

string[] inputs = args.Length > 0 ? args : new[] { "wavebank_14.xwb" };
int pass = 0;
int fail = 0;

foreach (string input in inputs)
{
    try
    {
        byte[] original = File.ReadAllBytes(input);

        if (input.EndsWith(".xgs", StringComparison.OrdinalIgnoreCase))
        {
            var engine = XnbConverter.Xact.AudioEngine.Reader.AudioEngineReader.Read(input);
            byte[] rebuiltEngine = XnbConverter.Xact.AudioEngine.Reader.AudioEngineReader.Build(engine);
            bool ok = rebuiltEngine.AsSpan().SequenceEqual(original);
            Console.WriteLine($"{Path.GetFileName(input),-22} 类别={engine.numCats} 变量={engine.numVars} " +
                              $"RPC={engine.numRpc} 原始={original.Length} 重建={rebuiltEngine.Length} " +
                              $"{(ok ? "逐字节相同 ✓" : "不一致 ✗")}");
            if (ok)
            {
                pass++;
            }
            else
            {
                fail++;
            }

            // 视图往返：导出 JSON -> 反序列化 -> 套回 -> 写回，未编辑时应仍逐字节相同
            var view = XnbConverter.Xact.AudioEngine.Entity.AudioEngineView.From(engine);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(view, Newtonsoft.Json.Formatting.Indented);
            var view2 = Newtonsoft.Json.JsonConvert.DeserializeObject<XnbConverter.Xact.AudioEngine.Entity.AudioEngineView>(json);
            var engine2 = XnbConverter.Xact.AudioEngine.Reader.AudioEngineReader.Read(input);
            view2.ApplyTo(engine2);
            byte[] rebuiltView = XnbConverter.Xact.AudioEngine.Reader.AudioEngineReader.Build(engine2);
            bool okView = rebuiltView.AsSpan().SequenceEqual(original);
            Console.WriteLine($"{"  视图往返",-22} JSON={json.Length} 字节  {(okView ? "逐字节相同 ✓" : "不一致 ✗")}");

            continue;
        }

        if (input.EndsWith(".xsb", StringComparison.OrdinalIgnoreCase))
        {
            var bank = XnbConverter.Xact.SoundBank.Reader.SoundBankReader.Read(input);
            byte[] rebuiltBank = XnbConverter.Xact.SoundBank.Reader.SoundBankReader.Build(bank);
            bool ok = rebuiltBank.AsSpan().SequenceEqual(original);
            Console.WriteLine($"{Path.GetFileName(input),-22} 简单Cue={bank.Header.NumSimpleCues} 复杂Cue={bank.Header.NumComplexCues} " +
                              $"声库={bank.Header.NumWaveBanks} 原始={original.Length} 重建={rebuiltBank.Length} " +
                              $"{(ok ? "逐字节相同 ✓" : "不一致 ✗")}");
            if (ok)
            {
                pass++;
            }
            else
            {
                fail++;
            }

            // 视图往返
            var sbView = XnbConverter.Xact.SoundBank.Entity.SoundBankView.From(bank);
            string sbJson = Newtonsoft.Json.JsonConvert.SerializeObject(sbView, Newtonsoft.Json.Formatting.Indented);
            var sbView2 = Newtonsoft.Json.JsonConvert.DeserializeObject<XnbConverter.Xact.SoundBank.Entity.SoundBankView>(sbJson);
            var bank2 = XnbConverter.Xact.SoundBank.Reader.SoundBankReader.Read(input);
            sbView2.ApplyTo(bank2);
            byte[] rebuiltView2 = XnbConverter.Xact.SoundBank.Reader.SoundBankReader.Build(bank2);
            bool okView2 = rebuiltView2.AsSpan().SequenceEqual(original);
            Console.WriteLine($"{"  视图往返",-22} JSON={sbJson.Length} 字节  Sound={sbView.Sounds.Count} 变体={sbView.Variations.Count}  " +
                              $"{(okView2 ? "逐字节相同 ✓" : "不一致 ✗")}");

            continue;
        }
        string outDir = Path.Combine("rt", Path.GetFileNameWithoutExtension(input));

        if (Directory.Exists(outDir))
        {
            Directory.Delete(outDir, true);
        }

        Directory.CreateDirectory(outDir);

        // ---- 解包 ----
        Stopwatch sw = Stopwatch.StartNew();
        WaveBank wb = WaveBankReader.Read(input);
        wb.OutputPath = outDir;
        for (int i = 0; i < wb.Entries.Count; i++)
        {
            // 无 sound bank 时 XACT.Load 用十六进制序号命名，这里保持一致
            wb.Entries[i].FilePath = outDir + Path.DirectorySeparatorChar;
            wb.Entries[i].FileName ??= $"{i:x8}";
        }

        WaveBankReader.Save(wb);
        long unpackMs = sw.ElapsedMilliseconds;

        string manifest = wb.OutputPath + ".xwb.config";
        int wavCount = Directory.GetFiles(outDir, "*.wav").Length;

        // ---- 打包 ----
        sw.Restart();
        byte[] rebuilt = WaveBankWriter.Build(manifest);
        long packMs = sw.ElapsedMilliseconds;

        // ---- 比对 ----
        bool same = rebuilt.Length == original.Length && rebuilt.AsSpan().SequenceEqual(original);
        int firstDiff = -1;
        if (!same)
        {
            int n = Math.Min(rebuilt.Length, original.Length);
            for (int i = 0; i < n; i++)
            {
                if (rebuilt[i] != original[i])
                {
                    firstDiff = i;
                    break;
                }
            }
        }

        Console.WriteLine($"{Path.GetFileName(input),-22} 条目={wb.Entries.Count,4} wav={wavCount,4} " +
                          $"原始={original.Length,10} 重建={rebuilt.Length,10} " +
                          $"{(same ? "逐字节相同 ✓" : $"不一致 ✗ 首个差异={firstDiff}")} " +
                          $"解包={unpackMs}ms 打包={packMs}ms");

        if (same)
        {
            pass++;
        }
        else
        {
            fail++;
        }

        // ---- 同尺寸替换：改动第 0 个条目的载荷，重建后应只有那一段变化 ----
        if (same && wb.Entries.Count > 0)
        {
            string[] wavs = Directory.GetFiles(outDir, "*.wav");
            Array.Sort(wavs);
            string firstWav = wavs[0];
            byte[] wav = File.ReadAllBytes(firstWav);
            int dataStart = FindDataChunk(wav);
            int dataLen = BitConverter.ToInt32(wav, dataStart - 4);
            for (int i = 0; i < 8; i++)
            {
                wav[dataStart + i] ^= 0x5A;
            }

            File.WriteAllBytes(firstWav, wav);
            byte[] patched = WaveBankWriter.Build(manifest);

            int diffCount = 0;
            int minDiff = int.MaxValue;
            int maxDiff = -1;
            for (int i = 0; i < Math.Min(patched.Length, original.Length); i++)
            {
                if (patched[i] != original[i])
                {
                    diffCount++;
                    minDiff = Math.Min(minDiff, i);
                    maxDiff = Math.Max(maxDiff, i);
                }
            }

            int entryOff = (int)wb.Entries[0].PlayRegion.Offset;
            int entryLen = (int)wb.Entries[0].PlayRegion.Length;
            bool contained = minDiff >= entryOff && maxDiff < entryOff + entryLen;
            Console.WriteLine($"{"  同尺寸替换",-22} 改动 {diffCount} 字节，范围 [{minDiff}..{maxDiff}]，" +
                              $"条目区 [{entryOff}..{entryOff + entryLen})  " +
                              $"{(contained ? "全部落在目标条目内 ✓" : "越界 ✗")}  载荷长度={dataLen}");
        }

        static int FindDataChunk(byte[] wav)
        {
            for (int i = 12; i + 8 < wav.Length; i++)
            {
                if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
                {
                    return i + 8;
                }
            }

            throw new InvalidOperationException("找不到 data 块");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{Path.GetFileName(input),-22} 异常: {ex.GetType().Name}: {ex.Message}");
        fail++;
    }
}

Console.WriteLine($"\n通过 {pass} / 失败 {fail}");
return fail == 0 ? 0 : 1;
