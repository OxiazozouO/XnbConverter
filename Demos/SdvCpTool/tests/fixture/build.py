#!/usr/bin/env python3
"""
构建 SdvCpTool 的快速夹具并运行（开发期回归用）。

用法：
    python tests/fixture/build.py [--sdv-tree D:\\sdv_tree] [--run]

默认只构建到 tests/fixture/run/（已被 .gitignore 忽略），加 --run 则紧接着跑一次。

为什么要有它：全量 RSV 端到端要处理 1664 条补丁 + 7594 个文件，约 12 分钟。
本夹具压到 16 条补丁 + 7 个资产，**约 7 秒**，且每条补丁覆盖一个机制，
同时充当一张回归覆盖表（含 2 条负例）。

注意：夹具需要的**原版资产从本地的星露谷解包树复制**，不随仓库分发
（避免把游戏资源提交进仓库）。仓库里只有我们自己的 pack 与这个脚本。
"""
import argparse
import glob
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PACK_SRC = os.path.join(HERE, 'pack')          # 我们自己的，已入库
RUN = os.path.join(HERE, 'run')                # 生成物，已 gitignore

# 夹具树需要从原版解包树复制的资产（挑小的、且覆盖各种落点）
TREE_ASSETS = [
    'Data/Festivals/spring13.json',      # _spouse 键改写 / 节日对话
    'Data/mail.json',                    # 键即条件（<season>_<day>）
    'Data/Locations.json',               # Condition 字段类落点
    'Strings/StringsFromCSFiles.json',   # i18n 语言后缀
    'Characters/Dialogue/Abigail.json',  # $query 对话内联
    'Data/Events/Town.json',             # 事件键即条件（键含 /）
    'Characters/Grandpa.png',            # 图片资产：提供 Texture2DReader 的 .config 模板
]
# 图片 Load 的源文件从这里挑一张（最小的 png）
PNG_SOURCE_GLOBS = ['LooseSprites/*.png', 'Characters/*.png', 'Animals/*.png']


def build(sdv_tree: str) -> None:
    pack = os.path.join(RUN, 'pack')
    tree = os.path.join(RUN, 'tree')
    mods = os.path.join(RUN, 'mods')
    if os.path.exists(RUN):
        shutil.rmtree(RUN)

    # 1) 我们自己的 pack 原样复制
    shutil.copytree(PACK_SRC, pack)
    assets = os.path.join(pack, 'assets')
    os.makedirs(assets, exist_ok=True)

    # 2) 从原版解包树复制资产（json/png + 同名 .config）
    missing = []
    for rel in TREE_ASSETS:
        src = os.path.join(sdv_tree, rel.replace('/', os.sep))
        if not os.path.isfile(src):
            missing.append(rel)
            continue
        dst = os.path.join(tree, rel.replace('/', os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
        cfg = os.path.splitext(src)[0] + '.config'
        if os.path.isfile(cfg):
            shutil.copy2(cfg, os.path.splitext(dst)[0] + '.config')
    if missing:
        print('警告：解包树里没有这些资产，相关补丁会失败：', missing, file=sys.stderr)

    # 3) 图片 Load 的源：挑一张最小的 png
    png = None
    for pat in PNG_SOURCE_GLOBS:
        hits = sorted(glob.glob(os.path.join(sdv_tree, pat.replace('/', os.sep))),
                      key=os.path.getsize)
        if hits:
            png = hits[0]
            break
    if png:
        shutil.copy2(png, os.path.join(assets, 'icon.png'))
    else:
        print('警告：找不到可用作 icon.png 的源图，09 号补丁会失败', file=sys.stderr)

    # 4) 假 mod：供 --mods 与命名空间令牌烘焙（SomeToggle=true / AnotherToggle=false）
    fake = os.path.join(mods, 'FixtureMod')
    os.makedirs(fake, exist_ok=True)
    json.dump({"Name": "Fixture Mod", "Author": "fixture",
               "Version": "1.0.0", "UniqueID": "SdvCpTool.FixtureMod"},
              open(os.path.join(fake, 'manifest.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=2)
    json.dump({"SomeToggle": True, "AnotherToggle": False},
              open(os.path.join(fake, 'config.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=2)

    print(f'夹具已构建：{RUN}')


def run(sdv_tree: str) -> int:
    repo = os.path.abspath(os.path.join(HERE, '..', '..'))
    out = os.path.join(RUN, 'out')
    if os.path.exists(out):
        shutil.rmtree(out)
    content_hashes = os.path.join(os.path.dirname(sdv_tree), 'Stardew Valley',
                                  'Content', 'ContentHashes.json')
    cmd = ['dotnet', 'run', '--project', os.path.join(repo, 'src', 'SdvCpTool.Cli'), '--',
           'convert',
           '--pack', os.path.join(RUN, 'pack'),
           '--tree', os.path.join(RUN, 'tree'),
           '--mods', os.path.join(RUN, 'mods'),
           '--out', out,
           '--report', os.path.join(out, 'report.json')]
    if os.path.isfile(content_hashes):
        cmd += ['--content-hashes', content_hashes]
    rc = subprocess.call(cmd, cwd=repo)

    rep = os.path.join(out, 'report.json')
    if os.path.isfile(rep):
        d = json.load(open(rep, encoding='utf-8'))
        print(f"\n=== 夹具结果 ===")
        print(f"total={d['total']} convertible={d['convertible']} "
              f"written={d['written']} writeFailed={d['writeFailed']}")
        for k, v in sorted(d['summary'].items(), key=lambda x: -x[1]):
            if v:
                print(f'  {v:4d}  {k}')
    return rc


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--sdv-tree', default=r'D:\sdv_tree',
                    help='星露谷 Content 解包树（默认 D:\\sdv_tree）')
    ap.add_argument('--run', action='store_true', help='构建后立即运行一次')
    a = ap.parse_args()
    build(a.sdv_tree)
    return run(a.sdv_tree) if a.run else 0


if __name__ == '__main__':
    sys.exit(main())
