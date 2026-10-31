#!/usr/bin/env python3
"""**报告自洽性**：把 `report.json` 当数据看，核几条必须成立的不变量。

为什么要有它：所有「改动前后对比」都是在这些数字上做的 —— 报告自己不自洽，
比出来的差就不知道是谁的。这个脚本**只读**，不改任何东西。

核这几条（都用 JSON 里的字段重算，不信工具算好的那个数）：
  ① `total` == `patches` 的条数
  ② `total` == `summary` 各档之和
  ③ `written` == `written is True` 的条数；`writeFailed` == `written is False` 的条数
  ④ `blocked` == `total` − 可合并各档 − `groupEmitted` 的条数
  ⑤ 每条的 `verdict` 要么是判定档、要么是被发射覆盖而改写的 `Emitted`

用法：
    python tools/report-invariants.py <含 report.json 的目录> [<目录> …]
"""
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# `Verdict` 枚举里的档 + `ReportWriter` 对「被集合发射覆盖」写出去的那一个字符串。
VERDICTS = {
    'Converted', 'ConvertedNoCondition', 'NeedsReview', 'NotApplicable', 'CreatesNewAsset',
    'NoLandingPoint', 'NonTranslatableCondition', 'NeedsRuntime', 'LocalEdit',
    'TargetContainsTokens', 'TargetMissing', 'UnsupportedAction', 'Emitted',
}
CONVERTIBLE = {
    'Converted', 'ConvertedNoCondition', 'NeedsReview', 'CreatesNewAsset', 'NotApplicable',
}


def check(path):
    r = json.load(open(path, encoding='utf-8'))
    patches = r.get('patches') or []
    problems = []

    if r.get('total') != len(patches):
        problems.append(f"total={r.get('total')} 但 patches 有 {len(patches)} 条")

    summary = r.get('summary') or {}
    if r.get('total') != sum(summary.values()):
        problems.append(f"total={r.get('total')} 但 summary 之和={sum(summary.values())}")

    unknown = sorted({p.get('verdict') for p in patches} - VERDICTS)
    if unknown:
        problems.append(f"出现不认识的 verdict：{unknown}")

    w = sum(1 for p in patches if p.get('written') is True)
    f = sum(1 for p in patches if p.get('written') is False)
    if r.get('written') != w:
        problems.append(f"written={r.get('written')} 但标志位为真的 {w} 条")
    if r.get('writeFailed') != f:
        problems.append(f"writeFailed={r.get('writeFailed')} 但标志位为假的 {f} 条")

    emitted = sum(1 for p in patches if p.get('groupEmitted'))
    convertible = sum(v for k, v in summary.items() if k in CONVERTIBLE)
    expected_blocked = (r.get('total') or 0) - convertible - emitted
    if r.get('blocked') != expected_blocked:
        problems.append(
            f"blocked={r.get('blocked')} 但重算是 {expected_blocked}"
            f"（total {r.get('total')} − 可合并 {convertible} − 已发射 {emitted}）")

    return problems


def main():
    roots = sys.argv[1:]
    if not roots:
        print(__doc__)
        return 2

    bad = total = 0
    for root in roots:
        for path in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
            total += 1
            problems = check(path)
            if problems:
                bad += 1
                print(f'✗ {os.path.basename(os.path.dirname(path))[:40]}')
                for p in problems:
                    print(f'    {p}')
    print(f'\n核了 {total} 份报告，不自洽的 {bad} 份')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
