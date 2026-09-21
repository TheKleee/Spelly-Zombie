"""Steam achievement icons from Noto Emoji PNGs.

Reads icons.json next to this file. For every entry it writes
  out/<API>.jpg          achieved, colour on the side's background
  out/<API>_locked.jpg   unachieved, grayscale and dimmed
and out/_sheet.png, one contact sheet to eyeball the set.

Emoji files are looked up as emoji_u<code>.png in the "sources" folders
(the project's sz-emoji folder first, then downloads/ next to this file).
An entry with "hat" wears that second emoji on its head, tilted by
"hat_tilt" degrees (the Golem Lord's crown). An entry with "crowd" adds up
to three more emoji and draws them all in two rows (a group of friends).
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


def card(size, rgb):
    """Solid card with a soft inner border."""
    img = Image.new("RGB", (size, size), tuple(rgb))
    d = ImageDraw.Draw(img)
    dark = tuple(max(0, c - 60) for c in rgb)
    d.rectangle([4, 4, size - 5, size - 5], outline=dark, width=6)
    return img


def tile(size, rgb, emoji, emoji_px):
    """The card with the emoji centred."""
    img = card(size, rgb)
    em = Image.open(emoji).convert("RGBA")
    em.thumbnail((emoji_px, emoji_px), Image.LANCZOS)
    x = (size - em.width) // 2
    y = (size - em.height) // 2
    img.paste(em, (x, y), em)
    return img


def fit(path, px):
    """The emoji cropped to its drawing and scaled to fit px."""
    em = Image.open(path).convert("RGBA")
    em = em.crop(em.split()[3].getbbox())
    em.thumbnail((px, px), Image.LANCZOS)
    return em


def worn(size, rgb, emoji, hat, emoji_px, tilt):
    """The card with the emoji wearing a second one on its head."""
    img = card(size, rgb)
    body = fit(emoji, int(emoji_px * 0.83))
    hat_px = int(emoji_px * 0.55)
    top = fit(hat, hat_px)
    if tilt:
        top = top.rotate(tilt, resample=Image.BICUBIC, expand=True)
    overlap = int(hat_px * 0.315)
    y = (size - (body.height + top.height - overlap)) // 2
    x = (size - body.width) // 2
    img.paste(body, (x, y + top.height - overlap), body)
    img.paste(top, (x + int(body.width * 0.52) - top.width // 2, y), top)
    return img


def crowd(size, rgb, emojis, emoji_px):
    """The card with up to four emoji in two rows, each row standing on one line."""
    img = card(size, rgb)
    px = int(emoji_px * 0.5)
    gap = int(px * 0.08)
    ems = [fit(p, px) for p in emojis[:4]]
    rows = [r for r in (ems[:2], ems[2:]) if r]
    tall = [max(e.height for e in r) for r in rows]
    y = (size - (sum(tall) + gap * (len(rows) - 1))) // 2
    for r, h in zip(rows, tall):
        x = (size - (sum(e.width for e in r) + gap * (len(r) - 1))) // 2
        for e in r:
            img.paste(e, (x, y + h - e.height), e)
            x += e.width + gap
        y += h + gap
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
        hat = find(e["hat"], spec["sources"]) if e.get("hat") else None
        if e.get("hat") and hat is None:
            missing.append((e["api"], "emoji_u%s.png" % e["hat"]))
            continue
        group = [find(c, spec["sources"]) for c in e.get("crowd", [])]
        if None in group:
            missing.extend((e["api"], "emoji_u%s.png" % c)
                           for c, p in zip(e["crowd"], group) if p is None)
            continue
        rgb = spec["backgrounds"][e.get("side", "both")]
        on = (crowd(size, rgb, [path] + group, emoji_px) if group
              else worn(size, rgb, path, hat, emoji_px, float(e.get("hat_tilt", 0))) if hat
              else tile(size, rgb, path, emoji_px))
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
        print("source: https://github.com/googlefonts/noto-emoji/tree/main/2D/png/512")
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
