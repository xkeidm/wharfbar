"""Render WharfBar Dusk solid mark to multi-size ICO + PNG.

Matches docs/brand/logo/wharfbar-icon-small.svg and BrandLogo geometry.
Writes a proper multi-entry .ico (PNG payloads; Windows Vista+).
"""
from __future__ import annotations

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

BG = (0x16, 0x32, 0x4F, 255)
SUN = (0xF2, 0xB6, 0x6B, 255)
PIER = (0xEA, 0xF1, 0xF7, 255)
GRID = 128.0
RX = 28.0
SIZES = (16, 24, 32, 48, 64, 128, 256)


def render(size: int) -> Image.Image:
    scale = 8 if size <= 32 else 4
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    k = s / GRID

    def px(x: float) -> float:
        return x * k

    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=px(RX), fill=BG)
    d.pieslice([px(42), px(16), px(86), px(60)], start=180, end=360, fill=SUN)
    d.polygon(
        [
            (px(55), px(43)),
            (px(73), px(43)),
            (px(108), px(112)),
            (px(20), px(112)),
        ],
        fill=PIER,
    )
    return img.resize((size, size), Image.Resampling.LANCZOS)


def png_bytes(image: Image.Image) -> bytes:
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()


def write_ico(path: Path, images: list[Image.Image]) -> None:
    """ICO with one PNG image per size (preferred on modern Windows)."""
    payloads = [png_bytes(im) for im in images]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = bytearray()
    for im, payload in zip(images, payloads, strict=True):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        entries += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(payload), offset)
        offset += len(payload)

    path.write_bytes(header + entries + b"".join(payloads))


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    assets = root / "src" / "WharfBar" / "Assets"
    brand = root / "docs" / "brand" / "logo"
    media = root / "docs" / "media"
    assets.mkdir(parents=True, exist_ok=True)

    images = [render(sz) for sz in SIZES]
    ico_path = assets / "WharfBar.ico"
    write_ico(ico_path, images)

    images[SIZES.index(128)].save(assets / "WharfBar.png", format="PNG")
    images[SIZES.index(256)].save(brand / "wharfbar-icon.png", format="PNG")
    images[SIZES.index(128)].save(media / "wharfbar-icon.png", format="PNG")

    data = ico_path.read_bytes()
    count = struct.unpack_from("<H", data, 4)[0]
    print(f"wrote {ico_path} ({count} entries, {len(data)} bytes)")
    off = 6
    for i in range(count):
        w, h, _, _, _, bpp, size, _ = struct.unpack_from("<BBBBHHII", data, off)
        print(f"  [{i}] {w or 256}x{h or 256} bpp={bpp} bytes={size}")
        off += 16


if __name__ == "__main__":
    main()
