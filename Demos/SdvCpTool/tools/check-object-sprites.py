#!/usr/bin/env python3
"""`Data/Objects`（以及 `BigCraftables` / `Furniture`）里每条的精灵下标，在图集范围内吗？

**为什么要它**：条目的 `Texture` 指向一张图集、`SpriteIndex` 是那上面的第几格。
下标越界 ⇒ 游戏取到的矩形落图外 ⇒ **物品在游戏里画不出来**（"有这个东西但看不见"）。
这一类离线就能判：图集 PNG 的尺寸就在产出树里。

**判据**：照抄游戏**真正**取源矩形的那两个算法，判算出来的矩形在不在图内。

```
Data/Objects  → Game1.getSourceRectForStandardTileSheet(tex, i, 16, 16)
                new Rectangle(i*16 % W,  i*16 / W * 16,      16, 16)

Data/BigCraftables → Object.getSourceRectForBigCraftable(tex, i)
                new Rectangle(i % (W/16) * 16,  i*16 / W * 16 * 2,  16, 32)
                ⚠ 另一套：列数用**整除** W/16，行距 **32**，精灵 **16×32**
```

⇒ 判 `x + 宽 ≤ W 且 y + 高 ≤ H`（外加 `index ≥ 0`）。

⚠ **别把这条简写成"下标 ≤ 格数-1"** —— 两者只在图宽高都是 16 的倍数时才恰好等价，
图宽不是 16 的倍数时那套会**漏报**（下标算出来"有格子"，取的矩形却横着压出去）。

⚠ `index < 0` 要**单独判**：C# 里 `-16 % W == -16`（负数），Python 里是 `W-16`，靠 rect 体现不出来。

**基准**：带原版树对照 —— 原版自己应当报 0（实测 0）；产出里若有新增，先怀疑产出。

两类问题：①引用的图集在树里**找不到**；②`SpriteIndex` **越界**。

`Data/Objects` 与 `Data/BigCraftables` 1.6 是**结构化对象**，上面那条判据直接可用。

**`Data/Furniture` 1.6 仍是斜杠分隔的老字符串**，但那份格式已从反编译查实，所以这里也管它。
字段语义（来源：`StardewValley.Objects.Furniture.getData` / `getDefaultSourceRect` /
`getTypeNumberFromName`，及 `ItemTypeDefinitions.FurnitureDataDefinition.ParseDataAndErrorIfNeeded`）：

| 段 | 含义 |
|---|---|
| 0 | 名字 |
| 1 | 类型名 → 类型号（见下表；`bed*` → 15）|
| 2 | 精灵尺寸 `宽 高`（**格**）；`-1` ⇒ 按类型查默认尺寸 |
| 8 | `SpriteIndex`；缺/空 ⇒ 取**字典键**（键不是整数时作 `-1`）|
| 9 | 纹理；缺/空 ⇒ `TileSheets\furniture` |

```
SpriteIndex = int(段8) 若 ≥0，否则 int(键) 若键是整数，否则 -1     ← FurnitureDataDefinition.GetSpriteIndex
x = SpriteIndex*16 % 图宽      y = SpriteIndex*16 / 图宽 * 16
精灵尺寸 = 段2 非 "-1" ? (段2 前两段) : 类型表[类型号]（表里没有 ⇒ 1×2）
检查：SpriteIndex ≥ 0 且 x + 宽*16 ≤ 图宽 且 y + 高*16 ≤ 图高
```

**自洽性核对**（判据闭合的凭据）：原版 645 条家具按上式算，`SpriteIndex` 无一为负（实测 0/645），
类型名也全部落在表内。⚠ 但与 `Objects` 不同，家具的精灵**不是 1×1**，所以上一条判据
（`index ≤ 格数-1`）**不能**套用，必须用矩形界内这条 —— 见 `scan_furniture`。

用法：
    python tools/check-object-sprites.py <产出树> [<基准树>]
"""
import collections
import json
import os
import struct
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

TILE = 16

# 两件事分开：
#   · **贴图能不能解析** —— 对所有带 `Texture` 的资产通用（少了 = 有这个 NPC/建筑/作物但看不见）；
#   · **精灵下标在不在范围内** —— 只有结构化、且带下标字段的资产才判。

# (资产, 图集路径字段, 缺省图集) —— 只判"贴图能不能解析"
TEXTURE_ONLY = [
    ('Data/Characters', 'Texture', None),
    ('Data/Buildings', 'Texture', None),
    ('Data/Crops', 'Texture', 'Maps/springobjects'),
    ('Data/WildTrees', 'Texture', None),
    ('Data/FruitTrees', 'Texture', None),
    ('Data/FarmAnimals', 'Texture', None),
    ('Data/Pets', 'Texture', None),
    ('Data/FishPonds', 'Texture', None),
]

# (资产, 图集路径字段, 下标字段, 缺省图集, 取源矩形的方式)
#   · `standard16`  —— `Game1.getSourceRectForStandardTileSheet(tex, i, 16, 16)`
#   · `bigcraftable` —— `Object.getSourceRectForBigCraftable(tex, i)`（**另一套**）
# ⚠ 两套的**缺省图集也不同**，且都是反编译里读出来的，别照着感觉写：
#   `ObjectDataDefinition` → `rawData.Texture ?? "Maps\\springobjects"`
#   `BigCraftableDataDefinition` → `rawData.Texture ?? "TileSheets\\Craftables"`
ASSETS = [
    ('Data/Objects', 'Texture', 'SpriteIndex', 'Maps/springobjects', 'standard16'),
    ('Data/BigCraftables', 'Texture', 'SpriteIndex', 'TileSheets/Craftables', 'bigcraftable'),
]


def sprite_rect(mode, img_w, img_h, index):
    """照抄游戏取源矩形那两个算法 → (x, y, 宽, 高)；推不出（图不够一格）返回 None。"""
    if mode == 'bigcraftable':
        # new Rectangle(index % (W/16) * 16, index * 16 / W * 16 * 2, 16, 32)
        # ⚠ 列数用的是**整除** `W/16`，行距是 **32**（一格高 32px），全跟 standard16 不一样。
        cols = img_w // TILE
        if cols <= 0 or img_h < 2 * TILE:
            return None
        return index % cols * TILE, index * TILE // img_w * TILE * 2, TILE, 2 * TILE
    # getSourceRectForStandardTileSheet：new Rectangle(i*16 % W, i*16 / W * 16, 16, 16)
    return index * TILE % img_w, index * TILE // img_w * TILE, TILE, TILE

# ---- Data/Furniture 专用的那套（字段语义见文件头）----

# 类型名 → 类型号（`Furniture.getTypeNumberFromName`；`bed*` → 15、其余 → 9，在此表外）
FURN_TYPE_NUMBER = {
    'chair': 0, 'bench': 1, 'couch': 2, 'armchair': 3, 'dresser': 4, 'long table': 5,
    'painting': 6, 'lamp': 7, 'decor': 8, 'bookcase': 10, 'table': 11, 'rug': 12,
    'window': 13, 'fireplace': 14, 'torch': 16, 'sconce': 17,
}

# 类型号 → 默认精灵尺寸（格）（`getDefaultSourceRectForType`；9/15 与未列者走 default 1×2）
FURN_DEFAULT_SIZE = {
    0: (1, 2), 1: (2, 2), 2: (3, 2), 3: (2, 2), 4: (2, 2), 5: (5, 3), 6: (2, 2),
    7: (1, 3), 8: (1, 2), 10: (2, 3), 11: (2, 3), 12: (3, 2), 13: (1, 2),
    14: (2, 5), 16: (1, 2), 17: (1, 2),
}
FURN_FALLBACK_SIZE = (1, 2)
FURN_DEFAULT_TEXTURE = 'TileSheets/furniture'


def png_size(path):
    """只读 PNG 头（IHDR），不解码整图。"""
    try:
        with open(path, 'rb') as f:
            head = f.read(24)
    except Exception:
        return None
    if head[:8] != b'\x89PNG\r\n\x1a\n' or head[12:16] != b'IHDR':
        return None
    return struct.unpack('>II', head[16:24])


def image_path(tree, asset_name):
    """资产名 `Maps/X` → 树里的 `Maps/X.png`（也认 `.jpg` 与 `Tilesheets/`）。"""
    rel = asset_name.replace(chr(92), '/').lstrip('/')
    for cand in (rel, 'Maps/' + rel, 'Tilesheets/' + rel):
        for ext in ('.png', '.jpg'):
            p = os.path.join(tree, *cand.split('/')) + ext
            if os.path.isfile(p):
                return p
    return None


def furn_sprite_size(fields, type_name):
    """段2 给了显式尺寸就用它（单位=格），否则按类型号查默认表。"""
    if len(fields) > 2 and fields[2] != '-1':
        parts = fields[2].split()
        if len(parts) >= 2:
            try:
                return int(parts[0]), int(parts[1])
            except ValueError:
                pass
    name = type_name.strip().lower()
    num = 15 if name.startswith('bed') else FURN_TYPE_NUMBER.get(name, 9)
    return FURN_DEFAULT_SIZE.get(num, FURN_FALLBACK_SIZE)


def scan_furniture(tree):
    """家具单判（斜杠串、精灵非 1×1）→ ({条目: (原因, 资产)}, 条目数)"""
    bad = {}
    p = os.path.join(tree, 'Data', 'Furniture.json')
    if not os.path.isfile(p):
        return bad, 0
    try:
        data = json.load(open(p, encoding='utf-8-sig'))
    except Exception:
        return bad, 0
    if not isinstance(data, dict):
        return bad, 0
    n = 0
    for key, raw in data.items():
        if not isinstance(raw, str):
            continue
        n += 1
        fields = raw.split('/')
        if len(fields) < 3:
            bad[key] = (f'只有 {len(fields)} 段，读不出类型/尺寸', 'Data/Furniture')
            continue
        field8 = fields[8].strip() if len(fields) > 8 else ''
        tex = (fields[9].strip() if len(fields) > 9 else '') or FURN_DEFAULT_TEXTURE
        try:
            index = int(field8)
        except ValueError:
            index = -1
        if index < 0:
            try:
                index = int(key)
            except ValueError:
                index = -1
        img = image_path(tree, tex)
        if img is None:
            bad[key] = (f'引用的图集 `{tex}` 在产出树里找不到', 'Data/Furniture')
            continue
        size_px = png_size(img)
        if size_px is None:
            continue
        if index < 0:
            # 段8 空、键又不是整数 ⇒ 游戏算出来也是 -1（负源矩形）⇒ 画不出来。
            bad[key] = (f'推不出 `SpriteIndex`（段8={field8 or "空"}，键非整数）', 'Data/Furniture')
            continue
        w, h = size_px
        sw, sh = furn_sprite_size(fields, fields[1])
        x = index * TILE % w
        y = index * TILE // w * TILE
        if x + sw * TILE > w or y + sh * TILE > h:
            bad[key] = (f'第 {index} 格起 {sw}×{sh} 格精灵超出图集 {tex}（{w}×{h}）',
                        'Data/Furniture')
    return bad, n


def scan(tree):
    """→ {越界的条目: (原因, 来源资产)}"""
    bad = {}
    counts = collections.Counter()
    for asset, tex_field, idx_field, default_tex, mode in (
            [(a, t, None, d, None) for a, t, d in TEXTURE_ONLY] + ASSETS):
        p = os.path.join(tree, *asset.split('/')) + '.json'
        if not os.path.isfile(p):
            continue
        try:
            data = json.load(open(p, encoding='utf-8-sig'))
        except Exception:
            continue
        if not isinstance(data, dict):
            continue
        for key, entry in data.items():
            if not isinstance(entry, dict):
                continue
            counts[asset] += 1
            index = entry.get(idx_field) if idx_field else None
            tex = entry.get(tex_field) or default_tex
            if not isinstance(tex, str):
                continue
            img = image_path(tree, tex)
            if img is None:
                # ⚠ 这里**不能静默跳过**：条目引用的图集在树里找不到，正是
                #   "物品拿不到贴图"那一类（实机报 `Failed loading texture X for item Y`）。
                bad[key] = (f'引用的图集 `{tex}` 在产出树里找不到', asset)
                continue
            size = png_size(img)
            if size is None:
                continue
            if mode is None or not isinstance(index, int):
                continue                    # 没有下标字段：只判过"贴图能不能解析"
            w, h = size
            if (w < TILE or h < TILE) and index == 0:
                # 整张图比一格还小、下标又是 0 ⇒ 取的必然就是这整张图，**没有"取错格"的余地**。
                # 源矩形会压出图外一点点，但游戏按图边界画，照显示（SVE 的 `Birch Water` 15×16
                # 就是这种）。这不在本检查要抓的那一类里（那类是"下标指向了图外的格子"）。
                continue
            # ⚠ 判的是**游戏真正取出来的那个矩形在不在图内**，不是"下标 < 格数"。
            #   两者只在图宽高都是 16 的倍数时才恰好等价 —— 别拿"格数"当判据。
            #   还要防 C# 与 Python 的取模差异（C# 里 -16 % W = -16，Python 是 W-16），
            #   所以 index < 0 单独判，不靠 rect 里的负数体现。
            rect = sprite_rect(mode, w, h, index)
            if rect is None:
                bad[key] = (f'图集 {tex}（{w}×{h}）连一格都放不下', asset)
                continue
            x, y, sw, sh = rect
            if index < 0 or x < 0 or y < 0 or x + sw > w or y + sh > h:
                bad[key] = (f'`{idx_field}`={index} 的精灵 {sw}×{sh} 落在图外'
                            f'（图集 {tex} 是 {w}×{h}）', asset)
    furn, furn_n = scan_furniture(tree)
    if furn_n:
        counts['Data/Furniture'] = furn_n
    bad.update(furn)
    for asset, n in counts.items():
        print(f'  {asset:24} 条目 {n}')
    return bad


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    print(f'{sys.argv[1]}')
    mine = scan(sys.argv[1])
    base = scan(sys.argv[2]) if len(sys.argv) > 2 else {}
    new = {k: v for k, v in mine.items() if k not in base}
    print(f'  有问题的条目（图集找不到 / 下标越界）：产出 {len(mine)} 个，基准 {len(base)} 个，'
          f'**产出新增 {len(new)} 个**')
    for key, (why, asset) in sorted(mine.items(), key=lambda kv: kv[1][1])[:25]:
        mark = '★新增' if key in new else ' 基准已有'
        print(f'    {mark}  [{asset}] {key:34} {why}')
    if len(mine) > 25:
        print(f'    …（另 {len(mine) - 25} 个）')
    return 0 if not new else 1


if __name__ == '__main__':
    sys.exit(main())
