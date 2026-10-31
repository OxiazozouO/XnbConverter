#!/usr/bin/env python3
"""把一次转换的**目标资产名按"命名空间归谁"**分桶 —— 回答「这条到底写给自己还是别人」。

**为什么要有它**：`docs/research/2026-10-09-non-cp-framework-channels.md` §7.2 原来把
`Mods/FlashShifter.StardewValleyExpandedCP/…`（**SVE 自己**的 UniqueID）记成"别人的 mod ⇒ 不转"，
差点让 SVE 的物品贴图整批丢掉。根因就是**没有拿目标的命名空间去比包自己的 `UniqueID`**。
这条判据现在固化在这里，顺带把"理由说别人的、其实写给自己"的对不上当**不变量**查出来。

判据（**不是**"看目标里有没有 `Mods/` 或点号"）：
  · `Mods/<X>/…` → 命名空间是 `X`；`<X>/…` → 命名空间是 `X`；
  · 命名空间 == 本包 manifest 的 `UniqueID` ⇒ **自己**（消费方可能是游戏本体，比如 `Data/*.json`
    里的 `"Texture": "Mods/<自己的UID>/…"`，实测 472 处）；
  · 否则第一段带点（`作者.模组名`）、或挂在 `Mods/` 下 ⇒ **别人的**（那个 mod 的 C# 读，纯 XNB 没那段代码）。

⚠ `UniqueID` 只能从**源模组目录**读（`report.json` 里没有这个字段，产出树的包目录里也没有
`manifest.json`）—— 脚本按报告里的 `pack` 字段回源读；源目录搬走/删了就会显示 `UID=?`，
那时**这几种桶都不可信**，别拿来下结论。

用法：
    python tools/namespace-ownership.py D:\\tmp\\prof32
    python tools/namespace-ownership.py D:\\tmp\\prof32 --list-own     # 列出"自己"的命名空间明细
    python tools/namespace-ownership.py D:\\tmp\\prof32 --tree <产出树>
        # 把**没写**的自定义命名空间目标拿去产出树里查引用（有没有消费方）。
        # ⚠ 这才是"该不该写"的判据 —— §7.2 的错报就是把"归属"当成了"有没有消费方"：
        #   实测有 10 条是**别的包**写进 SVE 的命名空间，而游戏**读**它们（`Data/Fences.json`
        #   里的 `Texture`、SVE 自己的图集名）⇒ 照"别人的就不写"做会把它们丢掉。
"""
import collections
import glob
import json
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

BS = chr(92)


def reports(root):
    if os.path.isfile(root):
        return [root]
    out = glob.glob(os.path.join(root, '*', 'report.json'))
    out += glob.glob(os.path.join(root, '**', 'report.json'), recursive=True)
    return sorted(set(out))


def read_uid(pack_dir):
    """源模组目录的 manifest.json 里的 UniqueID（读不出来返回空串）。"""
    path = os.path.join(pack_dir or '', 'manifest.json')
    if not os.path.isfile(path):
        return ''
    try:
        with open(path, encoding='utf-8-sig') as fh:
            return json.load(fh).get('UniqueID', '') or ''
    except Exception:
        return ''


def classify(target, uid):
    """返回 (桶, 命名空间)。桶 ∈ {own, other-mod, vanilla-ish}。"""
    t = (target or '').replace(BS, '/').strip().strip('/')
    if not t:
        return 'vanilla-ish', ''
    segs = t.split('/')
    if segs[0].lower() == 'mods':
        if len(segs) < 2:
            return 'other-mod', 'Mods'
        ns = segs[1]
        return ('own' if uid and ns.lower() == uid.lower() else 'other-mod'), ns
    ns = segs[0]
    if uid and ns.lower() == uid.lower():
        return 'own', ns
    # 第一段带点 = `作者.模组名` 的命名空间约定（原版资产名没有点）
    return ('other-mod' if '.' in ns else 'vanilla-ish'), ns


def main(argv):
    args = [a for a in argv if not a.startswith('--')]
    tree = argv[argv.index('--tree') + 1] if '--tree' in argv else None
    if tree in args:
        args.remove(tree)
    if not args:
        print(__doc__)
        return 2
    list_own = '--list-own' in argv

    bucket = collections.defaultdict(collections.Counter)   # 桶 -> verdict 计数
    own_by_pack = collections.defaultdict(collections.Counter)
    other_ns = collections.Counter()
    own_ns = collections.Counter()
    no_uid = []
    mismatched = []          # 理由写着"别的 mod"、命名空间却是自己的 ⇒ 不变量破了
    unwritten = collections.defaultdict(list)   # 资产名 -> [(包, action, verdict)]
    for rp in reports(args[0]):
        with open(rp, encoding='utf-8') as fh:
            rep = json.load(fh)
        uid = read_uid(rep.get('pack'))
        pack = os.path.basename(os.path.dirname(rp))
        if not uid:
            no_uid.append((pack, rep.get('pack')))
        for p in rep.get('patches', []):
            kind, ns = classify(p.get('target'), uid)
            bucket[kind][p.get('verdict')] += 1
            if kind == 'own':
                own_ns[ns] += 1
                own_by_pack[pack][ns] += 1
            elif kind == 'other-mod':
                other_ns[ns] += 1
            if kind == 'own' and '别的 mod 注册的自定义资产' in (p.get('reason') or ''):
                mismatched.append((pack, p.get('target'), p.get('verdict')))
            if kind in ('own', 'other-mod') and p.get('verdict') in ('NoLandingPoint', 'TargetMissing'):
                t = (p.get('target') or '').replace(BS, '/').strip().strip('/')
                unwritten[t].append((pack, p.get('action'), p.get('verdict')))

    names = {'own': '**自己**的 UniqueID', 'other-mod': "**别人**的 UniqueID", 'vanilla-ish': '原版资产名'}
    for kind in ('own', 'other-mod', 'vanilla-ish'):
        c = bucket[kind]
        if not c:
            continue
        total = sum(c.values())
        print('%s：%d 条' % (names[kind], total))
        for v, n in c.most_common():
            print('    %-22s %6d' % (v, n))

    if own_ns:
        print('\n"自己"各命名空间：')
        for ns, n in own_ns.most_common():
            print('    %-45s %6d' % (ns, n))
    if other_ns:
        print('\n"别人"各命名空间：')
        for ns, n in other_ns.most_common():
            print('    %-45s %6d' % (ns, n))

    if list_own:
        print('\n按包：')
        for pack in sorted(own_by_pack):
            print('    %-40s %s' % (pack, dict(own_by_pack[pack])))

    if no_uid:
        print('\n⚠ %d 个包读不到 UniqueID（源模组目录不在原处？）—— 上表"自己/别人"的划分**不可信**：'
              % len(no_uid))
        for pack, src in no_uid[:20]:
            print('    %-40s %s' % (pack, src))

    if mismatched:
        print('\n⚠ **不变量破了**：%d 条理由写着「别的 mod 注册的自定义资产」，命名空间却是**本包自己的** —— '
              '这正是 §7.2 那条错报的代码版本：' % len(mismatched))
        for pack, target, verdict in mismatched[:20]:
            print('    %-40s %-50s %s' % (pack, target, verdict))
    else:
        print('\n✓ 不变量：没有一条"理由说别人的、其实写给自己"。')

    if tree:
        check_consumed(tree, unwritten, bucket)

    return 0


def check_consumed(tree, unwritten, bucket):
    """把**没写的**自定义命名空间目标拿去产出树里查"有没有人引用"。

    判定"该不该写"的判据是**消费方**，不是归属：引用了它（数据资产里的 `Texture`、图集名、
    立绘名…）就说明游戏会去读 ⇒ 必须写。这里**按 JSON 解析后逐值比**（不按文本比 ——
    JSON 源码里的路径是 `A\\B`，文本比会漏）。
    """
    tree = os.path.abspath(tree)
    wanted = {name: name.lower() for name in unwritten}
    have = set()                            # 树里实际存在的文件（小写、正斜杠）
    for dirpath, _, files in os.walk(tree):
        for f in files:
            rel = os.path.relpath(os.path.join(dirpath, f), tree)
            have.add(rel.replace(os.sep, '/').lower())

    values = set()                          # 树里所有 JSON 字符串值（`\`→`/`、小写）
    scanned = 0
    for dirpath, _, files in os.walk(tree):
        for f in files:
            if not f.lower().endswith('.json') or f.lower() == 'contenthashes.json':
                continue                 # ⚠ ContentHashes 是**我们自己的**键表（游戏拿它查"资产存不存在"），
                                         #   不是消费方 —— 拿它当引用会得到一堆假阳性
            path = os.path.join(dirpath, f)
            try:
                with open(path, encoding='utf-8-sig') as fh:
                    data = json.load(fh)
            except Exception:
                continue
            scanned += 1
            stack = [data]
            while stack:
                node = stack.pop()
                if isinstance(node, dict):
                    stack.extend(node.values())
                elif isinstance(node, list):
                    stack.extend(node)
                elif isinstance(node, str):
                    values.add(node.replace(BS, '/').strip().lower())

    referenced, missing_file, have_file = [], [], []
    for name in wanted:
        if name.lower() not in values:
            continue
        referenced.append(name)
        base = name.lower()
        if any(base + e in have for e in ('.png', '.xnb', '.tbin', '.json')) or base in have:
            have_file.append(name)
        else:
            missing_file.append(name)
    print('\n== 没写的自定义命名空间目标：%d 个资产名（产出树扫了 %d 个 JSON）' % (len(wanted), scanned))
    if not referenced:
        print('   ✓ 一个都没被引用（没写 = 真没有消费方，不是丢东西）')
    if missing_file:
        print('   ★ **被引用、且树里连文件都没有**（这些是真丢 —— 游戏会去读）：')
        for name in missing_file:
            print('       %-60s %s' % (name, unwritten[name][:3]))
    if have_file:
        print('   ○ 被引用，但**树的文件在** —— 这些"没写"只是这条补丁的某几种变体：')
        for name in have_file:
            print('       %-60s %s' % (name, unwritten[name][:3]))


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
