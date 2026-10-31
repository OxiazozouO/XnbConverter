#!/usr/bin/env python3
"""
`TargetMissing` 里有多少条其实**带条件**？（**负结果工具，别照着它改代码**）

背景：判定链是「先常量折叠 → 再查目标存在」。能静态求值的叶子折掉后整条恒假 ⇒
`NotApplicable`（对）；但含**运行时**令牌（`Random`/`Time`/`FarmType`…）的条件折不掉，
于是走到目标检查，被报成 `TargetMissing`。

★ 这个数看着像「分类错了，该先报条件」，**实测是错的** ——
见 `docs/research/2026-10-09-target-missing-condition-order-negative.md`：
改成先报条件会让 `convertible` 虚涨 323（`NeedsReview` 计入 convertible，而
`TargetMissing`/`LocalEdit`/`NoLandingPoint` 不计入），`written` 一条没变，
而且把 `LocalEdit`（「地图没有条件通道」）这种**更根本**的理由盖成了
「条件翻译不成 GSQ」这种更浅的。**判定链从具体到一般的顺序是对的。**

这个脚本留着，是为了以后再有同样的念头时能一眼看到当初量过的数。

用法：python tools/targetmissing-with-condition.py [语料目录]   （默认最新一轮）
"""
import collections
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cppacks  # noqa: E402
import jsonc  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

ROOT = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_anl_msg'


def patches_in(path):
    """→ [(Target, When 的 JSON 文本 or None)]，文件读不了就返回空。"""
    try:
        d = jsonc.load(path)
    except Exception:
        return []

    def walk(node, out):
        if isinstance(node, dict):
            if 'Target' in node and 'Action' in node:
                out.append((node.get('Target'), node.get('When')))
            for v in node.values():
                walk(v, out)
        elif isinstance(node, list):
            for v in node:
                walk(v, out)

    out = []
    walk(d, out)
    return out


def split_targets(t):
    if isinstance(t, list):
        return [str(x) for x in t]
    return [p.strip() for p in str(t or '').split(',') if p.strip()]


def main():
    packs = {n: p for n, p in cppacks.find_cp_packs()}
    # 报告目录名是 slug（`[CP]` → `__CP_`），用「唯一后缀数字」反查最稳
    by_suffix = {}
    for n, p in packs.items():
        num = n.strip().split('-')[-1].strip()
        by_suffix.setdefault(num, []).append((n, p))

    with_cond = 0
    no_cond = 0
    unresolved = 0
    reasons = collections.Counter()

    for rp in sorted(glob.glob(os.path.join(ROOT, '*', 'report.json'))):
        d = json.load(open(rp, encoding='utf-8'))
        slug = os.path.basename(os.path.dirname(rp))
        num = slug.rstrip('_').split('-')[-1].strip()
        cands = by_suffix.get(num) or []
        # slug 里 `[CP]` → `__CP_`、空格 → `_`；用「去掉非字母数字后前 8 个字符」再筛一次
        pack_root = None
        key = ''.join(ch for ch in slug if ch.isalnum())[:8]
        for n, p in cands:
            if ''.join(ch for ch in n if ch.isalnum())[:8] == key:
                pack_root = p
                break
        if pack_root is None and cands:
            pack_root = cands[0][0] if False else cands[0][1]
        if pack_root is None:
            unresolved += 1
            continue

        for pt in d.get('patches', []):
            if pt.get('verdict') != 'TargetMissing':
                continue
            src = str(pt.get('source') or '')
            tgt = str(pt.get('target') or '')
            full = os.path.join(pack_root, *src.split('/'))
            whens = [w for t, w in patches_in(full) if tgt in split_targets(t)]
            if not whens:
                no_cond += 1
                reasons['(源文件里没找到这条补丁 / 无 When)'] += 1
                continue
            if any(w for w in whens if w):
                with_cond += 1
                w = next(w for w in whens if w)
                reasons[json.dumps(w, ensure_ascii=False)[:90]] += 1
            else:
                no_cond += 1
                reasons['(有补丁但 When 为空)'] += 1

    print(f'包目录没解析出来的报告: {unresolved}')
    print(f'TargetMissing 中**带 When** 的: {with_cond}')
    print(f'TargetMissing 中**不带 When** 的: {no_cond}')
    print()
    for k, n in reasons.most_common(12):
        print(f'{n:4d}  {k}')


main()
