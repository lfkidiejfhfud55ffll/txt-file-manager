# -*- coding: utf-8 -*-
"""纯标准库绘制应用图标 -> src/app.ico (+ 预览 png)。
画布按 4x/2x 超采样后盒式降采样得到抗锯齿效果。"""
import math
import os
import struct
import zlib

BG_TOP = (59, 130, 246)      # #3B82F6
BG_BOT = (29, 78, 216)       # #1D4ED8
PAGE_TOP = (255, 255, 255)
PAGE_BOT = (238, 242, 247)
FOLD = (203, 213, 225)
LINE1 = (37, 99, 235)
LINE2 = (147, 197, 253)


def sdf_rrect(x, y, cx, cy, hw, hh, r):
    qx = abs(x - cx) - (hw - r)
    qy = abs(y - cy) - (hh - r)
    mx = qx if qx > 0 else 0.0
    my = qy if qy > 0 else 0.0
    return math.hypot(mx, my) + min(max(qx, qy), 0.0) - r


def in_tri(px, py, ax, ay, bx, by, cx, cy):
    d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by)
    d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy)
    d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay)
    neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
    pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    return not (neg and pos)


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t,
            a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t)


def sample(u, v):
    """返回 (r,g,b,a) 0..255, a=0 表示透明"""
    # 底板
    if sdf_rrect(u, v, 0.5, 0.5, 0.485, 0.485, 0.225) > 0:
        return (0, 0, 0, 0)
    col = lerp(BG_TOP, BG_BOT, min(max(v, 0.0), 1.0))

    # 纸张（圆角矩形）
    page = sdf_rrect(u, v, 0.5, 0.505, 0.235, 0.295, 0.045) <= 0
    if page:
        # 右上角折角切掉
        cut = in_tri(u, v, 0.735, 0.21, 0.735, 0.355, 0.585, 0.21)
        if not cut:
            col = lerp(PAGE_TOP, PAGE_BOT, min(max((v - 0.21) / 0.6, 0.0), 1.0))
        else:
            if in_tri(u, v, 0.735, 0.355, 0.735, 0.27, 0.635, 0.27):
                col = FOLD

    # 文本行
    if page:
        for cy, x0, x1, colr in ((0.455, 0.345, 0.655, LINE1),
                                 (0.565, 0.345, 0.655, LINE2),
                                 (0.675, 0.345, 0.565, LINE2)):
            hw = (x1 - x0) / 2.0
            if sdf_rrect(u, v, (x0 + x1) / 2.0, cy, hw, 0.026, 0.026) <= 0:
                col = colr

    return (int(col[0]), int(col[1]), int(col[2]), 255)


def render(size):
    ss = 4 if size <= 64 else 2
    w = size * ss
    acc = [[0, 0, 0, 0] for _ in range(size * size)]
    inv = 1.0 / w
    for py in range(w):
        v = (py + 0.5) * inv
        row_base = (py // ss) * size
        for px in range(w):
            r, g, b, a = sample((px + 0.5) * inv, v)
            idx = row_base + (px // ss)
            cell = acc[idx]
            cell[0] += r * a
            cell[1] += g * a
            cell[2] += b * a
            cell[3] += a
    n = ss * ss
    out = bytearray(size * size * 4)
    for i, cell in enumerate(acc):
        a = cell[3]
        if a <= 0:
            continue
        # 非预乘平均
        out[i * 4 + 0] = min(255, int(cell[0] / a + 0.5))
        out[i * 4 + 1] = min(255, int(cell[1] / a + 0.5))
        out[i * 4 + 2] = min(255, int(cell[2] / a + 0.5))
        out[i * 4 + 3] = min(255, int(a / n + 0.5))
    return bytes(out)


def ico_entry(rgba, size):
    # 32bpp DIB, bottom-up BGRA + AND mask
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, size * size * 4, 0, 0, 0, 0)
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        row = rgba[y * size * 4:(y + 1) * size * 4]
        for x in range(size):
            r, g, b, a = row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]
            xor += bytes((b, g, r, a))
    row_bytes = ((size + 31) // 32) * 4
    andmask = bytes(row_bytes * size)
    data = header + bytes(xor) + andmask
    return data


def png_bytes(rgba, size):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        raw += rgba[y * size * 4:(y + 1) * size * 4]

    def chunk(tag, payload):
        c = struct.pack('>I', len(payload)) + tag + payload
        return c + struct.pack('>I', zlib.crc32(tag + payload) & 0xFFFFFFFF)

    ihdr = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr)
            + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b''))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)
    sizes = [256, 64, 48, 32, 16]
    images = []
    for s in sizes:
        rgba = render(s)
        images.append((s, rgba))
        if s == 256:
            with open(os.path.join(root, 'tools', 'icon_preview.png'), 'wb') as f:
                f.write(png_bytes(rgba, s))

    entries = []
    offset = 6 + 16 * len(images)
    for s, rgba in images:
        data = ico_entry(rgba, s)
        entries.append(struct.pack('<BBBBHHII', 0 if s >= 256 else s, 0 if s >= 256 else s,
                                   0, 0, 1, 32, len(data), offset) + b'')
        offset += len(data)
    out = struct.pack('<HHH', 0, 1, len(images)) + b''.join(entries)
    for s, rgba in images:
        out += ico_entry(rgba, s)
    with open(os.path.join(root, 'src', 'app.ico'), 'wb') as f:
        f.write(out)
    print('wrote app.ico bytes=%d' % len(out))


if __name__ == '__main__':
    main()
