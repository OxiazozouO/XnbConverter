#!/usr/bin/env python3
"""产出里 `[LocalizedText <资产>:<键>]` 的引用，目标键都在吗？

**为什么要它**：1.6 的显示名/描述可以写成
`[LocalizedText Strings\\Objects:Grape_Name]` 指向另一份资产；**键不存在 ⇒ 玩家看到的是
那段方括号或裸键**。跟地图里的 `Message "键"`（见 `check-map-messages.py`）是同族问题：
「引用了一份没写进去的文本」。

## ⚠ 两个都踩过的坑（第一版两个都犯了，报出一堆假阳性）

1. **资产名里带反斜杠，别把它塞进正则的字符类** —— 字符类里的反斜杠极易被各层转义吃掉。
   用排除式 `[^\\]\\s]+`（不是 `]` 也不是空白即可）。
2. **必须扫「解析后的值」，不能扫原始文本** —— JSON 里反斜杠是**双写**的，
   拿原文做 `\\`→`/` 替换会得到 `Strings//1_6_Strings` 这种**假名字**，于是把
   `Strings\\1_6_Strings`（正确引用、树里确实有 `Strings/1_6_Strings`）误报成"取不到"。
   实测：改成扫解析值之后，全量产出从"一堆假阳性"变成**只有 1 处**。

## 基准

**必须带原版树对照**：原版自己就该是 0（实测 0）。
产出里若有新增，先按上面两条自查一遍工具，再怀疑产出。

用法：
    python tools/check-localized-text.py <产出树> [<基准树>]
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

REF = re.compile(r'\[LocalizedText\s+([^\]\s]+)\s*:\s*([A-Za-z0-9_.]+)\s*\]')


def collect(node, out):
    if isinstance(node, dict):
        for v in node.values():
            collect(v, out)
    elif isinstance(node, list):
        for v in node:
            collect(v, out)
    elif isinstance(node, str):
        out.append(node)
    return out


def load_keys(tree, asset):
    """`Strings\\X` 与 `Strings/X` 都归一成后者。"""
    rel = asset.replace(chr(92), '/')
    path = os.path.join(tree, *rel.split('/')) + '.json'
    if not os.path.isfile(path):
        return None
    try:
        return set(json.load(open(path, encoding='utf-8-sig')).keys())
    except Exception:
        return None


def scan(tree):
    """→ {取不到的 (资产,键): {含该引用的文件}}"""
    cache = {}
    missing = collections.defaultdict(set)
    n = 0
    for dp, _dirs, fns in os.walk(os.path.join(tree, 'Data')):
        for f in fns:
            if not f.endswith('.json'):
                continue
            try:
                parsed = json.load(open(os.path.join(dp, f), encoding='utf-8-sig'))
            except Exception:
                continue
            for text in collect(parsed, []):          # ← 扫**解出的值**
                for m in REF.finditer(text):
                    n += 1
                    asset, key = m.group(1).replace(chr(92), '/'), m.group(2)
                    if asset not in cache:
                        cache[asset] = load_keys(tree, asset)
                    keys = cache[asset]
                    if keys is None or key not in keys:
                        missing[(asset, key)].add(f)
    print(f'{tree}\n  LocalizedText 引用 {n} 处；**取不到文本的 {len(missing)} 种**')
    return missing


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    mine = scan(sys.argv[1])
    base = scan(sys.argv[2]) if len(sys.argv) > 2 else {}
    new = {k: v for k, v in mine.items() if k not in base}
    print(f'  基准 {len(base)} 种，**产出新增 {len(new)} 种**')
    for (asset, key), files in sorted(mine.items(), key=lambda kv: -len(kv[1]))[:25]:
        mark = '★新增' if (asset, key) in new else ' 基准已有'
        print(f'    {mark}  {asset}:{key:38} ← {",".join(sorted(files)[:3])}')
    return 0 if not new else 1


if __name__ == '__main__':
    sys.exit(main())
