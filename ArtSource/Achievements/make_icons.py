"""Steam achievement icons from Noto Emoji PNGs.

Reads icons.json next to this file. For every entry it writes
  out/<API>.jpg          achieved, colour on the side's background
  out/<API>_locked.jpg   unachieved, grayscale and dimmed
and out/_sheet.png, one contact sheet to eyeball the set.

Emoji files are looked up as emoji_u<code>.png in the "sources" folders
(the project's sz-emoji folder first, then downloads/ next to this file).
Missing files are listed at the end; nothing else stops.
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")


def find(code, sources):
    name = "emoji_u%s.png" % code
    for src in sources:
        p = os.path.normpath(os.path.join(HERE, src, name))
        if os.path.exists(p):
            return p
    return None


def tile(size, rgb, emoji, emoji_px):
    """Solid card with a soft inner border and the emoji centred."""
    img = Image.new("RGB", (size, size), tuple(rgb))
    d = ImageDraw.Draw(img)
    dark = tuple(max(0, c - 60) for c in rgb)
    d.rectangle([4, 4, size - 5, size - 5], outline=dark, width=6)
    em = Image.open(emoji).convert("RGBA")
    em.thumbnail((emoji_px, emoji_px), Image.LANCZOS)
    x = (size - em.width) // 2
    y = (size - em.height) // 2
    img.paste(em, (x, y), em)
    return img


def locked(img):
    g = ImageOps.grayscale(img)
    g = g.point(lambda v: int(v * 0.55 + 40))  # dim, keep it readable
    return g.convert("RGB")


def main():
    spec = json.load(open(os.path.join(HERE, "icons.json"), encoding="utf-8"))
    size = int(spec.get("size", 256))
    emoji_px = int(spec.get("emoji_px", 196))
    os.makedirs(OUT, exist_ok=True)
    made, missing, sheet = [], [], []
    for e in spec["icons"]:
        path = find(e["emoji"], spec["sources"])
        if path is None:
            missing.append((e["api"], "emoji_u%s.png" % e["emoji"]))
            continue
        rgb = spec["backgrounds"][e.get("side", "both")]
        on = tile(size, rgb, path, emoji_px)
        off = locked(on)
        on.save(os.path.join(OUT, e["api"] + ".jpg"), quality=92)
        off.save(os.path.join(OUT, e["api"] + "_locked.jpg"), quality=92)
        made.append(e["api"])
        sheet.append((e["api"], on, off))

    if sheet:
        cols = 4
        cell = size // 2
        rows = (len(sheet) + cols - 1) // cols
        page = Image.new("RGB", (cols * (cell * 2 + 24) + 24, rows * (cell + 40) + 24), (40, 36, 32))
        d = ImageDraw.Draw(page)
        for i, (api, on, off) in enumerate(sheet):
            x = 24 + (i % cols) * (cell * 2 + 24)
            y = 24 + (i // cols) * (cell + 40)
            page.paste(on.resize((cell, cell)), (x, y))
            page.paste(off.resize((cell, cell)), (x + cell + 4, y))
            d.text((x, y + cell + 6), api, fill=(230, 220, 200))
        page.save(os.path.join(OUT, "_sheet.png"))

    print("made %d icon pairs in %s" % (len(made), OUT))
    if missing:
        print("missing emoji files (drop them in sz-emoji or downloads/):")
        for api, name in missing:
            print("  %s  %s" % (name, api))
        print("source: https://github.com/googlefonts/noto-emoji/tree/main/png/512")
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
