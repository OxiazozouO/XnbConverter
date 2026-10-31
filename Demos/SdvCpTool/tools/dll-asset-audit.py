#!/usr/bin/env python3
"""审计：**带 DLL 的模组**在运行时**写入**了哪些**原版**资产。

为什么要它：CP 内容包只描述"数据怎么改"，而带 DLL 的模组能在运行时用 C# 改资产。
纯 XNB 场景里没有 DLL ⇒ 这些写入复现不了，是一条**能力边界**，得先量清楚有多少。

用法：
    1) 先把带 DLL 的模组反编译到某个目录（每个模组一个子目录，内含 *.cs）
    2) python tools/dll-asset-audit.py <反编译根目录>

判据（有意做得**保守**，宁可少报）：
  · 只看资产名形状像原始资产路径的字符串字面量（`Data/…`、`Maps/…`、`LooseSprites/…` 等
    顶层目录之一，且用 `/` 分隔）；
  · 只算**写入**：同一行或**前 3 行**里出现
    `EditData / Edit( / EditImage / PatchMap / ApplyMapOverride / ReplaceWith /
     LoadFromModFile / AddWarps / SetProperties / SetIndex / AsMap( / AsDictionary /
     AsImage / CopyFrom / RemoveWhen / PatchMapTile / InvalidateCache`；
  · **排除 `Mods/…`**（模组自有资产，不是原版的）。

⚠ 已知的偏保守处：`e.Name.IsEquivalentTo("X")` 这种写法里，读和写长得一样 ——
  本脚本靠"前后文有没有写入方法名"区分。实测 `LooseSprites/Cursors` ×4 全是
  `OnAssetReady` / `Load<Texture2D>` 的**读**，不算。所以本脚本报的是**下界**。
"""
import collections
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

TOPS = (r'(?:Data|Maps|LooseSprites|Characters|Strings|TileSheets|Buildings|Animals|'
        r'TerrainFeatures|Weapons|Objects|BigCraftables|Cursors|Fonts|DayTiles|NightTiles|'
        r'Effects|Portraits|Minigames|Menus|Movies|Music|Sound|Sky|Mods)')
ASSET = re.compile(r'"((?:%s)/[^"\n]{1,120})"' % TOPS)

WRITE_CTX = re.compile(
    r'\b(EditData|Edit\s*\(|EditImage|PatchMap|ApplyMapOverride|ReplaceWith|'
    r'LoadFromModFile|AddWarps|SetProperties|SetIndex|AsMap\s*\(|AsDictionary|'
    r'AsImage|CopyFrom|RemoveWhen|PatchMapTile|InvalidateCache)\b')
READ_CTX = re.compile(
    r'\b(Load<|Load\s*\(|ContainsKey|TryGetValue|GetValue|DoesAssetExist|AssetExists|'
    r'GetData|Contains)\b')


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    root = sys.argv[1]
    if not os.path.isdir(root):
        print(f'目录不存在：{root}')
        return 2

    written = collections.Counter()
    by_mod = collections.defaultdict(set)

    for mod in sorted(os.listdir(root)):
        mod_dir = os.path.join(root, mod)
        if not os.path.isdir(mod_dir):
            continue
        for dp, _, files in os.walk(mod_dir):
            for fn in files:
                if not fn.endswith('.cs'):
                    continue
                lines = open(os.path.join(dp, fn), encoding='utf-8',
                             errors='replace').read().splitlines()
                for i, line in enumerate(lines):
                    for m in ASSET.finditer(line):
                        asset = m.group(1)
                        if asset.startswith('Mods/'):       # 模组自有资产
                            continue
                        ctx = line if WRITE_CTX.search(line) else ' '.join(lines[max(0, i - 3):i + 1])
                        if not WRITE_CTX.search(ctx) or READ_CTX.search(line):
                            continue
                        written[asset] += 1
                        by_mod[asset].add(mod)

    print(f'{root}：被 DLL **写入**的原版资产 {len(written)} 个')
    for asset, n in written.most_common():
        print(f'  {n:2}×  {asset:52} ← {", ".join(sorted(by_mod[asset]))}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
