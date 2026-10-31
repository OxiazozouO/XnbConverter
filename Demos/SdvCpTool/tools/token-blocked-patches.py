#!/usr/bin/env python3
"""量化：**真正写出去的补丁里**，值上还带无法解析令牌的，有多少条、分别属于哪些资产。

用途：决定「值里带无法解析的令牌时，要不要整条不写」。两种口径各有代价：
  · 现状（写）—— 产物里有**永远不生效**的条目（`Condition` 非法 GSQ）或**画不出来**的引用，
    游戏会**每次**记一条错误日志；
  · 若改成不写 —— 少写这些补丁，但没有坏数据（与 `TargetContainsTokens` 的处理一致）。

**两个口径都报，别混**：**处数**是 `UnexpandedTokenAudit` 报的字符串处数（**资产级**，点不到补丁）；
**受影响补丁数**来自写入路径逐条记的 `writeNote`（**精确**）。先前只有前者，
只能拿「该包已写入数」估**上界**；补上 `writeNote` 之后才有精确数。

用法：python tools/token-blocked-patches.py [报告目录]
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b21'
    per_pack = []
    assets = collections.Counter()
    total = 0
    grand_affected = 0
    unexpanded_total = 0

    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        t = d.get('unexpandedTokens') or {}
        if not t.get('harmful'):
            continue
        unexpanded_total += t['harmful']
        # ★ **精确数**：写入路径把「值里还有没展开的令牌」记进了该条的 `writeNote`，
        #   于是数得出来是**哪几条补丁** —— 不再需要拿「该包已写入数」估上界。
        affected = sum(1 for x in d['patches'] if '无法静态解析' in (x.get('writeNote') or ''))
        grand_affected += affected
        written = sum(1 for x in d['patches'] if x.get('written') is True)
        per_pack.append((t['harmful'], affected, written, os.path.basename(d['pack'])[:34]))
        for f in t.get('findings', []):
            if not f['identifierOnly']:
                assets[f['asset'].split('/')[0]] += 1
        total += t['harmful']

    print(f'带无法解析令牌的**处数**合计：{total}')
    print(f'★ 受影响**补丁**：{grand_affected}（精确，来自逐条 writeNote）')
    print('（处数 = 资产级自检报的字符串处数；受影响补丁 = 写入时逐条记的 writeNote，精确）\n')
    print('按资产（首段）：')
    for k, v in assets.most_common(8):
        print(f'  {v:5}  {k}')
    print('\n按包（处数 / 受影响补丁 / 该包已写入）：')
    for n, a, w, name in sorted(per_pack, reverse=True):
        print(f'  {n:5} 处 / {a:4} 条 / {w:5} 条已写入   {name}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
