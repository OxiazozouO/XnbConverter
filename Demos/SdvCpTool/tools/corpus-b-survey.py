#!/usr/bin/env python3
"""
Task 13a：把 SdvCpTool 跑遍语料 B（Steam 目录）的全部 CP 内容包。

背景：我们此前所有的判定/优先级/预期数字，样本量都是 1（RSV 一个包）。
D:\sdv_merged* 共 22 个目录，全是 RSV 的跑。语料 B 的 32 个 CP 包一次都没跑过。

本脚本只统计、不修——把问题列出来排序。
产物：D:\tmp\corpus_b\<slug>\  （每个包一个目录）
汇总：D:\tmp\corpus_b\SUMMARY.json

三个可调项（都是环境变量）：
  · `CORPUS_OUT` —— 输出目录（改代码后换一个，就能与上一轮逐项对比）；
  · `CORPUS_FOCUS=<清单文件>` —— **只跑清单里列出的包**（见 `tools/corpus-focus.txt`）。
    32 包全量实测 **1417s ≈ 24 分钟**，其中 10 个包一条「该写没写」都没有；
    焦点集 23 包 + `CORPUS_JOBS=4` 实测 **9m34s**，且**逐包逐指标与全量一致**（已核）。
    ⚠ **焦点集只用来提速，不能用来下结论** —— 正式对比必须跑全量。
  · `CORPUS_JOBS=<n>` —— 并行 n 个包。**每个执行线程一棵自己的 scratch 树**
    （早先按「任务号 % n」命名是错的：那只是名义分派，线程池会同时跑两个同号任务，
    它们互相删树 ⇒ 实测 3 条「进程无法访问该文件」且数字整体偏移）。
"""
import json, os, glob, shutil, subprocess, sys, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cppacks  # noqa: E402  （CP 包清单：与 roundtrip-all.py 共用一份）

# 控制台默认是 GBK，报告里的中文/替换字符会让 print 直接抛 UnicodeEncodeError
# （实测：第 1 个包就因为 stderr 里的 U+FFFD 中断了整轮普查）。强制 UTF-8。
try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'
TREE = r'D:\sdv_tree'
# 输出目录可覆盖：改代码后跑 `CORPUS_OUT=D:\tmp\corpus_b3` 就能与上一轮逐项对比。
OUT = os.environ.get('CORPUS_OUT') or r'D:\tmp\corpus_b'
REPO = r'D:\SdvCpTool'
HASHES = r'D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json'
PER_PACK_TIMEOUT = 900  # 15 分钟/包

# `CORPUS_SHARED_TREE=1`：用**已编译的 exe** + 一份**共享 scratch 树**，每包跑完即删。
# 为什么要有这个模式：每包一份整树拷贝（~600MB）x 32 个包会把盘撑爆（实测 D: 直接 100%），
# 而整树内容对统计没有意义 —— 报告里要的是判定与写入的**条数**。
SHARED = os.environ.get('CORPUS_SHARED_TREE') == '1'
# `SDVCP_EXE=<另一份 exe>`：改代码前后**同参数对比**用 —— 基线那份从
# `git worktree`（HEAD）里单独编译，两轮各自的产物互不干扰。
EXE = os.environ.get('SDVCP_EXE') or os.path.join(
    REPO, 'src', 'SdvCpTool.Cli', 'bin', 'Release', 'net8.0', 'SdvCpTool.Cli.exe')
SCRATCH = os.path.join(OUT, '_scratch_tree')

# `CORPUS_FOCUS=<清单文件>`：**只跑文件里列出的包**（每行一个**子串**，`#` 开头是注释）。
# 为什么要有它：整轮 32 包实测 **1417s（24 分钟）**，而其中 **10 个包一条「该写没写」都没有**
# （见 `tools/corpus-focus.txt` 的说明）—— 迭代时没必要每次陪跑。
# ⚠ **下结论之前仍要跑全量**：焦点集只是提速，不是替代。
FOCUS_FILE = os.environ.get('CORPUS_FOCUS')

# `CORPUS_JOBS=<n>`：并行跑 n 个包（每个 worker 一份自己的 scratch 树，互不干扰）。
# 单包耗时被「整树拷贝 ~600MB」压在 ~30s 的地板上，串行 32 包就是 24 分钟；
# 并行才是真正把墙钟压下来的那一步。默认 1 = 老行为。
JOBS = max(1, int(os.environ.get('CORPUS_JOBS') or '1'))


def focus_patterns():
    if not FOCUS_FILE:
        return None
    out = []
    with open(FOCUS_FILE, encoding='utf-8') as fh:
        for line in fh:
            line = line.split('#')[0].strip()
            if line:
                out.append(line)
    return out


def find_cp_packs():
    """CP 内容包清单 —— 与 `roundtrip-all.py` 共用一份（见 `tools/cppacks.py`）。"""
    return cppacks.find_cp_packs(MODS)


def slug(s):
    out = ''.join(c if (c.isalnum() or c in '-_') else '_' for c in s)[:48]
    return out or 'pack'


def run_one(i, total, name, path, worker):
    """跑一个包，返回 (退出码, 用时)。产物写进 OUT/<slug>/report.json。"""
    d = os.path.join(OUT, slug(name))
    rep = os.path.join(d, 'report.json')
    if os.path.isfile(rep):
        print(f'[{i}/{total}] 跳过（已完成）: {name}', flush=True)
        return ('SKIP', 0.0)

    os.makedirs(d, exist_ok=True)
    print(f'[{i}/{total}] 跑: {name}', flush=True)
    if SHARED:
        # ⚠ scratch 目录必须**按执行线程**命名，不能按「任务编号 % 并行度」——
        #   后者只是**名义**分派，线程池完全可能同时跑两个同号的任务，
        #   于是它们互相删对方的树（实测：3 条「写入时异常：进程无法访问该文件」，
        #   而且数字整体偏移）。`get_ident()` 保证一个线程一棵树。
        import threading
        scratch = f'{SCRATCH}_{threading.get_ident()}'
        shutil.rmtree(scratch, ignore_errors=True)
        cmd = [EXE, 'convert', '--pack', path, '--tree', TREE, '--mods', MODS,
               '--out', scratch, '--report', rep]
    else:
        # 同样用**已编译的 exe**（`dotnet run` 每次都要过一遍 MSBuild，白等十几秒）。
        cmd = [EXE, 'convert', '--pack', path, '--tree', TREE, '--mods', MODS,
               '--out', os.path.join(d, 'tree'), '--report', rep]
    if os.path.isfile(HASHES):
        cmd += ['--content-hashes', HASHES]

    t0 = time.time()
    try:
        r = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True,
                           timeout=PER_PACK_TIMEOUT, encoding='utf-8', errors='replace')
        rc, secs = r.returncode, time.time() - t0
        err = (r.stderr or '')[-800:]
    except subprocess.TimeoutExpired:
        rc, secs, err = 'TIMEOUT', time.time() - t0, ''
    print(f'[{i}/{total}] 退出码={rc} 用时={secs:.0f}s  {name}', flush=True)
    if err.strip():
        print(f'[{i}/{total}] stderr: {err.strip()[:300]}', flush=True)
    if SHARED:
        shutil.rmtree(f'{SCRATCH}_{threading.get_ident()}', ignore_errors=True)   # 跑完即删，别让整树占盘
    return (rc, secs)


def main():
    os.makedirs(OUT, exist_ok=True)
    packs = find_cp_packs()
    patterns = focus_patterns()
    if patterns is not None:
        packs = [p for p in packs if any(pat.lower() in p[0].lower() or pat.lower() in p[1].lower()
                                         for pat in patterns)]
        print(f'CORPUS_FOCUS：按 {len(patterns)} 条子串筛出 {len(packs)} 个包', flush=True)
    print(f'发现 {len(packs)} 个 CP 内容包；并行度 {JOBS}', flush=True)

    summary = []
    results = {}
    if JOBS == 1:
        for i, (name, path) in enumerate(packs, 1):
            results[name] = run_one(i, len(packs), name, path, i)
    else:
        # 每个 worker 用自己的 scratch 树，互不干扰；报告写各自的包目录。
        from concurrent.futures import ThreadPoolExecutor
        with ThreadPoolExecutor(max_workers=JOBS) as pool:
            futs = {pool.submit(run_one, i, len(packs), name, path, worker=i % JOBS): name
                    for i, (name, path) in enumerate(packs, 1)}
            for f in futs:
                name = futs[f]
                results[name] = f.result()

    for i, (name, path) in enumerate(packs, 1):
        d = os.path.join(OUT, slug(name))
        rep = os.path.join(d, 'report.json')
        row = {'name': name, 'path': path}
        if os.path.isfile(rep):
            try:
                j = json.load(open(rep, encoding='utf-8'))
                row.update({'total': j['total'], 'convertible': j['convertible'],
                            'blocked': j['blocked'], 'written': j['written'],
                            'writeFailed': j['writeFailed'], 'summary': j['summary']})
            except Exception as e:
                row['error'] = f'报告解析失败: {e}'
        else:
            row['error'] = '没有产出报告'
        summary.append(row)
        json.dump(summary, open(os.path.join(OUT, 'SUMMARY.json'), 'w', encoding='utf-8'),
                  ensure_ascii=False, indent=1)

    print('\n===== 汇总 =====', flush=True)
    ok = [r for r in summary if 'total' in r]
    print(f'跑出报告的: {len(ok)}/{len(summary)}')
    if ok:
        print(f"补丁合计 {sum(r['total'] for r in ok)}，"
              f"可合并 {sum(r['convertible'] for r in ok)}，"
              f"已写入 {sum(r['written'] for r in ok)}，"
              f"写入失败 {sum(r['writeFailed'] for r in ok)}")
        agg = {}
        for r in ok:
            for k, v in (r.get('summary') or {}).items():
                agg[k] = agg.get(k, 0) + v
        print('--- 判定合计 ---')
        for k, v in sorted(agg.items(), key=lambda x: -x[1]):
            if v:
                print(f'  {v:6d}  {k}')
    for r in summary:
        if 'error' in r:
            print(f"  [失败] {r['name']}: {r['error'][:120]}")


if __name__ == '__main__':
    main()
