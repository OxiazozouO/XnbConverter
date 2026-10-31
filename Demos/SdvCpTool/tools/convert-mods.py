#!/usr/bin/env python3
"""**一次转一整个 Mods 目录**：按内容包自带的 `Dependencies` 自动定序，依次转进同一棵产出树。

**为什么要有它**：很多 CP 包**互相依赖**（实测 SVE 的季节性服装编辑的是 **SVE 本体**的资产）。
逐包各自从原版树出发时，那些资产根本不存在 ⇒ 判 `TargetMissing`；
而真实用户的 Mods 目录里这些包**同时装着**，CP 是在**同一个游戏内容树上**应用它们的。

**定序依据**：每个包的 `manifest.json` 里的 `Dependencies[].UniqueID`。
只把**已装的 CP 包**作为图的节点 —— 依赖里那些 DLL mod（`spacechase0.SpaceCore` 之类）
不产出内容资产，不进图。**循环依赖不猜**：直接报出来并停下。

**两趟**（复刻 CP 的 `PatchManager.ApplyPatchesToAsset`）：第 1 趟所有包只装 `Load`，
第 2 趟再按包序应用所有 `Edit*`。这样「后加载的包创建的资产」也能被先加载的包编辑
（跨包 `EditMap`/`EditData`；实测 `Lnh's Farms Convenient Road` 的 `Maps/Custom_ConvenientRoad`）。
每包的两趟报告分别是 `report.loads.json` 与 `report.json`（后者只含 Edit 那一趟）。

用法：
    python tools/convert-mods.py <产出根目录> [--packs 子串1 子串2 …]
                                 [--tree D:\\sdv_tree] [--no-required-deps]
                                 [--checkpoint-every N] [--checkpoint-dir 目录]
                                 [--resume 检查点名或路径]
                                 [--bake-frameworks [--cccb-variant Pure_Vanilla]] [--dedup-cosmetics [--dedup-threshold 0] [--dedup-color-tolerance 0] [--dedup-max-pixel-diff 1.0] [--dedup-color-consistency 2.0]]

热更新：`--checkpoint-every 10` 会在 Load 跑完后、以及每 10 个包的编辑后整份复制语法树到
`<产出根>/checkpoints/`。改了后面某个包/某段转换逻辑后，用
`--resume edits-180` 从那份检查点恢复，只重跑第 181 个包以后的部分（前面的材料直接复用）。
检查点里的包序必须与当前 Mods 完全一致，否则拒绝续跑。

`--packs` 同时匹配 manifest `Name` 与目录名，并**自动带上 required 依赖**；
只想跑字面命中的包（例如复现旧的单包子集）时加 `--no-required-deps`。
跑完会把**与基础资产逐字相同的新增语言变体**删掉（游戏会回退基础资产），
`--keep-identical-locales` 可保留；`--prune-baseline-locales` 连**原版**里与基础
逐字相同的语言副本也删（游戏 LocalizedContentManager 会回退基础资产，风险自负）。

每个包都跑全树自检（CLI 那边没有开关了），增量缓存固定落在 <产出根>/.audit-cache.json。
"""
import collections
import glob
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as _manifest_load   # ★ 同上：manifest 可能带注释/尾逗号

def _safe_name(name, limit):
    """把包名净化成合法的 Windows 目录名（`<>:"/\|?*` 与控制字符 → `_`），并按 `limit` 截断。"""
    out = ''.join(c if (c.isalnum() or c in '-_. []()') else '_' for c in name)[:limit]
    return out.strip() or 'pack'


# ⚠ 非 CP/FTM 的内容包**一个都不会被转** —— 必须列出来（不然就是静默跳过）。
def write_inventory(out_root, mods, converted):
    """把"这份产出里**有什么、没有什么**"落成一个文件 —— stdout 会滚走，报告才是有人回头看的。"""
    lines = [f'产出根：{out_root}', f'Mods：{mods}', '']
    lines.append(f'转进来的内容包 {len(converted)} 个：')
    lines += [f'    · {n}' for n in converted]
    lines.append('')
    bad = cppacks.unparseable_manifests(mods)
    if bad:
        lines.append(f'⚠ manifest.json **读不出来**的 {len(bad)} 个 —— 读不出来就是**没转**（不是"没有内容"）：')
        lines += [f'    · {d}  ← {why}' for d, why in bad]
    lines.append(f'代码模组（DLL）{cppacks.count_dll_mods(mods)} 个 —— 纯 XNB 场景没有 SMAPI，它们用不上，没转。')
    others = cppacks.find_other_packs(mods)
    lines.append(f'别的框架的内容包 {len(others)} 个 —— 不走 CP/FTM 主转换链；'
                 f'AT/SF/CC/CCCB/MFM 由 bake-frameworks 增量烘焙，其余未转'
                 f'（接法见 docs/how-to-add-a-framework-channel.md）：')
    for uid, n in collections.Counter(u for _n, _p, u in others).most_common():
        lines.append(f'    · {uid}  {n} 个')
    for name, path, uid in others:
        volume = cppacks.framework_volume(path, uid)
        lines.append(f'    · {name}  ← ContentPackFor={uid}'
                     + (f'（{volume}）' if volume else ''))
    os.makedirs(out_root, exist_ok=True)
    io.open(os.path.join(out_root, '_not-converted.txt'), 'w', encoding='utf-8',
            newline=chr(10)).write(chr(10).join(lines) + chr(10))


def report_unconverted(mods):
    n_dll = cppacks.count_dll_mods(mods)
    if n_dll:
        print(f'ℹ 本机另有 {n_dll} 个**代码模组**（DLL）—— 纯 XNB 场景里没有 SMAPI，它们用不上，'
              f'也不会进产物（见 docs/research/2026-10-20-pure-xnb-dll-losses.md）')
    others = cppacks.find_other_packs(mods)
    if not others:
        return
    print(f'⚠ 另有 {len(others)} 个**别的框架**的内容包，不走 CP/FTM 主链'
          f'（AT/SF/CC/CCCB/MFM 由 bake-frameworks 增量烘焙，其余没转；'
          f'接法见 docs/how-to-add-a-framework-channel.md）：')
    for uid, n in collections.Counter(u for _n, _p, u in others).most_common():
        print(f'    · {uid}  {n} 个')
    for name, path, uid in others:
        volume = cppacks.framework_volume(path, uid)
        print(f'    · {name}  ← ContentPackFor={uid}' + (f'（{volume}）' if volume else ''))


if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

import cppacks   # 同目录
MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'
REPO = r'D:\SdvCpTool'
HASHES = r'D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json'
CP_UID = 'pathoschild.contentpatcher'
FTM_UID = 'esca.farmtypemanager'
EXE = os.environ.get('SDVCP_EXE') or os.path.join(
    REPO, 'src', 'SdvCpTool.Cli', 'bin', 'Release', 'net8.0', 'SdvCpTool.Cli.exe')


def read_packs():
    """CP 包 + **已支持的非 CP 框架包**（FTM）。

    FTM 包（`ContentPackFor = Esca.FarmTypeManager`）由 `FtmPackTranslator` 在读取时翻成
    `Data/Locations` 的 EditData 补丁，所以它也进这条链。⚠ 它改的 `Data/Locations.<地图>` 条目
    通常**由同目录下的 CP 包创建** —— 而 FTM 的 manifest 里**不写**这层依赖，
    所以这里替它补上：「同父目录下的 CP 包」都算它的前置。
    """
    packs = []
    for manifest in glob.glob(os.path.join(MODS, '**', 'manifest.json'), recursive=True):
        try:
            data = _manifest_load(manifest)
        except Exception:
            continue
        if not isinstance(data, dict):
            continue
        framework = str(data.get('ContentPackFor', {}).get('UniqueID', '')).lower()
        is_cp = framework.startswith(CP_UID)
        is_ftm = framework == FTM_UID
        if not (is_cp or is_ftm):
            continue
        pack_dir = os.path.dirname(manifest)
        dependency_entries = [d for d in (data.get('Dependencies') or []) if isinstance(d, dict)]
        deps = [str(d.get('UniqueID')) for d in dependency_entries]
        # ⚠ `--packs` 只选了一部分包时，**必需**依赖必须一起带上；可选依赖不自动拉
        #   （否则 SVE/RSV 这些巨大的可选前置会被无意拖进来）。
        # SMAPI 的 `IsRequired` 默认是 **true**（省略 = 必需）；只有显式 false 才是可选。
        required = [str(d.get('UniqueID')) for d in dependency_entries
                    if str(d.get('IsRequired', True)).lower() != 'false']
        if is_ftm:
            deps += _sibling_cp_uids(os.path.dirname(pack_dir))
        packs.append({
            'name': data.get('Name') or os.path.basename(pack_dir),
            'uid': str(data.get('UniqueID') or ''),
            'path': pack_dir,
            'deps': deps,
            'required': required,
            'ftm': is_ftm,
        })

    # ⚠ **CP 包对 FTM 包的依赖不算排序边**：那是 SMAPI 的「装了才算数」（presence），
    #   而不是"要先跑"。反过来才是对的 —— FTM 改的 `Data/Locations.<地图>` 条目
    #   **正是那些 CP 包创建出来的**（实测：SVE 自己声明依赖 `FlashShifter.SVE-FTM`，
    #   若认这条边就成了环，环的兜底排序会把 SVE[FTM] 排到 SVE 前面，
    #   它的 TargetField 全落空，SVE 的产出也跟着变了 395 个文件）。
    ftm_uids = {p['uid'] for p in packs if p['ftm'] and p['uid']}
    for p in packs:
        if not p['ftm']:
            p['deps'] = [d for d in p['deps'] if d not in ftm_uids]
    return packs


def _sibling_cp_uids(container):
    """同一个容器目录下的 CP 包 UniqueID（FTM 包的隐式前置）。"""
    out = []
    if not os.path.isdir(container):
        return out
    for child in os.listdir(container):
        manifest = os.path.join(container, child, 'manifest.json')
        if not os.path.isfile(manifest):
            continue
        try:
            d = _manifest_load(manifest)
        except Exception:
            continue
        if str((d.get('ContentPackFor') or {}).get('UniqueID', '')).lower().startswith(CP_UID):
            out.append(str(d.get('UniqueID') or ''))
    return out


def order(packs):
    """按 **SMAPI 的实际加载顺序**排：**按名字遍历**，遇到还没加载的依赖就**先把依赖拉进来**（DFS）。

    返回 (排好序的列表, 说明)。

    ⚠ 为什么不是 Kahn（"反复取就绪的、名字最小的那个"）：两者在「某个包被依赖拖后」时**结果不同**，
    而 CP 的语义是**后加载的补丁后应用** —— "谁最后写"完全取决于这个顺序。
    实测（2026-10-15）：`Grandpa's Farm` 依赖 `Immersive Farm 2 Remastered`，
    Kahn 把 `Lnh's Farms Convenient Road` 先落位 ⇒ `Grandpa's Farm` 那条
    **整张替换 `Maps/Farm` 的 `Load`** 后跑，把 Lnh 盖上去的便捷通道**冲掉**了；
    而 SMAPI 里 `Grandpa's Farm`（G）名字比 `Lnh's…`（L）靠前、且依赖先行
    ⇒ **先**换图、Lnh **后**盖通道 ⇒ 通道在。**实机看到的"农场地图传送没合并进来"就是这么来的。**

    名字顺序按 **不区分大小写**（SMAPI 自己的口径）；有环就**不猜**，按遇到顺序继续并说明。
    """
    by_uid = {p['uid']: p for p in packs if p['uid']}
    out, note = [], []
    placed, in_progress = set(), set()

    def key(p):
        return p['uid'] or p['name']

    def put(p):
        k = key(p)
        if k in placed:
            return
        if k in in_progress:                      # 环：不猜，就地继续
            note.append('!! 循环依赖：%s（按遇到顺序继续，顺序**未确证**）' % p['name'][:24])
            return
        in_progress.add(k)
        for d in sorted({x for x in p['deps'] if x in by_uid}):
            put(by_uid[d])
        in_progress.discard(k)
        if k not in placed:
            placed.add(k)
            out.append(p)

    for p in sorted(packs, key=lambda p: (p['name'].lower(), p['name'])):
        put(p)

    # 报告「依赖了已装的 CP 包」这件事本身 —— 这才是链式转换的收益来源
    linked = [p for p in packs if any(d in by_uid and by_uid[d]['uid'] != p['uid'] for d in p['deps'])]
    note.append(f'依赖了**其它已装 CP 包**的有 {len(linked)}/{len(packs)} 个：'
                + ', '.join(p['name'][:16] for p in linked))
    return out, note


LOCALE_SUFFIXES = ('.de-DE', '.es-ES', '.fr-FR', '.hu-HU', '.it-IT',
                   '.ja-JP', '.ko-KR', '.pt-BR', '.ru-RU', '.tr-TR', '.zh-CN')


def prune_identical_locale_variants(tree, base_tree, include_baseline=False):
    """删掉**与基础资产逐字相同**的语言变体（游戏会回退到基础资产）。

    转换器按 CP 语义生成 `.zh-CN.json` 等变体；当包只给了语言文件、实际没翻译，
    整份内容会和基础资产一模一样。保留它只会多一份同内容资源。
    默认只删**基线树里没有**的文件（原版语言资产一根不动）；`include_baseline`
    会把原版里同样的重复副本也删掉（LocalizedContentManager 会回退基础资产）。
    同步清掉 ContentHashes 键。
    """
    removed = 0
    saved = 0
    removed_keys = set()
    removed_paths = []
    for ext in ('.json', '.png'):
        for path in glob.glob(os.path.join(tree, '**', '*' + ext), recursive=True):
            stem = path[:-len(ext)]
            suffix = next((s for s in LOCALE_SUFFIXES if stem.endswith(s)), None)
            if suffix is None:
                continue
            base = stem[:-len(suffix)] + ext
            if not os.path.isfile(base):
                continue
            rel = os.path.relpath(path, tree).replace(os.sep, '/')
            is_baseline = os.path.isfile(os.path.join(base_tree, rel))
            if is_baseline and not include_baseline:
                continue
            try:
                if open(path, 'rb').read() != open(base, 'rb').read():
                    continue
            except OSError:
                continue
            try:
                saved += os.path.getsize(path)
                os.remove(path)
                removed += 1
                removed_paths.append(rel)
            except OSError:
                continue
            cfg = path[:-len(ext)] + '.config'
            if os.path.isfile(cfg):
                try:
                    os.remove(cfg)
                except OSError:
                    pass
            removed_keys.add((os.path.splitext(rel)[0] + '.xnb').lower())

    hashes_path = os.path.join(tree, 'ContentHashes.json')
    if removed_keys and os.path.isfile(hashes_path):
        try:
            hashes = json.load(open(hashes_path, encoding='utf-8'))
            kept = {k: v for k, v in hashes.items() if k.lower() not in removed_keys}
            if len(kept) != len(hashes):
                json.dump(kept, open(hashes_path, 'w', encoding='utf-8', newline='\n'),
                          ensure_ascii=False, separators=(',', ':'))
        except Exception as e:
            print(f'（清理重复语言变体的 ContentHashes 失败：{type(e).__name__} {e}）')
    return removed, saved, removed_paths


def copy_tree(src, dst):
    """整树复制（robocopy 多线程；返回码 < 8 都算成功）。"""
    os.makedirs(dst, exist_ok=True)
    r = subprocess.run(['robocopy', src, dst, '/E', '/COPY:DAT', '/R:1', '/W:1',
                        '/NFL', '/NDL', '/NJH', '/NJS', '/MT:16'],
                       capture_output=True, text=True, encoding='gbk', errors='replace')
    if r.returncode >= 8:
        raise RuntimeError(f'robocopy {src} -> {dst} 失败（{r.returncode}）：{r.stdout[-500:]}')


def file_sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def make_checkpoint(shared_tree, ckpt_dir, name, packs, last_edit_index, out_root, base_tree):
    """把**当前语法树**整份复制成检查点，供 `--resume` 从后面继续。

    只复制树，不带 `.audit-cache.json` —— 恢复是在同一个 `out_root` 里做的，
    那份缓存还在原地，文件没变时仍可复用。
    """
    dest = os.path.join(ckpt_dir, name)
    tree_dst = os.path.join(dest, 'tree')
    if os.path.isdir(dest):
        shutil.rmtree(dest)
    os.makedirs(dest, exist_ok=True)
    t0 = time.time()
    copy_tree(shared_tree, tree_dst)
    meta = {
        'version': 1,
        'name': name,
        'created': time.strftime('%Y-%m-%d %H:%M:%S'),
        'out_root': os.path.abspath(out_root),
        'base_tree': os.path.abspath(base_tree),
        'last_edit_index': last_edit_index,
        'packs': [p['path'] for p in packs],
        # 检查点**之前**的内容是用哪份转换器算出来的；续跑时会提示不一致。
        'cli_sha256': file_sha256(EXE) if os.path.isfile(EXE) else None,
    }
    with open(os.path.join(dest, 'checkpoint.json'), 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
    print(f'💾 检查点 {name}（编辑到第 {last_edit_index} 个包，复制 {time.time() - t0:.0f}s）', flush=True)


def load_checkpoint(path, packs):
    """读检查点并核对包序；返回 (树目录, 已应用到的编辑序号)。"""
    meta_path = os.path.join(path, 'checkpoint.json')
    if not os.path.isfile(meta_path):
        raise SystemExit(f'✗ 不是检查点（缺 checkpoint.json）：{path}')
    with open(meta_path, encoding='utf-8') as f:
        meta = json.load(f)
    expected = [p['path'] for p in packs]
    if meta.get('packs') != expected:
        raise SystemExit('✗ 检查点的包序与当前 Mods 不一致，不能从它续跑。'
                         '（增删/改名过内容包，或 --packs 选择不同）')
    tree = os.path.join(path, 'tree')
    if not os.path.isdir(tree):
        raise SystemExit(f'✗ 检查点里没有 tree：{tree}')
    return tree, int(meta.get('last_edit_index') or 0), meta


def main():
    report_unconverted(MODS)
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if not args:
        print(__doc__)
        return 2
    out_root = args[0]
    tree = r'D:\sdv_tree'
    if '--tree' in sys.argv:
        tree = sys.argv[sys.argv.index('--tree') + 1]
    only = []
    if '--packs' in sys.argv:
        # 只取 `--packs` 之后、**下一个选项之前**的值。
        # ⚠ 旧写法把后面所有非 `--` 开头的词都收进来，于是 `--packs X --checkpoint-every 1`
        #   会把选项值 `1` 也当成包名子串（实测匹配到 35 个包）。
        i = sys.argv.index('--packs') + 1
        while i < len(sys.argv) and not sys.argv[i].startswith('--'):
            only.append(sys.argv[i])
            i += 1

    all_packs = read_packs()
    packs = all_packs
    if only:
        # 同时匹配 manifest `Name` 与**目录名**：实测 `[CP] Lumisteria Serene Meadow`
        # 的 Name 被作者改成了无关中文串，只按 Name 会漏掉整包。
        packs = [p for p in all_packs
                 if any(n.lower() in p['name'].lower()
                        or n.lower() in os.path.basename(p['path']).lower()
                        for n in only)]
        # required 依赖自动带上（可选依赖不拉，避免误拖 SVE/RSV）。
        # `--no-required-deps` 可关掉，用于复现旧的单包子集。
        if '--no-required-deps' not in sys.argv:
            by_uid = {p['uid']: p for p in all_packs if p['uid']}
            selected = {p['uid'] for p in packs if p['uid']}
            added = []
            changed = True
            while changed:
                changed = False
                for p in list(packs):
                    for dep in p.get('required') or []:
                        if dep in selected:
                            continue
                        # 只在依赖**本身也是 CP/FTM 包**时自动加入；DLL/框架依赖纯 XNB 用不上，
                        # 不在这里刷警告（它们本来就不会进可转集合）。
                        if dep in by_uid:
                            packs.append(by_uid[dep])
                            selected.add(dep)
                            added.append(by_uid[dep]['name'])
                            changed = True
            if added:
                print('ℹ --packs 自动带上 required 依赖：' + '、'.join(added))
    ordered, note = order(packs)
    for line in note:
        print(line)

    # 检查点/续跑：语法树每 N 个包整份复制一份，之后可以从指定检查点恢复，
    # 前面的包不再重跑（默认关；`--checkpoint-every 10` 打开）。
    ckpt_every = 0
    if '--checkpoint-every' in sys.argv:
        i = sys.argv.index('--checkpoint-every')
        ckpt_every = int(sys.argv[i + 1]) if i + 1 < len(sys.argv) else 0
    ckpt_dir = os.path.join(out_root, 'checkpoints')
    if '--checkpoint-dir' in sys.argv:
        i = sys.argv.index('--checkpoint-dir')
        ckpt_dir = sys.argv[i + 1]
    resume_path = None
    if '--resume' in sys.argv:
        i = sys.argv.index('--resume')
        resume_path = sys.argv[i + 1] if i + 1 < len(sys.argv) else ''
        if not os.path.isdir(resume_path):
            resume_path = os.path.join(ckpt_dir, resume_path)

    shared_tree = os.path.join(out_root, 'tree')
    os.makedirs(out_root, exist_ok=True)
    resume_last = 0
    resume_meta = None
    if resume_path:
        ckpt_tree, resume_last, resume_meta = load_checkpoint(resume_path, ordered)
        if os.path.abspath(out_root) != resume_meta.get('out_root'):
            raise SystemExit(f'✗ 检查点属于另一棵产出根：{resume_meta.get("out_root")}\n'
                             f'  当前是：{os.path.abspath(out_root)}')
        saved_cli = resume_meta.get('cli_sha256')
        if saved_cli and os.path.isfile(EXE) and file_sha256(EXE) != saved_cli:
            print('⚠ 转换器二进制与检查点不同：前面的包**不会重算**，'
                  '只重跑检查点之后的包。', flush=True)
        if os.path.isdir(shared_tree):
            print(f'♻ 用检查点覆盖现有产出树：{shared_tree}', flush=True)
            shutil.rmtree(shared_tree)
        t0 = time.time()
        copy_tree(ckpt_tree, shared_tree)
        print(f'♻ 已从 {resume_path} 恢复（编辑到第 {resume_last} 个包，'
              f'复制 {time.time() - t0:.0f}s），接下来只跑后面的包', flush=True)
    elif os.path.isdir(shared_tree) and os.listdir(shared_tree):
        print(f'⚠ 产出树已存在：{shared_tree}\n'
              '  本脚本会在它上面**续写**（第 1 趟不会重新从原版树建树）。\n'
              '  要一份干净产出，请先删除该目录。', flush=True)
    failed = []

    # ★ **两趟：先装所有 Load，再应用所有 Edit** —— 复刻 CP 的 `PatchManager.ApplyPatchesToAsset`：
    #   请求某资产时先选出 Load 装上去，再把**所有包**的 Edit 依次叠上。逐包单趟做不到：
    #   后加载的包创建的资产，先加载的包那会儿还不存在，`EditMap`/`EditData` 会被判 TargetMissing。
    #   实测 `Lnh's Farms Convenient Road` 创建的 `Maps/Custom_ConvenientRoad`、
    #   SVE 编辑的 `Maps/Custom_GrampletonFields`（地图由后加载的 Grampleton Fields 创建）。
    #   第 1 趟第 1 个包从原版树建树；之后都在同一棵树上续写。
    first_run = not resume_path
    for phase, label in (('loads', '装填 Load'), ('edits', '应用 Edit')):
        if phase == 'loads' and resume_path:
            print('\n=== 第 1 趟：跳过（检查点里已经装好全部 Load）===', flush=True)
            continue
        print(f'\n=== 第 {1 if phase == "loads" else 2} 趟：{label} ===', flush=True)
        for i, pack in enumerate(ordered, 1):
            if phase == 'edits' and resume_path and i <= resume_last:
                continue
            # ⚠ **包名要净化**：`manifest.json` 的 `Name` 里什么字符都可能有 —— 实测新语料里
            #    `East Scarp: Locations` 带**冒号**，Windows 目录名非法 ⇒ `makedirs` 抛
            #    `NotADirectoryError (WinError 267)` 把整轮跑崩在第 71 个包上。
            dst = os.path.join(out_root, '%02d_%s' % (i, _safe_name(pack['name'], 26)))
            os.makedirs(dst, exist_ok=True)
            report = os.path.join(dst, 'report.json' if phase == 'edits' else 'report.loads.json')
            cmd = [EXE, 'convert', '--pack', pack['path'],
                   '--tree', tree if first_run else shared_tree,
                   '--mods', MODS, '--out', shared_tree, '--report', report,
                   '--pass', phase,
                   # 链式转换：翻转格必须等所有包的图片 Load 都跑完再统一烘焙，
                   # 否则后面的短图集会把前面追加的格子冲掉、索引也会互相踩踏。
                   '--defer-flips',
                   # ★ 一进程一个包 ⇒ 每包都要递归扫一遍 Mods 树（实测 0.6 s × 2~4 次）
                   #   ⇒ 232 包白扫几分钟。缓存放在产出根（**产出树之外**，免得被 `pack` 当资源）。
                   #   键 = Mods 目录 mtime + 一级目录数（加/删 mod 会失效）—— 见 `InstalledModIndex`。
                   '--mod-index-cache', os.path.join(out_root, '.mod-index.json')]
            # 全树自检**总是跑**（CLI 那边没有开关了），增量缓存固定落在 <产出根>/.audit-cache.json
            # —— 每个包是一个独立进程，靠它把「这份文件上次的结论」跨包记住，文件没变就不重算。
            if os.path.isfile(HASHES):
                cmd += ['--content-hashes', HASHES]
            source = '原版树（建树）' if first_run else '同一棵树（续写）'
            first_run = False
            print(f'{pack["name"][:40]} ← {source}', flush=True)
            t0 = time.time()
            # ⚠ 子进程的输出是 **GBK** 不是 UTF-8（.NET 在 Windows 上按控制台代码页走）——
            #   按 utf-8 解会把中文全变成替换符（2026-10-10 拿 profile 时踩过）。
            r = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True,
                               encoding='gbk', errors='replace', timeout=3600)
            secs = time.time() - t0
            if r.returncode != 0:
                failed.append(f'{pack["name"]}（{phase}）')
            print(f'退出码={r.returncode} 用时={secs:.0f}s', flush=True)
            if os.path.isfile(report):
                j = json.load(open(report, encoding='utf-8'))
                print(f'total={j["total"]} written={j["written"]} failed={j["writeFailed"]}',
                      flush=True)
            else:
                # ⚠ **报告没写出**要和"跑完但 0 条"分开说：前者是这次运行**根本没跑到底**
                # （实测：一轮完整跑批里 3 个包退出码 2、目录空着 —— 当时整树回包在抢磁盘，
                # 单跑那 3 个包又完全正常 ⇒ 环境性瞬时失败）。混在一起看会以为"这包没内容"。
                print(f'⚠ 报告没写出（{report}）：这次运行没跑到底，别把它的产物当结论',
                      flush=True)
                # 报告是**最后**才写的 ⇒ "没写出"多半是**半路抛异常**（退出码 2 也涵盖未捕获异常）。
                # 把子进程的 stderr 原样带出来，这类失败才能自证 —— 否则只看得到一个空目录。
                if (r.stderr or '').strip():
                    print('── stderr ──', flush=True)
                    for line in (r.stderr or '').strip().splitlines()[-12:]:
                        print(line[:160], flush=True)
            # ⚠ 子进程的 stdout/stderr 默认被 capture 掉 —— SDVCP_PROFILE 的分段计时写在 stderr 上，
            #   不吐出来就等于没跑（2026-10-10 踩过：跑了一整轮，profile 一条没留下）。
            if os.environ.get('SDVCP_PROFILE') and r.stderr.strip():
                print(r.stderr.rstrip(), flush=True)
            if phase == 'edits' and ckpt_every > 0 and i % ckpt_every == 0:
                make_checkpoint(shared_tree, ckpt_dir, f'edits-{i:03d}', ordered, i,
                                out_root, tree)
        if phase == 'loads' and ckpt_every > 0 and not resume_path:
            make_checkpoint(shared_tree, ckpt_dir, 'loads', ordered, 0, out_root, tree)

    # 两趟报告合并：既有工具都读 `report.json`；只留 Edit 那趟会让 Load 写出的资产
    # 在统计里"消失"（total/written 与旧基线也不可比）。保留原始两趟为
    # `report.loads.json` / `report.edits.json`，`report.json` 放合并结果。
    merged_reports = 0
    for i, pack in enumerate(ordered, 1):
        dst = os.path.join(out_root, '%02d_%s' % (i, _safe_name(pack['name'], 26)))
        loads_path = os.path.join(dst, 'report.loads.json')
        edits_path = os.path.join(dst, 'report.json')
        if not (os.path.isfile(loads_path) and os.path.isfile(edits_path)):
            continue
        try:
            with open(loads_path, encoding='utf-8') as f:
                loads = json.load(f)
            with open(edits_path, encoding='utf-8') as f:
                edits = json.load(f)
            merged = dict(edits)
            merged['patches'] = list(loads.get('patches') or []) + list(edits.get('patches') or [])
            for key in ('total', 'convertible', 'groupEmitted', 'blocked',
                        'attemptedWrite', 'written', 'writeFailed'):
                if isinstance(loads.get(key), int) and isinstance(edits.get(key), int):
                    merged[key] = loads[key] + edits[key]
            if isinstance(loads.get('summary'), dict) and isinstance(edits.get('summary'), dict):
                keys = set(loads['summary']) | set(edits['summary'])
                merged['summary'] = {k: loads['summary'].get(k, 0) + edits['summary'].get(k, 0)
                                     for k in keys}
            # 两趟里 FTM 翻译说明是**同一批**（Load/Edit 都会产出），直接拼会整段重复。
            # 按原顺序去重，别让报告读起来像“落了两遍”。
            merged['notes'] = []
            seen_notes = set()
            for note in list(loads.get('notes') or []) + list(edits.get('notes') or []):
                if note in seen_notes:
                    continue
                seen_notes.add(note)
                merged['notes'].append(note)
            merged['twoPassMerged'] = True
            with open(os.path.join(dst, 'report.edits.json'), 'w', encoding='utf-8') as f:
                json.dump(edits, f, ensure_ascii=False, indent=2)
            with open(edits_path, 'w', encoding='utf-8') as f:
                json.dump(merged, f, ensure_ascii=False, indent=2)
            merged_reports += 1
        except Exception as e:
            print(f'（合并 {pack["name"]} 的两趟报告失败：{type(e).__name__} {e}）')
    if merged_reports:
        print(f'两趟报告已合并：{merged_reports} 个包的 report.json = Load + Edit')

    if '--keep-identical-locales' not in sys.argv:
        pruned, saved, pruned_paths = prune_identical_locale_variants(
            shared_tree, tree, include_baseline='--prune-baseline-locales' in sys.argv)
        if pruned:
            print(f'省略与基础资产相同的语言变体：{pruned} 个（省 {saved / 1048576:.1f} MB）')
            try:
                with open(os.path.join(os.path.dirname(shared_tree.rstrip('\\/')),
                                       '_deduped-locales.txt'), 'w', encoding='utf-8', newline='\n') as f:
                    f.write('\n'.join(pruned_paths) + '\n')
            except OSError:
                pass

    # ★ 所有包都写完后，统一把翻转/旋转烘进图集。必须在这里做：
    #   链式转换是多个独立进程，先烘的追加格会被后面的 `Load` 短图集冲掉。
    print('\n=== 全树翻转烘焙 ===', flush=True)
    try:
        r = subprocess.run([EXE, 'bake-flips', '--tree', shared_tree], cwd=REPO,
                           capture_output=True, text=True, encoding='gbk',
                           errors='replace', timeout=3600)
        if (r.stdout or '').strip():
            print(r.stdout.rstrip(), flush=True)
        if r.returncode != 0:
            failed.append('bake-flips')
            if (r.stderr or '').strip():
                print('── bake-flips stderr ──\n' + (r.stderr or '').strip()[-2000:], flush=True)
    except Exception as e:
        print(f'⚠ 全树翻转烘焙失败：{type(e).__name__} {e}', flush=True)
        failed.append('bake-flips')

    # 可选：把非 CP/FTM 框架的内容按“新增数据表行”烘进同一棵树。
    # 当前实现 AT/SF/CC/CCCB/MFM + MoreGrass/Bus/Train 静态近似；
    # 见 docs/research/2026-10-20-additive-baking-reassessment.md §2.8。
    if '--bake-frameworks' in sys.argv:
        # C# 实现：SdvCpTool.Cli bake-frameworks。
        cmd = [EXE, 'bake-frameworks', '--tree', shared_tree, '--mods', MODS]
        if only:
            cmd += ['--packs'] + only
        if '--cccb-variant' in sys.argv:
            i = sys.argv.index('--cccb-variant')
            if i + 1 < len(sys.argv):
                cmd += ['--cccb-variant', sys.argv[i + 1]]
        if '--dedup-cosmetics' in sys.argv:
            cmd.append('--dedup-cosmetics')
        if '--dedup-threshold' in sys.argv:
            i = sys.argv.index('--dedup-threshold')
            if i + 1 < len(sys.argv):
                cmd += ['--dedup-threshold', sys.argv[i + 1]]
        if '--dedup-color-tolerance' in sys.argv:
            i = sys.argv.index('--dedup-color-tolerance')
            if i + 1 < len(sys.argv):
                cmd += ['--dedup-color-tolerance', sys.argv[i + 1]]
        if '--dedup-max-pixel-diff' in sys.argv:
            i = sys.argv.index('--dedup-max-pixel-diff')
            if i + 1 < len(sys.argv):
                cmd += ['--dedup-max-pixel-diff', sys.argv[i + 1]]
        if '--dedup-color-consistency' in sys.argv:
            i = sys.argv.index('--dedup-color-consistency')
            if i + 1 < len(sys.argv):
                cmd += ['--dedup-color-consistency', sys.argv[i + 1]]
        print('\n=== 烘焙框架内容（C#：AT/SF/CC/CCCB/MFM + MoreGrass/Bus/Train）===', flush=True)
        try:
            r = subprocess.run(cmd, cwd=REPO, timeout=3600)
            if r.returncode != 0:
                print(f'⚠ 框架烘焙退出码 {r.returncode}')
        except Exception as e:
            print(f'⚠ 框架烘焙失败：{type(e).__name__} {e}')

    print(f'\n完成 {len(ordered)} 个包 ×2 趟；产出树：{shared_tree}'
          + (f'；退出码非 0 的：{failed}' if failed else ''))

    # 把"这份产出里有什么、没有什么"落成文件（stdout 会滚走，报告才是有人回头看的）
    try:
        write_inventory(os.path.dirname(shared_tree.rstrip('\\/')), MODS,
                        [p['name'] for p in ordered])
    except Exception as e:
        print(f'（写 _not-converted.txt 失败：{type(e).__name__} {e}）')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
