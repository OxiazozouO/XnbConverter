#!/usr/bin/env python3
"""比较两轮语料里**真正写到的资产集合**（而不是补丁条数）—— 区分「内容变化」与「记账变化」。

**为什么要有它**：把一条多目标补丁拆成 N 条之后，报告里的 `written` 会**多出 N-1 条**，
但那可能只是**同一批内容换了个记法**。要判断是不是真多写了东西，只能比
「(包, 资产) 有没有被写到」这个集合。

用法：
    python tools/written-assets.py <A目录> <B目录>
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def written(root):
    out = set()
    for path in glob.glob(os.path.join(root, '*', 'report.json')):
        data = json.load(open(path, encoding='utf-8'))
        # ⚠ **不能用目录名当包名**：两轮的目录命名不同（普查用 `slug()` 转义过，
        #   链式转换用 `NN_<原始名>`）。实测踩过两次 —— 第一次差异全是假的。
        #   报告里本来就带 `pack`（内容包**完整路径**），拿它的目录名做键，两轮才可比。
        pack = os.path.basename(data['pack'])[:18]
        for patch in data['patches']:
            # ⚠ **集合发射（`groupEmitted`）也要算**：那一条自己没有 `written=true`
            #   （产物由发射器直接落盘，如 `spring_<图集>.png` + 地图改指），
            #   只看 `written` 会把这批内容整个漏掉 —— 实测就漏过一次，
            #   差点把「发射成功」报成「+0 资产」。
            if patch.get('written') is not True and not patch.get('groupEmitted'):
                continue
            for target in (patch.get('target') or '').split(','):
                target = target.strip().replace('\\', '/').strip('/')
                if target:
                    out.add((pack, target))
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    a, b = written(sys.argv[1]), written(sys.argv[2])
    print(f'A 写到的 (包, 资产)：{len(a)}')
    print(f'B 写到的 (包, 资产)：{len(b)}')
    print(f'新增 {len(b - a)}，失去 {len(a - b)}')
    for label, diff in (('新增', b - a), ('失去', a - b)):
        if not diff:
            continue
        by_pack = collections.Counter(pack for pack, _ in diff)
        print(f'\n{label}按包：')
        for pack, n in by_pack.most_common(10):
            print(f'  {n:5}  {pack}')
        print(f'{label}样例：')
        for item in sorted(diff)[:8]:
            print('   ', item)
    return 0


if __name__ == '__main__':
    sys.exit(main())
