#!/usr/bin/env python3
"""回包抽查：把转换产出树喂给 XnbConverter 打成 XNB，报**哪些资产打不出来**。

为什么要有它：「产出能用吗」的唯一硬判据是**真的回包一次**。本轮就靠它抓到两类
前几道自检结构上都够不着的问题（同一对象内仅大小写不同的键、`Vector2` 写成字符串）。

**不集成进每次转换** —— 整树一次十几分钟，只适合发布前抽查。

用法：
    python tools/xnb-roundtrip-check.py <产出树目录> [--packer <XnbConverter.Cli.exe>]

⚠ 两条踩过的坑（都写在这里免得下次再踩）：
  1. 打包器 `Program.Parse` 末尾要求「输入输出在调用时都**必须真实存在**」——
     **输出目录得先建好**，否则报「错误命令-o [必填]输出的文件夹」，看不出是这条要求。
  2. `Parse` 按 `" -"`（空格+减号）切分选项，所以**路径里不能出现「空格+减号」**
     （Steam 的 mod 名里很常见 `---`，所以这里**只对产出树**跑，不对 mod 目录跑）。

★ **不要用 junction 把产出树挂进打包器目录** —— 那是往 `D:\XnbConverter` 里写东西。
  打包器的依赖按 **exe 所在目录**解析（.NET app base），与 cwd 无关，
  所以**绝对路径 + cwd 设成 exe 目录**就够了。
"""
import os
import shutil
import subprocess
import sys

DEFAULT_PACKER = r'D:\XnbConverter\out_publish\XnbConverter.Cli.exe'


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if not args:
        print(__doc__)
        return 2

    tree = os.path.abspath(args[0])
    packer = DEFAULT_PACKER
    if '--packer' in sys.argv:
        packer = sys.argv[sys.argv.index('--packer') + 1]

    if not os.path.isdir(tree):
        print(f'产出树不存在：{tree}')
        return 2
    if not os.path.isfile(packer):
        print(f'打包器不存在：{packer}')
        return 2
    if ' -' in tree:
        print('产出树路径里含「空格+减号」，打包器的选项切分会吃错 —— 换个路径。')
        return 2

    out = tree.rstrip('\\/') + '_xnb'
    # 坑 1：输出目录必须先存在。
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out, exist_ok=True)

    cwd = os.path.dirname(packer)
    r = subprocess.run([packer, f'pack -c -i {tree} -o {out}'],
                       capture_output=True, encoding='gbk', errors='replace',
                       timeout=7200, cwd=cwd)

    text = r.stdout or ''
    for line in text.splitlines():
        s = line.strip()
        if '成功' in s or '失败' in s:
            print(s)
        elif '[ERROR]' in s or 'XNB.Encode' in s or 'Path ' in s:
            print('  ', s[:200])

    # 逐资产核对：输入树里有 `.json`/`.png`/`.tbin` 而输出里没有对应 `.xnb` 的，就是没打出来。
    missing = []
    for root, _, files in os.walk(tree):
        for f in files:
            stem, ext = os.path.splitext(f)
            if ext.lower() not in ('.json', '.png', '.tbin'):
                continue
            if '.' in stem:            # 本地化变体（`X.de-DE.json`）与主资产同形，不单列
                continue
            rel = os.path.relpath(os.path.join(root, stem + '.xnb'), tree)
            if not os.path.isfile(os.path.join(out, rel)):
                missing.append(rel.replace('\\', '/'))

    print(f'\n输入 {tree}')
    print(f'输出 {out}')
    print(f'**打不出 .xnb 的资产：{len(missing)} 个**')
    for m in sorted(missing)[:40]:
        print('  ', m)
    if len(missing) > 40:
        print(f'   ... 其余 {len(missing) - 40} 个见输出目录')
    return 0 if not missing else 1


if __name__ == '__main__':
    sys.exit(main())
