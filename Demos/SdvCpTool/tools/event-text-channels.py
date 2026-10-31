#!/usr/bin/env python3
"""原版事件脚本里，哪些「文本通道」真的支持 `@`（玩家名占位符）？

背景：CP 的 `{{PlayerName}}` 是**运行时**令牌（`TokenManager.cs`：
`PerPlayerValueProvider(ConditionType.PlayerName, player => player.Name)`），
我们代不了 ⇒ 只能放弃写，或改成**原版等价物**。
原版等价物是 `@`（`InternalConstants.PlayerNamePrefix = "@"`；
`Dialogue.ReplacePlayerEnteredStrings` 里 `str.Replace("@", Utility.FilterUserName(farmer.Name))`）。

但 `@` 只在**部分通道**被替换 —— `NPC.showTextAboveHead` 只调
`Dialogue.applyGenderSwitchBlocks`，不替换 `@`。所以「`@` 能不能用」是**逐通道**的事，
必须实地取证，不能一概而论。

本脚本就做这件事：解析原版 `Data/Events/*.json`（只要基础语言那份），
把 `textAboveHead` / `speak` / `question` / `message` 等通道的**文本实参**取出来，
数一数各自有多少处出现 `@`。`@` 出现过的通道 = 原版自己的作者也这么用 ⇒ 该通道支持。

用法：python tools/event-text-channels.py [事件目录，默认 D:\\sdv_tree\\Data\\Events]
"""
import collections
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# ⚠ **必须先按 `/` 切命令、再在命令内取引号段**。早先用一条宽松正则在整段脚本上找
#   `(?:^|/)([A-Za-z]+)\s+…"…"`，会把 `@` 记到**错误**的命令头上：实测原版
#   `Town.json` 的 `15389722/j 57` 里 `@` 实际在 `speak Lewis "Ah, hello there, @. …"`，
#   却被算进了 `quickQuestion` —— 于是「quickQuestion 支持 @」这个结论是**假的**。
#   命令切分照搬游戏：`Event.ParseCommands` = `ArgUtility.SplitQuoteAware(rawScript, '/', …)`。
#   （JSON 已解码，所以这里没有文件里那些 `\"` 的干扰。）


def split_commands(script):
    out, buf, in_quote = [], [], False
    i = 0
    while i < len(script):
        c = script[i]
        if c == '\\' and i + 1 < len(script):
            buf.append(script[i:i + 2]); i += 2; continue
        if c == '"':
            in_quote = not in_quote
            buf.append(c); i += 1; continue
        if c == '/' and not in_quote:
            out.append(''.join(buf)); buf = []; i += 1; continue
        buf.append(c); i += 1
    out.append(''.join(buf))
    return [s.strip() for s in out if s.strip()]


def text_args(command):
    """一条命令里的**文本实参**：所有引号段；没有引号段的命令（`quickQuestion` 等）取整段其余部分。"""
    parts = command.split(' ', 1)
    rest = parts[1] if len(parts) > 1 else ''
    if not rest:
        return []
    # ★ **先切 `(break)`，再找引号**。顺序反了就会把 `(break)` 之后那些
    #   `\speak Sam "…@…"` 的**内嵌命令**当成这条命令的文本 —— 实测原版
    #   `IslandWest.json 6497423` 的 `quickQuestion` 因此被误记成「文本含 @」。
    rest = rest.split('(break)')[0]
    quoted = re.findall(r'"([^"]*)"', rest)
    return quoted if quoted else [rest]


def scan(root):
    per_cmd = collections.defaultdict(lambda: [0, 0])       # 命令 → [总数, 含@]
    examples = collections.defaultdict(list)
    events = 0
    for f in sorted(os.listdir(root)):
        if not f.endswith('.json'):
            continue
        base = f[:-5]
        if '.' in base:            # 跳过 en-US / zh-CN 等本地化那份
            continue
        try:
            data = json.load(open(os.path.join(root, f), encoding='utf-8-sig'))
        except Exception as e:
            print(f'  [跳过] {f}: {e}')
            continue
        if not isinstance(data, dict):
            continue
        events += len(data)
        for key, script in data.items():
            if not isinstance(script, str):
                continue
            for command in split_commands(script):
                cmd = command.split(' ', 1)[0]
                texts = [t for t in text_args(command) if t]
                if not texts:
                    continue
                slot = per_cmd[cmd]
                slot[0] += 1                                     # 出现次数
                hit = [t for t in texts if '@' in t]
                if hit:
                    slot[1] += 1                                 # 其中文本里带 @ 的次数
                    if len(examples[cmd]) < 3:
                        examples[cmd].append((f, key[:32], hit[0][:60]))
    return per_cmd, examples, events


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else r'D:\sdv_tree\Data\Events'
    per_cmd, examples, events = scan(root)
    print(f'原版事件 {events} 条，带文本实参的命令（命令次数 / 其中文本带 @）：\n')
    print(f'{"命令":22} {"命令次数":>8} {"含@":>6}   支持 @')
    for cmd, (total, at) in sorted(per_cmd.items(), key=lambda x: -x[1][0]):
        mark = 'YES' if at else '-'
        print(f'{cmd:22} {total:8} {at:6}   {mark}')
    print('\n含 @ 的样例：')
    for cmd, ex in examples.items():
        for f, key, text in ex:
            print(f'  [{cmd}] {f} / {key} : {text}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
