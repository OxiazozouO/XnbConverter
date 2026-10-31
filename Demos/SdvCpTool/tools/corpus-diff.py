#!/usr/bin/env python3
"""逐项对比两轮语料普查（**同参数**：同一个 `--tree`/`--mods`/`--content-hashes`，
只有 exe 不同）—— 改代码前后下结论之前必须先跑这个，别拿记忆里的数字说话。

用法：python tools/corpus-diff.py D:\\tmp\\c8 D:\\tmp\\c9
      （A = 改**前**、B = 改**后**；输出 `A→B` 的逐项差）
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def load(root):
    packs = {}
    for p in glob.glob(os.path.join(root, '*', 'report.json')):
        d = json.load(open(p, encoding='utf-8'))
        packs[os.path.basename(os.path.dirname(p))] = d
    return packs


# 逐条记录里要跟着比的字段（判定/写入的变化要能追到**具体哪条补丁**）
PER_PATCH = ('verdict', 'written', 'channel', 'reason', 'writeNote')


def main():
    a_root, b_root = sys.argv[1], sys.argv[2]
    A, B = load(a_root), load(b_root)
    print(f'A={a_root}  {len(A)} 个包\nB={b_root}  {len(B)} 个包\n')

    only = set(A) ^ set(B)
    if only:
        print('!! 包集合不一致：', sorted(only))

    keys = ['total', 'convertible', 'blocked', 'written', 'writeFailed', 'attemptedWrite']
    print(f'{"指标":16} {"A(改前)":>10} {"B(改后)":>10} {"差":>8}')
    for k in keys:
        a = sum(d.get(k, 0) or 0 for d in A.values())
        b = sum(d.get(k, 0) or 0 for d in B.values())
        print(f'{k:16} {a:10} {b:10} {b-a:+8}')

    verdicts = collections.Counter()
    for d in A.values():
        verdicts.update(d.get('summary') or {})
    verdicts_b = collections.Counter()
    for d in B.values():
        verdicts_b.update(d.get('summary') or {})
    print(f'\n{"判定":24} {"A":>8} {"B":>8} {"差":>8}')
    for k in sorted(set(verdicts) | set(verdicts_b), key=lambda x: -(verdicts[x] + verdicts_b[x])):
        a, b = verdicts[k], verdicts_b[k]
        if a or b:
            print(f'{k:24} {a:8} {b:8} {b-a:+8}')

    # 未展开令牌：处数 + 逐条位置
    ua = sum((d.get('unexpandedTokens') or {}).get('harmful', 0) for d in A.values())
    ub = sum((d.get('unexpandedTokens') or {}).get('harmful', 0) for d in B.values())
    ia = sum((d.get('unexpandedTokens') or {}).get('identifierOnly', 0) for d in A.values())
    ib = sum((d.get('unexpandedTokens') or {}).get('identifierOnly', 0) for d in B.values())
    print(f'\n未展开令牌（影响行为）  A={ua}  B={ub}  {ub-ua:+}')
    print(f'未展开令牌（仅标识字段）A={ia}  B={ib}  {ib-ia:+}')

    def findings(P):
        out = collections.Counter()
        for name, d in P.items():
            for f in (d.get('unexpandedTokens') or {}).get('findings', []):
                if not f['identifierOnly']:
                    out[(f['asset'], f['token'])] += 1
        return out

    fa, fb = findings(A), findings(B)
    if fa or fb:
        print(f'\n{"资产 / 令牌":60} {"A":>5} {"B":>5}')
        for key in sorted(set(fa) | set(fb), key=lambda k: -(fa[k] + fb[k])):
            if fa[key] != fb[key]:
                print(f'{key[0] + " / " + key[1]:60} {fa[key]:5} {fb[key]:5}')

    # 逐条补丁的**判定/写入**变化：这是「到底动了哪些补丁」的唯一精确答案
    moved = collections.Counter()
    samples = collections.defaultdict(list)
    for name in sorted(set(A) & set(B)):
        pa = {p.get('source', '') + '#' + str(p.get('index', i)): p
              for i, p in enumerate(A[name].get('patches', []))}
        pb = {p.get('source', '') + '#' + str(p.get('index', i)): p
              for i, p in enumerate(B[name].get('patches', []))}
        for pid in set(pa) | set(pb):
            x, y = pa.get(pid, {}), pb.get(pid, {})
            for f in PER_PATCH:
                if (x.get(f) is None and y.get(f) is None) or x.get(f) == y.get(f):
                    continue
                key = (f, str(x.get(f))[:40], str(y.get(f))[:40])
                moved[key] += 1
                if len(samples[key]) < 2:
                    samples[key].append(f'{name} :: {x.get("logName") or x.get("displayName") or pid}')
    if moved:
        print('\n逐条补丁的变化（字段 / 旧值 / 新值）：')
        for key, n in moved.most_common():
            print(f'  {n:4}  {key[0]}: {key[1]!r} → {key[2]!r}')
            for s in samples[key]:
                print(f'          e.g. {s}')
    else:
        print('\n逐条补丁无任何变化。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
