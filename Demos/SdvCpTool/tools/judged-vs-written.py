#!/usr/bin/env python3
"""两个方向的一致性检查（判定 vs 结局）：

  ① **判定说可写、实际没写** —— 判定不准，伪装成「写入失败」（`tools/write-note.py` 专查）
  ② **判定说不可写、实际却写成功了** —— 判定**过严**，白丢覆盖

② 平时看不出来（报告里 `written: true` 就完了），要专门对一遍才知道有没有。

用法：python tools/judged-vs-written.py [报告目录]
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b17'
    strict = collections.Counter()      # 判不可写却写成功
    loose = collections.Counter()       # 判可写却没写
    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        for x in d['patches']:
            v = x['verdict']
            wrote = x.get('written') is True or x.get('groupEmitted')
            if v == 'Emitted':
                # 报告已如实标成「集合发射产出」的，本就不该出现在任一方向里
                continue
            if v in ('Converted', 'ConvertedNoCondition'):
                if not wrote:
                    loose[v] += 1
            else:
                # ⚠ 口径必须与报告自己的 `BlockedCount` 一致：
                #   `CreatesNewAsset` / `NeedsReview` / `NotApplicable` 都**算可合并**
                #   （见 `AnalysisReport.ConvertibleCount`），把它们当「不可写」会虚报问题。
                #   真正的「blocked」只有下面这几档。
                blocked = ('LocalEdit', 'NoLandingPoint', 'TargetMissing',
                           'TargetContainsTokens', 'NeedsRuntime')
                if v in blocked and wrote:
                    strict[(v, bool(x.get('groupEmitted')))] += 1

    print('① 判可写却没写：', sum(loose.values()), dict(loose))
    print('② ★ 判**不可写**却写成功了（判定过严 / 白丢覆盖）：', sum(strict.values()))
    for k, n in strict.most_common():
        tag = '（集合发射产出）' if k[1] else '（**直接写入**）'
        print(f'   {n:5}  {k[0]} {tag}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
