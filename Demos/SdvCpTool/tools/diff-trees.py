#!/usr/bin/env python3
"""逐字节对比两棵产出树 —— 「改代码前后必须同参数对比」的现成工具。

用法：
    python tools/diff-trees.py <旧树> <新树> [--show N]

输出：只在旧树里的 / 只在新树里的 / 内容不同的，各给条数与样例。
**同参数**才有意义：两棵树必须是用**同一批包、同一顺序**跑出来的（`tools/chain-packs.py`）。
"""
import hashlib
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def snapshot(root):
    out = {}
    for dp, _dirs, fns in os.walk(root):
        for f in fns:
            path = os.path.join(dp, f)
            rel = os.path.relpath(path, root)
            try:
                out[rel] = hashlib.md5(open(path, 'rb').read()).hexdigest()
            except Exception:
                out[rel] = '<读失败>'
    return out


def main() -> int:
    # ⚠ 别用「过滤掉以 -- 开头的参数」来取位置参数：那样 `--show 20` 里的 `20`
    #   会被当成第一棵树（实测踩过，报出「只有新树有 7594 个文件」这种假结果）。
    args, show, i = [], 20, 1
    while i < len(sys.argv):
        if sys.argv[i] == '--show':
            show = int(sys.argv[i + 1])
            i += 2
            continue
        args.append(sys.argv[i])
        i += 1
    if len(args) < 2:
        print(__doc__)
        return 2

    old, new = args[0], args[1]
    a, b = snapshot(old), snapshot(new)
    only_old = sorted(set(a) - set(b))
    only_new = sorted(set(b) - set(a))
    changed = sorted(k for k in set(a) & set(b) if a[k] != b[k])

    print(f'旧：{old}  （{len(a)} 个文件）')
    print(f'新：{new}  （{len(b)} 个文件）')
    print(f'只有旧树有：{len(only_old)}    只有新树有：{len(only_new)}    内容不同：{len(changed)}')
    for label, items in (('只有旧树有', only_old), ('只有新树有', only_new), ('内容不同', changed)):
        for rel in items[:show]:
            print(f'    {label}  {rel}')
        if len(items) > show:
            print(f'    …（{label} 另 {len(items) - show} 个）')
    return 0 if not (only_old or only_new or changed) else 1


if __name__ == '__main__':
    sys.exit(main())
