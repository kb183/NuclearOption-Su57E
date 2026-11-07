"""Author the Su-57 metallic/smoothness atlas from its own texture and mesh UVs.

Requires Pillow. This generates assets only; it does not launch Unity, build a
bundle, render a preview or run validation. Values are art direction, not measured
Su-57 material data. R = metallic, A = final URP smoothness; G/B unused.
"""
from pathlib import Path
import json

from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/Blueprinter/Mods/SU57/Source"
REPORT = ROOT / "Reference/SU57/Surface/v0.2.10"


def read_obj(path):
    vertices, uvs, faces = [], [], []
    group = ""
    for line in path.read_text(encoding="utf-8").splitlines():
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "v":
            vertices.append(tuple(map(float, fields[1:4])))
        elif fields[0] == "vt":
            uvs.append(tuple(map(float, fields[1:3])))
        elif fields[0] == "g":
            group = fields[1]
        elif fields[0] == "f" and group.endswith("_1"):
            corners = [tuple(int(i) - 1 for i in f.split("/")[:2]) for f in fields[1:]]
            for i in range(1, len(corners) - 1):
                faces.append((corners[0], corners[i], corners[i + 1]))
    return vertices, uvs, faces


def clip_nose(poly, boundary=7.6):
    """Clip in model space and interpolate UVs at the radome boundary."""
    clipped = []
    for start, end in zip(poly, poly[1:] + poly[:1]):
        a, b = start[0] >= boundary, end[0] >= boundary
        if a:
            clipped.append(start)
        if a != b:
            t = (boundary - start[0]) / (end[0] - start[0])
            clipped.append(tuple(s + (e - s) * t for s, e in zip(start, end)))
    return clipped


def main():
    REPORT.mkdir(parents=True, exist_ok=True)
    base = Image.open(SOURCE / "SU-57_SU57_1_BaseMap_4131054348559121509.png").convert("RGB")
    width, height = base.size
    nozzle = Image.new("L", base.size)
    radome = Image.new("L", base.size)
    nd, rd = ImageDraw.Draw(nozzle), ImageDraw.Draw(radome)
    vertices, uvs, faces = read_obj(SOURCE / "RU_SU_57_-837618460868326853.obj")
    selection = json.loads((SOURCE / "SU57_NozzleSkin.json").read_text())["lods"][0]
    nozzle_vertices = set(selection["left"]) | set(selection["right"])

    def pixel(u, v):
        return u * (width - 1), (1 - v) * (height - 1)

    for face in faces:
        if all(i in nozzle_vertices for i, _ in face):
            nd.polygon([pixel(*uvs[j]) for _, j in face], fill=255)
        poly = clip_nose([(vertices[i][2], *uvs[j]) for i, j in face])
        if len(poly) >= 3:
            rd.polygon([pixel(u, v) for _, u, v in poly], fill=255)

    # Pad UV islands into their gutters for filtered sampling and mipmaps.
    nozzle = nozzle.filter(ImageFilter.MaxFilter(7))
    radome = radome.filter(ImageFilter.MaxFilter(7))
    broad = base.convert("L").filter(ImageFilter.GaussianBlur(2))
    out = bytearray(width * height * 4)
    metal_view = bytearray(width * height)
    smooth_view = bytearray(width * height)
    def pixels(image):
        return image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata()

    for i, ((r, g, b), luma, n, nose) in enumerate(zip(pixels(base), pixels(broad), pixels(nozzle), pixels(radome))):
        # Limit albedo-driven variation to gloss: camouflage is never embossed.
        grain = (((i * 1664525 + (i // width) * 1013904223) >> 13) & 7) - 3.5
        metallic = 51
        smoothness = 124 + 12 * luma / 255 + grain * .3
        red_paint = r > g * 1.5 and r > b * 1.4
        warm_metal = r > g + 6 and g > b + 4 and r > 60 and not red_paint
        if warm_metal or n:
            metallic = 219
            smoothness = 148 + 20 * luma / 255 + grain * .3
        elif max(r, g, b) < 42:
            metallic, smoothness = 5, 61
        elif red_paint:
            # Painted stars and red stencils retain pigment rather than copper.
            metallic, smoothness = 10, 110
        if nose:
            metallic, smoothness = 0, 82
        metal_view[i], smooth_view[i] = int(metallic), int(smoothness)
        out[i * 4:i * 4 + 4] = bytes((int(metallic), 0, 0, int(smoothness)))
    Image.frombytes("RGBA", base.size, bytes(out)).save(SOURCE / "SU57_MetallicSmoothness.png")

    # Authoring view only: this is a texture sheet, not a rendered game result.
    panels = [base, Image.frombytes("L", base.size, bytes(metal_view)), Image.frombytes("L", base.size, bytes(smooth_view))]
    sheet = Image.new("RGB", (1536, 540), "#242424")
    labels = ("Su-57 albedo (unchanged)", "R: metallic", "A: smoothness")
    draw = ImageDraw.Draw(sheet)
    for i, (panel, label) in enumerate(zip(panels, labels)):
        sheet.paste(ImageOps.contain(panel.convert("RGB"), (512, 512)), (512 * i, 28))
        draw.text((512 * i + 8, 8), label, fill="white")
    sheet.save(REPORT / "surface-atlas.png")
    print("Authored:", SOURCE / "SU57_MetallicSmoothness.png")
    print("Texture sheet:", REPORT / "surface-atlas.png")


if __name__ == "__main__":
    main()
