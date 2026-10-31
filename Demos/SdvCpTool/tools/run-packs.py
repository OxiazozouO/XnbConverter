#!/usr/bin/env python3
"""跑指定的几个 CP 包（默认用编译好的 exe），**保留产出树**供实地查看。

为什么不用 `corpus-b-survey.py`：那个跑全部 32 包、每包跑完即删树（省盘）。
本轮要「把残留的 13 处未展开令牌**在产出里定位到具体文件/字段**」，
必须先有树才能看。

用法：
    python tools/run-packs.py D:\\tmp\\t49 谢恩 SVE
    python tools/run-packs.py D:\\tmp\\t49 --all      # 全部 32 包（等同普查，但留树）

第一个参数是输出根目录，其余是包名的**子串**（不区分大小写，匹配 Mods 下目录名）。
产物：<out>/<slug>/report.json 与 <out>/<slug>/tree/
"""
import glob
import json
import os
import shutil
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


def find_cp_packs():
    packs = []
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
        if framework.startswith('pathoschild.contentpatcher') or framework == 'esca.farmtypemanager':
            packs.append((d.get('Name') or os.path.basename(os.path.dirname(m)), os.path.dirname(m)))
    return sorted(set(packs), key=lambda x: x[0])


def slug(s):
    return (''.join(c if (c.isalnum() or c in '-_') else '_' for c in s)[:48]) or 'pack'


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    out_root, want = sys.argv[1], sys.argv[2:]
    packs = find_cp_packs()
    if want != ['--all']:
        packs = [p for p in packs if any(w.lower() in p[0].lower() or w.lower() in p[1].lower() for w in want)]
    print(f'将要跑 {len(packs)} 个包 → {out_root}', flush=True)
    os.makedirs(out_root, exist_ok=True)

    for i, (name, path) in enumerate(packs, 1):
        d = os.path.join(out_root, slug(name))
        os.makedirs(d, exist_ok=True)
        rep = os.path.join(d, 'report.json')
        cmd = [EXE, 'convert', '--pack', path, '--tree', TREE, '--mods', MODS,
               '--out', os.path.join(d, 'tree'), '--report', rep]
        if os.path.isfile(HASHES):
            cmd += ['--content-hashes', HASHES]
        print(f'[{i}/{len(packs)}] {name}', flush=True)
        t0 = time.time()
        r = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True,
                           encoding='utf-8', errors='replace', timeout=1800)
        print(f'      退出码={r.returncode} 用时={time.time()-t0:.0f}s', flush=True)
        if (r.stderr or '').strip():
            print('      stderr:', r.stderr.strip()[-500:], flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
