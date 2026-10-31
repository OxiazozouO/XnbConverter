#!/usr/bin/env python3
"""把**整棵产出树**打成可安装的 XNB 目录 —— 装机流程的最后一步（`how-to-build-an-install.md` §3）。

与 `pack-check.py` 的分工：那个只打**相对原版改过/新增**的文件（用来回答"这批新写出来的东西
打不打得了"），这个打**整棵树**（用来产出真正要装的那份 Content）。整树约 13k 文件、二十多分钟。

用法：
    python tools/pack-tree.py <产出树> [--out <输出目录>] [--packer <打包器>] [--no-dedupe]

`--out` 默认 `<产出树>_xnb`。打包后会按内容哈希把相同文件改成**硬链接**
（保留所有资产名，只省磁盘；不同文件系统/无权限时自动退回原样）。
"""

import argparse
import collections
import hashlib
import os
import shutil
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# ⚠ **别用 `out_publish\XnbConverter.Cli.exe`**：它是 2026-10-05 的发布版，**早于**
#   「波形库直接吃 .ogg」那几次提交 —— 拿它打这棵树会**静默丢掉整个音频**：
#   它读不了注入进去的 `.ogg`（报 `无效的 WAV 格式：OggS，应为 RIFF`），
#   `Wave Bank.xwb` 根本不生成、`Sound Bank.xsb` 还是原版那份（我们的 cue 全没了），
#   而汇总行仍然写着「成功 N 失败 0」（实测踩过，整树 6488/0 但有 1 条 ERROR）。
#
#   要重新构建的那份 —— 但它**不能直接从 `bin\Release\net8.0` 跑**：那里缺替身程序集与数据 dll，
#   打真资产会全灭（实测 0/6488，报错还长得像"JSON 格式不对"）。先跑 `tools/stage-packer.py`。
DEFAULT_PACKER = r'D:\tmp\packer\XnbConverter.Cli.exe'


def hardlink_duplicates(out):
    """把内容相同的产出文件改成硬链接。

    资产名不同 ⇒ 打包器仍会各写一份 XNB；很多是同一张图/同一份 JSON
    （原版语言变体、同一 NPC 的 vendor/portrait、共享空白占位图）。
    硬链接保留所有路径、内容不变，只省磁盘；失败就保留原文件。
    """
    by_size = collections.defaultdict(list)
    for root, _, files in os.walk(out):
        for name in files:
            path = os.path.join(root, name)
            try:
                size = os.path.getsize(path)
            except OSError:
                continue
            if size > 0:
                by_size[size].append(path)

    linked = 0
    saved = 0
    for size, paths in by_size.items():
        if len(paths) < 2:
            continue
        by_hash = collections.defaultdict(list)
        for path in paths:
            h = hashlib.sha256()
            try:
                with open(path, 'rb') as f:
                    for chunk in iter(lambda: f.read(1 << 20), b''):
                        h.update(chunk)
            except OSError:
                continue
            by_hash[h.digest()].append(path)
        for group in by_hash.values():
            if len(group) < 2:
                continue
            keep = group[0]
            for dup in group[1:]:
                tmp = dup + '.dedupe-tmp'
                try:
                    os.link(keep, tmp)
                    os.replace(tmp, dup)
                    linked += 1
                    saved += size
                except OSError:
                    try:
                        os.remove(tmp)
                    except OSError:
                        pass
    return linked, saved


def main(argv):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('tree')
    ap.add_argument('--out')
    ap.add_argument('--packer', default=DEFAULT_PACKER)
    ap.add_argument('--no-dedupe', action='store_true',
                    help='不把内容相同的产出文件硬链接（默认会链接，省磁盘）')
    args = ap.parse_args(argv[1:])

    if not os.path.isdir(args.tree):
        print(f'产出树不存在：{args.tree}')
        return 2
    if not os.path.isfile(args.packer):
        print(f'打包器不存在：{args.packer}')
        return 2

    out = args.out or (args.tree.rstrip('\\/') + '_xnb')
    # ⚠ 输出目录**必须先存在**（打包器自己不会建），而且不能落在输入树里面
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out, exist_ok=True)
    if os.path.abspath(out).startswith(os.path.abspath(args.tree) + os.sep):
        print('输出目录不能是输入树的子目录')
        return 2

    # ⚠ `cwd` 必须是**打包器自己的目录**：它按相对路径 `.\.config\custom dll\` 找模型程序集
    #   （`StardewValley.GameData.dll` 等）；从别处跑会失败，报的是「请检查程序集 …」。
    r = subprocess.run([args.packer, 'pack', '-c', '-i', args.tree, '-o', out],
                       capture_output=True, encoding='gbk', errors='replace',
                       cwd=os.path.dirname(args.packer), timeout=7200)

    lines = (r.stdout or '').strip().splitlines()
    errors = [l for l in lines if '[ERROR]' in l]
    for line in errors[:20]:
        print('  ', line)
    if len(errors) > 20:
        print(f'   …… 另有 {len(errors) - 20} 条错误')
    # ⚠ 汇总行（`成功: N 失败: M`）**只有走过 ProcessFiles 的那几类文件才打**
    #   （`.config`/`.json`/`.png`/…）；只含 XACT 的树走的是另一条分支，压根没有汇总行。
    #   读不到就把打包器 stdout 的尾巴打出来，别让人以为它没跑。
    tail = [l for l in lines if l.startswith('成功') or l.startswith('失败')]
    if tail:
        print('  '.join(tail))
    else:
        print(f'（没读到汇总行，退出码 {r.returncode}）stdout 末尾：')
        for line in [l for l in lines if l.strip()][-5:]:
            print('  ', line)

    count = sum(len(files) for _, _, files in os.walk(out))
    if not args.no_dedupe:
        linked, saved = hardlink_duplicates(out)
        if linked:
            print(f'重复文件硬链接：{linked} 个（省 {saved / 1048576:.1f} MB）')
    print(f'产出：{out}（{count} 个文件）')
    return 1 if errors or r.returncode != 0 else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
