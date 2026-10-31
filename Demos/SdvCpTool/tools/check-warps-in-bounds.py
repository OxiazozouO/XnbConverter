#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""扫一棵产出树：找出**目标坐标落在地图外**的传送（`Warp` 地图属性 / 图块 `TouchAction`）。

用法：python -I check-warps-in-bounds.py <tree> [--only Farm,BusStop]

只查树里**存在**的目标地图（mod 新增地点按 `Data/Locations` 的 MapPath 不在树里时跳过）。
坐标按 tbin 里的 raw 字符串取，与 `warp-diff.py` 同一种结构取法。
"""
import os, re, struct, sys

tree = sys.argv[1]
only = None
if '--only' in sys.argv:
    only = {x.strip() for x in sys.argv[sys.argv.index('--only') + 1].split(',')}

def walk(root, ext):
    for dp, _d, ns in os.walk(root):
        for f in ns:
            if f.lower().endswith(ext):
                yield os.path.join(dp, f)

MAPS = os.path.join(tree, 'Maps')
maps = {}
for p in walk(MAPS, '.tbin'):
    rel = os.path.relpath(p, MAPS).replace('\\', '/')[:-5]
    maps[('maps/' + rel).lower()] = p


def dims(path):
    """从 tbin 的 `Back` 图层头取 (宽,高)。tbin 10：字符串是 int32 长度前缀。"""
    raw = open(path, 'rb').read()
    i = raw.find(b'\x04\x00\x00\x00Back')
    if i < 0:
        return None
    j = i + 8      # 长度前缀 + 'Back'
    j += 1         # visible
    n = struct.unpack_from('<i', raw, j)[0]
    j += 4 + n     # description
    return struct.unpack_from('<ii', raw, j)


sizes = {k: dims(v) for k, v in maps.items()}

PROP = re.compile(rb'(?:Warp|NPCWarp)\x03')
TOUCH = re.compile(rb'(?:TouchAction|Action)\x03')


def prop_strings(raw, rx):
    for m in rx.finditer(raw):
        if m.end() + 4 > len(raw):
            continue
        n = struct.unpack_from('<i', raw, m.end())[0]
        if 0 < n <= 8192 and m.end() + 4 + n <= len(raw):
            yield raw[m.end() + 4:m.end() + 4 + n].decode('utf-8', 'replace')


def wants(src, loc):
    return not only or loc in only or src in only


bad = 0
checked = 0
for p in walk(MAPS, '.tbin'):
    src = os.path.relpath(p, MAPS).replace('\\', '/')[:-5]
    raw = open(p, 'rb').read()
    for text in prop_strings(raw, PROP):
        t = text.split()
        for i in range(0, len(t) // 5 * 5, 5):
            loc = t[i + 2]
            if not (t[i + 3].lstrip('-').isdigit() and t[i + 4].lstrip('-').isdigit()):
                continue
            tx, ty = int(t[i + 3]), int(t[i + 4])
            if not wants(src, loc):
                continue
            key = ('maps/' + loc).lower()
            if key not in sizes or sizes[key] is None:
                continue
            checked += 1
            w, h = sizes[key]
            if tx < 0 or ty < 0 or tx >= w or ty >= h:
                bad += 1
                print(f'MAPWARP {src} ({t[i]},{t[i + 1]}) -> {loc} {tx} {ty}  [target {w}x{h}]')
    for text in prop_strings(raw, TOUCH):
        t = text.split()
        op = t[0].lower() if t else ''
        if op in ('warp', 'magicwarp', 'loadmap') and len(t) >= 4:
            loc, tx, ty = t[1], t[2], t[3]
        else:
            continue
        if not (tx.lstrip('-').isdigit() and ty.lstrip('-').isdigit()):
            continue
        tx, ty = int(tx), int(ty)
        if not wants(src, loc):
            continue
        key = ('maps/' + loc).lower()
        if key not in sizes or sizes[key] is None:
            continue
        checked += 1
        w, h = sizes[key]
        if tx < 0 or ty < 0 or tx >= w or ty >= h:
            bad += 1
            print(f'TILEWARP {src} -> {loc} {tx} {ty}  [{w}x{h}]  ({text[:60]})')

print(f'checked={checked} out-of-bounds={bad}')
