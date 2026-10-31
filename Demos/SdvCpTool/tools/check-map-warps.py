#!/usr/bin/env python3
"""产出树的**地图/地点图自洽审计**：两条独立检查，都必带基准树对照。

## 检查一：每张地图的 `Warp` 目标是否存在

**为什么要它**：SDV 1.6 的地图把传送点存在**地图属性 `Warp`** 里（一串
`fromX fromY toMap toX toY` 五元组重复）。`toMap` 指向的地点若不存在，
那条传送要么报错要么把人送到错地方 —— 表现就是"地图接不起来"。

**判 `toMap` 存在**（三选一）：
  1. `Data/Locations` 里有这个名字；或
  2. 某个地点的 `FormerLocationNames` 里列了它（CP `CustomLocations.MigrateLegacyNames`
     就是写进这个字段的 —— SVE 的 `ShearwaterBridge` 全靠它）；或
  3. `Maps/<名>` 有 `.tbin`/`.png`（老式做法）。

## 检查二：每个地点的 `CreateOnLoad.MapPath` 是否有对应地图

地点建不出来的最直接原因就是这条地图路径不存在。

## 必须和原版树对照着看

原版自己就有查不到的（火山那一组 `VolcanoEntrance` / `VolcanoDungeon*`
是运行时生成的，`Data/Locations` 里没有）。所以带上基准树、只报**新增**的。

用法：
    python tools/check-map-warps.py <产出树> [<原版树>]
      <产出树> 要有 `Maps/`（`.tbin`）；有 `Data/Locations.json` 时用它做地名判定。
"""
import collections
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# 地名里可以有 `.`（`Lumisteria.MtVapius_Farm`）和 `-`；早先的 `[A-Za-z_0-9]*`
# 会在第一个 `.` 截断 ⇒ 把存在的 ID 误报成缺失（实测 7 处假阳性）。
WARP = re.compile(rb'(?:-?\d+ ){2}[A-Za-z_][A-Za-z_0-9.\-]*(?: -?\d+ -?\d+)+')


def load_tree(tree):
    """→ (Maps 下的资产名集合（含子目录、`/` 分隔、小写）, 地点数据字典)"""
    maps_dir = os.path.join(tree, 'Maps')
    names = set()
    if os.path.isdir(maps_dir):
        for dp, _dirs, fns in os.walk(maps_dir):
            for f in fns:
                stem, ext = os.path.splitext(f)
                if ext.lower() in ('.tbin', '.png', '.jpg'):
                    names.add(norm(os.path.relpath(os.path.join(dp, stem), maps_dir)))

    data = {}
    lp = os.path.join(tree, 'Data', 'Locations.json')
    if os.path.isfile(lp):
        try:
            data = json.load(open(lp, encoding='utf-8-sig'))
        except Exception:
            data = {}
    return names, data


def norm(path):
    """统一成 `/` 分隔、小写（Windows 上 os.sep 是反斜杠）。"""
    return path.replace(os.sep, '/').replace('/', '/').lower()


def location_names(data):
    """合法地名集合 = 键 + 每个地点的 `FormerLocationNames`。

    后者是 CP `CustomLocations.MigrateLegacyNames` 写进去的 ——
    SVE 的 `ShearwaterBridge` / `GrandpasShed` 这些老名字全靠它才能被传送解析到。
    """
    out = set()
    for key, value in data.items():
        out.add(key)
        if isinstance(value, dict):
            for former in (value.get('FormerLocationNames') or []):
                out.add(former)
    return out


def map_warps(path, prop=b'Warp'):
    """抽出地图属性 `Warp` / `NPCWarp` 里的 `toMap` 列表。

    ⚠ 两个属性**是分开的**（`AddWarps` 写 `Warp`、`AddNpcWarps` 写 `NPCWarp`），
    只查 `Warp` 会漏掉一半 —— 所以 `prop` 是个参数。

    ⚠ 判据是**近似**的：本工具不解析 tbin 结构，而是在可打印串里找最长的
    「五元组重复」串。tbin 的字符串**没有长度前缀**，抽出来会跟后面的字节粘住
    （实测见过 `... 58 42S`），所以只按五元组的形状匹配、忽略尾部余数。
    """
    raw = open(path, 'rb').read()

    def parse(value):
        tokens = value.split()
        found = []
        i = 0
        while i + 4 < len(tokens) + 1:
            try:
                int(tokens[i]); int(tokens[i + 1])          # fromX fromY
                name = tokens[i + 2].decode('ascii')        # toMap
                int(tokens[i + 3]); int(tokens[i + 4])      # toX toY
                found.append(name)
                i += 5
            except Exception:
                i += 1
        return found

    # ★ 精确路径：属性名 + 类型字节 0x03 + int32 长度 + 值。旧启发式在**所有可打印串**
    #   里挑最长的五元组，`Doors`（`14 16 indoor 120 …`，恰好也是五元组重复）会被当成
    #   `Warp` —— 实测 Downtown Zuzu 的 4 张公寓图因此误报 `indoor`。
    precise = []
    marker = re.compile(rb'(?<![A-Za-z])' + re.escape(prop) + rb'\x03')
    for m in marker.finditer(raw):
        i = m.end()
        if i + 4 > len(raw):
            continue
        length = int.from_bytes(raw[i:i + 4], 'little')
        if not 0 < length <= 1_000_000 or i + 4 + length > len(raw):
            continue
        value = raw[i + 4:i + 4 + length]
        if WARP.match(value):
            precise.extend(parse(value))

    # 找不到属性名就是**没有这个属性**。不要退回“最长可打印五元组”的启发式：
    # 没有 `Warp` 的地图里，`Doors` 的值（`14 16 indoor 120 …`）也会被选中，
    # 于是凭空多出 `indoor` 这个不存在的目标（Downtown Zuzu 的 4 张公寓图）。
    return precise


def audit(tree):
    """检查一：→ {查不到的 toMap: {来源地图}}"""
    names, data = load_tree(tree)
    # 游戏 `_locationLookup` 是 OrdinalIgnoreCase（实测 `Custom_LK_JojaBreakroom` 与
    # `Custom_LK_JojaBreakRoom` 只差大小写，仍能到达）⇒ 地名比较也要小写。
    locations = {name.lower() for name in location_names(data)}
    maps_dir = os.path.join(tree, 'Maps')
    missing = collections.defaultdict(set)
    n_maps = n_warps = 0
    for f in sorted(os.listdir(maps_dir)):
        if not f.endswith('.tbin'):
            continue
        n_maps += 1
        # `Warp` 与 `NPCWarp` 是两个**分开**的地图属性（`AddWarps` / `AddNpcWarps`），
        # 都要查 —— 只查一个会漏掉一半传送。
        for target in map_warps(os.path.join(maps_dir, f), b'Warp')                 + map_warps(os.path.join(maps_dir, f), b'NPCWarp'):
            n_warps += 1
            if norm(target) in names or target.lower() in locations:
                continue
            missing[target].add(f[:-5])
    print(f'{tree}\n  Maps {len(names)} 个资产名 / 合法地名 {len(locations)} 个'
          f' / 地图 {n_maps} 张、warp {n_warps} 条')
    return missing


def audit_paths(tree):
    """检查二：→ {找不到的 MapPath: [地点名]}"""
    names, data = load_tree(tree)
    missing = collections.defaultdict(list)
    n = 0
    for key, value in data.items():
        if not isinstance(value, dict):
            continue
        map_path = (value.get('CreateOnLoad') or {}).get('MapPath')
        if not map_path:
            continue
        n += 1
        rel = norm(map_path)
        if rel.startswith('maps/'):
            rel = rel[5:]
        if rel not in names:
            missing[rel].append(key)
    print(f'  {n} 个地点有 CreateOnLoad.MapPath；地图找不到的 {len(missing)} 种')
    return missing


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    base_tree = sys.argv[2] if len(sys.argv) > 2 else None

    print('=== 检查一：warp 目标 ===')
    mine = audit(sys.argv[1])
    base = audit(base_tree) if base_tree else {}
    new = {t: s for t, s in mine.items() if t not in base}
    print(f'  查不到的 warp 目标：产出 {len(mine)} 个，基准 {len(base)} 个，'
          f'**产出新增 {len(new)} 个**')
    for t, src in sorted(mine.items(), key=lambda x: -len(x[1])):
        print(f'    {"★新增" if t in new else " 基准已有"}  {t:34} ← '
              f'{len(src)} 张：{",".join(sorted(src)[:6])}')

    print('=== 检查二：地点 CreateOnLoad.MapPath ===')
    mine2 = audit_paths(sys.argv[1])
    base2 = audit_paths(base_tree) if base_tree else {}
    new2 = {t: v for t, v in mine2.items() if t not in base2}
    print(f'  找不到地图的 MapPath：产出 {len(mine2)} 种，基准 {len(base2)} 种，'
          f'**产出新增 {len(new2)} 种**')
    for t, locs in sorted(mine2.items(), key=lambda x: -len(x[1])):
        print(f'    {"★新增" if t in new2 else " 基准已有"}  {t:44} ← {",".join(locs[:4])}')

    return 0 if not new and not new2 else 1


if __name__ == '__main__':
    sys.exit(main())
