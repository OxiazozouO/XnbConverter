#!/usr/bin/env python3
"""**产出树比原版少掉的传送点** —— 玩家说的「地图之间的传送不见了」到底少了哪些。

为什么要有它：`EditMap` 里**带运行时条件**的那些（`HasSeenEvent` / `HasFlag` / `Weather` /
`Time` / `Relationship` …）在静态 XNB 树里**没有通道** ⇒ 整条丢掉（`PatchJudge` 判 `LocalEdit`）。
丢掉的东西里，玩家最容易察觉的就是**加门 / 加通路**——某张原版地图的 `Warp` 属性比原版少几条。

判据复用一个近似实现：tbin 里的字符串没有长度前缀，所以按**五元组形状**
（`fromX fromY toMap toX toY`，可重复）从可打印串里抽 `Warp` / `NPCWarp` 两个属性
（`check-map-warps.py` 里那份正则，同一个实现）。

⚠ 判据是**结构取**的（`Warp`/`NPCWarp` 属性 + int32 长度 + 内容），不是早先那版
"在整份二进制里抓五元组形状" —— 后者抓到的是噪声（原版 `SeedShop` 明明只有 1 条 `Warp`，
它数出 25 条，"少 18 条"整栏都是假的）。仍然**只算地图级属性**：图块级的 `Warp` 动作
（图块属性里也叫 `Warp`）虽然同样编码、会被一起抓到，但两边一致，不影响差值。
⚠ 另一类**不是丢失**的：地图被包**整张 `Load` 换掉**时（包自带一版），原版那几条本就不该在。

★ 给了 `--reports <转换产出根>` 时会把这类**分开报**：报告里
`action=Load` 且 `target=Maps/<名>` 的，就是"包自带整张图" ⇒ 那张图的差异**不是丢失**。
真正要盯的是**没被整张 Load 替换、却比原版少**的那些。

用法：
    python tools/lost-warps.py <产出树> <原版树> [--reports <产出根>] [--top 25] [--json <落盘>]
"""
import argparse
import collections
import io
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

FIVE = re.compile(rb'(?:-?\d+ ){2}[A-Za-z_][A-Za-z_0-9]*(?: -?\d+ -?\d+)')
# ★ **按结构取**，不是在可打印串里瞎找：TBin10 里属性是
#   `int32 名字长度 + 名字 + 类型字节 + (字符串：int32 长度 + 内容)`，字符串类型是 `0x03`。
#   实测（原版 `Maps/SeedShop`）：`Warp\x03` + `0f 00 00 00` + `6 30 Town 43 57` 恰好 15 字节。
#   ⚠ 早期版本拿 `(数字 数字 名字 数字 数字)+` 在整份二进制里抓，**抓到的是噪声**：
#   同一张 `SeedShop` 被数出"25 条"，而它的 `Warp` 属性其实只有 **1** 条 ——
#   于是"少掉 18 条"整栏都是假的。结构取法把这一整类误报去掉。
PROP = re.compile(rb'(?:Warp|NPCWarp)\x03')


def warps(path):
    """→ 五元组字符串集合（`"12 7 Town 30 40"`）。"""
    raw = open(path, 'rb').read()
    out = set()
    for m in PROP.finditer(raw):
        if m.end() + 4 > len(raw):
            continue
        n = int.from_bytes(raw[m.end():m.end() + 4], 'little')
        if n <= 0 or m.end() + 4 + n > len(raw) or n > 4096:
            continue
        text = raw[m.end() + 4:m.end() + 4 + n].decode('ascii', 'replace')
        parts = text.split()
        for i in range(0, len(parts) // 5 * 5, 5):
            tup = parts[i:i + 5]
            if not tup[2].lstrip('-').isdigit():
                out.add(' '.join(tup))
    return out


def collect(tree):
    root = os.path.join(tree, 'Maps')
    out = {}
    for dp, _dirs, fns in os.walk(root):
        for f in fns:
            if not f.lower().endswith('.tbin'):
                continue
            rel = os.path.relpath(os.path.join(dp, f), root).replace(os.sep, '/')[:-5]
            out[rel] = warps(os.path.join(dp, f))
    return out


def loaded_maps(root):
    """整个包**自带一版**的地图（报告里 `Load` 且 `Target` 是 `Maps/<名>`）—— 这些图的差异不是丢失。"""
    import glob
    out = set()
    for p in glob.glob(os.path.join(root, '*', 'report.json')):
        try:
            j = json.load(io.open(p, encoding='utf-8'))
        except Exception:
            continue
        for pt in j.get('patches', []):
            if str(pt.get('action', '')).lower() != 'load':
                continue
            for t in str(pt.get('target') or '').split(','):
                t = t.strip()
                if t.lower().startswith('maps/'):
                    out.add(t[5:].lower())     # ⚠ 统一小写：调用侧按 `name.lower()` 比（踩过 —— 大小写不一致时一个都匹配不上）
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('tree')
    ap.add_argument('base')
    ap.add_argument('--reports')
    ap.add_argument('--top', type=int, default=25)
    ap.add_argument('--json')
    a = ap.parse_args()

    ours, base = collect(a.tree), collect(a.base)
    loaded = loaded_maps(a.reports) if a.reports else set()
    lost, replaced = {}, {}
    for name, b in base.items():
        if not b:
            continue
        gone = b - ours.get(name, set())
        if not gone:
            continue
        (replaced if name.lower() in loaded else lost)[name] = gone
    added = {n: len(w - base.get(n, set())) for n, w in ours.items() if w - base.get(n, set())}

    def total(d):
        return sum(len(v) for v in d.values())

    print(f'原版有 {len(base)} 张地图；产出里有 {len(ours)} 张')
    print(f'★ 少掉传送点的地图 **{len(lost)}** 张，合计 **{total(lost)}** 条  ← 要盯的是这一栏')
    print(f'  （另有 {len(replaced)} 张 / {total(replaced)} 条是**包自带整张图**（`Load` 替换）'
          f'—— 原版那几条本就不该在，CP 在游戏里同样如此）')
    print(f'  （{len(added)} 张地图**多出**传送点，合计 {sum(added.values())} 条）\n')
    for name, gone in sorted(lost.items(), key=lambda x: -len(x[1]))[:a.top]:
        print(f'{name}  少 {len(gone)} 条（原版 {len(base[name])}）')
        for g in sorted(gone)[:6]:
            print(f'      {g}')
        if len(gone) > 6:
            print(f'      …… 另有 {len(gone) - 6} 条')
    if a.json:
        io.open(a.json, 'w', encoding='utf-8', newline='\n').write(json.dumps(
            {'lost': {k: sorted(v) for k, v in lost.items()},
             'loadReplaced': {k: sorted(v) for k, v in replaced.items()}},
            ensure_ascii=False, indent=1))
        print(f'\n明细落盘：{a.json}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
