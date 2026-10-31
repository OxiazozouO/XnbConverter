#!/usr/bin/env python3
"""**全语料逐包回包**：一个包转完就打、打完就删树，一行一个包。

`verify-packs.py` 是「一次转完再挨个打」—— 32 个包的产出树同时留在盘上约 19 GB，
这台机器只剩 9.6 GB 时跑不动。这个脚本把两件事**交错**做，峰值只占一个包
（实测 SVE 那种大包几百 MB~2 GB）。

用法：
    python tools/roundtrip-all.py                # 全部 32 包
    python tools/roundtrip-all.py SVE 聚宝盆      # 只跑名字含这些子串的
    python tools/roundtrip-all.py --keep         # 保留产出树（调试用）
"""
import glob
import json
import os
import shutil
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
WORK = os.environ.get('ROUNDTRIP_OUT') or r'D:\tmp\roundtrip_all'

sys.path.insert(0, HERE)
import cppacks  # noqa: E402  （CP 包清单与 corpus-b-survey.py 共用一份）


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    keep = '--keep' in sys.argv

    # ⚠ 只跑**本工具真的能转的内容包**（CP + FTM）：
    #   Mods 目录里还有 SMAPI 自带包、SaveBackup 之类，它们没有 content.json，
    #   跑一遍只会得到一行「转换没出报告」的噪音（实测踩过）。
    #   FTM 是后加的通道，别漏（漏了不报错）。
    packs = [path for _, path in cppacks.find_cp_packs() + cppacks.find_ftm_packs()]
    if args:
        packs = [p for p in packs if any(a.lower() in os.path.basename(p).lower() for a in args)]
    if not packs:
        print('没有匹配的包')
        return 2

    os.makedirs(WORK, exist_ok=True)
    total_ok = total_bad = 0
    rows = []
    for i, pack in enumerate(packs, 1):
        out = os.path.join(WORK, 'one')
        shutil.rmtree(out, ignore_errors=True)
        r = subprocess.run([sys.executable, os.path.join(HERE, 'run-packs.py'), out, pack],
                           cwd=REPO, capture_output=True, text=True, encoding='utf-8',
                           errors='replace', timeout=5400)
        reports = glob.glob(os.path.join(out, '*', 'report.json'))
        if not reports:
            rows.append(f'{pack[:36]:38} 转换没出报告（退出码 {r.returncode}）')
            print(rows[-1], flush=True)
            continue
        report = reports[0]
        tree = os.path.join(os.path.dirname(report), 'tree')
        name = os.path.basename(json.load(open(report, encoding='utf-8'))['pack'])[:36]

        check = subprocess.run([sys.executable, os.path.join(HERE, 'pack-check.py'), tree,
                                '--chunk', os.environ.get('PACK_CHECK_CHUNK', '500')],
                               cwd=REPO, capture_output=True, text=True, encoding='utf-8',
                               errors='replace', timeout=7200)
        lines = [l.strip() for l in (check.stdout or '').splitlines() if l.strip()]
        ok = next((l for l in reversed(lines) if l.startswith('成功')), '?')
        changed = next((l for l in lines if l.startswith('产出相对输入')), '')
        if ok != '?':
            parts = ok.replace('成功:', '').replace('失败:', '').split()
            total_ok += int(parts[0])
            total_bad += int(parts[1])
        rows.append(f'[{i}/{len(packs)}] {name:38} {changed:24} {ok}')
        print(rows[-1], flush=True)

        if not keep:
            shutil.rmtree(tree, ignore_errors=True)          # 峰值只占一个包
            shutil.rmtree(tree + '_mini', ignore_errors=True)
            shutil.rmtree(tree + '_mini_xnb', ignore_errors=True)

    print(f'\n合计  成功: {total_ok}  失败: {total_bad}')
    open(os.path.join(WORK, 'rows.txt'), 'w', encoding='utf-8').write('\n'.join(rows))
    return 1 if total_bad else 0


if __name__ == '__main__':
    sys.exit(main())
