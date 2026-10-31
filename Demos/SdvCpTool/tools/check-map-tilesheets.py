#!/usr/bin/env python3
"""检查地图 tbin 引用的**季节性图集**在产出树里是否真的存在。

**为什么要它**：`GameLocation.updateSeasonalTileSheets`（`GameLocation.cs:16411-16447`）
在**建地点**时会给每个 tilesheet 的名字套一遍季节替换，套完去加载**那个名字**：

```csharp
// GetSeasonalTilesheetName（:16439）
string fileName = Path.GetFileName(sheet_path);
if (fileName.StartsWith("spring_") || ... "summer_" || "fall_" || "winter_")
    sheet_path = Path.Combine(Path.GetDirectoryName(sheet_path),
                              current_season + fileName.Substring(fileName.IndexOf('_')));
return sheet_path;
```

**加载不到就整张地图建不出来**，游戏日志里写
`Location 'X' failed to load seasonal asset name '...' for tilesheet ID '...'`
⇒ 地图进不去、传送点也不会触发。这是**只有实机才会暴露**的一类问题
（回包打得出来、`Helper.GameContent.Load` 也加载得出来），所以做一道离线检查。

⚠ 判据是**近似**的：图集名是从 tbin 的二进制里**抽字符串**得到的（本项目不解析 tbin 结构），
而 tbin 里的字符串**没有长度前缀**，抽出来会跟后面的字节粘在一起
（实测见过 `spring_townS`、`spring_townS2`、`spring_SVE_Tilesheet2.pngL` 这类）。

所以判据收紧成两步：

1. **只判带季节前缀的** —— 不带前缀的串里噪声太多（图层名 `Back`/`Buildings`、
   属性名、以及原版 tbin 里也有的 `monsterGraveTiles` 之类），拿它们当图集判会误报；
2. **基名必须在图集目录里出现过**（`X` 或 `spring_X`/`summer_X`/…）—— 这一条把粘连噪声滤掉，
   因为噪声的基名（`townS`、`SVE_Tilesheet2.pngL`）不会命中。

⚠ **已知局限**（第 2 条带来的）：**"图集整个缺失"这一类查不出来** ——
那种情况下基名也不在图集目录里，会被当成噪声滤掉。这个工具查的是
**"某一季的变体缺了"**（例如只有 spring/summer、缺 fall/winter）那类。

用法：python tools/check-map-tilesheets.py <产出树>
      （会去查它下面的 `Maps/`；图集也按同目录的图片文件找）
"""
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

SEASONS = ('spring', 'summer', 'fall', 'winter')
IMG_EXT = ('.png', '.jpg', '.jpeg', '.bmp')


def base_and_season(name):
    """→ (去掉季节前缀的基名, 原季节前缀 or None)"""
    for s in SEASONS:
        if name.startswith(s + '_'):
            return name[len(s) + 1:], s
    return name, None


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    tree = sys.argv[1]
    maps = os.path.join(tree, 'Maps')
    if not os.path.isdir(maps):
        print(f'找不到 {maps}')
        return 2

    # 图集 = 同目录图片文件去掉扩展名 —— 也就是**打包剥掉扩展名之后**的形态
    assets = set()
    for f in os.listdir(maps):
        if os.path.splitext(f)[1].lower() in IMG_EXT:
            assets.add(os.path.splitext(f)[0])
    print(f'{maps}：{len(assets)} 个图集')

    checked = 0
    missing = []
    for fn in sorted(os.listdir(maps)):
        if not fn.endswith('.tbin'):
            continue
        raw = open(os.path.join(maps, fn), 'rb').read()
        seen = set()
        for m in re.findall(rb'[ -~]{4,}', raw):
            s = m.decode('ascii')
            if ' ' in s or '@' in s:
                continue
            stem = s
            for e in IMG_EXT:
                if stem.lower().endswith(e):
                    stem = stem[: -len(e)]
                    break
            name, season = base_and_season(stem)
            if season is None or stem in seen:
                continue
            # 噪声过滤：基名得在图集目录里出现过（见文件头「已知局限」）
            if not any(f'{x}_{name}' in assets for x in SEASONS):
                continue
            seen.add(stem)
            checked += 1
            # ★ 到这儿才判：串里的那个季节变体在不在。
            #   游戏会把 `spring_X` 换成 `<当前季节>_X` 再去加载，缺哪一季就是缺。
            if stem not in assets:
                missing.append((fn, stem))

    print(f'查了 {checked} 处**带季节前缀**的图集引用')
    print(f'有问题的 {len(missing)} 处：')
    for fn, s in missing[:30]:
        print(f'   {fn:36} {s}')
    return 0 if not missing else 1


if __name__ == '__main__':
    sys.exit(main())
