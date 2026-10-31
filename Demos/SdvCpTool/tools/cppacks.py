#!/usr/bin/env python3
"""**CP 内容包清单** —— 语料脚本们共用的那一份。

判据只有一条，且来自 CP 自己的约定：包的 `manifest.json` 里
`ContentPackFor.UniqueID` 是 `Pathoschild.ContentPatcher`。

★ 为什么抽成模块：`corpus-b-survey.py` 与 `roundtrip-all.py` 都要这份清单，
各写一份就会各自漂 —— 这个仓库已经吃过一次亏（手抄的 `Merging/ArrayAssets.cs`
白名单与生成器是同一份 `DataLoader.cs` 的两个副本，反倒是手抄那份抓出了生成器的漏）。
"""
import glob
import json
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as _manifest_load   # ★ manifest **常带注释/尾逗号**，严格 json.load 会
                                          #   解析失败 ⇒ 以前直接 continue = **静默跳过整包**

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'


# ⚠ **本工具现在不止转 CP 包**：Farm Type Manager 包（`ContentPackFor = Esca.FarmTypeManager`）
#   也会被 `FtmPackTranslator` 翻成 `Data/Locations` 的补丁。要"跑遍所有能转的包"时，
#   必须 `find_cp_packs() + find_ftm_packs()`；只取前者会**静默漏掉 4 个 FTM 包**。
FTM_UID = 'esca.farmtypemanager'


def find_ftm_packs(mods=MODS):
    """→ [(显示名, 包目录)]：FTM 包（同样按名字排序去重）。"""
    packs = []
    for m in glob.glob(os.path.join(mods, '**', 'manifest.json'), recursive=True):
        try:
            d = _manifest_load(m)
        except Exception:
            continue
        if not isinstance(d, dict):
            continue
        if str(d.get('ContentPackFor', {}).get('UniqueID', '')).lower() == FTM_UID:
            packs.append((d.get('Name') or os.path.basename(os.path.dirname(m)), os.path.dirname(m)))
    return sorted(set(packs), key=lambda x: x[0])


def find_cp_packs(mods=MODS):
    """→ [(显示名, 包目录)]，按名字排序去重。"""
    packs = []
    for m in glob.glob(os.path.join(mods, '**', 'manifest.json'), recursive=True):
        try:
            d = _manifest_load(m)
        except Exception:
            continue
        if not isinstance(d, dict):
            continue
        if str(d.get('ContentPackFor', {}).get('UniqueID', '')).lower().startswith('pathoschild.contentpatcher'):
            packs.append((d.get('Name') or os.path.basename(os.path.dirname(m)), os.path.dirname(m)))
    return sorted(set(packs), key=lambda x: x[0])

def find_other_packs(mods=MODS):
    """→ [(显示名, 包目录, ContentPackFor 的 UniqueID)]：**既不是 CP 也不是 FTM** 的内容包。

    ★ 为什么要有它：这类包**一个都不会被转**。不说话就等于**静默跳过** ——
      而"哪些内容拿不到"恰恰是这个项目最不该藏的东西（同一课在 FTM 的 notes、
      `CustomLocations 展开` 那里各上过一次）。所以语料脚本要**把它们列出来**：
      要么等 `how-to-add-a-framework-channel.md` 那条路把它们接进来，要么让用户知道它们不在产物里。
    """
    out = []
    for m in glob.glob(os.path.join(mods, '**', 'manifest.json'), recursive=True):
        try:
            d = _manifest_load(m)
        except Exception:
            continue
        if not isinstance(d, dict):
            continue
        cpf = d.get('ContentPackFor')
        if not isinstance(cpf, dict):
            continue                      # 没有 ContentPackFor ⇒ 是框架/DLL 本体，不是内容包
        uid = str(cpf.get('UniqueID') or '')
        low = uid.lower()
        if low.startswith('pathoschild.contentpatcher') or low == FTM_UID:
            continue
        out.append((d.get('Name') or os.path.basename(os.path.dirname(m)), os.path.dirname(m), uid))
    return sorted(set(out), key=lambda x: x[0])

def count_dll_mods(mods=MODS):
    """→ 本机**代码模组**（`manifest` 里有 `EntryDll`、没有 `ContentPackFor`）的个数。

    这些**一个都进不了产物** —— 纯 XNB 场景里没有 SMAPI，DLL 跑不起来。
    那是这个项目的**前提**，但"有多少个"仍然该报（用户会问"我的自动机/作弊菜单哪去了"）。
    """
    n = 0
    for m in glob.glob(os.path.join(mods, '**', 'manifest.json'), recursive=True):
        try:
            d = _manifest_load(m)
        except Exception:
            continue
        if not isinstance(d, dict):
            continue
        # ★ 字段名按不区分大小写读：真实语料 `Fish Info` 写的是 `EntryDLL`（SMAPI 照常加载）。
        entry = next((v for k, v in d.items() if k.lower() == 'entrydll'), None)
        if not isinstance(d.get('ContentPackFor'), dict) and entry:
            n += 1
    return n


def framework_volume(path, uid):
    """→ 该非 CP/FTM 内容包的**可数内容量**（读不出来就返回空串）。

    只数格式稳定、能机械识别的对象；不把"JSON 文件数"冒充成内容量。
    """
    low = (uid or '').lower()

    def read(rel):
        try:
            return _manifest_load(os.path.join(path, rel))
        except Exception:
            return None

    if 'alternativetextures' in low:
        total = 0
        for root, _, files in os.walk(path):
            if 'texture.json' not in files:
                continue
            data = read(os.path.relpath(os.path.join(root, 'texture.json'), path))
            if not isinstance(data, dict):
                continue
            try:
                total += int(data.get('Variations') or 0)
            except (TypeError, ValueError):
                pass
            manual = data.get('ManualVariations')
            if isinstance(manual, list):
                total += len(manual)
        return f'纹理变体 {total}' if total else ''

    if 'customcompanions' in low:
        return '%d 个同伴' % sum(
            1 for root, _, files in os.walk(path) if 'companion.json' in files)

    if 'solidfoundations' in low:
        return '%d 个建筑' % sum(
            1 for root, _, files in os.walk(path) if 'building.json' in files)

    if 'mailframework' in low:
        data = read('mail.json')
        if isinstance(data, list):
            return '%d 封信' % len(data)
        if isinstance(data, dict):
            return '%d 封信' % len(data)
        return ''

    if 'alja.cccb' in low:
        data = read('content.json')
        if isinstance(data, dict):
            return '%d 个捆绑包变体' % sum(
                1 for v in data.values() if isinstance(v, dict))
        return ''

    if 'moregrass' in low:
        n = 0
        for season in ('spring', 'summer', 'fall', 'winter'):
            d = os.path.join(path, season)
            if os.path.isdir(d):
                n += len([f for f in os.listdir(d) if f.lower().endswith('.png')])
        return '%d 张草贴图' % n if n else ''

    if 'bushbloom' in low:
        data = read('content.json')
        if isinstance(data, list):
            return '%d 个灌木定义' % len(data)
        return ''

    if 'buslocations' in low:
        return '1 个巴士站点' if isinstance(read('content.json'), dict) else ''

    if 'trainstation' in low:
        data = read('TrainStops.json')
        if isinstance(data, dict) and isinstance(data.get('TrainStops'), list):
            return '%d 个火车站点' % len(data['TrainStops'])
        return ''

    return ''


def unparseable_manifests(mods=MODS):
    """→ [(包目录, 异常)]：**连容错解析都读不出来**的 manifest。

    ★ 为什么要有它：`manifest.json` 读不出来时，以前是 `continue` —— **整包静默消失**。
    实测（2026-10-11 合并语料）：53 个 manifest 只有容错解析能读（带注释/尾逗号），其中
    约 30 个 CP 包 + 5 个 FTM 包因此从没被转过，而"没转清单"里也**没有**它们。
    ⇒ 现在：解析一律走 `jsonc.load`；**真读不出来的必须报出来**（不猜、但也不许不说）。
    """
    out = []
    for m in glob.glob(os.path.join(mods, '**', 'manifest.json'), recursive=True):
        try:
            _manifest_load(m)
        except Exception as e:
            out.append((os.path.dirname(m), '%s: %s' % (type(e).__name__, e)))
    return sorted(out)
