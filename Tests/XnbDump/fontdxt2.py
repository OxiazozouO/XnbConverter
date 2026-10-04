"""验证「字体编码器 + 全透明块写 0」这个变体。"""
import sys

src = open(r"D:\XnbConverter\Tests\XnbDump\dxtdecode.py", encoding="utf-8").read()
g = {}
exec(src.split("A = payload(sys.argv[1])")[0], g)
decode_dxt3 = g["decode_dxt3"]

XNA_MAP = [1, 3, 2, 0]


def nib(v):
    a = int(v / 255.0 * 15.0)
    return (a & 0xC) | (a >> 2)


def encode(px, zero_transparent):
    out = bytearray(16)
    al = [nib(p[3]) for p in px]
    if zero_transparent and all(a == 0 for a in al):
        return bytes(16)                      # 全透明块写全 0
    for i in range(8):
        out[i] = (al[2 * i] | (al[2 * i + 1] << 4)) & 0xFF
    out[8], out[9], out[10], out[11] = 0xFF, 0xFF, 0x00, 0x00
    gr = [nib(p[0]) for p in px]
    for i in range(4):
        out[12 + i] = (XNA_MAP[gr[4 * i + 3] >> 2] << 6) | (XNA_MAP[gr[4 * i + 2] >> 2] << 4) \
                    | (XNA_MAP[gr[4 * i + 1] >> 2] << 2) | XNA_MAP[gr[4 * i] >> 2]
    return bytes(out)


for path in sys.argv[1:]:
    A = g["payload"](path)
    surface, w, h, off, ds = g["texture_region"](A)
    total = ds // 16
    plain = zeroed = 0
    for i in range(total):
        o = off + i * 16
        orig = A[o:o + 16]
        px = decode_dxt3(orig)
        if encode(px, False) == orig:
            plain += 1
        if encode(px, True) == orig:
            zeroed += 1
    name = path.split("\\")[-1]
    print(f"{name:24s} {total} 块   纯字体编码器 {plain}/{total}   全透明块写0变体 {zeroed}/{total}")
