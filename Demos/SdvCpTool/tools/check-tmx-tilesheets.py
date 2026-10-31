#!/usr/bin/env python3
"""全语料：内容包 `.tmx` 地图引用的 tileset 图片，在产出树里都能找到吗？

**为什么要它**：SDV 把地图图集的 `image source` 当资产名解析，相对产出树的
`Maps/` 目录。`FromFile`/`Load` 一张 tmx 进来，它引用的每张图都得能找到，
否则**整张地图建不出来**（表现就是"这个地点进不去"）。

**路径要按原样拼**（这是本工具踩过的坑）：`image source` 可以是**子目录**形式，
例如 SVE 的 `Caldera.tmx` 写的是 `Mines/volcano_caldera` —— 要判
`<tree>/Maps/Mines/volcano_caldera`，**不能取 basename**。取 basename 会把
`volcano_caldera` / `volcano_dungeon` / `mine_frost` 全误报成缺失（实测踩过）。

**⚠ 必须拿「同一批包」跑出来的树来对**：本工具扫的是**全部 32 个 CP 包**的 tmx，
所以要拿 `tools/chain-packs.py` 跑完全部包的那棵树。拿只跑了一个包的树来对，
别的包 `Load` 的图集当然找不到 —— 那是**参数不同**，不是缺口（实测踩过）。

用法：python tools/check-tmx-tilesheets.py <产出树>
"""
import collections
import glob
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cppacks  # noqa: E402

IMAGE = re.compile(r'<image\s+source="([^"]+)"')


def norm(path):
    """统一成 `/` 分隔、小写（Windows 上 os.sep 是反斜杠）。"""
    sep = os.sep
    return path.replace(sep, '/').lower()


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    tree = sys.argv[1]
    maps = os.path.join(tree, 'Maps')
    if not os.path.isdir(maps):
        print(f'找不到 {maps}')
        return 2

    have = set()
    for dp, _dirs, fns in os.walk(maps):
        for f in fns:
            stem, ext = os.path.splitext(f)
            if ext.lower() in ('.png', '.jpg', '.tbin'):
                have.add(norm(os.path.relpath(os.path.join(dp, stem), maps)))

    total = 0
    missing = collections.Counter()
    examples = collections.defaultdict(list)
    for _name, pack in cppacks.find_cp_packs():
        # ⚠ 用 os.walk 而不是 glob：模组目录名里常带 `[CP]` 这种方括号，
        #   glob 会当成字符类，**整包被悄悄跳过**（实测踩过）。
        for dp, _dirs, fns in os.walk(pack):
            for fn in fns:
                if not fn.lower().endswith('.tmx'):
                    continue
                tmx = os.path.join(dp, fn)
                try:
                    text = open(tmx, encoding='utf-8', errors='replace').read()
                except Exception:
                    continue
                for m in IMAGE.finditer(text):
                    src = m.group(1)
                    stem = norm(os.path.splitext(src)[0].lstrip('/'))
                    total += 1
                    if stem in have:
                        continue
                    missing[stem] += 1
                    if len(examples[stem]) < 4:
                        examples[stem].append(os.path.relpath(tmx, pack))

    print(f'{tree}：Maps 下 {len(have)} 个图集')
    print(f'全语料 .tmx 的 tileset 图片引用 {total} 处；找不到的 {len(missing)} 种 '
          f'/ {sum(missing.values())} 处')
    for stem, n in missing.most_common(25):
        print(f'   {n:4}  {stem:36} 例：{", ".join(examples[stem][:3])}')
    return 0 if not missing else 1


if __name__ == '__main__':
    sys.exit(main())
