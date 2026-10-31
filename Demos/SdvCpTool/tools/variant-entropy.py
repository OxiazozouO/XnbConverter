#!/usr/bin/env python3
"""跨包汇总**变体归一化的熵** —— 从 CLI 自己产出的报告里读，不再自带表。

用法：
    python tools/variant-entropy.py <report.json> [<report.json> ...]

**为什么不再自带发射表**：先前本文件里有一份手写的 `EMITTER`，与 C# 的
`EmitterTable` 是两份手工同步的表 —— 实测漂移过（C# 侧已修正两格，Python 侧
还留着旧结论，重跑才发现）。现在归一化由 `ReportWriter` 写进报告的
`normalization` 节，**表只剩一份**（`src/SdvCpTool.Core/Normalizing/EmitterTable.cs`）。

四档各自成列、**互不合并**：把「未定」并进「有」会虚报覆盖率。
"""
import json
import sys
import collections

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

KIND_ORDER = ['Native', 'None', 'CapacityExceeded', 'Unknown']
# 报告 JSON 里的键名（小写）与内部枚举名（大写）的对应
JSON_KEY = {
    'Native': 'native',
    'None': 'none',
    'CapacityExceeded': 'capacityExceeded',
    'Unknown': 'unknown',
}

KIND_LABEL = {
    'Native': '[有]  有原生最简形式',
    'None': '[无]  已确证无机制',
    'CapacityExceeded': '[装不下] 有机制但装不下',
    'Unknown': '[未定] 待查',
}


def main(paths):
    cells = collections.Counter()          # (dim, carrier, assetType) -> patches
    kind_of = {}
    forms = {}
    totals = collections.Counter()
    spread = {}                            # 维度 -> 载体集合（就是「熵」）
    approx = 0
    dim_patches = 0
    native_intents = 0
    collapsed = 0

    for path in paths:
        try:
            d = json.load(open(path, encoding='utf-8'))
        except Exception:
            continue
        n = d.get('normalization')
        if not n:
            continue
        dim_patches += n.get('dimensionPatches', 0)
        approx += n.get('approximatePatches', 0)
        native_intents += n.get('mergedIntents', 0)
        collapsed += n.get('collapsedPatches', 0)
        for kind in KIND_ORDER:
            totals[kind] += n.get(JSON_KEY[kind], 0)
        for dim, cnt in (n.get('carrierSpread') or {}).items():
            spread.setdefault(dim, set())
        for c in n.get('cells', []):
            key = (c['dimension'], c['carrier'], c['assetType'])
            cells[key] += c['patches']
            kind_of[key] = c['kind']
            forms[key] = c.get('nativeForm') or c.get('precondition') or ''
            spread.setdefault(c['dimension'], set()).add(c['carrier'])

    if dim_patches == 0:
        print('这些报告里没有 normalization 节 —— 请用新版 CLI 重跑。')
        return

    print('=== 各维度的实现熵（载体数越多 = 同一意图被越多写法实现）===\n')
    for dim, cars in sorted(spread.items(), key=lambda kv: -len(kv[1])):
        print(f'  {dim:12s} {len(cars)} 种载体')

    print('\n=== 各结论的格子（按条数）===\n')
    for kind in KIND_ORDER:
        rows = [(k, v) for k, v in cells.items() if kind_of[k] == kind and v]
        if not rows:
            continue
        print(f'--- {KIND_LABEL[kind]}：{sum(v for _, v in rows)} 条，{len(rows)} 格 ---')
        for (dim, car, at), v in sorted(rows, key=lambda kv: -kv[1])[:15]:
            form = f'  -> {forms[(dim, car, at)]}' if forms[(dim, car, at)] else ''
            print(f'  {v:5d}  {dim} × {car} × {at}{form}')
        print()

    tot = sum(totals.values())
    if native_intents:
        print()
        print(f'★ 真·合并：{collapsed + native_intents} 条补丁 -> {native_intents} 个最简形式'
              f'（压掉 {collapsed} 条）')
    print()
    print('=== 汇总 ===')
    for kind in KIND_ORDER:
        v = totals[kind]
        print(f'{KIND_LABEL[kind]:22s} {v:5d} / {tot}（{v * 100 // max(tot, 1)}%）')
    if approx:
        print(f'\n⚠ 其中收敛会丢语义：{approx} 条（条件带了本维度之外的东西，只能标「近似」）')


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1:])
