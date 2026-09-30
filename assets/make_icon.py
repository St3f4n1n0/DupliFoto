"""
Draws the DupliFoto icon: two overlapping photos, the front one showing a small landscape.

    pip install pillow
    python assets/make_icon.py      -> assets/DupliFoto.ico (16-256 px) and assets/DupliFoto.png (512 px)
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024  # drawn large, then scaled down with a high-quality filter
HERE = Path(__file__).parent


def rounded_card(size, radius, fill, outline=None, width=0):
    card = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(card).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius, fill=fill, outline=outline, width=width)
    return card


def landscape(size, radius):
    w, h = size
    img = Image.new("RGBA", size)
    d = ImageDraw.Draw(img)
    top, bottom = (84, 160, 232), (182, 222, 250)  # sky
    for y in range(h):
        t = y / h
        d.line([(0, y), (w, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    d.ellipse([w * 0.62, h * 0.14, w * 0.84, h * 0.14 + w * 0.22], fill=(255, 204, 77, 255))  # sun
    d.polygon([(0, h), (0, h * 0.62), (w * 0.30, h * 0.36), (w * 0.58, h * 0.70), (w * 0.58, h)], fill=(46, 125, 91, 255))
    d.polygon([(w * 0.34, h), (w * 0.70, h * 0.50), (w, h * 0.74), (w, h)], fill=(62, 158, 111, 255))
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, h - 1], radius, fill=255)
    img.putalpha(mask)
    return img


def paste_rotated(canvas, layer, center, angle):
    rotated = layer.rotate(angle, resample=Image.BICUBIC, expand=True)
    x, y = int(center[0] - rotated.width / 2), int(center[1] - rotated.height / 2)
    # Soft shadow, on a padded layer so the blur is never cut off.
    pad = int(SIZE * 0.08)
    shadow = Image.new("RGBA", (rotated.width + 2 * pad, rotated.height + 2 * pad), (16, 24, 40, 0))
    alpha = Image.new("L", shadow.size, 0)
    alpha.paste(rotated.getchannel("A").point(lambda a: int(a * 0.30)), (pad, pad))
    shadow.putalpha(alpha.filter(ImageFilter.GaussianBlur(SIZE * 0.022)))
    canvas.alpha_composite(shadow, (x - pad, y - pad + int(SIZE * 0.022)))
    canvas.alpha_composite(rotated, (x, y))


def photo_card(size, radius, faded):
    """A white-framed photo; the faded one is the copy behind."""
    w, h = size
    card = rounded_card(size, radius, fill=(255, 255, 255, 255), outline=(196, 206, 220, 255), width=int(SIZE * 0.010))
    border = int(SIZE * 0.045)
    picture = landscape((w - 2 * border, h - 2 * border), int(radius * 0.6))
    if faded:
        veil = Image.new("RGBA", picture.size, (255, 255, 255, 0))
        veil.putalpha(picture.getchannel("A").point(lambda a: int(a * 0.55)))
        picture.alpha_composite(veil)
    card.alpha_composite(picture, (border, border))
    return card


def draw():
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    card = (int(SIZE * 0.64), int(SIZE * 0.50))
    radius = int(SIZE * 0.06)
    paste_rotated(canvas, photo_card(card, radius, faded=True), (SIZE * 0.43, SIZE * 0.40), 9)
    paste_rotated(canvas, photo_card(card, radius, faded=False), (SIZE * 0.57, SIZE * 0.59), -5)
    return canvas


def main():
    icon = draw()
    icon.resize((512, 512), Image.LANCZOS).save(HERE / "DupliFoto.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    icon.resize((256, 256), Image.LANCZOS).save(HERE / "DupliFoto.ico", sizes=[(s, s) for s in sizes])
    print("Written assets/DupliFoto.ico and assets/DupliFoto.png")


if __name__ == "__main__":
    main()
