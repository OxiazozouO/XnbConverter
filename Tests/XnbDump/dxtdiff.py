import sys
from collections import Counter


def payload(path):
    d = open(path, "rb").read()
    flags = d[5]
    if flags & 0xC0:
        raise SystemExit("只支持未压缩的 XNB")
    end = int.from_bytes(d[6:10], "little")
    return d[10:end]


def r7(b, p):
    v = 0
    s = 0
    while True:
        c = b[p]
        p += 1
        v |= (c & 0x7F) << s
        s += 7
        if not (c & 0x80):
            return v, p


def texture_region(b, fmt):
    """返回 (宽, 高, 纹理数据起始偏移)"""
    p = 0
    n, p = r7(b, p)
    names = []
    for _ in range(n):
        L, p = r7(b, p)
        names.append(b[p:p + L].decode("utf8", "replace"))
        p += L + 4
    _, p = r7(b, p)          # 共享资源数
    _, p = r7(b, p)          # 根对象类型索引
    if "SpriteFont" in names[0]:
        _, p = r7(b, p)      # SpriteFont 里第一个嵌套对象就是 Texture2D
    surface, p = int.from_bytes(b[p:p + 4], "little", signed=True), p + 4
    w = int.from_bytes(b[p:p + 2], "little"); p += 2
    p += 2                                     # 内容宽
    h = int.from_bytes(b[p:p + 2], "little"); p += 2
    p += 2                                     # 内容高
    p += 4                                     # mip 数
    ds = int.from_bytes(b[p:p + 4], "little"); p += 4
    return surface, w, h, p, ds, names[0]


A = payload(sys.argv[1])
B = payload(sys.argv[2])
surface, w, h, off, ds, name = texture_region(A, 0)
print(f"{name}  SurfaceFormat={surface}  纹理 {w}x{h}  数据 {ds} 字节  起始偏移 {off}")
print(f"起始偏移 % 16 = {off % 16}")

blk = ds // 16
diff_blocks = []
alpha_diff = color_diff = 0
ep_same = 0
for i in range(blk):
    o = off + i * 16
    a = A[o:o + 16]
    b = B[o:o + 16]
    if a == b:
        continue
    diff_blocks.append((i, a, b))
    if a[:8] != b[:8]:
        alpha_diff += 1
    if a[8:] != b[8:]:
        color_diff += 1
    if a[8:12] == b[8:12]:
        ep_same += 1

print(f"\n块总数 {blk}   有差异 {len(diff_blocks)}"
      f"   alpha 半边不同 {alpha_diff}   颜色半边不同 {color_diff}"
      f"   颜色端点相同 {ep_same}")

# 统计差异块里，alpha 具体差在哪
print("\n前 8 个差异块:")
for i, a, b in diff_blocks[:8]:
    print(f"  块#{i}")
    print(f"    原始 {a.hex(' ')}")
    print(f"    重打 {b.hex(' ')}")
    da = [x for x in range(16) if a[:8] != b[:8]]
    print(f"    alpha 半边相同={a[:8]==b[:8]}   颜色端点相同={a[8:12]==b[8:12]}   索引相同={a[12:]==b[12:]}")
