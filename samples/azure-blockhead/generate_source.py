"""Generate the Azure body atlas and transparent hammer icon source textures."""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).parent
SOURCE = ROOT / "Source"


def write_base_color(path: Path) -> None:
    """Resize the authored four-tile atlas and compensate for UE4's FBX V flip."""
    art_path = SOURCE / "T_RMM_AzureBlockhead_Atlas_Art.png"
    if not art_path.is_file():
        raise FileNotFoundError(f"Missing authored atlas artwork: {art_path}")
    resampling = getattr(Image, "Resampling", Image)
    image = Image.open(art_path).convert("RGB").resize(
        (512, 512), resampling.LANCZOS
    )
    # The edited mesh's cooked UV0 has V inverted relative to the authored
    # FBX UVs. The face regions use 8% insets in each quadrant to keep the
    # atlas details away from neighboring tiles after mip filtering.
    transpose = getattr(Image, "Transpose", Image)
    image.transpose(transpose.FLIP_TOP_BOTTOM).save(path)


def draw_icon(path: Path) -> None:
    """Draw the transparent in-game hammer icon as an RGBA Texture2D source."""
    image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    outline = (20, 38, 65, 255)
    cyan = (32, 188, 210, 255)
    cyan_light = (112, 234, 235, 255)
    cyan_dark = (13, 92, 151, 255)
    gold = (248, 179, 43, 255)
    gold_light = (255, 220, 101, 255)
    shaft = (18, 70, 127, 255)

    draw.polygon([(120, 116), (151, 120), (139, 227), (108, 224)], fill=shaft, outline=outline, width=6)
    draw.polygon([(112, 185), (145, 188), (142, 203), (109, 200)], fill=gold, outline=outline, width=4)
    draw.polygon([(106, 218), (141, 221), (133, 241), (101, 238)], fill=cyan_dark, outline=outline, width=5)
    draw.line([(117, 207), (136, 210)], fill=cyan_light, width=5)
    draw.line([(115, 216), (134, 219)], fill=cyan_light, width=5)

    draw.polygon([(54, 47), (91, 24), (205, 43), (168, 69)], fill=cyan_light, outline=outline, width=7)
    draw.polygon([(54, 47), (168, 69), (168, 142), (54, 119)], fill=cyan, outline=outline, width=7)
    draw.polygon([(168, 69), (205, 43), (205, 117), (168, 142)], fill=cyan_dark, outline=outline, width=7)
    draw.polygon([(65, 56), (84, 60), (84, 109), (65, 105)], fill=gold, outline=outline, width=4)
    draw.polygon([(151, 72), (165, 75), (165, 124), (151, 121)], fill=gold, outline=outline, width=4)
    draw.polygon(
        [(104, 73), (122, 76), (122, 93), (137, 87), (140, 101),
         (122, 107), (122, 119), (104, 115)],
        fill=gold_light,
        outline=outline,
        width=3,
    )
    draw.line([(58, 125), (165, 146), (204, 122)], fill=outline, width=6)
    image.save(path)


def main() -> None:
    SOURCE.mkdir(parents=True, exist_ok=True)
    write_base_color(SOURCE / "T_RMM_AzureBlockhead_BaseColor.png")
    draw_icon(SOURCE / "T_RMM_AzureBlockhead_Icon.png")
    print("Generated Azure base-color atlas and transparent icon")


if __name__ == "__main__":
    main()
