"""`FarmType` 门控的地图改动里，有多少条的**目标本身是按农场类型分的**？

农场类型这条通道**是真的**：原版每种农场类型各有地图文件
（`Maps/Farm_Combat` / `Farm_Fishing` / `Farm_Foraging` / `Farm_FourCorners` …）。

但只有在**目标本身按类型分**时才用得上 —— 目标是 `Maps/Town` 那种全类型共用的地图，
就算条件里写了 `FarmType`，也没有"每类型一份"的地方可放。
"""
import collections
import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.path.insert(0, 'D:/SdvCpTool/tools')
import jsonc  # noqa: E402

SVE = (r'D:\Steam\steamapps\common\Stardew Valley\Mods\大型扩展---SVE--------------------3753'
       r'\大型扩展---SVE------------------------------3753')
BAKEABLE = {'HasMod', 'ModId', 'HasFile', 'FirstValidFile'}

# 目标是"按农场类型分"的：农场地图本身（含令牌那种）与农场屋
FARM_TARGETS = ('Maps/{{SelectedWarpMap}}', 'Maps/Farm', 'Maps/FarmHouse')

rows = []


def walk(node):
    if isinstance(node, dict):
        if node.get('Action') == 'EditMap' and node.get('When'):
            ks = {k.split('|')[0].strip() for k in node['When']}
            if 'FarmType' in ks and not (ks - {'FarmType'} - BAKEABLE - {'Season'}):
                t = str(node.get('Target'))
                rows.append((t, sorted(ks), bool(node.get('FromFile')),
                             bool(node.get('MapTiles')), bool(node.get('MapProperties')),
                             bool(node.get('AddWarps')), bool(node.get('TextOperations'))))
        for v in node.values():
            walk(v)
    elif isinstance(node, list):
        for v in node:
            walk(v)


for dp, _dirs, fns in os.walk(SVE):
    for f in fns:
        if not f.endswith('.json') or f == 'manifest.json':
            continue
        try:
            walk(jsonc.load(os.path.join(dp, f)))
        except Exception:
            continue

print(f'SVE 里「条件 = FarmType +（可烘焙/Season）」的 EditMap 共 {len(rows)} 条：')
byk = collections.Counter((r[0],) for r in rows)
for (t,), n in byk.most_common(12):
    print(f'   {n:4d}  {t[:56]}')
usable = [r for r in rows if r[0] in FARM_TARGETS]
print(f'\n**目标本身就按农场类型分**的（这条通道用得上）：{len(usable)} 条')
for t, ks, ff, mt, mp, aw, to in usable:
    kinds = ','.join(k for k, v in (('FromFile', ff), ('MapTiles', mt), ('MapProperties', mp),
                                    ('AddWarps', aw), ('TextOperations', to)) if v)
    print(f'   {t:34} {ks}  [{kinds}]')
print(f'\n目标共用、用不上的：{len(rows) - len(usable)} 条')
