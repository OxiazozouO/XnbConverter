#!/usr/bin/env python3
"""**跨包覆盖审计**：同一个资产被**多个包**写过时，谁把谁冲掉了。

为什么要它：CP 的语义是「**后加载的补丁后应用**」，所以同一个资产被两个包改动时**总是后者赢** ——
这没错，但**丢的那一方**从不报错：报告是**逐包**的，每个包都说自己 `written=True`，
只有把整条链的写入按顺序合起来看，才知道"某包的成果其实没进最终产物"。

实测来源（2026-10-15，实机「农场地图传送没合并进来」）：`Lnh's Farms Convenient Road`
把便捷通道盖到 `Maps/{{Farm}}` 上，而 `[CP] Grandpa's Farm` 有一条**无条件**的
`Load: Maps/Farm,Maps`（整张替换）—— 谁后跑谁说了算。顺序修好之后，**这一类还在别处存在**，
所以要有这道自检把它报出来，而不是让它藏在"谁后写"里。

用法：
    python tools/audit-overwrites.py <产出根>          # 里面有 NN_<包名>/report.json
    python tools/audit-overwrites.py <产出根> --lost   # 只列"改被整张替换冲掉"的那些
"""
import glob
import io
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# **覆盖式**动作：它们把整个资产**换掉**，先前对同一资产的改动一律作废
REPLACING = {'Load'}


def writes(root):
    """按链顺序返回 [(序号, 包名, 动作, 目标, 来源文件, 补丁序号)]，只收**真的写出去**的。"""
    out = []
    for rep in glob.glob(os.path.join(root, '*', 'report.json')):
        m = re.match(r'(\d+)_', os.path.basename(os.path.dirname(rep)))
        idx = int(m.group(1)) if m else 10 ** 6
        try:
            d = json.load(io.open(rep, encoding='utf-8'))
        except Exception:
            continue
        pack = os.path.basename(os.path.dirname(rep))
        for p in (d.get('patches') or []):
            if not p.get('written'):
                continue
            for tgt in [t.strip() for t in str(p.get('target') or '').split(',') if t.strip()]:
                out.append((idx, pack, p.get('action'), tgt, p.get('source'), p.get('index')))
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    root = sys.argv[1]
    only_lost = '--lost' in sys.argv

    ledger = {}
    for row in writes(root):
        ledger.setdefault(row[3], []).append(row)

    multi = {k: v for k, v in ledger.items() if len(v) > 1}
    lost = []
    for asset, rows in multi.items():
        rows.sort()
        # 最后一次写入之前的**所有**写入，只要中间夹着一次"整张替换"，就都作废
        for i, r in enumerate(rows[:-1]):
            if not any(later[2] in REPLACING for later in rows[i + 1:]):
                continue
            killer = next(later for later in rows[i + 1:] if later[2] in REPLACING)
            lost.append((asset, r, killer))

    print('产出根：%s' % root)
    print('被写过的资产 %d 个；其中被**多个包**写过的 %d 个' % (len(ledger), len(multi)))
    print('⚠ 其中 "%s" 这类**整张替换**动作把先前的改动冲掉的：**%d 处**'
          % ('/'.join(sorted(REPLACING)), len(lost)))

    if not only_lost:
        print('\n=== 被多个包写过的资产（前 20，按写过的包数）===')
        for asset, rows in sorted(multi.items(), key=lambda kv: -len(kv[1]))[:20]:
            print('  %-46s %d 个包：%s' % (asset[:46], len(rows),
                                          ' → '.join('%s(%s)' % (r[1].split('_', 1)[-1][:14], r[2]) for r in sorted(rows))))

    if lost:
        print('\n=== 被整张替换冲掉的改动（前 30）===')
        for asset, victim, killer in lost[:30]:
            print('  %-40s' % asset[:40])
            print('      被冲掉：%s 的 %s（源 %s#%s）' % (victim[1].split('_', 1)[-1][:30], victim[2], victim[4], victim[5]))
            print('      冲掉者：%s 的 %s（源 %s#%s）' % (killer[1].split('_', 1)[-1][:30], killer[2], killer[4], killer[5]))
    return 0


if __name__ == '__main__':
    sys.exit(main())
