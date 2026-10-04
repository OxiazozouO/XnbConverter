"""验证 XNA 字体专用 DXT3 编码器能否逐块复现原文件。"""
import sys

src = open(r"D:\XnbConverter\Tests\XnbDump\dxtdecode.py", encoding="utf-8").read()
g = {}
exec(src.split("A = payload(sys.argv[1])")[0], g)
decode_dxt3 = g["decode_dxt3"]

MONO_MAP = [0, 2, 3, 1]      # c0=黑, c1=白
XNA_MAP = [1, 3, 2, 0]       # c0=白, c1=黑（XOR 1）


def nib(v):
    """v in 0..255 的浮点版本: (int)(v/255.0 * 15.0)，再把高 2 位复制到低 2 位"""
    a = int(v / 255.0 * 15.0)
    return (a & 0xC) | (a >> 2)


def encode(px, xna_order):
    """px: 16 个 (r,g,b,a)"""
    out = bytearray(16)
    al = [nib(p[3]) for p in px]
    for i in range(8):
        out[i] = (al[2 * i] | (al[2 * i + 1] << 4)) & 0xFF
    gr = [nib(p[0]) for p in px]
    mp = XNA_MAP if xna_order else MONO_MAP
    if xna_order:
        out[8], out[9], out[10], out[11] = 0xFF, 0xFF, 0x00, 0x00   # c0=白 c1=黑
    else:
        out[8], out[9], out[10], out[11] = 0x00, 0x00, 0xFF, 0xFF   # c0=黑 c1=白
    for i in range(4):
        out[12 + i] = (mp[gr[4 * i + 3] >> 2] << 6) | (mp[gr[4 * i + 2] >> 2] << 4) \
                    | (mp[gr[4 * i + 1] >> 2] << 2) | mp[gr[4 * i] >> 2]
    return bytes(out)


for path in sys.argv[1:]:
    A = g["payload"](path)
    surface, w, h, off, ds = g["texture_region"](A)
    total = ds // 16
    hit_xna = hit_mono = hit_orig = 0
    for i in range(total):
        o = off + i * 16
        orig = A[o:o + 16]
        px = decode_dxt3(orig)
        if encode(px, True) == orig:
            hit_xna += 1
        if encode(px, False) == orig:
            hit_mono += 1
        # 也直接和原块比对（从原块解码再编码）
        hit_orig += 1
    print(f"{path.split(chr(92))[-1]}: {total} 块")
    print(f"   XNA 顺序(c0=白,c1=黑) 复现: {hit_xna}/{total}")
    print(f"   MonoGame 顺序(c0=黑,c1=白) 复现: {hit_mono}/{total}")
