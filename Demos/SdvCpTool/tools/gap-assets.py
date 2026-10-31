#!/usr/bin/env python3
"""列出「资产级缺口」的**具体是哪些资产** —— 配合 `asset-coverage.py` 用。

用法：python tools/gap-assets.py [报告目录] [理由子串]
      python tools/gap-assets.py D:\\tmp\\c32 烘焙求值失败
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def norm(t):
    return t.strip().replace('\\', '/').strip('/')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\c32'
    needle = sys.argv[2] if len(sys.argv) > 2 else ''

    written, touched, why = set(), set(), {}
    for path in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        data = json.load(open(path, encoding='utf-8'))
        pack = os.path.basename(os.path.dirname(path))[:16]
        for patch in data['patches']:
            targets = [norm(t) for t in (patch.get('target') or '').split(',') if norm(t)]
            touched.update((pack, t) for t in targets)
            if patch.get('written') is True:
                written.update((pack, t) for t in targets)

    # 重新扫一遍只为拿理由（与 asset-coverage 口径一致：writeNote 优先）
    for path in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        data = json.load(open(path, encoding='utf-8'))
        pack = os.path.basename(os.path.dirname(path))[:16]
        for patch in data['patches']:
            if patch.get('written') is True or patch.get('groupEmitted'):
                continue
            note = patch.get('writeNote') or patch.get('reason') or '（没给理由）'
            for t in [norm(x) for x in (patch.get('target') or '').split(',') if norm(x)]:
                why.setdefault((pack, t), note)

    rows = sorted((pack, t, why[(pack, t)]) for (pack, t) in (touched - written) if (pack, t) in why)
    rows = [r for r in rows if needle.lower() in r[2].lower()]
    print(f'缺口（含「{needle}」）：{len(rows)} 个资产\n')
    by_pack = collections.Counter(r[0] for r in rows)
    for pack, n in by_pack.most_common():
        print(f'{n:5}  {pack}')
    print()
    for pack, asset, note in rows[:40]:
        print(f'  {pack:18} {asset[:56]:58} {note[:60]}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
