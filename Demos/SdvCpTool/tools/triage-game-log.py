#!/usr/bin/env python3
"""把一轮实机的 **SMAPI 日志按错误类别过一遍** —— 装机验收的最后一环。

**为什么要有它**：自检 mod（`tools/GameSmoke/`）只检查它自己覆盖的东西
（地点建不建得起来、地图探针、四季图集族）。**游戏自己**发现的问题写在 SMAPI 日志里，
而日志有十几万字符 ⇒ 没人会一条条看。2026-10-13 就是靠这一步（手搓的版本）逮到两个真 bug：

· `Failed parsing condition 'DAY_OF_MONTH 1-4': '1-4' isn't a valid day of month.`（121 行）
  —— 我们 FTM 翻译器把区间写成了非法 GSQ ⇒ 那些觅食条目的条件整条失效；
· `Failed parsing warp '26 35 Farm {{Query:58}} {{Query:15 + 2}} …'`（10 行）
  —— `{{Query:}}` 只代了内层 ⇒ 残留进产物 ⇒ 整串传送被游戏丢弃。

用法：
    python tools/triage-game-log.py [<日志路径>] [--count 30]
    # 默认读 SMAPI 的 %APPDATA%\\StardewValley\\ErrorLogs\\SMAPI-latest.txt

判读：每一类都应当**有解释**。把类别与结论写进当轮的验证记录 ——
已知且非我们的（包自带的自定义 GSQ、别的框架的动作/触发器、Steam 登录失败…）要明确写出来，
别让"0 行"和"没看"混在一起。
"""
import argparse
import collections
import io
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

DEFAULT_LOG = os.path.join(os.environ.get('APPDATA', ''), 'StardewValley', 'ErrorLogs', 'SMAPI-latest.txt')


def normalise(line):
    """把一行压成"类别"：去掉引号里的具体名字与数字，好把同类聚合起来。"""
    text = re.sub(r'\]\s*', '] ', line.strip())
    text = re.sub(r"'[^']*'", "'…'", text)
    text = re.sub(r'\d+', 'N', text)
    return text[:120]


def main(argv):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('log', nargs='?', default=DEFAULT_LOG)
    ap.add_argument('--count', type=int, default=30)
    args = ap.parse_args(argv[1:])

    if not os.path.isfile(args.log):
        print(f'找不到日志：{args.log}')
        return 2

    text = io.open(args.log, encoding='utf-8', errors='replace').read()
    lines = text.split('\n')
    groups = collections.Counter()
    samples = {}
    for line in lines:
        if not re.search(r'\b(ERROR|WARN)\b', line):
            continue
        key = normalise(line)
        groups[key] += 1
        samples.setdefault(key, line.strip()[:220])

    total = sum(groups.values())
    print(f'日志：{args.log}')
    print(f'行数 {len(lines)}；ERROR/WARN **{total}** 行，归为 {len(groups)} 类\n')
    for key, n in groups.most_common(args.count):
        print(f'{n:>5}  {key}')
        print(f'         例：{samples[key]}')
    if len(groups) > args.count:
        print(f'\n…… 另有 {len(groups) - args.count} 类（--count 调大）')
    print('\n⚠ 每一类都要**有解释**：是我们的（改）／包自带的（记档）／别的框架的（记档）——'
          '\n   写进当轮验证记录，别让"0 行"与"没看"混在一起。')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
