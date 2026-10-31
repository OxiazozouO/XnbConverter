"""被拒的 Data/AudioChanges 条目，有多少张地图真的在用？（量一下这条边界的后果）

⚠ **这份量的是当时的边界，现在已经不适用了**（2026-10-10 起）：
`Data/AudioChanges` 不再整条被拒 —— 改成把 ogg 并进 Wave Bank、cue 加进 Sound Bank，
音频能播了；`Looped: true` 的循环**也已修**（改走「简单 Cue + 带 Clip 的 Sound」那条路）。
见 `docs/verification/2026-10-10-xact-music-injection.md` 与
`docs/verification/2026-10-10-xact-looped-cues.md`。

留着是为了两件事：① 当时那批数字（"14 个 id / 19 张地图"）的出处在这里，
       后来订正成 **13 张**（原来的数是拿短名做子串匹配算的）；
       ② 万一将来再出现"某个资产整条写不出去"，这套「量它的下游影响」的做法可以照搬。

`Data/AudioChanges` 的值是 `{{AbsoluteFilePath: assets/Music/*.ogg}}`（绝对路径），
当时静态代不了 ⇒ 条目没写进产出 ⇒ 用到它们的地图没音乐。

用 jsonc 解析（不拿正则猜形状 —— 第一版就是正则写错、报出 0）。
"""
import collections
import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.path.insert(0, 'D:/SdvCpTool/tools')
import jsonc  # noqa: E402
import cppacks  # noqa: E402

TREE = r'D:\tmp\final5_tree'


def has_abs_path(node):
    if isinstance(node, dict):
        return any(has_abs_path(v) for v in node.values())
    if isinstance(node, list):
        return any(has_abs_path(v) for v in node)
    return isinstance(node, str) and 'AbsoluteFilePath' in node


def walk_changes(node, out):
    if isinstance(node, dict):
        if str(node.get('Target')) == 'Data/AudioChanges':
            for k, v in (node.get('Entries') or {}).items():
                if has_abs_path(v):
                    out[k] = True
        for v in node.values():
            walk_changes(v, out)
    elif isinstance(node, list):
        for v in node:
            walk_changes(v, out)


refused = {}
for name, pack in cppacks.find_cp_packs():
    for dp, _dirs, fns in os.walk(pack):
        for f in fns:
            if not f.endswith('.json') or f == 'manifest.json':
                continue
            p = os.path.join(dp, f)
            try:
                j = jsonc.load(p)
            except Exception:
                continue
            found = {}
            walk_changes(j, found)
            for k in found:
                refused.setdefault(k, name[:24])

print(f'被拒的 Data/AudioChanges 条目 **{len(refused)} 个**')
for k in list(refused)[:4]:
    print(f'   {k:52} ← {refused[k]}')

p = os.path.join(TREE, 'Data', 'AudioChanges.json')
have = set(json.load(open(p, encoding='utf-8-sig')).keys()) if os.path.isfile(p) else set()
still = [k for k in refused if k in have]
print(f'\n产出 Data/AudioChanges 有 {len(have)} 个 id；其中属于被拒条目的 {len(still)} 个')

used = collections.defaultdict(set)
targets = [k.encode() for k in refused]
for dp, _dirs, fns in os.walk(os.path.join(TREE, 'Maps')):
    for f in fns:
        if not f.endswith('.tbin'):
            continue
        raw = open(os.path.join(dp, f), 'rb').read()
        for t in targets:
            if t in raw:
                used[t.decode()].add(f[:-5])

print(f'\n**有 {len(used)} 个被拒的音频 id 被地图引用**（那些地图没有音乐）：')
for k, ms in sorted(used.items(), key=lambda kv: -len(kv[1]))[:12]:
    print(f'   {k:52} ← {len(ms)} 张：{",".join(sorted(ms)[:3])}')
