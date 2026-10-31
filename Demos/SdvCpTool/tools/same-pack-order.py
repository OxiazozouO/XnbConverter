#!/usr/bin/env python3
"""量化：被判「新建资产」（目标不在树里）的补丁，有多少目标**是同包前面某条 `Load` 会造出来的**。

背景：`Program.cs` 是「**先把整包判定完，再统一写入**」（`verdicts = pack.Patches.Select(judge.Judge)`）。
于是判定阶段看到的是**原版树**：同一包里前一条 `Load Characters/Dialogue/Claire` 造出来的资产，
后面的条件补丁判定时**看不见** —— 被判成「新建资产带条件 ⇒ 不写」。

CP 的语义是**按顺序逐条应用**。若改成「判定→写入」交织，这类补丁就能看见前一条的产物。

用法：python tools/same-pack-order.py [报告目录]
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402


def changes(path):
    try:
        return jsonc_load(path)['Changes']
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)['Changes']
        except Exception:
            return None


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b16'
    stat = collections.Counter()
    examples = []
    cache = {}

    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        # 第一遍：这个包里**被 Load 造出来**的资产 + 它出现在第几条
        loaded = {}
        for f in glob.glob(os.path.join(pack, '**', '*.json'), recursive=True):
            if os.path.basename(f) in ('manifest.json', 'config.json'):
                continue
            if f not in cache:
                cache[f] = changes(f)
            for i, c in enumerate(cache[f] or []):
                if not isinstance(c, dict):
                    continue
                # ⚠ 不只 Load —— **EditData 同样会新建资产**（实测漏掉它会少算一大半）。
                if str(c.get('Action') or '').lower() in ('load', 'editdata'):
                    t = str(c.get('Target') or '').split(',')[0].strip().replace(chr(92), '/')
                    if t:
                        loaded.setdefault(t, i)

        # 第二遍：报告里判「新建资产」的，看目标是否在同包被 Load 过、且在**它前面**
        for x in d['patches']:
            if x.get('written') is True or x.get('groupEmitted'):
                continue
            r = x.get('reason') or ''
            if '新建资产' not in r and '不在资源树中' not in r:
                continue
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = changes(src)
            ch = cache[src]
            i = x.get('index', -1)
            c = ch[i] if ch and i < len(ch) else None
            if not isinstance(c, dict):
                continue
            t = str(c.get('Target') or '').split(',')[0].strip().replace(chr(92), '/')
            if t in loaded:
                stat['同包前面 Load 过'] += 1
                if len(examples) < 8:
                    examples.append((os.path.basename(pack)[:18], t[:40], loaded[t], i))
            else:
                stat['同包没有 Load'] += 1

    total = sum(stat.values())
    print(f'判「新建资产」且未写入的补丁：{total}')
    for k, v in stat.most_common():
        print(f'  {v:5}  {k}')
    print('\n样例（包 / 目标 / Load 在第几条 / 本条在第几条）：')
    for e in examples:
        print(f'  [{e[0]}] {e[1]}  Load#{e[2]}  本条#{e[3]}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
