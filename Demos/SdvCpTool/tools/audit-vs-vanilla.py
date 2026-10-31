#!/usr/bin/env python3
"""**逐包对原版树**跑那两项"链式跑法里会跳过"的自检，把 findings 汇总出来。

为什么要它：`convert-mods.py` 的链式跑法是「`--tree` 与 `--out` 同一棵」，
于是 `ConditionPlacementAudit`（条件落点在游戏不读的位置）与 `OutputShapeAudit`
（原版是数组、我们写成对象）**无从对照 ⇒ 整体跳过**（报告里 `skippedSameTree: true`，
文档里写明「0 是没比，不是没问题」）。要真跑就得**每包对原版树**单独起一棵产出树。

⚠ 每包一棵新树（原版树 600 MB 上下）⇒ 跑完就删，不然 232 包会吃掉 400+ GB。

用法：
    python tools/audit-vs-vanilla.py [--packs 子串 …] [--keep]
"""
import glob
import io
import json
import os
import shutil
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'
TREE = r'D:\sdv_tree'
REPO = r'D:\SdvCpTool'
HASHES = r'D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json'
EXE = os.environ.get('SDVCP_EXE') or os.path.join(
    REPO, 'src', 'SdvCpTool.Cli', 'bin', 'Release', 'net8.0', 'SdvCpTool.Cli.exe')
OUT = r'D:\tmp\perpack-audit'


def packs(want):
    """候选包：**用仓库自己的口径**（`cppacks`）。

    ⚠ 别在这里自己 glob manifest + `json.load` —— 第一版就是这么写的，结果**漏了 93 个包**
    （清单里带注释/尾逗号的是 JSONC，严格解析会静默跳过；`CapeStardew`、`East Scarp: NPCs`
    这些都在其中）。`cppacks` 用的是项目那份 JSONC 加载器，与 `convert-mods.py` 同源。
    """
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import cppacks
    out = []
    for name, path in cppacks.find_cp_packs() + cppacks.find_ftm_packs():
        if want and not any(w.lower() in name.lower() or w.lower() in path.lower() for w in want):
            continue
        out.append((name, path))
    return sorted(set(out))


def main():
    want = [a for a in sys.argv[1:] if not a.startswith('--')]
    keep = '--keep' in sys.argv
    os.makedirs(OUT, exist_ok=True)
    agg = {}
    total = 0
    start = time.time()
    for i, (name, path) in enumerate(packs(want), 1):
        tree = os.path.join(OUT, 'tree')
        rep = os.path.join(OUT, 'report.json')
        shutil.rmtree(tree, ignore_errors=True)
        cmd = [EXE, 'convert', '--pack', path, '--tree', TREE, '--out', tree,
               '--mods', MODS, '--report', rep, '--content-hashes', HASHES]
        subprocess.run(cmd, capture_output=True, cwd=REPO, timeout=3600)
        try:
            j = json.load(io.open(rep, encoding='utf-8'))
        except Exception:
            print(f'[{i}] {name[:40]:42} 报告没写出（跳过）')
            continue
        rows = []
        # ★ **所有**需要"原版那棵树"作对照的自检都收进来 —— 不只是这两项：
        #   链式跑法（`--tree` 与 `--out` 同一棵）下，凡是"拿产出跟原版比"的**全是空转**
        #   （`assetType` 的 `0 mismatch`、`conditionPlacement`/`outputShape` 的 `skippedSameTree`
        #   都属这一类）。`gameData`/`keyCase`/`spriteSheet` 走的是模型/自洽判据，链式下也有效，
        #   但既然每包都起了一棵树，顺手一起收、免得将来又要重跑一遍。
        for key in ('conditionPlacement', 'outputShape', 'assetType', 'gameData',
                    'keyCase', 'spriteSheet', 'tokenConditions'):
            sect = j.get(key)
            if not isinstance(sect, dict): continue
            if sect.get('skippedSameTree'): rows.append(f'{key}=跳过（同一棵树）')
            for f in (sect.get('findings') or []):
                rows.append(f'{key}: ' + json.dumps(f, ensure_ascii=False)[:160])
        if rows:
            agg[name] = rows
            print(f'[{i}] {name[:40]:42} {len(rows)} 处')
            for r in rows[:6]: print('      ', r)
        total += len(rows)
        if not keep:
            shutil.rmtree(tree, ignore_errors=True)
    io.open(os.path.join(OUT, 'findings.json'), 'w', encoding='utf-8', newline='\n').write(
        json.dumps(agg, ensure_ascii=False, indent=1))
    print(f'\n完成 {len(agg)} 个包有 findings，共 {total} 处；用时 {(time.time()-start)/60:.1f} 分')
    print(f'明细：{os.path.join(OUT, "findings.json")}')


main()
