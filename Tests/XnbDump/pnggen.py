"""生成一批 PNG 变体 + 每个的期望 RGBA，用来验证自研的解码器。

覆盖：颜色类型 0/2/3/4/6、位深 1/2/4/8/16、tRNS、五种过滤、Adam7 交错、多 IDAT。
"""
import os
import struct
import sys
import zlib

OUT = sys.argv[1] if len(sys.argv) > 1 else r"D:\XnbConverter\tmp_png\cases"


def chunk(kind, data):
    return (struct.pack(">I", len(data)) + kind + data
            + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF))


def pack_samples(color_type, bit_depth, rows, width):
    """把每行样本（已经按通道展开的整数）按位深打包成字节"""
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color_type]
    out = []
    for row in rows:
        if bit_depth == 16:
            b = bytearray()
            for v in row:
                b += struct.pack(">H", v)
            out.append(bytes(b))
        elif bit_depth == 8:
            out.append(bytes(row))
        else:
            b = bytearray()
            per = 8 // bit_depth
            for i in range(0, len(row), per):
                acc = 0
                for k in range(per):
                    acc = (acc << bit_depth) | (row[i + k] if i + k < len(row) else 0)
                b.append(acc)
            out.append(bytes(b))
    return out


def filter_rows(raw_rows, bpp, mode):
    """按指定过滤类型（0-4）编码；bpp 是过滤用的字节步长"""
    out = []
    prev = bytes(len(raw_rows[0])) if raw_rows else b""
    for row in raw_rows:
        if mode == 0:
            out.append(bytes([0]) + row)
        else:
            b = bytearray()
            for i in range(len(row)):
                left = row[i - bpp] if i >= bpp else 0
                up = prev[i]
                upleft = prev[i - bpp] if i >= bpp else 0
                if mode == 1:
                    pred = left
                elif mode == 2:
                    pred = up
                elif mode == 3:
                    pred = (left + up) >> 1
                else:
                    p = left + up - upleft
                    pa, pb, pc = abs(p - left), abs(p - up), abs(p - upleft)
                    pred = left if (pa <= pb and pa <= pc) else (up if pb <= pc else upleft)
                b.append((row[i] - pred) & 0xFF)
            out.append(bytes([mode]) + bytes(b))
        prev = row
    return b"".join(out)


ADAM7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
         (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]


def encode_png(width, height, color_type, bit_depth, rows, filters, interlace, plte, trns,
               split_idat=False):
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color_type]
    bpp = max(1, channels * bit_depth // 8)
    packed = pack_samples(color_type, bit_depth, rows, width)

    if interlace == 0:
        data = filter_rows(packed, bpp, filters[0])
    else:
        parts = []
        for (xs, ys, xstep, ystep) in ADAM7:
            pw = (width - xs + xstep - 1) // xstep if width > xs else 0
            ph = (height - ys + ystep - 1) // ystep if height > ys else 0
            if pw == 0 or ph == 0:
                continue
            sub = []
            for y in range(ys, height, ystep):
                row = rows[y]
                sub.append([row[(xs + x * xstep) * channels + c]
                            for x in range(pw) for c in range(channels)])
            sub_packed = pack_samples(color_type, bit_depth, sub, pw)
            parts.append(filter_rows(sub_packed, bpp, filters[0]))
        data = b"".join(parts)

    ihdr = struct.pack(">IIBBBBB", width, height, bit_depth, color_type, 0, 0, interlace)
    out = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
    if plte:
        out += chunk(b"PLTE", plte)
    if trns:
        out += chunk(b"tRNS", trns)
    if split_idat:
        # 切成多段的是**压缩后**的 zlib 流，不是原始扫描线数据
        z = zlib.compress(data, 6)
        for i in range(0, len(z), 64):
            out += chunk(b"IDAT", z[i:i + 64])
    else:
        out += chunk(b"IDAT", zlib.compress(data, 6))
    out += chunk(b"IEND", b"")
    return out


def scale(v, maxv):
    return v * 255 // maxv


CASES = []


def add(name, width, height, color_type, bit_depth, rgba, samples, plte=None, trns=None,
        filters=(0,), interlace=0, split_idat=False):
    png = encode_png(width, height, color_type, bit_depth, samples, filters, interlace,
                     plte, trns, split_idat)
    flat = bytearray()
    for (r, g, b, a) in rgba:
        flat += bytes([r, g, b, a])
    CASES.append((name, width, height, png, bytes(flat)))


def rgba_of_grey(v, maxv, alpha):
    g = scale(v, maxv)
    return (g, g, g, alpha)


# ---- 通用约定 ----
# pixels: 逐像素的元组（长度 = 通道数），按行优先铺开
# rows:   编码用的样本行，每行是展平的通道值
def rows_of(pixels, width, channels):
    out = []
    for y in range(len(pixels) // width):
        row = []
        for x in range(width):
            row.extend(pixels[y * width + x])
        out.append(row)
    return out


W, H = 13, 7          # 故意用非 4 的倍数，抓 stride 问题


def px(x, y):
    return ((x * 17 + y * 3) % 256, (x * 5) % 256, (y * 40) % 256, (x + y) % 256)


# ---- 1. RGBA8，五种过滤 + 多 IDAT + Adam7 ----
rgba_pixels = [px(x, y) for y in range(H) for x in range(W)]
rows_rgba = rows_of(rgba_pixels, W, 4)
for f in range(5):
    add(f"rgba8_filter{f}", W, H, 6, 8, rgba_pixels, rows_rgba, filters=(f,))
add("rgba8_multi_idat", W, H, 6, 8, rgba_pixels, rows_rgba, split_idat=True)
add("rgba8_interlaced", W, H, 6, 8, rgba_pixels, rows_rgba, interlace=1)

# ---- 2. RGB8 ----
rgb_pixels = [((x * 9) % 256, (y * 33) % 256, (x * y) % 256) for y in range(H) for x in range(W)]
add("rgb8", W, H, 2, 8, [c + (255,) for c in rgb_pixels], rows_of(rgb_pixels, W, 3))

# ---- 3. 灰度 8/4/2/1 ----
for depth in (8, 4, 2, 1):
    maxv = (1 << depth) - 1
    vals = [(x * (maxv + 1) // W) % (maxv + 1) for _y in range(H) for x in range(W)]
    expect = [rgba_of_grey(v, maxv, 255) for v in vals]
    add(f"grey{depth}", W, H, 0, depth, expect, rows_of([(v,) for v in vals], W, 1))

# ---- 4. 灰度 + tRNS ----
grey8_vals = [(x * 20) % 256 for _y in range(H) for x in range(W)]
grey8_rows = rows_of([(v,) for v in grey8_vals], W, 1)
expect = [rgba_of_grey(v, 255, 0 if v == 40 else 255) for v in grey8_vals]
add("grey8_trns", W, H, 0, 8, expect, grey8_rows, trns=struct.pack(">H", 40))

# ---- 5. RGB8 + tRNS ----
expect = [(c[0], c[1], c[2], 0 if c == (9, 33, 0) else 255) for c in rgb_pixels]
add("rgb8_trns", W, H, 2, 8, expect, rows_of(rgb_pixels, W, 3),
    trns=struct.pack(">HHH", 9, 33, 0))

# ---- 6/7. 调色板 8 位 / 2 位 + tRNS ----
plte = bytes([(i * 30) % 256 for i in range(12)])
pal_alpha = bytes([255, 200, 128, 0])
pal_idx = [(x + y) % 4 for y in range(H) for x in range(W)]
pal_expect = [(plte[i * 3], plte[i * 3 + 1], plte[i * 3 + 2], pal_alpha[i]) for i in pal_idx]
add("palette8_trns", W, H, 3, 8, pal_expect, rows_of([(i,) for i in pal_idx], W, 1),
    plte=plte, trns=pal_alpha)
add("palette2_trns", W, H, 3, 2, pal_expect, rows_of([(i,) for i in pal_idx], W, 1),
    plte=plte, trns=pal_alpha)

# ---- 8. 灰 + alpha 8 位 ----
ga_pixels = [((x * 11) % 256, (y * 30) % 256) for y in range(H) for x in range(W)]
add("grey_alpha8", W, H, 4, 8, [(v, v, v, a) for (v, a) in ga_pixels],
    rows_of(ga_pixels, W, 2))

# ---- 9. 16 位 ----
g16_vals = [(x * 5000) % 65536 for _y in range(H) for x in range(W)]
add("grey16", W, H, 0, 16, [rgba_of_grey(v >> 8, 255, 255) for v in g16_vals],
    rows_of([(v,) for v in g16_vals], W, 1))

rgba16_pixels = [((x * 5000) % 65536, (y * 9000) % 65536, (x + y) * 1000 % 65536,
                  (x * y) * 300 % 65536) for y in range(H) for x in range(W)]
add("rgba16", W, H, 6, 16,
    [(r >> 8, g >> 8, b >> 8, a >> 8) for (r, g, b, a) in rgba16_pixels],
    rows_of(rgba16_pixels, W, 4))

# ---- 10. 1x1 / 4x4 / 灰度交错 ----
add("rgba8_1x1", 1, 1, 6, 8, [(1, 2, 3, 4)], [[1, 2, 3, 4]])
add("rgba8_4x4", 4, 4, 6, 8, [px(x, y) for y in range(4) for x in range(4)],
    rows_of([px(x, y) for y in range(4) for x in range(4)], 4, 4))
add("grey8_interlaced", W, H, 0, 8,
    [rgba_of_grey(v, 255, 255) for v in grey8_vals], grey8_rows, interlace=1)

os.makedirs(OUT, exist_ok=True)
for (name, w, h, png, flat) in CASES:
    with open(os.path.join(OUT, name + ".png"), "wb") as f:
        f.write(png)
    with open(os.path.join(OUT, name + ".rgba"), "wb") as f:
        f.write(struct.pack(">II", w, h) + flat)
print(f"生成 {len(CASES)} 个用例 -> {OUT}")
