"""Draws the ShiftDrop icon (a 7-slot wheel whose two bottom-right slots became a plus) at every size."""
import math, sys
from PIL import Image, ImageDraw

BG = (11, 18, 32, 255)
SLOTS = {0: "#3B82F6", 1: "#60A5FA", 4: "#2563EB", 5: "#3B82F6", 6: "#93C5FD"}
PLUS = (255, 255, 255, 255)

def hex2rgba(h):
    h = h.lstrip("#"); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4)) + (255,)

def render(size, ss=8):
    S = size * ss
    k = S / 256.0
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, S - 1, S - 1], radius=58 * k, fill=BG)
    cx = cy = 128 * k
    R, r = 108 * k, 58 * k
    step = 360 / 7
    gap = 3 if size >= 32 else 4.5  # wider gaps survive small sizes
    for i, col in SLOTS.items():
        c = -90 + i * step
        a0, a1 = c - step / 2 + gap, c + step / 2 - gap
        pts = []
        n = 64
        for j in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * j / n)
            pts.append((cx + R * math.cos(a), cy + R * math.sin(a)))
        for j in range(n, -1, -1):
            a = math.radians(a0 + (a1 - a0) * j / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        d.polygon(pts, fill=hex2rgba(col))
    # plus: bars 72 x 22, centred at (191.5, 178)
    px, py, L, T = 191.5 * k, 178 * k, 72 * k, 22 * k
    if size <= 20:  # thicker plus for the tray
        L, T = 80 * k, 30 * k
    d.rounded_rectangle([px - L / 2, py - T / 2, px + L / 2, py + T / 2], radius=T / 2, fill=PLUS)
    d.rounded_rectangle([px - T / 2, py - L / 2, px + T / 2, py + L / 2], radius=T / 2, fill=PLUS)
    return img.resize((size, size), Image.LANCZOS)

out = sys.argv[1]
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [render(s) for s in sizes]
imgs[-1].save(out + "/shiftdrop.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
render(512).save(out + "/shiftdrop-512.png")
render(256).save(out + "/shiftdrop-256.png")
print("ok")
