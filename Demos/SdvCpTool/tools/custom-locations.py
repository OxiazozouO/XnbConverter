#!/usr/bin/env python3
"""量一量 **CP 的 `CustomLocations`** 这块被我们忽略的东西值多少。

CP 的 `content.json` 除了 `Changes`，还可以有 `CustomLocations`：每条
`{ "Name": X, "FromMapFile": f }` 等价于「**游戏按需加载 `Maps/X` 时，内容取自包内的 f**」
（`CustomLocationData.PublicMapPath = "Maps/" + Name`，
`CustomLocationManager.cs:184` 的 `e.LoadFrom(... ModContent.Load<Map>(FromMapFile) ...)`），
另外还会往 `Data/Locations` 注册一条 `CreateOnLoad.MapPath`。

本脚本只统计：**每个包声明了哪些自定义地点、有多少补丁的目标是它们**。

用法：python tools/custom-locations.py
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'


def cp_packs():
    out = []
    for manifest in glob.glob(os.path.join(MODS, '**', 'manifest.json'), recursive=True):
        try:
            data = json.load(open(manifest, encoding='utf-8-sig'))
        except Exception:
            continue
        if not isinstance(data, dict):
            continue
        if str(data.get('ContentPackFor', {}).get('UniqueID', '')).lower().startswith('pathoschild.contentpatcher'):
            out.append((data.get('Name') or '', os.path.dirname(manifest)))
    return sorted(set(out))


def main():
    total_locations = total_targets = 0
    for name, root in cp_packs():
        locations = []
        for path in glob.glob(os.path.join(root, '**', '*.json'), recursive=True):
            if os.path.basename(path) == 'manifest.json':
                continue
            try:
                data = jsonc_load(path)
            except Exception:
                continue
            if not isinstance(data, dict):
                continue
            for entry in data.get('CustomLocations') or []:
                if isinstance(entry, dict) and entry.get('Name'):
                    locations.append((entry['Name'], entry.get('FromMapFile') or ''))
        if not locations:
            continue

        names = {n for n, _ in locations}
        targets = collections.Counter()
        for path in glob.glob(os.path.join(root, '**', '*.json'), recursive=True):
            if os.path.basename(path) == 'manifest.json':
                continue
            try:
                data = jsonc_load(path)
            except Exception:
                continue
            if not isinstance(data, dict) or not isinstance(data.get('Changes'), list):
                continue
            for change in data['Changes']:
                if not isinstance(change, dict):
                    continue
                for t in str(change.get('Target') or '').split(','):
                    t = t.strip()
                    if t.startswith('Maps/') and t[5:] in names:
                        targets[t[5:]] += 1

        print(f'{name[:34]:36} 自定义地点 {len(locations):3}，被补丁改到的 {len(targets):3}，'
              f'涉及补丁 {sum(targets.values()):4}')
        total_locations += len(locations)
        total_targets += sum(targets.values())
    print(f'\n合计：地点 {total_locations}，涉及补丁 {total_targets}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
