#!/usr/bin/env python3
"""两棵产出树**逐文件比 sha1**（多进程）。

为什么要它：`diff-trees.py` 也在比树，但它按内容分类差异、是单线程的；
全量语料两棵树的文件数在 25 万量级、体积几十 GB，单线程算哈希要一刻钟以上跑不完。

用法：
    python tools/compare-trees-by-hash.py <树A> <树B> [--ignore report.json]

`--ignore` 可重复，给的是**文件名**（不是路径），比如报告里有 `generatedAt` 时间戳，
两遍跑必然不同，比之前要排除掉。
"""
import hashlib
import os
import sys
from concurrent.futures import ProcessPoolExecutor

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

CHUNK = 1 << 20


def sha1_of(path):
    h = hashlib.sha1()
    with open(path, 'rb') as f:
        while True:
            block = f.read(CHUNK)
            if not block:
                break
            h.update(block)
    return h.hexdigest()


def list_files(root, ignore):
    out = []
    for dp, _dirs, fns in os.walk(root):
        for f in fns:
            if f in ignore:
                continue
            full = os.path.join(dp, f)
            out.append((os.path.relpath(full, root).replace(os.sep, '/'), full))
    return out


def hash_one(item):
    rel, full = item
    try:
        return rel, sha1_of(full)
    except OSError as err:
        return rel, f'ERROR:{err}'


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2

    a_root, b_root = argv[1], argv[2]
    ignore = set()
    i = 3
    while i < len(argv):
        if argv[i] == '--ignore' and i + 1 < len(argv):
            ignore.add(argv[i + 1])
            i += 2
        else:
            i += 1

    for root in (a_root, b_root):
        if not os.path.isdir(root):
            print(f'不是目录：{root}')
            return 2

    files_a = list_files(a_root, ignore)
    files_b = list_files(b_root, ignore)
    print(f'A {len(files_a)} 个文件，B {len(files_b)} 个文件')

    # 只算两边都有的；只在一边的直接列出来
    set_b = {rel for rel, _ in files_b}
    set_a = {rel for rel, _ in files_a}
    only_a = sorted(set_a - set_b)
    only_b = sorted(set_b - set_a)
    common_a = [(rel, full) for rel, full in files_a if rel in set_b]
    common_b = [(rel, full) for rel, full in files_b if rel in set_a]

    workers = min(os.cpu_count() or 4, 16)
    print(f'并行 {workers} 进程算 sha1 …')
    with ProcessPoolExecutor(max_workers=workers) as pool:
        ha = dict(pool.map(hash_one, common_a, chunksize=64))
        hb = dict(pool.map(hash_one, common_b, chunksize=64))

    diff = sorted(rel for rel in ha if ha[rel] != hb[rel])

    print(f'\n只在 A：{len(only_a)}    只在 B：{len(only_b)}    内容不同：{len(diff)}')
    for rel in (only_a[:5] + only_b[:5] + diff[:10]):
        print(f'   {rel[:100]}')

    if not only_a and not only_b and not diff:
        print('\n✅ 两棵树逐文件相同')
        return 0

    print('\n⚠ 有差异（见上）')
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
