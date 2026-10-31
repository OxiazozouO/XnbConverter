#!/usr/bin/env python3
"""按**资产**（而不是补丁条数）统计覆盖率与缺口 —— 判断「到底还有多少东西没写进去」。

**为什么换这把尺子**：补丁条数会被记账方式左右（一条多目标补丁拆成 N 条，`written` 就从 1 变 N），
拿它当进度会虚报。真正要紧的是「**(包, 资产)** 这个组合有没有被写到」。

用法：python tools/asset-coverage.py [报告目录，默认 D:\\tmp\\c32]
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def norm(target):
    return target.strip().replace('\\', '/').strip('/')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\c32'
    written = set()
    covered_by_group = set()
    touched = set()
    gap_reason = {}          # (pack, asset) -> 第一次没写的理由
    reason_count = collections.Counter()

    for path in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        data = json.load(open(path, encoding='utf-8'))
        pack = os.path.basename(os.path.dirname(path))[:16]
        for patch in data['patches']:
            targets = [norm(t) for t in (patch.get('target') or '').split(',') if norm(t)]
            for asset in targets:
                touched.add((pack, asset))
            if patch.get('written') is True:
                for asset in targets:
                    written.add((pack, asset))
            elif patch.get('groupEmitted'):
                for asset in targets:
                    covered_by_group.add((pack, asset))
            else:
                # ⚠ 用 **writeNote 优先**：判定期的 reason 可能还是「需提供 --mods 才能求值」
                #   这种**当时**的话，而真正拦住它的是写入期的 writeNote —— 拿旧话当结论
                #   会把「烘焙求值失败：…」这一档全归错类（实测 100 个资产因此被张冠李戴）。
                why = re.sub(r'`[^`]*`', '`X`',
                             patch.get('writeNote') or patch.get('reason') or '（没写，报告里没给理由）')[:52]
                reason_count[why] += 1
                for asset in targets:
                    gap_reason.setdefault((pack, asset), why)

    gap = touched - written - covered_by_group
    print(f'涉及到的 (包, 资产) 对：{len(touched)}')
    print(f'  已写到：        {len(written)}')
    print(f'  由集合发射覆盖：{len(covered_by_group)}')
    print(f'  **缺口**：      {len(gap)}')

    by_why = collections.Counter(gap_reason.get(pair, '（未知）') for pair in gap)
    print('\n缺口按理由（一条补丁可能带多个目标，这里按「资产第一次被拒的理由」记）：')
    for why, n in by_why.most_common(15):
        print(f'  {n:5}  {why}')

    by_pack = collections.Counter(pack for pack, _ in gap)
    print('\n缺口按包（前 10）：')
    for pack, n in by_pack.most_common(10):
        print(f'  {n:5}  {pack}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
