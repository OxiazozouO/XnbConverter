#!/usr/bin/env python3
"""量化 `DynamicTokens` 的影响面：`Target`/`FromFile` 里含令牌的补丁，
有多少用的令牌是**这个包自己在 `DynamicTokens` 里定义的**（⇒ 有可能静态展开）。

`DynamicTokens` 是「包的配置」：它在 `content.json` 顶层定义，值可以是字面量、
也可以是 `{{Query: …}}`/嵌套令牌。能静态求值的部分展开掉，就能把
`Target: Maps/{{season}}_town` 这类「看着不可解析」的补丁救回来。

用法：python tools/dyntoken-scope.py <CP 包目录> [...]   （不给参数就扫语料 B）
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'
TOKEN = re.compile(r'\{\{\s*([A-Za-z_][A-Za-z0-9_.\-/]*)')


def changes(path):
    try:
        return jsonc_load(path)
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)
        except Exception:
            return None


def main():
    packs = sys.argv[1:]
    if not packs:
        packs = sorted(os.path.dirname(m) for m in glob.glob(os.path.join(MODS, '**', 'manifest.json'), recursive=True))
    stat = collections.Counter()
    names = collections.Counter()
    for pack in packs:
        files = [f for f in glob.glob(os.path.join(pack, '**', '*.json'), recursive=True)
                 if os.path.basename(f) not in ('manifest.json', 'config.json')]
        docs = [(f, changes(f)) for f in files]
        # ⚠ `DynamicTokens` 定义在包的**顶层文档**（通常 content.json），而 `Changes` 可能
        #   在 Include 进来的别的文件里 —— **必须按包收集**，按文件收会漏掉绝大多数。
        #   （第一版就按文件收，量出来是 0，差点得出「没人用 DynamicTokens」的错结论。）
        defined = set()
        for _, d in docs:
            if not isinstance(d, dict):
                continue
            for t in d.get('DynamicTokens') or []:
                if isinstance(t, dict) and t.get('Name'):
                    defined.add(str(t['Name']))
        if not defined:
            continue
        for f, d in docs:
            if not isinstance(d, dict):
                continue
            for c in d.get('Changes') or []:
                if not isinstance(c, dict):
                    continue
                for field in ('Target', 'FromFile'):
                    v = c.get(field)
                    if not isinstance(v, str) or '{{' not in v:
                        continue
                    used = {n for n in TOKEN.findall(v)}
                    known = used & defined
                    other = used - defined
                    if known:
                        stat[('包内定义的令牌', field, tuple(sorted(used))[:3])] += 1
                        for n in known:
                            names[n] += 1
                    elif other:
                        stat[('包外/内置令牌', field, tuple(sorted(other))[:3])] += 1
    print('Target/FromFile 含令牌的补丁：')
    for k, n in stat.most_common(18):
        print(f'  {n:5}  {k[0]:14} {k[1]:9} {k[2]}')
    print('\n用到的**包内定义**令牌名：')
    for n, c in names.most_common(20):
        print(f'  {c:5}  {n}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
