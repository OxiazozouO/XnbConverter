#!/usr/bin/env python3
"""地图**图块上**的 `Warp` 动作，目标地点存在吗？

`check-map-warps.py` 查的是**地图属性 `Warp`**（整条传送表）。但传送还能挂在**图块**上：

```
"Action": "Warp 58 42 Custom_WaterfallForest"
"TouchAction": "LockedDoorWarp 6 29 SeedShop 900 2100"
```

这两条路是分开的 —— 只查地图属性会漏掉图块那一半（用户报的"传送代码没被触发"
正是这一类）。

**怎么读的**：不解析 tbin 的 RLE（那要还原图层），而是**扫可打印串**再按形状匹配。
tbin 里字符串**没有长度前缀**、会跟后面的字节粘住（实测 `Warp Custom_WaterfallForest 58 42S`，
末尾 `S` 是下一条串的首字节），所以判据收紧成「`Warp`/`LockedDoorWarp` 后面跟
`[数字 ]*` 再跟一个标识符」，**只看那个标识符**，粘在后面的字节不影响。

⚠ 这是**近似**：真正的目标是哪一段必须看 SDV 的 `Warp` 语法
（`Warp <loc>` / `Warp <x> <y> <loc>` / `LockedDoorWarp <x> <y> <loc> <t> <t>`），
本工具按这些形状取「`Warp` 之后第一个标识符」。

用法：
    python tools/check-map-tile-warps.py <产出树> [<基准树>]
"""
import collections
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# `Warp` / `LockedDoorWarp` 后面：可选的一串数字，然后一个地点名（标识符）。
# 用**排除式**避开「字符类里放反斜杠」那个坑（见 check-localized-text.py 的说明）。
# 地名里可以有 `.`（`Lumisteria.MtVapius_Farm`）和 `-`；旧字符集在第一个 `.` 截断，
# 把存在的 ID 误报成缺失（子代理实测 7 处假阳性）。`-` 放末尾，字符类里不用反斜杠。
WARP = re.compile(rb'(?:LockedDoorWarp|Warp)[ ]+(?:-?[0-9]+[ ]+)*([A-Za-z_][A-Za-z0-9_.-]{1,80})')

PRINTABLE = re.compile(rb'[ -~]{6,}')


def locations(tree):
    """合法地点名 = Data/Locations 的键 + 各地点 FormerLocationNames。"""
    out = set()
    p = os.path.join(tree, 'Data', 'Locations.json')
    if not os.path.isfile(p):
        return out
    try:
        data = json.load(open(p, encoding='utf-8-sig'))
    except Exception:
        return out
    for key, value in data.items():
        out.add(key)
        if isinstance(value, dict):
            out.update(value.get('FormerLocationNames') or [])
    return out


def maps_assets(tree):
    """`Maps/` 下的资产名（老式做法允许目标写成地图资产名）。"""
    out = set()
    base = os.path.join(tree, 'Maps')
    for dp, _dirs, fns in os.walk(base):
        for f in fns:
            stem, ext = os.path.splitext(f)
            if ext.lower() in ('.tbin', '.png'):
                out.add(stem)
    return out


def scan(tree):
    # 游戏地点查找是 OrdinalIgnoreCase；比较统一小写，避免只差大小写的假阳性。
    locs = {name.lower() for name in locations(tree)}
    maps = {name.lower() for name in maps_assets(tree)}
    missing = collections.defaultdict(set)
    n = 0
    base = os.path.join(tree, 'Maps')
    for dp, _dirs, fns in os.walk(base):
        for f in fns:
            if not f.endswith('.tbin'):
                continue
            raw = open(os.path.join(dp, f), 'rb').read()
            for run in PRINTABLE.finditer(raw):
                for m in WARP.finditer(run.group(0)):
                    n += 1
                    target = m.group(1).decode('ascii')
                    # ⚠ 粘字节：tbin 的串没有长度前缀，抓出来的名字**末尾会多一个**
                    #   下一条串的首字节（原版实测 `IslandFarmHouseS` / `WitchHutS`）。
                    #   所以从长到短试前缀，命中即算存在 —— 不这么做原版自己就报 3 处假阳性。
                    hit = False
                    for cut in range(len(target), 2, -1):
                        probe = target[:cut].lower()
                        if probe in locs or probe in maps:
                            hit = True
                            break
                    if hit:
                        continue
                    # `Warp 5 7` 这种「省了地点」的写法不会带标识符，上面已经排除
                    missing[target].add(f[:-5])
    print(f'{tree}\n  图块 Warp 动作 {n} 处；合法地名 {len(locs)} 个 / Maps 资产 {len(maps)} 个；'
          f'**目标不存在的 {len(missing)} 种**')
    return missing


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    mine = scan(sys.argv[1])
    base = scan(sys.argv[2]) if len(sys.argv) > 2 else {}
    new = {k: v for k, v in mine.items() if k not in base}
    print(f'  基准 {len(base)} 种，**产出新增 {len(new)} 种**')
    for target, maps in sorted(mine.items(), key=lambda kv: -len(kv[1]))[:25]:
        mark = '★新增' if target in new else ' 基准已有'
        print(f'    {mark}  {target:34} ← {len(maps)} 张：{",".join(sorted(maps)[:4])}')
    return 0 if not new else 1


if __name__ == '__main__':
    sys.exit(main())
