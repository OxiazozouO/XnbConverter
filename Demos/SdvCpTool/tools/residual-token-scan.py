#!/usr/bin/env python3
"""扫产出树里的**残留 CP 令牌**（`{{…}}`，排除通道 B1 的 `{{i18n:…}}`）。

**为什么要有它**：报告里的 `unexpandedTokens` 自检是**按资产类型**走的（只认 `.json`），
而这里是「不管什么来源、把产出树整个翻一遍」的**兜底**核对 —— 两者不一致就说明有内容
绕过了自检（历史上真出现过：i18n 实参里的令牌一路漏到产出）。

用法：python tools/residual-token-scan.py <产出树> [<产出树> ...]

⚠ **已知的假阳性**：游戏自己的字符串里就会写 `{{0}…}`（`${{0}을(를)…}$` 这种，
韩文 `Strings/StringsFromCSFiles` 里有两处）—— 那是占位符语法、不是 CP 令牌。
实测语料里残留报的就是这 2 处，改前改后都在，**不是我们写漏的**。
"""
import collections
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

PATTERN = re.compile(r'\{\{(?!i18n)')


def scan(tree):
    hits = collections.Counter()
    files = 0
    for dp, dn, fn in os.walk(tree):
        for f in fn:
            if not f.endswith('.json'):
                continue
            files += 1
            path = os.path.join(dp, f)
            text = open(path, encoding='utf-8', errors='replace').read()
            n = len(PATTERN.findall(text))
            if n:
                hits[os.path.relpath(path, tree).replace('\\', '/')] = n
    return files, hits


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    grand = 0
    for tree in sys.argv[1:]:
        if not os.path.isdir(tree):
            print(f'不是目录：{tree}')
            continue
        files, hits = scan(tree)
        n = sum(hits.values())
        grand += n
        label = os.path.basename(os.path.dirname(tree.rstrip('\\/'))) or tree
        print(f'{label[:34]:36} json={files:5}  残留 `{{{{` = {n}')
        for path, count in hits.most_common(6):
            print(f'      {count:4}  {path}')
    print('合计残留', grand)
    return 0


if __name__ == '__main__':
    sys.exit(main())
