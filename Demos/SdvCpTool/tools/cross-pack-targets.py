#!/usr/bin/env python3
"""量化「跨包目标」：被判 `TargetMissing` / `CreatesNewAsset` 的补丁里，
有多少目标**其实被语料里别的包 `Load` 出来了**。

为什么要有它：语料普查是**一包一棵新树**跑的（每包都从原版树拷一份），所以
A 包改 B 包的地图时必然「找不到」。真实用法是把所有包**依次**转进同一棵树。
⇒ 那两类数字是**被测量方法压低的**（不是虚高，是虚低），得量出来才知道差多少。

用法：python tools/cross-pack-targets.py <报告目录>
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
    root = sys.argv[1]
    files = sorted(glob.glob(os.path.join(root, '*', 'report.json')))
    if not files:
        print(__doc__)
        return 2

    # 第一遍：全语料里被任何包「造出来」的资产（Load / EditData 新建）
    created = collections.defaultdict(set)
    cache = {}
    payload = []
    for p in files:
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        for x in d.get('patches', []):
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = changes(src)
            ch = cache[src]
            c = (ch or [None] * (x.get('index', -1) + 1))[x.get('index', -1)]
            if not isinstance(c, dict):
                continue
            t = str(c.get('Target') or '').split(',')[0].strip()
            act = str(c.get('Action') or x.get('action') or '').lower()
            if act in ('load', 'editdata') and t:
                created[t].add(pack)
            payload.append((pack, t, x.get('verdict'), act))

    stat = collections.Counter()
    by_class = collections.Counter()
    cross = collections.Counter()
    for pack, t, verdict, act in payload:
        if verdict not in ('TargetMissing', 'CreatesNewAsset'):
            continue
        others = {q for q in created.get(t, ()) if q != pack}
        stat[(verdict, bool(others))] += 1
        if others:
            cross[t] += 1
            head = t.split('/')[0] + '/' if '/' in t else t
            by_class[head] += 1

    total = sum(stat.values())
    fixed = sum(n for (v, o), n in stat.items() if o)
    print(f'TargetMissing + CreatesNewAsset 合计 {total} 条')
    print(f'★ 其中目标**被语料里别的包造出来过**（链式转换就能解决）：**{fixed}** 条')
    for (v, o), n in sorted(stat.items()):
        print(f'   {v:16} 别的包造过={o!s:5} {n}')
    print('\n按资产前缀（前 12）：')
    for h, n in by_class.most_common(12):
        print(f'   {n:4}  {h}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
