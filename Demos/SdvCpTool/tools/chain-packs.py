#!/usr/bin/env python3
"""**链式转换**：把多个内容包依次转进**同一棵产出树**（后一个包的 `--tree` 是前一个的产出）。

**为什么这可能是对的用法**：很多包互相依赖（实测 SVE 的季节性服装编辑的是 **SVE 本体**的资产）。
逐包各自从原版树出发时，那些资产根本不存在 ⇒ 判 `TargetMissing`；而真实用户的 Mods 目录里
两个包**同时装着**，CP 是在**同一个游戏内容树上**应用它们的。

用法：
    python tools/chain-packs.py <产出根目录> <包名子串1> <包名子串2> ...
    python tools/chain-packs.py D:\\tmp\\chain SVE "季节性服装"

第一个包从 `--tree D:\\sdv_tree` 出发，之后的每个包都从**上一个包的产出树**出发。
"""
import glob
import json
import os
import subprocess
import sys
import time

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'
TREE = r'D:\sdv_tree'
REPO = r'D:\SdvCpTool'
HASHES = r'D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json'
EXE = os.environ.get('SDVCP_EXE') or os.path.join(
    REPO, 'src', 'SdvCpTool.Cli', 'bin', 'Release', 'net8.0', 'SdvCpTool.Cli.exe')


def find_packs(needle):
    out = []
    for m in glob.glob(os.path.join(MODS, '**', 'manifest.json'), recursive=True):
        try:
            d = json.load(open(m, encoding='utf-8-sig'))
        except Exception:
            continue
        if not isinstance(d, dict):
            continue
        # ⚠ 这里收的是「**本工具能转的**内容包」= CP + **FTM**（后者由 `FtmPackTranslator`
        #   在读取时翻成 `Data/Locations` 的补丁）。写死 `ContentPatcher` 会把 4 个 FTM 包
        #   静默漏掉 —— 那种"少算"不报错，最难发现。
        framework = str(d.get('ContentPackFor', {}).get('UniqueID', '')).lower()
        if not (framework.startswith('pathoschild.contentpatcher') or framework == 'esca.farmtypemanager'):
            continue
        name = d.get('Name') or os.path.basename(os.path.dirname(m))
        # ⚠ 只按**包名**匹配，**不按路径**：SVE 的季节性服装是嵌在 SVE 本体目录里的，
        #   按路径匹配会让 `SVE` 这个关键字先命中它（实测踩过：两轮跑的都是同一个包）。
        if needle.lower() in name.lower():
            out.append((name, os.path.dirname(m)))
    return sorted(set(out))


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    out_root, needles = sys.argv[1], sys.argv[2:]
    os.makedirs(out_root, exist_ok=True)

    prev_tree = TREE
    for i, needle in enumerate(needles, 1):
        packs = find_packs(needle)
        if not packs:
            print(f'!! 找不到含「{needle}」的包')
            continue
        name, path = packs[0]
        dst = os.path.join(out_root, f'{i}_{name[:24]}')
        os.makedirs(dst, exist_ok=True)
        out_tree = os.path.join(dst, 'tree')
        report = os.path.join(dst, 'report.json')
        cmd = [EXE, 'convert', '--pack', path, '--tree', prev_tree, '--mods', MODS,
               '--out', out_tree, '--report', report]
        if os.path.isfile(HASHES):
            cmd += ['--content-hashes', HASHES]
        print(f'[{i}] {name}  ← tree={prev_tree}', flush=True)
        t0 = time.time()
        r = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True,
                           encoding='utf-8', errors='replace', timeout=1800)
        print(f'     退出码={r.returncode} 用时={time.time()-t0:.0f}s', flush=True)
        if (r.stderr or '').strip():
            print('     stderr:', r.stderr.strip()[-200:], flush=True)
        if os.path.isfile(report):
            j = json.load(open(report, encoding='utf-8'))
            print(f'     total={j["total"]} written={j["written"]} failed={j["writeFailed"]}', flush=True)
        prev_tree = out_tree
    return 0


if __name__ == '__main__':
    sys.exit(main())
