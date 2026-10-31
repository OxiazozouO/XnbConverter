#!/usr/bin/env python3
"""地图里的 `Message "键"` 动作，在产出的 `Strings/StringsFromMaps` 里都有对应文本吗？

**为什么要它**：SDV 的 `Message` 动作自己给键加 `StringsFromMaps:` 前缀
（所以**地图源码里看不到前缀**，只有 `Message "BlueMoonVineyard.7"`）。
键不在 `Strings/StringsFromMaps` 里 ⇒ 游戏**把键名原样显示**给玩家：

```
Strings\StringsFromMaps:BlueMoonVineyard.7
```

这是用户实机截图报的那一类。它也是「一条坏条目让整条补丁不写」那个 bug 的**度量尺** ——
改前 SVE 一张包的地图上有 **554 种**键取不到文本，改后只剩 3 种。

用法：
    python tools/check-map-messages.py <产出树> [<基准树>]
"""
import collections
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# tbin 里的字符串**没有长度前缀**，抽出来会跟后面的字节粘住
# （`Message "BlueMoonVineyard.7"S`，末尾那个 `S` 是下一条串的首字节）——
# 所以靠**结尾的引号**定界，不要用「取到行尾」那种写法。
MESSAGE = re.compile(rb'Message "([A-Za-z0-9_.]{1,60})"')


def strings_keys(tree):
    out = set()
    d = os.path.join(tree, 'Strings')
    if not os.path.isdir(d):
        return out
    for f in os.listdir(d):
        if f.startswith('StringsFromMaps') and f.endswith('.json'):
            try:
                out |= set(json.load(open(os.path.join(d, f), encoding='utf-8-sig')).keys())
            except Exception:
                pass
    return out


def scan(tree):
    """→ {取不到文本的键: {来源地图}}"""
    keys = strings_keys(tree)
    maps = os.path.join(tree, 'Maps')
    missing = collections.defaultdict(set)
    n = 0
    for dp, _dirs, fns in os.walk(maps):
        for f in fns:
            if not f.endswith('.tbin'):
                continue
            raw = open(os.path.join(dp, f), 'rb').read()
            for m in MESSAGE.finditer(raw):
                n += 1
                key = m.group(1).decode('ascii')
                if key not in keys:
                    missing[key].add(f[:-5])
    print(f'{tree}\n  地图 Message 引用 {n} 处；StringsFromMaps 有 {len(keys)} 个键；'
          f'**取不到文本的 {len(missing)} 种**')
    return missing


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    mine = scan(sys.argv[1])
    base = scan(sys.argv[2]) if len(sys.argv) > 2 else {}
    new = {k: v for k, v in mine.items() if k not in base}
    print(f'  基准 {len(base)} 种，**产出新增 {len(new)} 种**')
    for key, maps in sorted(mine.items(), key=lambda kv: -len(kv[1]))[:25]:
        print(f'    {"★新增" if key in new else " 基准已有"}  {key:34} ← {len(maps)} 张：'
              f'{",".join(sorted(maps)[:4])}')
    return 0 if not new else 1


if __name__ == '__main__':
    sys.exit(main())
