#!/usr/bin/env python3
"""量化：被判「`$query` 没有无条件补丁提供原有值」的补丁里，
有多少的键**原版资产里本来就有**（⇒ 旧值的来源本可以是基础资产，不必非要一条无条件补丁）。

背景：`DialogueQueryWriter.TryRewrite` 在**判定阶段**要求「同一键上至少一条无条件补丁」，
理由是「要取到合并前的原有值」。但写入阶段的 `PriorString(target, key)` 读的是**合并前
的目标数据**——**包括基础资产本身**。⇒ 判定比写入严了一档，把本来写得出去的补丁判成了人工。

用法：python tools/prior-in-base.py [报告目录] [--tree D:\sdv_tree]
"""
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402


def changes(path):
    try:
        return jsonc_load(path)['Changes']
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)['Changes']
        except Exception:
            return None


def main():
    rest = sys.argv[1:]
    root = next((a for a in rest if not a.startswith('--')), r'D:\tmp\corpus_b13')
    tree = rest[rest.index('--tree') + 1] if '--tree' in rest else r'D:\sdv_tree'

    cache = {}
    base_cache = {}
    hit = miss = nofile = 0
    examples = []

    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        for x in d['patches']:
            r = x.get('reason') or ''
            if '没有无条件补丁' not in r:
                continue
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = changes(src)
            ch = cache[src]
            i = x.get('index', -1)
            c = ch[i] if ch and i < len(ch) else None
            if not isinstance(c, dict):
                continue

            target = str(c.get('Target') or '').replace(chr(92), '/')
            if target not in base_cache:
                bp = os.path.join(tree, target.replace('/', os.sep) + '.json')
                if os.path.isfile(bp):
                    try:
                        base_cache[target] = json.load(open(bp, encoding='utf-8-sig'))
                    except Exception:
                        base_cache[target] = None
                else:
                    base_cache[target] = None
            base = base_cache[target]

            for key in (c.get('Entries') or {}):
                if base is None:
                    nofile += 1
                    continue
                v = base.get(key) if isinstance(base, dict) else None
                if isinstance(v, str) and v.strip():
                    hit += 1
                    if len(examples) < 8:
                        examples.append((os.path.basename(pack)[:18], target[:34], key[:24], v[:40]))
                else:
                    miss += 1

    total = hit + miss + nofile
    print(f'「没有无条件补丁」的补丁，按条目键统计：共 {total} 个键')
    print(f'  ★ 键**原版资产里就有**（旧值本来取得到）：**{hit}**')
    print(f'    原版里没有这个键：{miss}')
    print(f'    原版没有这份资产：{nofile}')
    print('\n样例（键 / 原版值）：')
    for e in examples:
        print(f'  [{e[0]}] {e[1]}  {e[2]}  = {e[3]}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
