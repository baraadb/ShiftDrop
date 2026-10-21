"""Builds the Microsoft Store tile and logo images from assets/shiftdrop-1024.png."""
from PIL import Image
import os
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = Image.open(os.path.join(root, 'assets', 'shiftdrop-1024.png')).convert('RGBA')
src = src.crop(src.getbbox())
out = os.path.join(root, 'installer', 'msix', 'Assets')
os.makedirs(out, exist_ok=True)
for f in os.listdir(out):
    if f.endswith('.png'): os.remove(os.path.join(out, f))

def place(w, h, fill):
    """Icon centred on a transparent w×h canvas, its longest side = fill × the shorter canvas side."""
    canvas = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    side = max(1, round(min(w, h) * fill))
    scale = side / max(src.size)
    icon = src.resize((max(1, round(src.width * scale)), max(1, round(src.height * scale))), Image.LANCZOS)
    canvas.alpha_composite(icon, ((w - icon.width) // 2, (h - icon.height) // 2))
    return canvas

def save(img, name): img.save(os.path.join(out, name), optimize=True)

for scale in (100, 125, 150, 200, 400):
    k = scale / 100
    save(place(round(44 * k), round(44 * k), 0.84), f'Square44x44Logo.scale-{scale}.png')
    save(place(round(150 * k), round(150 * k), 0.62), f'Square150x150Logo.scale-{scale}.png')
    save(place(round(310 * k), round(150 * k), 0.62), f'Wide310x150Logo.scale-{scale}.png')
    save(place(round(71 * k), round(71 * k), 0.70), f'SmallTile.scale-{scale}.png')
    save(place(round(310 * k), round(310 * k), 0.55), f'LargeTile.scale-{scale}.png')
    save(place(round(50 * k), round(50 * k), 0.90), f'StoreLogo.scale-{scale}.png')
for size in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
    img = place(size, size, 0.94 if size <= 32 else 0.88)
    save(img, f'Square44x44Logo.targetsize-{size}.png')
    save(img, f'Square44x44Logo.targetsize-{size}_altform-unplated.png')
    save(img, f'Square44x44Logo.targetsize-{size}_altform-lightunplated.png')
print(len(os.listdir(out)), 'images')
