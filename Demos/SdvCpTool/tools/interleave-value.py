#!/usr/bin/env python3
"""量化「**判定→写入交织**」能救回多少：判「新建资产带条件」的补丁里，
有多少写的**条目键**被**同包更早的一条无条件补丁**写过。

为什么是这个判据：`$query` 的假分支要「合并前的原有值」。**判定期看不见本次写入**，
所以现在这类补丁一律被拒。若改成「逐条判定、逐条写入」，该键的旧值就有了
（就是那条更早的无条件补丁写的值）—— 但**只有这一种情形**能救：
条件补丁写的键，得真被更早的无条件补丁写过。

用法：python tools/interleave-value.py [报告目录]
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


def changes(path):
    try:
        return jsonc_load(path)['Changes']
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)['Changes']
        except Exception:
            return None


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus2'
    saved = collections.Counter()
    examples = []

    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        changes_cache = {}
        # 先把这个包的所有补丁按出现顺序摊平：(来源文件, 序号, 补丁)
        flat = []
        for f in glob.glob(os.path.join(pack, '**', '*.json'), recursive=True):
            base = os.path.basename(f)
            if base in ('manifest.json', 'config.json') or not f.endswith('.json'):
                continue
            if f not in changes_cache:
                changes_cache[f] = changes(f)
            for i, c in enumerate(changes_cache[f] or []):
                if isinstance(c, dict):
                    flat.append((f, i, c))

        for f, i, c in flat:
            if not c.get('When'):
                continue
            t = str(c.get('Target') or '').split(',')[0].strip().replace(chr(92), '/')
            keys = list((c.get('Entries') or {}).keys())
            if not keys:
                continue
            for key in keys:
                # 同包**更早**的无条件补丁里，有没有写同一个 (资产, 键)
                for f2, i2, c2 in flat:
                    if (f2, i2) >= (f, i):       # 只看更早的
                        break
                    if c2.get('When'):
                        continue
                    if str(c2.get('Target') or '').split(',')[0].strip().replace(chr(92), '/') != t:
                        continue
                    if key in (c2.get('Entries') or {}):
                        saved[(t, key)] += 1
                        if len(examples) < 8:
                            examples.append((os.path.basename(pack)[:18], t[:34], key[:22]))
                        break

    print(f'★ 交织**有可能**救回的 (资产,键)：**{len(saved)}** 个 / {sum(saved.values())} 处')
    print('（上界 —— 还得满足「该键的基础资产里没有值」等其他前提）')
    print('\n样例：')
    for e in examples:
        print(f'  [{e[0]}] {e[1]}  key={e[2]}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
