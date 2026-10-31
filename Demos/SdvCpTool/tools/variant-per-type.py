#!/usr/bin/env python3
"""
「逐类型」量化：把每个变体维度按**实际资产类型**拆开。

`variant-entropy.py` 按 (维度, 载体) 分桶；本工具再细一层，按**资产名**分桶 ——
回答「这个维度的熵到底压在哪些资产类型上」，好让「最简模型」按类型逐个定。

用法：
    python tools/variant-per-type.py <report.json> [...]
"""
import json
import os
import re
import sys
import collections

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402

DIMENSIONS = {
    '季节': re.compile(r'\bseason\b|spring_|_spring\b', re.I),
    '婚姻': re.compile(r'\bspouse\b|\bRoommate\b|married', re.I),
    '年份': re.compile(r'IsOddYear|_y[2-9]\b|\byear\b', re.I),
    '看过的剧情': re.compile(r'HasSeenEvent', re.I),
    '好感度': re.compile(r'\bHearts\b', re.I),
}


def asset_type(target):
    """把目标资产名归到「类型」：去掉条目/子路径那一层。

    **资产名必须大小写不敏感**：语料里 `Characters/schedules` / `characters/schedules` /
    `Characters/Schedules` 是同一个资产。不归一化会把一个类型拆成三个桶，虚增「类型数」。
    """
    t = str(target or '').split(',')[0].strip()
    if not t:
        return '(空)'
    low = t.lower().replace('\\', '/')
    # 「类型 + 实例名」的多级资产 —— 归到类型那一层
    for prefix, canonical in (
        ('data/festivals', 'Data/Festivals'),
        ('characters/schedules', 'Characters/schedules'),
        ('strings/schedules', 'Strings/schedules'),
        ('data/characters', 'Data/Characters'),
        ('data/bigcraftables', 'Data/BigCraftables'),
        ('data/animationdescriptions', 'Data/animationDescriptions'),
        ('data/events/', 'Data/Events/*'),
        ('data/events', 'Data/Events'),
    ):
        if low.startswith(prefix):
            return canonical
    if low.startswith('maps/') or low.startswith('data/maps'):
        return 'Maps'
    if low.startswith('characters/') or low.startswith('portraits/'):
        return 'Characters|Portraits'
    if low.startswith('mods/'):
        return 'Mods/*'
    return t


def flatten(o):
    out = []
    if isinstance(o, dict):
        for k, v in o.items():
            out.append(str(k))
            out.extend(flatten(v))
    elif isinstance(o, list):
        for v in o:
            out.extend(flatten(v))
    else:
        out.append(str(o))
    return out


def main(paths):
    # dim -> assetType -> count
    tbl = collections.defaultdict(collections.Counter)
    cache = {}

    for path in paths:
        d = json.load(open(path, encoding='utf-8'))
        root = d.get('pack') or ''
        for p in d['patches']:
            src = os.path.join(root, p.get('source') or '')
            if src not in cache:
                try:
                    cache[src] = jsonc_load(src).get('Changes', [])
                except Exception:
                    cache[src] = None
            ch = cache[src]
            if not ch or p['index'] >= len(ch):
                continue
            w = ch[p['index']].get('When')
            if not w:
                continue
            blob = ' '.join(flatten(w))
            for name, rx in DIMENSIONS.items():
                if rx.search(blob):
                    tbl[name][asset_type(p.get('target'))] += 1

    grand = 0
    for dim in DIMENSIONS:
        if dim not in tbl:
            continue
        total = sum(tbl[dim].values())
        grand += total
        kinds = len(tbl[dim])
        print(f'--- {dim}：{total} 条，压在 {kinds} 种资产类型上 ---')
        for at, n in tbl[dim].most_common():
            pct = n * 100 // total
            print(f'  {n:5d}  {pct:3d}%  {at}')
        print()
    print(f'合计 {grand} 条带维度条件的补丁')


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1:])
