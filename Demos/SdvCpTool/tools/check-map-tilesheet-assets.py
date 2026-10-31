#!/usr/bin/env python3
"""逐张地图检查：它引用的**每一个图集**在产出树里都有对应图片吗。

**为什么要它**：`check-map-tilesheets.py` 查的是"某一季的变体缺了"，它自己
在文件头写了已知局限 —— **"图集整个缺失"那一类查不出来**。本工具补的就是这一类，
而且判据**不是猜的**：直接按 tbin 的二进制格式把 `TileSheets[].ImageSource` 读出来。

## tbin 格式（xTile `Map.Store` / `StoreTileSheet`，反编译逐段对过）

```
magic  "tBIN10"            6 字节
id                          string
description                 string
properties                  count + 每组(key, 类型字节, 值)
tilesheets                  count + 每组:
                              Id            string
                              Description   string
                              ImageSource   string
                              SheetSize     2×int32
                              TileSize      2×int32
                              Margin        2×int32
                              Spacing       2×int32
                              properties
layers                      …
string = int32 长度 + UTF-8 字节
属性值按类型字节：0=bool(1 字节) 1=int32 2=float(4 字节) 3=string
```

## 图片名怎么判存在

游戏侧：`XnaDisplayDevice.LoadTileSheet` 直接拿 `ImageSource` 当资产名；
`GameLocation` 对**没有目录**的名字默认按 `Maps/` 找（`gameloc.cs:1138-1143`：
`Path.GetDirectoryName(...)` 为空时 `text = "Maps"`）。

本工具按**游戏规则**折算路径（见 `resolve()`），**按树里真实存在的文件**判：

- 名字**没有目录** ⇒ ① **地图自己那一层**（`Maps/Mines/31.tbin` 引用 `mine_dark` 实际是
  `Maps/Mines/mine_dark.png`，只按 `Maps/` 找会误报，实测踩过）② `Maps/<名>`
  ③ `Tilesheets/<名>`（1.6 把图集挪到了这里）；
- 名字**有目录** ⇒ 当内容根相对路径用；`../` 按地图自己那层折算
  （`../LooseSprites/Cursors.png` 落在 `LooseSprites/` —— 早先只扫 `Maps/`+`Tilesheets/`，
  把 7 个**存在**的引用误报成缺失；`Maps/Mines/mine.png` 这种带 `Maps/` 前缀的也一样）。

**判据用原版树校准**：跑原版应当报 **0 / 0**。

**别把上游的数据错读成我们的缺口**：实测 `[CP] Integrated Minecarts` 的 tmx 里
**字面写着** `.paths.png` / `.spring_outdoorsTileSheet.png`（前导点），
那不是我们算歪的 —— 先回包里的源文件里看一眼再下结论（2026-10-12 踩过这半步）。

## 另一档：落点上**不是图片**

同一个内容键只能是一类资产。`Maps/Mine.tbin`（原版地图）与 `Maps/mine.png`（模组图集）
在 Windows 上是**同一个键** —— 图集引用解析到它时，游戏拿到的可能是一张**地图**
（`Texture2D` 的位置给了 `Map`）⇒ 建不出来。所以除了「找不到」，还有这一档
「找到了，但那不是图」。判据：解析出来的键上有 `.tbin` 就算（两种都在更要算）。

实测（2026-10-12 新语料）：改前 7 种（`coop` / `mine` / `nightbgdwarf` / `sterlingdate1` /
`sterlingdate3` / `jonghyukspouseroom` / `jonghyukspousepatio`），其中 5 种是**模组自建地图**
（原版没有同名资产）—— 那几种 `AssetTypeAudit` 看不见，只有这里能兜。改后 0 种。


用法：
    python tools/check-map-tilesheet-assets.py <产出树> [<原版树>]
      （给原版树时会标出哪些是**产出新增**的缺失）
"""
import collections
import os
import struct
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

MAGIC = b'tBIN10'


class Reader:
    def __init__(self, data):
        self.d = data
        self.p = 0

    def i32(self):
        v = struct.unpack_from('<i', self.d, self.p)[0]
        self.p += 4
        return v

    def i16(self):
        v = struct.unpack_from('<h', self.d, self.p)[0]
        self.p += 2
        return v

    def f32(self):
        v = struct.unpack_from('<f', self.d, self.p)[0]
        self.p += 4
        return v

    def s(self):
        n = self.i32()
        v = self.d[self.p:self.p + n].decode('utf-8', 'replace')
        self.p += n
        return v

    def skip_props(self):
        for _ in range(self.i32()):
            self.s()                      # key
            t = self.d[self.p]
            self.p += 1
            if t == 0:
                self.p += 1               # bool
            elif t == 1:
                self.p += 4               # int32
            elif t == 2:
                self.p += 4               # float
            else:
                self.s()                  # string


def tilesheets(path):
    """→ [(Id, ImageSource)]；读不动时返回 None。"""
    try:
        data = open(path, 'rb').read()
        r = Reader(data)
        if data[:6] != MAGIC:
            return None
        r.p = 6
        r.s()                             # id
        r.s()                             # description
        r.skip_props()
        out = []
        for _ in range(r.i32()):
            sid = r.s()
            r.s()                         # description
            image = r.s()
            r.p += 8 * 4                  # 四个 Size
            r.skip_props()
            out.append((sid, image))
        return out
    except Exception:
        return None


def collect(tree):
    """→ ({树内路径（`/` 分隔小写）: {image, map}}, {地图: [(Id, Image)]})

    建索引的路子：整个内容根（`Maps/` 与 `Tilesheets/` 之外还有 `LooseSprites/` 一类 ——
    `../LooseSprites/Cursors.png` 这种相对引用会落到那里）。
    """
    kinds = collections.defaultdict(set)
    for dp, _dirs, fns in os.walk(tree):
        for f in fns:
            stem, ext = os.path.splitext(f)
            if ext.lower() not in ('.png', '.jpg', '.tbin'):
                continue
            rel = os.path.relpath(os.path.join(dp, stem), tree)
            kinds[rel.replace(os.sep, '/').lower()].add(
                'map' if ext.lower() == '.tbin' else 'image')

    maps = {}
    base = os.path.join(tree, 'Maps')
    for dp, _dirs, fns in os.walk(base):
        for f in fns:
            if f.lower().endswith('.tbin'):
                rel = os.path.relpath(os.path.join(dp, f), base)
                maps[rel.replace(os.sep, '/')[:-5]] = tilesheets(os.path.join(dp, f))
    return kinds, maps


def image_stem(image):
    """去掉**已知图片扩展名**，保留资产名里的点。

    ⚠ 不能用 `os.path.splitext`：语料里大量图集叫 `DN.SnS_Logophile_Rugs`，
    splitext 会把 `.SnS_Logophile_Rugs` 当扩展名，得到 `DN`，误报一堆“缺图”。
    """
    s = image.replace(os.sep, '/').lstrip('/')
    for ext in ('.png', '.jpg', '.jpeg'):
        if s.lower().endswith(ext):
            return s[:-len(ext)]
    return s


def resolve(map_name, image):
    """图集引用 → **按游戏规则**折算成树内路径候选（相对内容根，`/` 分隔小写）。

    规则（`gameloc.cs:1138-1143` + `XnaDisplayDevice.LoadTileSheet`）：
      · 名字**没有目录** ⇒ 先找**地图自己那一层**，再 `Maps/<名>`，再 `Tilesheets/<名>`；
      · 名字**有目录** ⇒ 当内容根相对路径用；`../` 按地图自己那一层折算（相对路径是相对地图文件的）。
    """
    here = os.path.dirname(map_name).replace(os.sep, '/').lower()          # 如 `mines`
    src = image_stem(image).lower()
    if not src:
        return []
    if src.startswith('../'):
        return [os.path.normpath(f'maps/{here}/{src}').replace(os.sep, '/')]
    if '/' in src:
        if src.startswith(('maps/', 'tilesheets/')):
            return [src]
        # 有目录但不是 `Maps/`/`Tilesheets/`：按游戏规则当**内容根相对路径**
        # （`LooseSprites/Cursors` 就落在 `LooseSprites/`）。再保留"地图同层子目录"
        # 作为兜底候选 —— 实测有地图把同层图集写成 `sub/x`。
        out = [src]
        if here:
            out.append(os.path.normpath(f'maps/{here}/{src}').replace(os.sep, '/'))
        # `Mines/mine` 这种写法：相对内容根不存在，但 `Maps/Mines/mine` 存在。
        out.append(f'maps/{src}')
        return out
    out = []
    if here:
        out.append(f'maps/{here}/{src}')
    out += [f'maps/{src}', f'tilesheets/{src}']
    return out


def audit(tree):
    kinds, maps = collect(tree)
    missing = collections.defaultdict(set)
    mismatch = collections.defaultdict(set)     # 落点上是**地图**，不是图
    n_sheets = 0
    for name, sheets in sorted(maps.items()):
        if sheets is None:
            missing['<tbin 读不出来>'].add(name)
            continue
        for _sid, image in sheets:
            if not image:
                continue
            n_sheets += 1
            cands = [c for c in resolve(name, image) if c]
            found = [c for c in cands if c in kinds]
            if not found:
                missing[image_stem(image).lower()].add(name)
                continue

            # **第一个存在的候选**决定拿到的类型（顺序同 `resolve`）。
            # 一个内容键只能是一类资产：`Maps/Mine.tbin` 与 `Maps/mine.png` 在 Windows 上是**同一个键**，
            # 谁赢看打包顺序 ⇒ 图集引用拿到的可能是地图（`Texture2D` 的位置给了 `Map`）⇒ 建不出来。
            if 'map' in kinds[found[0]]:
                mismatch[image_stem(image).lower()].add(name)
    bad = sum(1 for s in maps.values() if s is None)
    print(f'{tree}\n  Maps 下 {len(maps)} 张地图（读不出来 {bad} 张）/ '
          f'可用的图集名 {len(kinds)} 个 / 图集引用 {n_sheets} 处')
    return missing, mismatch


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    mine, mine_bad = audit(sys.argv[1])
    base, base_bad = audit(sys.argv[2]) if len(sys.argv) > 2 else ({}, {})
    new = {k: v for k, v in mine.items() if k not in base}
    new_bad = {k: v for k, v in mine_bad.items() if k not in base_bad}
    print(f'  找不到图片的图集名：产出 {len(mine)} 种，基准 {len(base)} 种，'
          f'**产出新增 {len(new)} 种**')
    for name, maps in sorted(mine.items(), key=lambda kv: -len(kv[1])):
        mark = '★新增' if name in new else ' 基准已有'
        print(f'    {mark}  {name:38} ← {len(maps)} 张：{",".join(sorted(maps)[:4])}')

    print(f'  落点上**不是图片**（键被地图占着）的图集名：产出 {len(mine_bad)} 种，'
          f'基准 {len(base_bad)} 种，**产出新增 {len(new_bad)} 种**')
    for name, maps in sorted(mine_bad.items(), key=lambda kv: -len(kv[1])):
        mark = '★新增' if name in new_bad else ' 基准已有'
        print(f'    {mark}  {name:38} ← {len(maps)} 张：{",".join(sorted(maps)[:4])}'
              '  ⇒ 游戏会拿地图当贴图（进那个地点就炸 / 图集渲染错）')
    return 0 if not (new or new_bad) else 1


if __name__ == '__main__':
    sys.exit(main())
