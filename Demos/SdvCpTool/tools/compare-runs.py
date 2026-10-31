#!/usr/bin/env python3
"""比**两轮全量转换**的报告（逐包、逐补丁）—— 回答「这次改动到底动了哪些判定」。

**为什么要有它**：2026-10-11 晚我用一段临时脚本比 `prof33`/`prof34`，脚本里做了个
字符串替换改路径，**结果两边的路径被替换成了同一个**，于是「36 包逐项相同」——**假绿**；
真正的差异（26 条 FTM 觅食被新守卫拦下、`Data/Locations.json` 掉了 220 KB）是**另一项**
逐字节树比才兜住的。所以：

  · 这里是**独立的脚本**，不再每次手搓（手搓就会替换错）；
  · 一进来就 **assert 两个根不是同一个**（含大小写/软链接）；
  · 顺带把「比了什么、哪些字段被抹掉」打出来（`generatedAt` 与 `tree` 路径是每轮必然不同的）。

⚠ 报告只说明"判定/写没写"，**不代表产出树相同** —— 两样都要比（树用
`compare-trees-by-hash.py`）。

用法：
    python tools/compare-runs.py <A 轮根> <B 轮根>
    python tools/compare-runs.py D:\\tmp\\prof33 D:\\tmp\\prof36 --max-diff 5
"""
import argparse
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# 每轮必然不同的字段：时间戳，以及**产出树路径**（两次跑的输出根不同）
MASK = {'generatedAt'}
SKIP_TOP = {'tree'}


def norm(o):
    if isinstance(o, dict):
        return {k: ('<t>' if k in MASK else norm(v)) for k, v in o.items() if k not in SKIP_TOP}
    if isinstance(o, list):
        return [norm(x) for x in o]
    return o


def packs(root):
    out = {}
    for name in sorted(os.listdir(root)):
        rp = os.path.join(root, name, 'report.json')
        if os.path.isfile(rp):
            out[name] = rp
    return out


def key_of(patch):
    """补丁的身份：**来源文件 + 序号 + 动作 + 目标**。

    ⚠ **不能用 `zip` 按位置配对**：同一份 `content.json` 里一次改动完全可能让补丁**条数**变
    （枚举展开、条件块合并…），条数一变，位置配对从那一行起全部**错配** —— 报出来的
    "A 是这条、B 是那条"根本对不上（2026-10-12 我自己踩过：临时脚本用 zip，把两条无关的补丁
    配成一对，差点得出"某改动害了它"的错结论）。
    """
    return (str(patch.get('source')), patch.get('index'), patch.get('action'), str(patch.get('target')))


def pair_patches(ra, rb):
    """→ (差异对列表, B 独有数, A 独有数, 变好数, 变坏数)。

    配对按 <see cref="key_of"/>；同一个键出现多次时按出现顺序成对（同名重复补丁是常态）。
    差异对里某一边是 <c>None</c> = 那一边没有这条。
    """
    ia, ib = {}, {}
    for p in ra.get('patches', []):
        ia.setdefault(key_of(p), []).append(p)
    for p in rb.get('patches', []):
        ib.setdefault(key_of(p), []).append(p)

    diffs, added, removed, better, worse = [], 0, 0, 0, 0
    for k, group_b in ib.items():
        group_a = ia.get(k, [])
        for i, y in enumerate(group_b):
            x = group_a[i] if i < len(group_a) else None
            if x is None:
                added += 1
                diffs.append((None, y))
                continue
            if x == y:
                continue
            diffs.append((x, y))
            was, now = x.get('written') is True, y.get('written') is True
            if now and not was:
                better += 1
            elif was and not now:
                worse += 1
    for k, group_a in ia.items():
        if k in ib:
            continue
        removed += len(group_a)
        for x in group_a:
            diffs.append((x, None))
    return diffs, added, removed, better, worse


def main(argv):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('a')
    ap.add_argument('b')
    ap.add_argument('--max-diff', type=int, default=8, help='每包最多列几条补丁差异')
    args = ap.parse_args(argv[1:])

    a, b = os.path.realpath(args.a), os.path.realpath(args.b)
    if a.lower() == b.lower():
        print(f'✗ 两边是**同一个目录**：{a}\n  这比的是自己跟自己 —— 别拿它下结论。')
        return 2
    for root in (a, b):
        if not os.path.isdir(root):
            print(f'✗ 目录不存在：{root}')
            return 2

    pa, pb = packs(a), packs(b)
    print(f'A = {a}（{len(pa)} 包）')
    print(f'B = {b}（{len(pb)} 包）')
    print(f'抹掉的字段：{sorted(MASK)} + 顶层 {sorted(SKIP_TOP)}（每轮必然不同）')

    same = 0
    total_added = total_removed = total_better = total_worse = 0
    only_a = sorted(set(pa) - set(pb))
    only_b = sorted(set(pb) - set(pa))
    for name in sorted(set(pa) & set(pb)):
        ra = norm(json.load(open(pa[name], encoding='utf-8')))
        rb = norm(json.load(open(pb[name], encoding='utf-8')))
        if ra == rb:
            same += 1
            continue
        top = sorted(k for k in set(ra) | set(rb) if ra.get(k) != rb.get(k))
        diffs, added, removed, better, worse = pair_patches(ra, rb)
        total_added += added
        total_removed += removed
        total_better += better
        total_worse += worse
        print(f'\n★ {name}：顶层不同的键 {top}，补丁不同 {len(diffs)} 条'
              + (f'（A 独有 {removed} / B 独有 {added}）' if added or removed else '')
              + (f'，其中**变好 {better} / 变坏 {worse}**' if better or worse else ''))
        for x, y in diffs[:args.max_diff]:
            print('    %s %s' % ((y or x).get('action'), (y or x).get('target')))
            print('      A: %s' % ('——（这条只在 B 有）' if x is None else
                                   '%-20s written=%s  %s' % (x.get('verdict'), x.get('written'),
                                                             (x.get('reason') or '')[:80])))
            print('      B: %s' % ('——（这条只在 A 有）' if y is None else
                                   '%-20s written=%s  %s' % (y.get('verdict'), y.get('written'),
                                                             (y.get('reason') or '')[:80])))
        if len(diffs) > args.max_diff:
            print(f'    …另有 {len(diffs) - args.max_diff} 条')

    print(f'\n⇒ 报告：相同 {same} 包 / 有差异 {len(set(pa) & set(pb)) - same} 包'
          f'（只在 A：{len(only_a)}，只在 B：{len(only_b)}）')
    print(f'   补丁级：**变好 {total_better} 条 / 变坏 {total_worse} 条 / 新增 {total_added} 条 / 消失 {total_removed} 条**')
    for n in only_a[:5]:
        print('   只在 A:', n)
    for n in only_b[:5]:
        print('   只在 B:', n)
    print('⚠ 报告相同**不代表**产出树相同 —— 树要用 tools/compare-trees-by-hash.py 再比一遍。')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
