"""Generates the Barline logo assets (concept A, "bars").

Outputs:
  Assets/Barline.ico                      application icon, 16–256 px
  docs/images/logo.png                    256 px logo for the repository page
  installer/images/wizard-small-<size>.png       installer header image for every DPI

Bars are hinted per size with the same formula the tray icon uses at runtime
(Services/TrayIconFactory.cs), so every size has crisp whole-pixel edges.
Requires Pillow:  py -m pip install pillow
"""
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SUPERSAMPLE = 8

TILE = (22, 24, 30)             # graphite
RIM = (52, 56, 66)              # faint edge of the tile
BAR_TOP = (139, 184, 255)       # light blue
BAR_BOTTOM = (77, 141, 255)     # blue
HEIGHTS = (0.56, 1.0, 0.75)     # left, middle, right bar relative to the tallest


def round_half_up(value: float) -> int:
    return int(value + 0.5)


def bar_rects(box: int):
    """Three bottom-aligned bars inside a square box of `box` pixels."""
    width = max(2, round_half_up(box * 0.1875))
    gap = max(1, round_half_up(box * 0.11))
    total = 3 * width + 2 * gap
    left = (box - total) // 2
    bottom = box - round_half_up(box * 0.125)
    tallest = round_half_up(box * 0.75)
    rects = []
    for index, ratio in enumerate(HEIGHTS):
        x = left + index * (width + gap)
        top = bottom - round_half_up(tallest * ratio)
        rects.append((x, top, x + width, bottom))
    return rects, width


def bars_mask(box: int) -> Image.Image:
    """Alpha mask of the bars at exactly `box` pixels."""
    rects, width = bar_rects(box)
    if width <= 4:
        # Small sizes: square bars, top corners softened by one half-transparent pixel.
        mask = Image.new('L', (box, box), 0)
        draw = ImageDraw.Draw(mask)
        for x0, y0, x1, y1 in rects:
            draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=255)
            mask.putpixel((x0, y0), 110)
            mask.putpixel((x1 - 1, y0), 110)
        return mask

    s = SUPERSAMPLE
    big = Image.new('L', (box * s, box * s), 0)
    draw = ImageDraw.Draw(big)
    radius = max(1, round_half_up(width * 0.3)) * s
    for x0, y0, x1, y1 in rects:
        draw.rounded_rectangle((x0 * s, y0 * s, x1 * s - 1, y1 * s - 1), radius=radius, fill=255)
    return big.resize((box, box), Image.LANCZOS)


def gradient(size: int) -> Image.Image:
    image = Image.new('RGBA', (size, size))
    draw = ImageDraw.Draw(image)
    for y in range(size):
        t = y / max(1, size - 1)
        color = tuple(round(BAR_TOP[i] * (1 - t) + BAR_BOTTOM[i] * t) for i in range(3))
        draw.line((0, y, size, y), fill=color + (255,))
    return image


def app_icon(size: int, inset: int | None = None) -> Image.Image:
    """Graphite rounded tile with blue bars and a faint rim that separates the tile
    from dark backgrounds (dark Start menu, dark installer)."""
    s = SUPERSAMPLE
    if inset is None:
        inset = 0 if size <= 24 else round_half_up(size * 0.04)
    radius = max(2, round_half_up(size * 0.22))
    box_outer = (inset * s, inset * s, (size - inset) * s - 1, (size - inset) * s - 1)

    tile_mask = Image.new('L', (size * s, size * s), 0)
    ImageDraw.Draw(tile_mask).rounded_rectangle(box_outer, radius=radius * s, fill=255)
    rim_mask = None
    if size >= 32:
        # One-pixel inner rim: the outer shape minus the shape inset by one pixel.
        inner = Image.new('L', (size * s, size * s), 0)
        x0, y0, x1, y1 = box_outer
        ImageDraw.Draw(inner).rounded_rectangle(
            (x0 + s, y0 + s, x1 - s, y1 - s), radius=(radius - 1) * s, fill=255)
        rim_mask = ImageChops.subtract(tile_mask, inner).resize((size, size), Image.LANCZOS)
    tile_mask = tile_mask.resize((size, size), Image.LANCZOS)

    icon = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    icon.paste(Image.new('RGBA', (size, size), TILE + (255,)), (0, 0), tile_mask)
    if rim_mask is not None:
        icon.paste(Image.new('RGBA', (size, size), RIM + (255,)), (0, 0), rim_mask)

    box = round_half_up(size * 0.66)
    offset = (size - box) // 2
    glyph = bars_mask(box)
    colored = gradient(box)
    icon.paste(colored, (offset, offset), glyph)
    return icon


def main() -> None:
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    images = [app_icon(size) for size in sizes]
    ico_path = ROOT / 'Assets' / 'Barline.ico'
    images[-1].save(ico_path, format='ICO', sizes=[(s, s) for s in sizes], append_images=images[:-1])

    wizard_dir = ROOT / 'installer' / 'images'
    wizard_dir.mkdir(parents=True, exist_ok=True)
    # Header image. The image area (58 px at 100 %) sits flush with the top-right corner of
    # the wizard, so the logo is drawn smaller inside it: its top lines up with the page title,
    # it spans the title and subtitle lines (32 px), and its right margin equals the 25 px
    # left margin of the text. Proportions are kept for every DPI size.
    for area in (58, 77, 97, 116, 124, 143, 159):
        header = Image.new('RGBA', (area, area), (0, 0, 0, 0))
        logo_size = round_half_up(area * 32 / 58)
        right = round_half_up(area * 23 / 58)
        top = round_half_up(area * 11 / 58)
        logo = app_icon(logo_size, inset=0)
        header.paste(logo, (area - right - logo_size, top), logo)
        header.save(wizard_dir / f'wizard-small-{area}.png')

    # Large side image of the finish page: the logo on a transparent background, so the
    # same files work with the light and the dark wizard. Aspect ratio 164:314 per DPI.
    for width in (164, 202, 246, 269, 336, 403, 430, 498, 534):
        height = round_half_up(width * 314 / 164)
        side = Image.new('RGBA', (width, height), (0, 0, 0, 0))
        # Centred in the side panel; the top lines up with the page heading and the bottom
        # with the end of the description, so the logo and the text read as one block.
        logo_size = round_half_up(width * 76 / 164)
        logo = app_icon(logo_size, inset=0)
        side.paste(logo, ((width - logo_size) // 2, round_half_up(height * 22 / 314)), logo)
        side.save(wizard_dir / f'wizard-large-{width}.png')

    # Logo for the README / repository page.
    docs = ROOT / 'docs' / 'images'
    docs.mkdir(parents=True, exist_ok=True)
    app_icon(256).save(docs / 'logo.png')

    # Preview sheet for review (not part of the build).
    preview = Image.new('RGBA', (sum(sizes) + 20 * len(sizes) + 20, 300), (245, 245, 247, 255))
    x = 20
    for size, image in zip(sizes, images):
        preview.paste(image, (x, 20), image)
        x += size + 20
    for row, (bg, fg) in enumerate((((32, 32, 32), 255), ((238, 238, 238), 0))):
        y = 200 + row * 46
        ImageDraw.Draw(preview).rectangle((20, y, 400, y + 40), fill=bg + (255,))
        tx = 36
        for box in (16, 20, 24, 32):
            mask = bars_mask(box)
            tray = Image.new('RGBA', (box, box), (fg, fg, fg, 255))
            preview.paste(tray, (tx, y + (40 - box) // 2), mask)
            tx += box + 24
    (ROOT / 'artifacts').mkdir(exist_ok=True)
    preview.save(ROOT / 'artifacts' / 'logo-preview.png')
    print('ok:', ico_path)


if __name__ == '__main__':
    main()
