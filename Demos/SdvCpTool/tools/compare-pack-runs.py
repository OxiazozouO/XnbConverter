#!/usr/bin/env python3
"""比较**同一个包**两次跑的报告（例如「单包从原版树」vs「链式接在别的包后面」）。

比的是**资产集合**（`written=true` 的那些补丁的 target），不是补丁条数 —— 见
`tools/written-assets.py` 的说明：条数会被记账方式左右。

用法：python tools/compare-pack-runs.py <reportA.json> <reportB.json>
"""
import json
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def assets(report):
    out = set()
    for patch in report['patches']:
        if patch.get('written') is not True:
            continue
        for target in (patch.get('target') or '').split(','):
            target = target.strip().replace(chr(92), '/').strip('/')
            if target:
                out.add(target)
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    a = json.load(open(sys.argv[1], encoding='utf-8'))
    b = json.load(open(sys.argv[2], encoding='utf-8'))
    sa, sb = assets(a), assets(b)
    print(f'A: {a["pack"]}')
    print(f'   total={a["total"]} written={a["written"]} failed={a["writeFailed"]} 资产={len(sa)}')
    print(f'B: {b["pack"]}')
    print(f'   total={b["total"]} written={b["written"]} failed={b["writeFailed"]} 资产={len(sb)}')
    print(f'\n资产：新增 {len(sb - sa)}，失去 {len(sa - sb)}')
    for label, diff in (('新增', sb - sa), ('失去', sa - sb)):
        for item in sorted(diff)[:12]:
            print(f'   {label} {item}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
