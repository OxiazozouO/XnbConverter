#!/usr/bin/env python3
"""一条命令跑完所有「产出树自检」，并和基准树对照 —— 每次都要手工跑七八个脚本，收成一个。

用法：
    python tools/verify-tree.py <产出树> [<基准树>] [--report <report.json>]

不带基准树时只报绝对数字，不给「新增」判定（大部分检查都靠对照才有意义）。

`--report` 给上时**顺带列出转换时内置的几段审计**（`gameData` / `spriteSheet` /
`unexpandedTokens` / `keyCase` / `conditionPlacement` / `outputShape`）——
那几段是 `convert` 跑的时候写进报告的，本脚本**不重跑**（判据在 C# 侧，只有一份）。

## 两个实现细节

1. **用 `runpy` 在进程内跑，不 `subprocess`** —— 传路径给 python.exe 时 Windows 会按
   ANSI 代码页解 argv，**含中文的路径会乱码**（本仓库的产出树路径常带模组中文名）。
   进程内设 `sys.argv` 绕开这一层。
2. **每项检查自己就是权威** —— 这里只负责跑 + 汇总，不重新实现它们的判据
   （判据散成两份迟早会漂移）。

退出码：有任何一项报「产出新增 ≠ 0」就是 1。
"""
import glob
import io
import os
import re
import runpy
import sys
from contextlib import redirect_stdout

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

TOOLS = os.path.dirname(os.path.abspath(__file__))

# (标签, 脚本, 是否需要基准树, 从输出里抓「新增」数字的正则)
CHECKS = [
    ('每条 Warp 的目标地点', 'check-map-warps.py', True, r'warp 目标：.*?产出新增 (\d+) 个'),
    ('地点的 CreateOnLoad.MapPath', 'check-map-warps.py', True, r'MapPath：.*?产出新增 (\d+) 种'),
    ('每张地图引用的图集图片', 'check-map-tilesheet-assets.py', True, r'产出新增 (\d+) 种'),
    # 图集算出来的键被**地图**占着（`Maps/Mine.tbin` 与 `Maps/mine.png` 是同一个键）——
    # 这一档 `AssetTypeAudit` 看不见（模组自建的地图没有原版对照），得在这里兜
    ('图集落点被地图占着', 'check-map-tilesheet-assets.py', True, r'不是图片.*?产出新增 (\d+) 种'),
    ('图块上的 Warp 动作', 'check-map-tile-warps.py', True, r'产出新增 (\d+) 种'),
    ('物品图集/精灵下标', 'check-object-sprites.py', True, r'产出新增 (\d+) 个'),
    ('季节性图集变体', 'check-map-tilesheets.py', False, r'有问题的 (\d+) 处'),
    ('语料 tmx 的 tileset 图片', 'check-tmx-tilesheets.py', False, r'找不到的 \d+ 种 / (\d+) 处'),
    ('地图 Message 的文本', 'check-map-messages.py', True, r'产出新增 (\d+) 种'),
    ('LocalizedText 引用', 'check-localized-text.py', True, r'产出新增 (\d+) 种'),
    # 上面那些都在**地图**那一侧；这一项补的是 `Data/*.json` 里的**图片名**
    ('数据资产里的贴图引用', 'audit-texture-refs.py', True, r'多出\*\*的「找不到」：(\d+)'),
]


def run(script, args):
    """→ (合并后的输出, 退出码)"""

    def go():
        sys.argv = [script] + args
        try:
            runpy.run_path(os.path.join(TOOLS, script), run_name='__main__')
            return 0
        except SystemExit as e:
            return e.code if isinstance(e.code, int) else 0

    buf = io.StringIO()
    with redirect_stdout(buf):
        code = go()
    return buf.getvalue(), code


def main() -> int:
    # ⚠ 别用「过滤掉以 -- 开头的参数」取位置参数：那样 `--report X` 里的 `X` 会被当成
    #   一棵树（`tools/diff-trees.py` 就踩过这个，报出「只有新树有 7594 个文件」）。
    trees, report, i = [], None, 1
    while i < len(sys.argv):
        if sys.argv[i] == '--report':
            report = sys.argv[i + 1]
            i += 2
            continue
        trees.append(sys.argv[i])
        i += 1
    if not trees:
        print(__doc__)
        return 2
    tree = trees[0]
    base = trees[1] if len(trees) > 1 else None

    print(f'产出树：{tree}')
    if base:
        print(f'基准树：{base}')
    print()

    results = []
    for label, script, needs_base, pattern in CHECKS:
        # `check-map-warps.py` 一次跑出两项检查（warp 目标 + MapPath），所以它跑两次
        args = [tree] + ([base] if (needs_base and base) else [])
        out, code = run(script, args)
        m = re.search(pattern, out)
        value = int(m.group(1)) if m else None
        results.append((label, value, code))
        mark = '—' if value is None else ('OK' if value == 0 else f'★ {value}')
        print(f'  {label:28} {mark}')
        if value is None:
            print('      （没能从输出里认出数字，下面是原始输出）')
            for line in out.splitlines():
                print('      ' + line)
    print()
    print('%-30s %s' % ('检查项', '产出相对基准的「新增问题数」（空 = 无基准）'))
    bad = [r for r in results if r[1]]
    for label, value, _code in results:
        print('%-30s %s' % (label, '' if value is None else value))
    if not base:
        print('\n⚠ 没给基准树：多数检查的绝对数字本身说明不了问题（原版自己就有查不到的）。')
    if report:
        show_report(report)
    return 1 if bad else 0


def show_report(path):
    """把 `convert` 写进报告的**内置审计段**列出来（不重跑 —— 判据在 C# 侧，只有一份）。"""
    import json
    try:
        r = json.load(open(path, encoding='utf-8'))
    except Exception as e:
        print(f'\n读不了报告：{e}')
        return
    print(f'\n内置审计（来自 {path}，由 convert 写入，本脚本不重跑）：')
    for k in ('gameData', 'spriteSheet', 'unexpandedTokens', 'keyCase',
              'conditionPlacement', 'outputShape'):
        v = r.get(k)
        if not isinstance(v, dict):
            continue
        checked = v.get('checkedAssets', v.get('assets'))
        nums = {kk: vv for kk, vv in v.items()
                if isinstance(vv, int) and kk not in ('checkedAssets', 'assets')}
        print(f'  {k:22} 核对 {checked}；{nums}')


if __name__ == '__main__':
    sys.exit(main())
