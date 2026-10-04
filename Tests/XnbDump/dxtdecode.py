import sys
from collections import Counter


def payload(path):
    d = open(path, "rb").read()
    if d[5] & 0xC0:
        raise SystemExit("只支持未压缩的 XNB")
    return d[10:int.from_bytes(d[6:10], "little")]


def r7(b, p):
    v = 0
    s = 0
    while True:
        c = b[p]; p += 1; v |= (c & 0x7F) << s; s += 7
        if not (c & 0x80):
            return v, p


def texture_region(b):
    p = 0
    n, p = r7(b, p)
    names = []
    for _ in range(n):
        L, p = r7(b, p)
        names.append(b[p:p + L].decode("utf8", "replace"))
        p += L + 4
    _, p = r7(b, p)      # 共享资源数
    _, p = r7(b, p)      # 根对象类型索引
    if "SpriteFont" in names[0]:
        _, p = r7(b, p)  # SpriteFont 里第一个嵌套对象
    surface = int.from_bytes(b[p:p + 4], "little", signed=True); p += 4
    w = int.from_bytes(b[p:p + 2], "little"); p += 4
    h = int.from_bytes(b[p:p + 2], "little"); p += 4
    p += 4
    ds = int.from_bytes(b[p:p + 4], "little"); p += 4
    return surface, w, h, p, ds


def unpack565(c):
    r = (c >> 11) & 0x1F
    g = (c >> 5) & 0x3F
    b = c & 0x1F
    return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2))


def decode_dxt3(blk):
    alphas = []
    for i in range(8):
        alphas.append((blk[i] & 0xF) * 17)
        alphas.append((blk[i] >> 4) * 17)
    p0 = unpack565(blk[8] | (blk[9] << 8))
    p1 = unpack565(blk[10] | (blk[11] << 8))
    pal = [p0, p1,
           tuple((2 * p0[k] + p1[k]) // 3 for k in range(3)),
           tuple((p0[k] + 2 * p1[k]) // 3 for k in range(3))]
    idx = int.from_bytes(blk[12:16], "little")
    return [pal[(idx >> (2 * i)) & 3] + (alphas[i],) for i in range(16)]


A = payload(sys.argv[1])
B = payload(sys.argv[2])
surface, w, h, off, ds = texture_region(A)
print(f"SurfaceFormat={surface} 纹理 {w}x{h} 数据 {ds} 字节 起始偏移 {off}")

same_decoded = diff_decoded = 0
kinds = Counter()
samples = []
for i in range(ds // 16):
    o = off + i * 16
    a, b = A[o:o + 16], B[o:o + 16]
    if a == b:
        continue
    da, db = decode_dxt3(a), decode_dxt3(b)
    if da == db:
        same_decoded += 1
        kinds["解码结果相同"] += 1
        if len(samples) < 6:
            samples.append((i, a, b, da, db))
    else:
        diff_decoded += 1
        kinds["解码结果不同"] += 1
        if len(samples) < 5:
            samples.append((i, a, b, da, db))

print(f"\n差异块: 解码结果相同 {same_decoded}   解码结果不同 {diff_decoded}")
for k, v in kinds.most_common():
    print(f"   {k}: {v}")
for i, a, b, da, db in samples:
    print(f"\n  块#{i}")
    print(f"    原始 {a.hex(' ')}")
    print(f"    重打 {b.hex(' ')}")
    for k in range(16):
        mark = "  " if da[k] == db[k] else "<<"
        print(f"      px{k:<2} 原始{da[k]}  重打{db[k]} {mark}")
