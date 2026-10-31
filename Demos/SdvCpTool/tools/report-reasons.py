#!/usr/bin/env python3
"""把多份 `report.json` 的**判定理由**按类聚合 —— 回答「现在到底卡在哪一类」。

**为什么要有它**：这一轮我反复在命令行里手打同一段聚合代码，还打过两次出错的版本
（缓存按包名键、结果串了包，差点把「我的脚本 bug」报成「工具的 bug」）。
固化成脚本，顺便把几个容易踩的坑写死在里面：

  · 索引必须按**每条补丁自己的 `source`** 去取，别用「这一包第一条的 source」——
    CP 包的 `Changes` 分散在多个文件（`content.json` + `assets/*.json`），串了就会张冠李戴；
  · `Include` 进来的文件在报告里是**单独的 source**，按包缓存会读错；
  · 报告里的 `action` 可能与原始 `Action` 大小写不同，比较时统一 lower。

用法：
    python tools/report-reasons.py <报告目录或 report.json...> [--verdict LocalEdit] [--top 20]
    python tools/report-reasons.py <目录> --show-when          # 顺带打印原始 When
"""
import collections
import glob
import json
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jsonc import load as jsonc_load  # noqa: E402


def reports(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            out += glob.glob(os.path.join(p, '*', 'report.json'))
            out += glob.glob(os.path.join(p, '**', 'report.json'), recursive=True)
        else:
            out.append(p)
    return sorted(set(out))


def load_changes(path):
    """读一个 CP 变更文件；宽松解析（有些包里有裸控制字符）。"""
    try:
        return jsonc_load(path)['Changes']
    except Exception:
        try:
            return json.loads(open(path, encoding='utf-8-sig').read(), strict=False)['Changes']
        except Exception:
            return None


def normalize(reason):
    """把理由里的**具体名字**抹掉，只留骨架，才能按类聚合。"""
    r = re.sub(r'（\d+ 组）$', '', (reason or '').strip())
    r = re.sub(r'`[^`]*`', '`X`', r)
    return r


def main():
    # ⚠ 位置参数要**跳过选项后面的值**：`--top 10` 里的 `10` 不是路径。
    #   （第一版没跳，把 `10` 当成报告文件去 open，报了个看不懂的 FileNotFoundError。）
    args, verdict, top, sample, show_when = [], None, 20, 0, '--show-when' in sys.argv
    rest = sys.argv[1:]
    i = 0
    while i < len(rest):
        a = rest[i]
        if a == '--verdict' and i + 1 < len(rest):
            verdict = rest[i + 1]; i += 2; continue
        if a == '--top' and i + 1 < len(rest):
            top = int(rest[i + 1]); i += 2; continue
        if a == '--sample' and i + 1 < len(rest):
            sample = int(rest[i + 1]); i += 2; continue
        if not a.startswith('--'):
            args.append(a)
        i += 1

    files = reports(args)
    if not files:
        print(__doc__)
        return 2

    cache = {}
    by_class = collections.Counter()
    by_verdict = collections.Counter()
    targets = collections.Counter()
    samples = {}
    checked = 0

    for p in files:
        d = json.load(open(p, encoding='utf-8'))
        pack = d.get('pack') or ''
        for x in d.get('patches', []):
            v = x.get('verdict')
            by_verdict[v] += 1
            if verdict and v != verdict:
                continue
            if v in ('Converted', 'ConvertedNoCondition', 'NotApplicable'):
                continue

            reason = x.get('reason') or ''
            cls = normalize(reason)
            by_class[cls] += 1

            # 只对**落在筛选里的**补丁去读原始变更，省一半 IO
            src = os.path.join(pack, x.get('source') or '')
            if src not in cache:
                cache[src] = load_changes(src)
            ch = cache[src]
            if ch and x.get('index', -1) < len(ch):
                c = ch[x['index']]
                checked += 1
                if str(c.get('Action') or '').lower() != str(x.get('action') or '').lower():
                    print(f'  ⚠ Action 不一致：{x.get("source")} #{x.get("index")} '
                          f'原始={c.get("Action")!r} 报告={x.get("action")!r}')
                t = str(c.get('Target') or '')
                # 抹掉具体条目名，只留资产
                targets[t.split(',')[0].split('/')[0] + '/' + (t.split(',')[0].split('/')[1] if '/' in t.split(',')[0] else '')] += 1
                if sample > 0:
                    bucket = samples.setdefault(cls, [])
                    if len(bucket) < sample:
                        body = {k: v for k, v in c.items()
                                if k not in ('LogName', 'Action', 'Target', 'When')}
                        bucket.append((os.path.basename(d['pack'])[:20], c.get('Action'), t[:38],
                                       [k for k in c],
                                       json.dumps(c.get('When'), ensure_ascii=False)[:120],
                                       json.dumps(body, ensure_ascii=False)[:170]))
                elif show_when and cls not in samples:
                    samples[cls] = (d['pack'].split('\\')[-1][:22], t[:44],
                                    json.dumps(c.get('When'), ensure_ascii=False)[:130])

    print(f'{len(files)} 份报告；判定合计：')
    for k, v in by_verdict.most_common():
        if v:
            print(f'  {v:6}  {k}')

    print(f'\n=== {verdict or "全部未写入的"}：按理由聚合（原始变更读到 {checked} 条）===')
    for cls, n in by_class.most_common(top):
        print(f'{n:6}  {cls[:110]}')
        if sample > 0:
            for ex in samples.get(cls, []):
                print(f'        · [{ex[0]}] {ex[1]} {ex[2]}')
                print(f'          字段={ex[3]}')
                print(f'          When={ex[4]}')
                if ex[5] != '{}':
                    print(f'          正文={ex[5]}')
        elif show_when and cls in samples:
            pk, tg, when = samples[cls]
            print(f'        例 [{pk}] {tg}  When={when}')

    if targets:
        print('\n=== 按资产（前 2 段）===')
        for t, n in targets.most_common(top):
            print(f'{n:6}  {t}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
