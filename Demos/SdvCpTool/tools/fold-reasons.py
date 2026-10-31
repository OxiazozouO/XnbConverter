#!/usr/bin/env python3
"""把「靠常量折叠判死」的补丁按**依据**聚合 —— 回答「这 804 条到底是凭什么被判不适用的」。

**为什么要有它**：语料里约 **12%** 的补丁是 `NotApplicable`（条件折成恒假、整条不写）。
折叠的依据（`HasMod` 看已装模组、配置令牌看包内 `config.json`、命名空间令牌看提供方的
`config.json`）在 2026-10-07 之前**只落进一个布尔**，报告里看不到 —— 判错一条就是**静默丢内容**，
事后无从核对。`PatchJudge` 现在会把**每片叶子的求值**写进理由，本脚本就按它聚合。

用法：
    python tools/fold-reasons.py [报告目录，默认 D:\\tmp\\c11]
    python tools/fold-reasons.py <目录> --by-leaf        # 只按叶子名聚合
    python tools/fold-reasons.py <目录> --sample 3       # 每类给几条出处
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# 理由尾巴：`（叶子求值：`HasMod |contains=X` = 假、`Weather = rain` = 不知道）`
LEAF = re.compile(r'`([^`]+)` = (真|假|不知道)')


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    root = args[0] if args else r'D:\tmp\c11'
    sample = 0
    if '--sample' in sys.argv:
        sample = int(sys.argv[sys.argv.index('--sample') + 1])

    by_leaf = collections.Counter()          # 叶子名（去掉参数）→ 次数
    by_leaf_full = collections.Counter()     # 叶子（含参数/值）→ 次数
    decisive = collections.Counter()         # 「第一条折成假的叶子」→ 次数
    no_reason = 0
    total = 0
    packs = collections.Counter()
    samples = collections.defaultdict(list)

    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        for x in d['patches']:
            if x.get('verdict') != 'NotApplicable':
                continue
            total += 1
            reason = x.get('reason') or ''
            leaves = LEAF.findall(reason)
            if not leaves:
                no_reason += 1
                continue
            packs[os.path.basename(d['pack'])[:26]] += 1
            for full, val in leaves:
                name = full.split(' ')[0].split('=')[0].strip()
                by_leaf[name] += 1
                if val == '假':
                    by_leaf_full[full] += 1
            false_leaves = [full for full, val in leaves if val == '假']
            if false_leaves:
                key = false_leaves[0].split(' ')[0].split('=')[0].strip()
                decisive[key] += 1
                if sample and len(samples[key]) < sample:
                    samples[key].append((os.path.basename(d['pack'])[:22],
                                         x.get('action'), x.get('target')))

    print(f'`NotApplicable`（折叠判死）合计 {total} 条；带叶子依据的 {total - no_reason} 条，'
          f'没带的 {no_reason} 条（旧报告/未折叠就是这个数）')
    print('\n★ 决定性叶子（理由里第一条折成假的）：')
    for k, v in decisive.most_common(20):
        print(f'  {v:5}  {k}')
        for s in samples[k]:
            print(f'          · {s[0]} {s[1]} {s[2]}')
    print('\n按折成假的叶子（含参数与值）：')
    for k, v in by_leaf_full.most_common(20):
        print(f'  {v:5}  {k}')
    print('\n按叶子名（含「不知道」的那些，看谁在旁边）：')
    for k, v in by_leaf.most_common(20):
        print(f'  {v:5}  {k}')
    print('\n按包：')
    for k, v in packs.most_common(12):
        print(f'  {v:5}  {k}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
