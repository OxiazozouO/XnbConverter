#!/usr/bin/env python3
"""
列出语料里 Target / FromFile / Include.FromFile 中「逗号落在 {{…}} 内」的字符串。

为什么要这个清单：`AssetNameList.Split` 把「深度为 0 的逗号」当分隔符，
深度 > 0（即在 `{{…}}` 令牌内）的逗号不当分隔符。改动的**唯一**影响面就是
这些字符串 —— 拿它当「预期差异清单」，跑完受控对比后逐条核对差异是否都落在这上面。

用法：python tools/token-comma-scan.py
"""
import json, os, sys, glob

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cppacks  # noqa: E402
import jsonc  # noqa: E402

MODS = r'D:\Steam\steamapps\common\Stardew Valley\Mods'

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass


def commas_in_token(text: str) -> int:
    """返回落在 {{…}} 内的逗号个数（不配对时按进入令牌后都算）。"""
    depth, n = 0, 0
    i = 0
    while i < len(text):
        if i + 1 < len(text) and text[i] == '{' and text[i + 1] == '{':
            depth += 1; i += 2; continue
        if i + 1 < len(text) and text[i] == '}' and text[i + 1] == '}':
            if depth > 0:
                depth -= 1
            i += 2; continue
        if text[i] == ',' and depth > 0:
            n += 1
        i += 1
    return n


KEYS = ('target', 'fromfile')     # CP 字段名大小写不敏感，这里统一小写比


def walk(node, where, out):
    if isinstance(node, dict):
        for key, value in node.items():
            if key.lower() in KEYS:
                # Target 可以是字符串，也可以是字符串数组；两种情况都要看。
                values = value if isinstance(value, list) else [value]
                for v in values:
                    if isinstance(v, str) and commas_in_token(v) > 0:
                        out.append((where, key, v))
            walk(value, where, out)
    elif isinstance(node, list):
        for item in node:
            walk(item, where, out)


def main():
    packs = cppacks.find_cp_packs()
    total = 0
    by_file = 0
    for name, path in packs:
        for f in glob.glob(os.path.join(path, '**', '*.json'), recursive=True):
                try:
                    data = jsonc.load(f)     # CP 的 json 普遍带注释/尾逗号，标准 json 会直接抛
                except Exception:
                    continue
                if f.lower().endswith('manifest.json'):
                    continue
                found = []
                walk(data, os.path.relpath(f, path), found)
                if found:
                    by_file += 1
                    for where, key, value in found:
                        total += 1
                        print(f'{name}\n  {where}  {key} = {value}')
    print(f'\n合计 {total} 处（分布在 {by_file} 个文件里）')


main()
