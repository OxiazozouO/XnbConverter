#!/usr/bin/env python3
"""**定点回包**：把一份产出树里**本轮真的写过/改过**的文件挑出来，连 `.config` 一起打个包。

**为什么要它**：整树回包（`xnb-roundtrip-check.py`）一次二十多分钟，而它要回答的往往只是
「**这批新写出来的文件**打不打得了」——而那个问题用**小树**几十秒就能回答
（实测三份文件 3.4 秒）。「产出能不能用」的硬判据是**真的回包一次**，这个脚本让这件事**随时能做**，
不用等到发布前。

**怎么找「改过的」**：拿**输入树**逐文件比 —— 先比大小，再比 sha1。
（不能只靠报告的 `target`：`.config` 边车、发射器直接落盘的 `spring_<图集>.png` 都不在里面。）

用法：
    python tools/pack-check.py <产出树> [--base <输入树>] [--limit N] [--packer <打包器>]

`--base` 省略时默认为 `D:\\sdv_tree`。`--limit` 只挑前 N 个（调试用）。
"""
import argparse
import hashlib
import os
import shutil
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

DEFAULT_TREE = r'D:\sdv_tree'
# ⚠ **别用 `out_publish\XnbConverter.Cli.exe`**：它是 2026-10-05 的发布版，**早于**
#   「波形库直接吃 .ogg」那几次提交 —— 拿它打这棵树会**静默丢掉整个音频**：
#   它读不了注入进去的 `.ogg`（报 `无效的 WAV 格式：OggS，应为 RIFF`），
#   `Wave Bank.xwb` 根本不生成、`Sound Bank.xsb` 还是原版那份（我们的 cue 全没了），
#   而汇总行仍然写着「成功 N 失败 0」（实测踩过，整树 6488/0 但有 1 条 ERROR）。
#
#   要重新构建的那份 —— 但它**不能直接从 `bin\Release\net8.0` 跑**：那里缺替身程序集与数据 dll，
#   打真资产会全灭（实测 0/6488，报错还长得像"JSON 格式不对"）。先跑 `tools/stage-packer.py`。
DEFAULT_PACKER = r'D:\tmp\packer\XnbConverter.Cli.exe'

# 产出树里**不是资产**的文件：转换器会把新建的 .json 资产补进这份哈希清单，
# 游戏是**直接读 JSON** 的，不进 XNB。它每轮都「改过」，打不进包，所以从「改过的文件」里剔掉
# —— 否则每个包都多算 1 个文件，还稳定带一条「缺 .config」的噪音
#（实测：只改过它的包会显示「新增/改过 1 个文件 … 缺 .config 的 1 个」）。
NON_ASSETS = {'ContentHashes.json'}


def changed_files(out_root, base_root):
    """产出树里相对输入树**新增或改过**的文件（相对路径，正斜杠）。"""
    changed = []
    for dirpath, dirnames, filenames in os.walk(out_root):
        for name in filenames:
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, out_root).replace('\\', '/')
            if rel in NON_ASSETS:
                continue
            other = os.path.join(base_root, rel.replace('/', os.sep))
            if not os.path.isfile(other):
                changed.append(rel)
                continue
            if os.path.getsize(full) != os.path.getsize(other) or digest(full) != digest(other):
                changed.append(rel)
    return sorted(changed)


def digest(path):
    h = hashlib.sha1()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument('tree')
    ap.add_argument('--base', default=DEFAULT_TREE)
    ap.add_argument('--limit', type=int, default=0)
    ap.add_argument('--packer', default=DEFAULT_PACKER)
    ap.add_argument('--keep', action='store_true', help='保留临时小树，便于人工查看')
    ap.add_argument('--chunk', type=int, default=0,
                    help='每批最多这么多个文件，0=一次打完。大包（SVE 近 3000 个文件）分批打'
                         '是为了**分段落盘、看得见进度**（小包一批几秒，SVE 那种大包一批几分钟），'
                         '不是为了绕开打不了 —— 一次打完本来也没问题')
    args = ap.parse_args()

    if not os.path.isdir(args.tree):
        print(f'产出树不存在：{args.tree}')
        return 2
    if not os.path.isdir(args.base):
        print(f'输入树不存在：{args.base}')
        return 2
    if not os.path.isfile(args.packer):
        print(f'打包器不存在：{args.packer}')
        return 2

    changed = changed_files(args.tree, args.base)
    print(f'产出相对输入：新增/改过 {len(changed)} 个文件')
    if args.limit:
        changed = changed[:args.limit]

    # 打包要 `.config` 认类型：把每个改过文件的**同目录同名 .config** 一起带上
    want = set(changed)
    for rel in changed:
        stem, _ = os.path.splitext(rel)
        want.add(stem + '.config')
    want = sorted(want)

    mini = args.tree.rstrip('\\/') + '_mini'
    out = args.tree.rstrip('\\/') + '_mini_xnb'
    missing_config = []
    for rel in want:
        src = os.path.join(args.tree, rel.replace('/', os.sep))
        if not os.path.isfile(src) and rel.endswith('.config'):
            missing_config.append(rel)

    print(f'小树：{len(want) - len(missing_config)} 个文件（含同名 .config）；'
          f'缺 .config 的 {len(missing_config)} 个' + (f'，例如 {missing_config[:3]}' if missing_config else ''))

    # ⚠ 分批**必须按资产切**，不能按文件下标切：`.json` 与它的 `.config` 被切到两批，
    #   打包器就没有类型描述符 ⇒ 报**假失败**（实测：同一棵小树按 150 个一批切成 2 批，
    #   报「失败 2」，而不分批是「失败 0」）。
    by_stem: dict[str, list[str]] = {}
    for rel in want:
        by_stem.setdefault(os.path.splitext(rel)[0], []).append(rel)

    batches: list[list[str]] = []
    current: list[str] = []
    for stem in sorted(by_stem):
        group = sorted(by_stem[stem])
        if current and len(current) + len(group) > args.chunk:
            batches.append(current)
            current = []
        current += group
    if current:
        batches.append(current)
    if not args.chunk:
        batches = [want]
    if not batches:                     # 一个改过的文件都没有（空树）
        print('成功: 0  失败: 0')
        return 0
    ok_total = bad_total = 0
    for index, batch in enumerate(batches, 1):
        shutil.rmtree(mini, ignore_errors=True)
        shutil.rmtree(out, ignore_errors=True)
        os.makedirs(out, exist_ok=True)
        for rel in batch:
            src = os.path.join(args.tree, rel.replace('/', os.sep))
            if not os.path.isfile(src):
                continue
            dst = os.path.join(mini, rel.replace('/', os.sep))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy(src, dst)

        r = subprocess.run([args.packer, 'pack', '-c', '-i', mini, '-o', out],
                           capture_output=True, encoding='gbk', errors='replace',
                           cwd=os.path.dirname(args.packer), timeout=3600)
        tail = (r.stdout or '').strip().splitlines()
        ok = next((l for l in tail if l.startswith('成功')), '成功: 0')
        bad = next((l for l in tail if l.startswith('失败')), '失败: 0')
        # 「成功/失败」数的是**产出**，一批的失败文件不会出现在产出里 —— 两批相加即总数。
        ok_total += int(ok.split(':')[1])
        bad_total += int(bad.split(':')[1])
        if len(batches) > 1:
            print(f'  退出码 {r.returncode}  批 {index}/{len(batches)}（{len(batch)} 个文件）｜{ok}｜{bad}')
        if r.returncode != 0:
            for line in tail[-4:]:
                print('    ', line)
    # 汇总行**以「成功」开头**：`verify-packs.py` 就是按这个前缀取数的。
    # ⚠ **单批也要打**：否则 `verify-packs.py` 只看得到带缩进的批次行，取不到数、显示「?」
    #   （实测：只改过一个非资产文件的包就是这样）。
    print(f'成功: {ok_total}  失败: {bad_total}')
    if not args.keep:
        shutil.rmtree(mini, ignore_errors=True)
        shutil.rmtree(out, ignore_errors=True)
    return 1 if bad_total else 0


if __name__ == '__main__':
    sys.exit(main())
