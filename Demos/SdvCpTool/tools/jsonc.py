"""容错 JSONC 解析：尊重字符串字面量地剥注释与尾逗号。"""
import json


def strip_jsonc(s: str) -> str:
    out = []
    i, n = 0, len(s)
    in_str = False
    while i < n:
        c = s[i]
        if in_str:
            out.append(c)
            if c == '\\' and i + 1 < n:
                out.append(s[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
            i += 1
            continue
        if c == '"':
            in_str = True
            out.append(c)
            i += 1
            continue
        if c == '/' and i + 1 < n and s[i + 1] == '/':
            while i < n and s[i] not in '\r\n':
                i += 1
            continue
        if c == '/' and i + 1 < n and s[i + 1] == '*':
            i += 2
            while i + 1 < n and not (s[i] == '*' and s[i + 1] == '/'):
                i += 1
            i += 2
            continue
        out.append(c)
        i += 1
    text = ''.join(out)
    # 反复去尾逗号，直到稳定（去注释可能新产生尾逗号）
    prev = None
    while prev != text:
        prev = text
        text = _drop_trailing_commas(text)
    return text


def _drop_trailing_commas(s: str) -> str:
    out = []
    i, n = 0, len(s)
    in_str = False
    while i < n:
        c = s[i]
        if in_str:
            out.append(c)
            if c == '\\' and i + 1 < n:
                out.append(s[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
            i += 1
            continue
        if c == '"':
            in_str = True
            out.append(c)
            i += 1
            continue
        if c == ',':
            j = i + 1
            while j < n and s[j] in ' \t\r\n':
                j += 1
            if j < n and s[j] in '}]':
                i += 1
                continue
        out.append(c)
        i += 1
    return ''.join(out)


def load(path: str):
    with open(path, encoding='utf-8-sig') as f:
        return json.loads(strip_jsonc(f.read()))
