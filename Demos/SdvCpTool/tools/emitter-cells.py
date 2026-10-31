#!/usr/bin/env python3
"""把多份报告里的**发射表格子**汇总起来：哪些格子有原生最简形式、哪些还「待查」。

**唯一事实来源是报告本身** —— `report.json` 的 `normalization.cells` 由 C# 的
`Normalizing/EmitterTable` 填。这个脚本**不重新判断维度、也不复制那张表**
（老版本 `variant-intents.py` 就是这么干的：正则猜维度 + 手抄一份 EMITTER 字典，
两处会各自漂移，实测它抄的那份已经缺了 `Data/Festivals` 的季节格、`Strings/schedules`
的「装不下」格、`Data/PassiveFestivals` 整格）。

用法：
    python tools/emitter-cells.py <report.json 或所在目录> [...] [--top N] [--kind Unknown]
    # 不给 --kind 就打印四档的合计 + 待查清单（按条数降序）
"""
import glob
import json
import os
import sys
import collections

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

KINDS = ('Native', 'None', 'CapacityExceeded', 'Unknown')


def reports(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            out += glob.glob(os.path.join(p, '**', 'report.json'), recursive=True)
        else:
            out.append(p)
    return sorted(set(out))


def main(paths, kind, top):
    files = reports(paths)
    if not files:
        print('没找到 report.json')
        return 2

    agg = collections.Counter()
    merged = {}
    precondition = {}
    stale = 0
    for path in files:
        try:
            d = json.load(open(path, encoding='utf-8'))
        except Exception as e:
            print(f'  跳过 {path}: {e}')
            continue
        norm = d.get('normalization')
        if not norm:
            # 老版本产物没有这一节 —— **如实说**，不要静默算成 0。
            stale += 1
            continue
        for c in norm.get('cells', []):
            key = (c['carrier'], c['dimension'], c['assetType'])
            agg[(key, c['kind'])] += c['patches']
            if c.get('nativeForm'):
                merged[key] = c.get('nativeForm')
            if c.get('precondition'):
                precondition[key] = c['precondition']

    if stale:
        print(f'⚠ {stale}/{len(files)} 份报告没有 `normalization` 一节（老版本产物），未计入\n')

    total = sum(v for (_, k), v in agg.items() if k != 'Unknown')
    gen = sum(agg[(key, 'Native')] for key, _ in agg)
    print(f'{len(files)-stale} 份报告，带维度条件的补丁合计 {total + sum(v for (_,k),v in agg.items() if k=="Unknown")}')
    print('  按发射表推：')
    for k in KINDS:
        n = sum(v for (_, kk), v in agg.items() if kk == k)
        extra = '（**待查**，既非「有」也非「无」）' if k == 'Unknown' else ''
        print(f'    {k:<18} {n:6}{extra}')
    if total:
        print(f'    ⇒ 已确证有原生机制的比例：{gen * 100 // total}%（分母不含待查）')

    show = kind or 'Unknown'
    cells = sorted(((key, v) for (key, k), v in agg.items() if k == show),
                   key=lambda kv: -kv[1])
    print(f'\n=== {show} 的格子：{len(cells)} 格 / {sum(v for _, v in cells)} 条 ===')
    for (carrier, dim, atype), n in cells[:top]:
        print(f'{n:5}  ({carrier}, {dim}, {atype})')
        if show != 'Unknown' and (carrier, dim, atype) in merged:
            print(f'       最简形式：{merged[(carrier, dim, atype)]}')
        if (carrier, dim, atype) in precondition:
            print(f'       ⚠ 前提：{precondition[(carrier, dim, atype)]}')
    if len(cells) > top:
        print(f'   … 其余 {len(cells)-top} 格见 --top')
    return 0


if __name__ == '__main__':
    args, top, kind = [], 25, None
    rest = sys.argv[1:]
    i = 0
    while i < len(rest):
        if rest[i] == '--top' and i + 1 < len(rest):
            top = int(rest[i + 1]); i += 2; continue
        if rest[i] == '--kind' and i + 1 < len(rest):
            kind = rest[i + 1]; i += 2; continue
        if not rest[i].startswith('--'):
            args.append(rest[i])
        i += 1
    if not args:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(args, kind, top))
