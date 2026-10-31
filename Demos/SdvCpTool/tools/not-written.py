#!/usr/bin/env python3
"""**真正没写出去的**补丁有哪些、为什么 —— 按 `written`/`groupEmitted` 算，不按判定类算。

⚠ **为什么必须区分**：`Verdict` 是**判定**，`written` 是**结局**，两者不是一回事。
例如 `CreatesNewAsset`（「目标不在树里，EditData 会新建它，需确认新资产名可达」）
**多数 `written: true`** —— 它是个**提醒**，不是失败。
把判定类当成损失会**虚报**问题规模（我前几轮就是这么报的，这轮改口径）。

用法：python tools/not-written.py <报告目录> [--top 20]
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b9'
    top = int(sys.argv[sys.argv.index('--top') + 1]) if '--top' in sys.argv else 20

    files = sorted(glob.glob(os.path.join(root, '*', 'report.json')))
    by_verdict = collections.Counter()
    reasons = collections.Counter()
    total = written = emitted = lost = 0
    lost_reasons = collections.Counter()

    for p in files:
        d = json.load(open(p, encoding='utf-8'))
        for x in d['patches']:
            total += 1
            by_verdict[x['verdict']] += 1
            if x.get('written') is True:
                written += 1
            elif x.get('groupEmitted'):
                emitted += 1
                continue
            else:
                lost += 1
                r = re.sub(r'`[^`]*`', '`X`', re.sub(r'（\d+ 组）$', '', (x.get('reason') or '').strip()))
                lost_reasons[r[:105]] += 1
            reasons[(x['verdict'], bool(x.get('written')), bool(x.get('groupEmitted')))] += 1

    print(f'{len(files)} 份报告，补丁 {total}')
    print(f'  已写入 {written}；由集合发射产出 {emitted}；**两者都不是 {lost}**')
    print('\n=== 判定 × 结局 ===')
    for (v, w, g), n in sorted(reasons.items(), key=lambda kv: -kv[1]):
        tag = '已写入' if w else ('集合发射' if g else '**没写**')
        print(f'  {n:6}  {v:24} {tag}')
    print(f'\n=== 真正没写的 {lost} 条，按理由（前 {top}）===')
    for r, n in lost_reasons.most_common(top):
        print(f'{n:6}  {r}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
