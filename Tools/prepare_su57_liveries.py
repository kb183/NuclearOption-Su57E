"""Author five selectable Su-57 atlases; no Unity/build/game/test invocation.

Immutable BA exports are kept in Reference, outside the shipped mod folder.
The 4K output adds sampling density, not invented texture detail. A subpixel
low-pass softens raster edges while retaining the original camouflage design.
Requires Pillow only. Re-running always starts from the original images.
"""
from pathlib import Path
import json
import re
import uuid
from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/Blueprinter/Mods/SU57/Source"
REPORT = ROOT / "Reference/SU57/Surface/v0.2.26"
ORIGINALS = REPORT / "Originals"
NAMES = [
    ("digital", "Su-57 Digital Camouflage"),
    ("blue_grey", "Su-57 Blue-Grey"),
    ("light_grey", "Su-57 Light Grey"),
    ("grey_splinter", "Su-57 Grey Splinter"),
    ("blue_splinter", "Su-57 Blue-Grey Splinter"),
]


def palette(image):
    reduced = image.convert("RGB").resize((256, 256), Image.Resampling.BOX)
    quantized = reduced.quantize(colors=12, method=Image.Quantize.MEDIANCUT)
    entries = quantized.getpalette()
    colors = []
    for count, index in sorted(quantized.getcolors(), reverse=True):
        r, g, b = entries[index * 3:index * 3 + 3]
        if max(r, g, b) < 45 or min(r, g, b) > 235 or r > g * 1.5:
            continue
        colors.append(dict(r=r, g=g, b=b, count=count))
        if len(colors) == 3:
            break
    return colors


def write_meta(path, size=4096, linear=False):
    meta = path.with_suffix(path.suffix + ".meta")
    template = meta if meta.exists() else SOURCE / "SU-57_SU57_1_BaseMap_4131054348559121509.png.meta"
    data = template.read_text(encoding="utf-8")
    if not meta.exists():
        data = re.sub(r"(?m)^guid: .*", "guid: " + uuid.uuid4().hex, data)
    values = dict(enableMipMap=1, mipMapMode=0, sRGBTexture=int(not linear),
        maxTextureSize=size, filterMode=2, aniso=16, mipBias="0.15",
        compressionQuality=100, textureCompression=2, crunchedCompression=0)
    for key, value in values.items():
        data = re.sub(rf"(?m)^(\s*{key}:) .*", rf"\g<1> {value}", data)
    def standalone(match):
        block = re.sub(r"(?m)^(\s*textureFormat:) .*", r"\g<1> 25", match[0])
        return re.sub(r"(?m)^(\s*overridden:) .*", r"\g<1> 1", block)
    data = re.sub(r"(?ms)^    buildTarget: Standalone\n.*?(?=^  - serializedVersion:|^  spriteSheet:)", standalone, data)
    meta.write_text(data, encoding="utf-8")


def main():
    SOURCE.mkdir(parents=True, exist_ok=True)
    entries = []
    overview = Image.new("RGB", (2000, 430), "#20252b")
    draw = ImageDraw.Draw(overview)
    for index, (key, name) in enumerate(NAMES, 1):
        matches = list(ORIGINALS.glob(f"SU-57_SU57_{index}_BaseMap_*.png"))
        if len(matches) != 1:
            raise RuntimeError(f"Expected one original atlas for livery {index}")
        original = Image.open(matches[0]).convert("RGBA")
        rgb = original.convert("RGB").resize((4096, 4096), Image.Resampling.LANCZOS)
        rgb = rgb.filter(ImageFilter.GaussianBlur(.45))
        authored = rgb.convert("RGBA")
        authored.putalpha(original.getchannel("A").resize((4096, 4096), Image.Resampling.LANCZOS))
        path = SOURCE / matches[0].name
        authored.save(path, optimize=True)
        write_meta(path)
        entries.append(dict(key=key, name=name, texture=path.name,
            asset="SU57_Livery.asset" if index == 1 else f"SU57_Livery_{index}.asset",
            colors=palette(original)))
        overview.paste(ImageOps.contain(authored.convert("RGB"), (400, 400)), ((index - 1) * 400, 30))
        draw.text(((index - 1) * 400 + 8, 9), name, fill="white")
        if index == 1:
            box = (300, 1450, 500, 1650)
            before = original.crop(box).resize((800, 800), Image.Resampling.NEAREST)
            after = authored.crop(tuple(v * 2 for v in box)).resize((800, 800), Image.Resampling.NEAREST)
            comparison = Image.new("RGB", (1600, 830), "#20252b")
            comparison.paste(before.convert("RGB"), (0, 30))
            comparison.paste(after.convert("RGB"), (800, 30))
            cd = ImageDraw.Draw(comparison)
            cd.text((8, 9), "Original texels (magnified)", fill="white")
            cd.text((808, 9), "Authored sampling edges (magnified)", fill="white")
            comparison.save(REPORT / "texture-edge-comparison.png")
        print("Authored:", path.name, "4096x4096")
    (SOURCE / "SU57_Liveries.json").write_text(json.dumps(dict(liveries=entries), indent=2) + "\n", encoding="utf-8")
    write_meta(SOURCE / "SU57_MetallicSmoothness.png", 2048, True)
    overview.save(REPORT / "authored-livery-atlases.png")


if __name__ == "__main__":
    main()
