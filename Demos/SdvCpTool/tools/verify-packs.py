#!/usr/bin/env python3
"""**一条命令跑完「转换 → 定点回包」**，每包一行汇总。

三个脚本各管一段，这个只管把它们串起来：
  · `run-packs.py`   —— 跑指定的包，**保留产出树**；
  · `pack-check.py`  —— 把产出相对输入树**改过/新增**的文件连 `.config` 一起真的打个包。

**为什么值得串**：「产出能不能用」的硬判据是**真的回包一次**。一个包跑完顺手就能验，
比发布前再回来跑整树靠谱得多 —— 而且**分工明确**：本脚本不解释任何结果，只把两边的输出并排放出来。

用法：
    python tools/verify-packs.py <产出根目录> <包名子串> [<包名子串> …]
    python tools/verify-packs.py D:\\tmp\\v1 野生动物 孩子剧情
"""
import os
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)

# 回包**分批**打：大包（SVE 近 3000 个改过的文件）分段落盘、看得见进度，不必等一整轮才有输出
# （实测小包一批几秒，SVE 那种批次几分钟）。小包本来就一批，无差别。`PACK_CHECK_CHUNK` 可覆盖。
#
# ⚠ 切法**必须按资产**（`pack-check.py` 里按 stem 分组）：`.json` 与它的 `.config` 切到两批，
#   打包器就没有类型描述符，报的是**假失败**。
CHUNK = int(os.environ.get('PACK_CHECK_CHUNK') or 400)
sys.path.insert(0, HERE)

import glob  # noqa: E402
import json  # noqa: E402


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    out_root, needles = sys.argv[1], sys.argv[2:]
    # ⚠ `run-packs.py` 要求**至少给一个**参数（没有就当打印用法退出），
    #   所以这里不能把 `--all` 吞成空表 —— 要**原样透传**（实测踩过：传出空表，一个包都没转）。
    if needles != ['--all'] and not needles:
        print('至少要给一个包名子串，或 `--all`')
        return 2

    print('=== 1/2 转换（保留产出树）')
    r = subprocess.run([sys.executable, os.path.join(HERE, 'run-packs.py'), out_root] + needles,
                       cwd=REPO, capture_output=True, text=True, encoding='utf-8', errors='replace')
    print((r.stdout or '').strip()[-600:], flush=True)

    print('\n=== 2/2 定点回包')
    for report in sorted(glob.glob(os.path.join(out_root, '*', 'report.json'))):
        tree = os.path.join(os.path.dirname(report), 'tree')
        if not os.path.isdir(tree):
            continue
        data = json.load(open(report, encoding='utf-8'))
        name = os.path.basename(data['pack'])[:30]
        check = subprocess.run([sys.executable, os.path.join(HERE, 'pack-check.py'), tree,
                                '--chunk', str(CHUNK)],
                               cwd=REPO, capture_output=True, text=True,
                               encoding='utf-8', errors='replace', timeout=3600)
        lines = [l.strip() for l in (check.stdout or '').splitlines() if l.strip()]
        # 取**最后一条**：分批打包时每批一行在前，汇总行在最后。
        ok = next((l for l in reversed(lines) if l.startswith('成功')), '?')
        bad = next((l for l in reversed(lines) if l.startswith('失败')), '')
        changed = next((l for l in lines if l.startswith('产出相对输入')), '')
        print(f'  {name:32} {changed:26} {ok}   {bad}', flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
