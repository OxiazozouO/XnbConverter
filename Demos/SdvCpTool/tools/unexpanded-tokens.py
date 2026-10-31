#!/usr/bin/env python3
"""汇总报告里的 **未展开令牌** 自检（`unexpandedTokens`）—— 产出的字符串里还留着 `{{…}}`。

**这一类是 2026-10-07 才发现的**：给 `SpriteSheetValidator` 补上 `Data/Characters` 的
`Appearance[]` 引用检查后，它报出 `Sprite: "Characters/Claire_{{Outfit}}"` —— 顺着查才发现。

分两档（口径见 `Validating/UnexpandedTokenAudit.cs`）：
  · **harmful** —— 在 `Condition`/`Sprite`/`Texture`/事件键… 里 ⇒ 行为会变（GSQ 非法 / 取不到图）；
  · **identifierOnly** —— 只在 `Id`/`Name` 这类**不透明标识**里 ⇒ 不影响行为。

用法：python tools/unexpanded-tokens.py [报告目录]
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b19'
    packs = 0
    harmful = ident = 0
    tokens = collections.Counter()
    fields = collections.Counter()
    assets = collections.Counter()
    worst = []

    for p in sorted(glob.glob(os.path.join(root, '*', 'report.json'))):
        d = json.load(open(p, encoding='utf-8'))
        t = d.get('unexpandedTokens')
        if not t or not (t.get('harmful') or t.get('identifierOnly')):
            continue
        packs += 1
        harmful += t.get('harmful', 0)
        ident += t.get('identifierOnly', 0)
        for f in t.get('findings', []):
            (tokens if not f['identifierOnly'] else collections.Counter())[f['token']] += 1
            fields[(f['field'] if not f['identifierOnly'] else '（标识字段）')] += 1
            assets[f['asset'].split('/')[0] + '/' + (f['asset'].split('/')[1] if '/' in f['asset'] else '')] += 1
        worst.append((t.get('harmful', 0), os.path.basename(d['pack'])[:34]))

    print(f'32 包中有未展开令牌的：**{packs}** 包')
    print(f'  ⚠ 影响行为（harmful）：**{harmful}**    仅标识（identifierOnly）：{ident}')
    print('\n按令牌名（前 12，只算 harmful）：')
    for k, v in tokens.most_common(12):
        print(f'  {v:5}  {{{{{k}}}}}')
    print('\n按字段（前 10）：')
    for k, v in fields.most_common(10):
        print(f'  {v:5}  {k}')
    print('\n按资产（前 8）：')
    for k, v in assets.most_common(8):
        print(f'  {v:5}  {k}')
    print('\n按包（前 8）：')
    for n, name in sorted(worst, reverse=True)[:8]:
        print(f'  {n:5}  {name}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
