"""Builds Windows .ico files from the 1024 px artwork.
shiftdrop.ico        BMP entries up to 128 px + PNG at 256 px (the standard Windows layout)
shiftdrop-setup.ico  BMP entries only, for tools that can't read PNG icons (Inno Setup)"""
import io, struct, sys
from PIL import Image

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

def bmp_entry(img):
    w, h = img.size
    px = img.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM).tobytes()
    bgra = bytearray(len(px))
    bgra[0::4], bgra[1::4], bgra[2::4], bgra[3::4] = px[2::4], px[1::4], px[0::4], px[3::4]
    mask_row = ((w + 31) // 32) * 4
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(bgra) + mask_row * h, 0, 0, 0, 0)
    return header + bytes(bgra) + bytes(mask_row * h)  # AND mask all zero: alpha decides

def png_entry(img):
    b = io.BytesIO(); img.save(b, "PNG", optimize=True); return b.getvalue()

def write_ico(path, images, png_from):
    entries = [png_entry(im) if im.size[0] >= png_from else bmp_entry(im) for im in images]
    out = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    for im, data in zip(images, entries):
        s = im.size[0]
        out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    open(path, "wb").write(out + b"".join(entries))

src = Image.open(sys.argv[1]).convert("RGBA")
imgs = [src.resize((s, s), Image.LANCZOS) for s in SIZES]
write_ico("assets/shiftdrop.ico", imgs, png_from=256)
write_ico("assets/shiftdrop-setup.ico", imgs, png_from=10_000)
print("ok")
