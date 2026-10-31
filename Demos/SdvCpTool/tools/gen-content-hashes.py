#!/usr/bin/env python3
"""按 **Content 目录里实际存在的文件** 生成 `ContentHashes.json` —— 装机流程的最后一步。

**为什么非有它不可**（2026-10-12 实机崩了两次才知道）：

游戏判断「这个资产存在吗」**不看文件系统**，而是查这个文件的**键**
（`StardewValley/LocalizedContentManager.cs::DoesAssetExist`：

    string item = 归一化(assetName) + ".xnb";
    EnsureManifestInitialized();          // 读 <Content>/ContentHashes.json 的键当集合
    return _manifest.Contains(item);

`PlatformEnsureManifestInitialized` 就是 `ContentHashParser.ParseFromFile(<Content>/ContentHashes.json)`，
把**每个键**塞进 `_manifest`）。所以：

- 键**缺了** ⇒ 资产明明在盘上也加载不了 ⇒ `ContentLoadException: Could not load X asset!`
  （实测：`Data/AdditionalFarms` 里 `IconTexture: Mods/DaisyNiko.LavenderMeadows/icon`
   ⇒ 建号菜单一列自定义农场就**整游戏崩**）；
- 键**多了**（列了盘上没有的文件）⇒ `FileNotFoundException: Could not find file …xnb`
  （实测：产出树里那份清单列了打包器**跳过**的资产 ⇒ 另一个崩）。

⚠ 所以清单必须**从最终的 Content 目录扫出来**（覆盖式安装后 = 原版 ∪ 我们的产出），
**不能**用转换阶段在产出树上生成的那份 —— 产出树 ≠ 打包产物 ≠ 最终安装（少了打包器
静默跳过的、多了没能打进包的）。

值与游戏那份一致：**文件字节的大写 hex MD5**（拿原版 3560 条对过，抽 300 条 300/300 相符）。

用法：
    python tools/gen-content-hashes.py <Content 目录>            # 生成/覆盖
    python tools/gen-content-hashes.py <Content 目录> --check     # 只比，不写
"""
import argparse
import hashlib
import io
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def build(root):
    out = {}
    for dp, _, files in os.walk(root):
        for name in files:
            if not name.lower().endswith('.xnb'):
                continue
            full = os.path.join(dp, name)
            rel = os.path.relpath(full, root).replace(os.sep, '/')
            with open(full, 'rb') as handle:
                out[rel] = hashlib.md5(handle.read()).hexdigest().upper()
    return out


def main(argv):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('content', help='Content 目录（覆盖式安装后的那一个）')
    ap.add_argument('--check', action='store_true', help='只比不写')
    args = ap.parse_args(argv[1:])

    if not os.path.isdir(args.content):
        print('Content 目录不存在：%s' % args.content)
        return 2

    fresh = build(args.content)
    target = os.path.join(args.content, 'ContentHashes.json')

    if args.check:
        if not os.path.isfile(target):
            print('没有 %s —— 游戏会把「所有资产都不存在」' % target)
            return 1
        try:
            current = json.load(io.open(target, encoding='utf-8-sig'))
        except Exception as exc:
            print('现有那份解析不了：%s' % exc)
            return 1
        missing = sorted(set(fresh) - set(current))
        stale = sorted(set(current) - set(fresh))
        print('盘上 %d 个 .xnb；清单 %d 条；缺 %d 条、多 %d 条'
              % (len(fresh), len(current), len(missing), len(stale)))
        for name in missing[:5]:
            print('   缺：', name)
        for name in stale[:5]:
            print('   多：', name)
        both = set(fresh) & set(current)
        diff = [k for k in both if current[k].upper() != fresh[k]]
        print('   两边都有但 MD5 不同的：%d' % len(diff))
        return 0 if not (missing or stale or diff) else 1

    with io.open(target, 'w', encoding='utf-8', newline='\n') as handle:
        json.dump(fresh, handle, ensure_ascii=False, separators=(',', ':'))
    print('写入 %s：%d 条' % (target, len(fresh)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
