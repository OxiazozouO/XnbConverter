#!/usr/bin/env python3
"""**判定说可写、却没写出去**的补丁，按 `writeNote` 聚合。

与 `not-written.py` 的分工：那个给全局账，这个专打「判定与写入不一致」那一档 ——
它才是真问题（judged Converted 却没落盘），其余大多是**本来就写不了**的（判定已如实说明）。

用法：python tools/write-note.py [报告目录] [--top 20]
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b12'
    top = int(sys.argv[sys.argv.index('--top') + 1]) if '--top' in sys.argv else 20

    notes = collections.Counter()
    packs = collections.Counter()
    total = 0
    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        for x in d['patches']:
            if x['verdict'] not in ('Converted', 'ConvertedNoCondition'):
                continue
            if x.get('written') is True or x.get('groupEmitted'):
                continue
            total += 1
            note = (x.get('writeNote') or '（writeNote 为空 —— 压根没尝试写）')[:100]
            notes[note] += 1
            packs[(os.path.basename(d['pack'])[:24], note[:60])] += 1

    print(f'判成可写却没落盘：**{total}** 条\n')
    for n, c in notes.most_common(top):
        print(f'{c:5}  {n}')
    print('\n按包：')
    for (pk, n), c in packs.most_common(12):
        print(f'{c:5}  [{pk}] {n}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
