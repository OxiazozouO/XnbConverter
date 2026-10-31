#!/usr/bin/env python3
"""量化「地图没被造出来」的**下游影响**：目标为 `Maps/*` 却被判找不到的补丁，有多少是
因为**同包前面那条 `Load` 的 FromFile 是本工具还不支持的形态**（`.tmx` / `.xnb`）。

用途：给 Task 23（`Load` 的 `.xnb`/`.tmx` 地图）量出真实收益面 ——
任务书里写的是「SVE 剩余 92 条写入失败」，但那**只是链条的第一环**；
真正被卡住的是**后面所有引用这张图的补丁**。

用法：python tools/map-load-chain.py <报告目录>
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402

SUPPORTED = ('.json', '.tbin')


def load_changes(path):
    try:
        return jsonc_load(path)['Changes']
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)['Changes']
        except Exception:
            return None


def main():
    root = sys.argv[1]
    files = sorted(glob.glob(os.path.join(root, '*', 'report.json')))
    if not files:
        print(__doc__)
        return 2

    blocked = collections.Counter()   # 目标 → 被卡的补丁数
    upstream = collections.Counter()  # 上游 Load 的不支持扩展名 → 次数
    explained = 0
    total = 0
    conditional = 0

    for p in files:
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        # 先扫一遍：这个包里哪些 Maps/* 资产的 Load 用了不支持的 FromFile
        unsupported = {}
        cache = {}
        for x in d.get('patches', []):
            if str(x.get('action') or '').lower() != 'load':
                continue
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = load_changes(src)
            ch = cache[src]
            if not ch or x.get('index', -1) >= len(ch):
                continue
            c = ch[x['index']]
            ff = str(c.get('FromFile') or '')
            t = str(c.get('Target') or '')
            if not t.startswith('Maps/'):
                continue
            ext = os.path.splitext(ff)[1].lower()
            if ext and ext not in SUPPORTED:
                unsupported[t] = ext
                upstream[ext] += 1

        for x in d.get('patches', []):
            if x.get('verdict') not in ('TargetMissing', 'CreatesNewAsset'):
                continue
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = load_changes(src)
            ch = cache[src]
            if not ch or x.get('index', -1) >= len(ch):
                continue
            c = ch[x['index']]
            t = str(c.get('Target') or '')
            if not t.startswith('Maps/'):
                continue
            total += 1
            blocked[t] += 1
            if c.get('When'):
                conditional += 1
            if t in unsupported:
                explained += 1

    print(f'目标为 Maps/* 却「找不到资产」的补丁：{total}')
    print(f'  其中同包有「本工具不支持的 Load」（.tmx/.xnb）指向同一目标：**{explained}**')
    print(f'  其中带 When（条件）的：{conditional}')
    print(f'\n上游不支持的 Load 扩展名分布：{dict(upstream)}')
    print('\n按目标（前 15）：')
    for t, n in blocked.most_common(15):
        mark = '  ← 上游是 .tmx/.xnb' if t in unsupported else ''
        print(f'  {n:4}  {t}{mark}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
