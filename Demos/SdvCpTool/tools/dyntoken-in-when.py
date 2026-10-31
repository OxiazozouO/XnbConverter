#!/usr/bin/env python3
"""量化 `DynamicTokens` 出现在 **`When` 的键名**里的规模。

和 `dyntoken-scope.py`（只看 Target/FromFile）不是一回事：**条件里才是大头** ——
`DynamicTokens` 的值往往本身就是一句 CP 条件（如 SVE 的
`GuntherUnlocked = "{{HasSeenEvent: currentPlayer, hostPlayer |contains=103042015}}"`），
**展开后可以直接翻译成 GSQ**，比判 `Unknown` 好得多。

用法：python tools/dyntoken-in-when.py [报告目录]
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

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'


def doc(path):
    try:
        return jsonc_load(path)
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)
        except Exception:
            return None


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\tmp\corpus_b10'
    files = sorted(glob.glob(os.path.join(root, '*', 'report.json')))
    if not files:
        print(__doc__)
        return 2

    # 每个包的 DynamicTokens（**按包收**：定义在顶层文档，Changes 可能在其 Include 的文件里）
    pack_tokens = {}
    for m in glob.glob(os.path.join(MODS, '**', 'manifest.json'), recursive=True):
        pack = os.path.dirname(m)
        toks = {}
        for f in glob.glob(os.path.join(pack, '**', '*.json'), recursive=True):
            if os.path.basename(f) == 'manifest.json':
                continue
            d = doc(f)
            if not isinstance(d, dict):
                continue
            for t in d.get('DynamicTokens') or []:
                if isinstance(t, dict) and t.get('Name'):
                    toks[str(t['Name']).lower()] = str(t.get('Value') or '')
        if toks:
            pack_tokens[pack] = toks

    hit = collections.Counter()
    val_is_condition = collections.Counter()
    cache = {}
    total = 0
    for p in files:
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        toks = pack_tokens.get(pack, {})
        if not toks:
            continue
        for x in d.get('patches', []):
            if x.get('written') is True or x.get('groupEmitted'):
                continue
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = (doc(src) or {}).get('Changes') if isinstance(doc(src), dict) else None
            ch = cache[src]
            i = x.get('index', -1)
            c = ch[i] if ch and i < len(ch) else None
            if not isinstance(c, dict):
                continue
            w = c.get('When')
            if not isinstance(w, dict):
                continue
            for k in w:
                base = str(k).split('|')[0].split(':')[0].strip().lower()
                if base in toks:
                    total += 1
                    hit[base] += 1
                    v = toks[base].strip()
                    kind = '值本身是条件表达式' if v.startswith('{{') else '值是字面量'
                    val_is_condition[(base, kind, v[:56])] += 1
                    break

    print(f'未写入的补丁里，`When` 键名命中**包内 DynamicTokens** 的：**{total}** 条')
    print('\n按令牌名（前 15）：')
    for k, n in hit.most_common(15):
        print(f'  {n:5}  {k}')
    print('\n它们的取值形态：')
    for k, n in val_is_condition.most_common(12):
        print(f'  {n:5}  {k[0]:22} {k[1]}  {k[2]}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
