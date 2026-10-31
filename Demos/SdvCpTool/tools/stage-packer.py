#!/usr/bin/env python3
"""把**重建的**打包器凑成一份**跑得起来**的目录（默认 `D:\\tmp\\packer`）—— 整树回包的前置步骤。

**为什么需要这一步**：`Demos\\XnbConverter.Cli\\bin\\Release\\net8.0` 那份是 framework-dependent 的，
直接拿来打真资产会**全灭**，而且报错很不像话 —— 它不说"缺 dll"，而是把每一个 `.config` 都失败成

    无效的JSON文件格式: The type initializer for 'XnbConverter.Readers.TypeReadHelper' threw an exception.

（实测整树 `成功: 0 失败: 6488`）。缺的是两样**运行时**的东西，都不在 bin 里：

1. `xna-shim\\MonoGame.Framework.dll` —— 替身程序集。Cli 的 csproj 有一条
   `DropXnaShimFromOutputRoot`，会把随构建传递到输出**根目录**的那份删掉（留着会被默认探测抢先命中，
   真的 MonoGame 就轮不上），可它没再往 `xna-shim\\` 子目录补一份（Core 自己的输出里有），
   于是 `RootPath.LoadBuiltinShim` 找不到它；
2. `.config\\custom dll\\{StardewValley.GameData,StardewValley,xTile}.dll` —— 数据模型程序集。
   打包器只按 **cwd** 找 `.config\\custom dll\\`，而 bin 下那份是它启动时自建的**空目录**。

（`out_publish` 那份没这两个毛病 —— 单文件自包含，替身和数据 dll 都在 exe 里 —— 但它**旧**，
不认后加的 `.ogg`，见 `pack-tree.py` 顶部的说明。所以这里补的不是"新打包器更好"，是"新打包器要配齐"。）

构建也顺带带上：`global.json` 钉的是 SDK `8.0.0`，本机没装 8.x，所以**必须从仓库外的目录**跑
`dotnet build`（那时那份 global.json 不生效，10.x 编 net8.0 一样能出活）。

用法：
    python tools/stage-packer.py [--dest <目录>] [--no-build]

产出：`<dest>\\XnbConverter.Cli.exe`，即 `pack-tree.py` / `pack-check.py` 的 `--packer` 默认值。
"""

import argparse
import os
import shutil
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

XNB = r'D:\XnbConverter'
BUILD_OUT = os.path.join(XNB, 'Demos', 'XnbConverter.Cli', 'bin', 'Release', 'net8.0')
PROJECT = os.path.join(XNB, 'Demos', 'XnbConverter.Cli', 'XnbConverter.Cli.csproj')
SHIM = os.path.join(XNB, 'XnbConverter.XnaShim', 'bin', 'Release', 'net8.0', 'MonoGame.Framework.dll')
DATA_DLLS = os.path.join(XNB, '.config', 'custom dll')
DATA_DLL_NAMES = ('StardewValley.GameData.dll', 'StardewValley.dll', 'xTile.dll')
DEFAULT_DEST = r'D:\tmp\packer'


def main(argv):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--dest', default=DEFAULT_DEST)
    ap.add_argument('--no-build', action='store_true', help='跳过 dotnet build，只重新拷')
    args = ap.parse_args(argv[1:])

    dest = args.dest
    os.makedirs(dest, exist_ok=True)

    if not args.no_build:
        # ⚠ cwd 用**仓库外**的目录：仓库里的 global.json 钉了没装的 SDK 8.0.0，
        #   在那儿跑 dotnet build 会直接 "SDK not found" 退出（而且退出码不看的话毫无提示）。
        # ⚠ `dotnet build` 的输出是 **UTF-8**（打包器自己是 GBK，别串了）—— 按 GBK 解会看到
        #   `0 涓�璀﹀憡` 那种双重乱码。
        r = subprocess.run(['dotnet', 'build', PROJECT, '-c', 'Release'],
                           capture_output=True, encoding='utf-8', errors='replace',
                           cwd=os.path.dirname(os.path.abspath(dest)), timeout=900)
        for line in (r.stdout or '').strip().splitlines()[-4:]:
            print('  ', line.strip())
        if r.returncode != 0:
            print('构建失败 —— 不往下走（拿旧二进制打出来的结果不算数）')
            return 1

    if not os.path.isfile(os.path.join(BUILD_OUT, 'XnbConverter.Cli.exe')):
        print(f'没找到构建产物：{BUILD_OUT}')
        return 2

    copied = 0
    for name in os.listdir(BUILD_OUT):
        src = os.path.join(BUILD_OUT, name)
        dst = os.path.join(dest, name)
        if os.path.isdir(src):
            if os.path.isdir(dst):
                shutil.rmtree(dst, ignore_errors=True)
            shutil.copytree(src, dst)
            copied += 1
        elif name.endswith(('.exe', '.dll', '.json', '.bat')):
            shutil.copy2(src, dst)
            copied += 1
    # 补 1：替身程序集
    shim_dst = os.path.join(dest, 'xna-shim')
    os.makedirs(shim_dst, exist_ok=True)
    shutil.copy2(SHIM, shim_dst)
    # 补 2：数据模型程序集（打包器按 cwd 找 .config\custom dll）
    dll_dst = os.path.join(dest, '.config', 'custom dll')
    os.makedirs(dll_dst, exist_ok=True)
    for name in DATA_DLL_NAMES:
        shutil.copy2(os.path.join(DATA_DLLS, name), dll_dst)

    print(f'暂存完成：{dest}（拷了 {copied} 项 + xna-shim + custom dll 3 个）')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
