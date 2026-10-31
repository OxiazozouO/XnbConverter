#!/usr/bin/env python3
"""扫**产出树**的 `Data/*.json`，把里面所有**贴图引用**抽出来，看它们是不是都指得到东西。

**为什么单独有这个**：`verify-tree.py` 那几项管的是**地图**那一侧（图块、图集、季节变体、Message 文本），
管不到**数据资产里的图片名** —— 物品定义里 `Texture` / `Portrait` / `TextureName` 写错一个字母，
表现是「东西在图鉴里有、画不出来」，而地图检查一切正常。

**判据**：字段名命中 `<见 KEYS>`，值是字符串 ⇒ 记一处引用；按 `/` 归一化后到树里找，
依次试「原样」「加 `.png`/`.xnb`/`.tbin`/`.json`/…」以及**游戏自己的两个前缀规则**
（`Data/Characters` 的 `TextureName` 是相对 `Characters/`、`Portrait` 相对 `Portraits/`）。

**四类要会读**：
- ★ **`找不到`** —— 真悬空，要逐个看是哪来的；
- ○ **`变体缺失（可接受）`** —— 形如 `<名>_Beach`：游戏用 `DoesAssetExist` 保护
  （`NPC.cs:1027/1060`），缺了就穿常服，不是错；
- ◎ **`上游笔误（可接受）`** —— **包自己**写错的名字（见 `UPSTREAM_DANGLING`）。它不在
  "产出比基准多出"里，因为 CP 场景下同一个字符串**原样**交给游戏 ⇒ 两边行为一模一样；
  但**必须逐个在源码里找到出处**，不许拿它当垃圾桶；
- · **跳过** —— 值里有 `{{令牌}}` / `%` / 空格 / 绝对路径（`C:`），本脚本判不了，**不冒充**。

用法：
    python tools/audit-texture-refs.py <产出树> [<基准树>]

**基准树是位置参数**（与仓库里其它 `check-*.py` 一致 —— `verify-tree.py` 就是这么调的）。
给上时两边并排跑 —— **原版自己也有一批"找不到"**（判据比游戏粗），
只有"产出比基准多出来的"才值得查；末尾会把这句单独打一行，好让自检抓数字。
"""
import argparse
import collections
import io
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# 算「贴图引用」的字段名（小写比）
KEYS = {
    'texture', 'portrait', 'texturename', 'image', 'imagesource', 'spritesheet',
    'overlaytexture', 'basecoattexture', 'sleevestexture', 'shirttexture', 'pantstexture',
    'hattexture', 'hairtexture', 'menutexture', 'tilesheet',
}
EXT = ('.png', '.xnb', '.tbin', '.json', '.tmx', '.xml', '.yaml', '.cso', '.fx')

# ◎ **包自己写错的**悬空名字（小写、正斜杠）。每条都必须有源码出处 —— 这些名字在 CP 场景下
#   也**没有**任何补丁创建，游戏拿到的就是这串字，行为与产出树完全一致 ⇒ 不算我们的账。
UPSTREAM_DANGLING = {
    # SVE 的笔误：`code/Shops/SVE/AxelVendor2.json:13` 写成 `…ExpandedCP_Aged_Blue_Moon_Wine\Axel_Vendor`
    # （同目录 `AxelVendor1.json:17` 写的是对的 `…ExpandedCP\Axel_Vendor`）。`Aged_Blue_Moon_Wine`
    # 是**物品 id**，被错当成路径的一段。游戏侧有保护：立绘取不到就退化成"按名字找 NPC 立绘"，
    # 再取不到返回 null（`ShopMenu.cs:800-813`）⇒ 商店不显示立绘，**不崩**。
    'mods/flashshifter.stardewvalleyexpandedcp_aged_blue_moon_wine/axel_vendor': 'SVE 笔误（AxelVendor2.json:13）',
}
# `Data/Characters` 的两个前缀规则：`TextureName` 相对 `Characters/`、`Portrait` 相对 `Portraits/`
PREFIX = {'texturename': 'Characters/', 'portrait': 'Portraits/'}


def index(root):
    """树里所有文件的相对路径（小写、正斜杠）。"""
    out = set()
    for dirpath, _, files in os.walk(root):
        for name in files:
            rel = os.path.relpath(os.path.join(dirpath, name), root)
            out.add(rel.replace(os.sep, '/').lower())
    return out


def refs(path):
    """文件里所有 (字段名, 值) 贴图引用。"""
    try:
        data = json.load(io.open(path, encoding='utf-8-sig'))
    except Exception:
        return []
    found, stack = [], [data]
    while stack:
        node = stack.pop()
        if isinstance(node, dict):
            for k, v in node.items():
                if isinstance(v, str) and k.lower() in KEYS:
                    found.append((k, v))
                else:
                    stack.append(v)
        elif isinstance(node, list):
            stack.extend(node)
    return found


def resolvable(idx, key, value):
    """这个引用在树里指得到东西吗？返回 (True, None) / (False, 原因)。"""
    if not value or '{{' in value or '%' in value or ' ' in value:
        return None, '令牌/空格'                      # 判不了，不冒充
    name = value.replace('\\', '/').strip()
    if name.startswith('/') or ':' in name:
        return None, '绝对路径'
    cands = [name] + [name + e for e in EXT]
    pref = PREFIX.get(key.lower())
    if pref is not None:
        cands += [pref + name] + [pref + name + e for e in EXT]
    if any(c.lower() in idx for c in cands):
        return True, None
    if name.endswith('_Beach'):
        base = name[:-len('_Beach')]
        if any((base + e).lower() in idx for e in EXT) or base.lower() in idx:
            return False, '变体缺失（可接受）'        # 游戏 DoesAssetExist 保护，缺了穿常服
    if name.lower() in UPSTREAM_DANGLING:
        return False, '上游笔误（可接受）'
    return False, '找不到'


def scan(root):
    idx = index(root)
    stat = collections.Counter()
    detail = collections.defaultdict(collections.Counter)
    for dirpath, _, files in os.walk(os.path.join(root, 'Data')):
        for name in files:
            # 只看主资产：`X.json`，跳过 `X.zh-CN.json` 这种语言变体
            if not name.endswith('.json') or '.' in name[:-5]:
                continue
            for key, value in refs(os.path.join(dirpath, name)):
                ok, why = resolvable(idx, key, value)
                if ok is None:
                    stat['跳过'] += 1
                    continue
                if ok:
                    stat['解析到'] += 1
                else:
                    stat[why] += 1
                    detail[why][(name, key, value)] += 1
    return stat, detail


def main(argv):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('tree')
    ap.add_argument('base', nargs='?')
    args = ap.parse_args(argv[1:])

    if not os.path.isdir(args.tree):
        print(f'产出树不存在：{args.tree}')
        return 2

    for root, label in ((args.tree, '产出树'), (args.base, '基准树')):
        if root is None:
            continue
        stat, detail = scan(root)
        print(f'== {label}：{root}')
        print('   解析到 {解析到}   跳过（令牌/空格/绝对路径）{跳过}   '
              '★找不到 {找不到}   ○变体缺失 {变体缺失（可接受）}   '
              '◎上游笔误 {上游笔误（可接受）}'.format(
                  **collections.Counter({k: stat[k] for k in
                                         ('解析到', '跳过', '找不到', '变体缺失（可接受）',
                                          '上游笔误（可接受）')})))
        for why in ('找不到', '变体缺失（可接受）', '上游笔误（可接受）'):
            for (f, k, v), n in detail[why].most_common():
                print(f'   {why[:4]} {n:3} {f}  {k} = {v}')

    if args.base:
        a, _ = scan(args.tree)
        b, _ = scan(args.base)
        print(f'\n⇒ 产出比基准**多出**的「找不到」：{a["找不到"] - b["找不到"]}'
              f'（产出 {a["找不到"]} / 基准 {b["找不到"]}）—— 这个数才是要查的'
              f'（另有 ◎上游笔误 {a["上游笔误（可接受）"]} 处，不计入）')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
