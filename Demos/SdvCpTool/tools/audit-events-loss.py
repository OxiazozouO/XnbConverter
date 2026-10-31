"""`Data/Events/*` 的补丁：写了多少、拒了多少、拒绝的缘由是什么。

事件键**本身就是条件槽**（`<id>/<前置条件>/<脚本>`），所以这一类我们本来就能落地
（见 EventKeyRewriter）—— 那"未量化"的到底是哪一部分，量一下。
"""
import collections
import glob
import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

roots = [(r'D:\tmp\chain5', '改后')]
for root, label in roots:
    ds = sorted(glob.glob(os.path.join(root, '*')), key=lambda p: int(os.path.basename(p).split('_')[0]))
    total = collections.Counter()
    reasons = collections.Counter()
    written = 0
    for d in ds:
        rp = os.path.join(d, 'report.json')
        if not os.path.isfile(rp):
            continue
        r = json.load(open(rp, encoding='utf-8'))
        for x in r['patches']:
            t = str(x['target'])
            if not t.lower().startswith(('data/events', 'data\\events')):
                continue
            total[x['verdict']] += 1
            if x.get('written'):
                written += 1
            elif x['verdict'] in ('NeedsReview', 'NoLandingPoint', 'LocalEdit', 'NonTranslatableCondition'):
                reasons[(x['verdict'], (x.get('reason') or '')[:70])] += 1
    print(f'== {label}  Data/Events/* 补丁 {sum(total.values())} 条，已写 {written}')
    for k, v in total.most_common(10):
        print(f'   {v:5d}  {k}')
    print('   被拒的缘由（前 8）：')
    for (verdict, why), v in reasons.most_common(8):
        print(f'      {v:4d}  [{verdict}] {why}')
