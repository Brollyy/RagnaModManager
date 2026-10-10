"""Generate reproducible source geometry and icon art for the UE4.27 guide."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).parent
SOURCE = ROOT / "Source"
SOURCE.mkdir(parents=True, exist_ok=True)


def write_base_color(path: Path) -> None:
    image = Image.new("RGB", (256, 256))
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            base = (22, 178, 204) if y < 218 else (16, 45, 87)
            if 30 <= x < 46 or 210 <= x < 226:
                base = (248, 179, 43)
            if 112 <= x < 144 and 36 <= y < 218:
                base = (7, 89, 137)
            pixels[x, y] = base
    image.save(path)


def draw_icon(path: Path) -> None:
    image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    outline = (20, 38, 65, 255)
    cyan = (32, 188, 210, 255)
    cyan_light = (112, 234, 235, 255)
    cyan_dark = (13, 92, 151, 255)
    gold = (248, 179, 43, 255)
    gold_light = (255, 220, 101, 255)
    shaft = (18, 70, 127, 255)

    # Handle sits behind the cuboid head and ends in a faceted pommel.
    draw.polygon([(120, 116), (151, 120), (139, 227), (108, 224)], fill=shaft, outline=outline, width=6)
    draw.polygon([(112, 185), (145, 188), (142, 203), (109, 200)], fill=gold, outline=outline, width=4)
    draw.polygon([(106, 218), (141, 221), (133, 241), (101, 238)], fill=cyan_dark, outline=outline, width=5)
    draw.line([(117, 207), (136, 210)], fill=cyan_light, width=5)
    draw.line([(115, 216), (134, 219)], fill=cyan_light, width=5)

    # Isometric block head with bright gold side plates and a simple rune mark.
    draw.polygon([(54, 47), (91, 24), (205, 43), (168, 69)], fill=cyan_light, outline=outline, width=7)
    draw.polygon([(54, 47), (168, 69), (168, 142), (54, 119)], fill=cyan, outline=outline, width=7)
    draw.polygon([(168, 69), (205, 43), (205, 117), (168, 142)], fill=cyan_dark, outline=outline, width=7)
    draw.polygon([(65, 56), (84, 60), (84, 109), (65, 105)], fill=gold, outline=outline, width=4)
    draw.polygon([(151, 72), (165, 75), (165, 124), (151, 121)], fill=gold, outline=outline, width=4)
    draw.polygon([(104, 73), (122, 76), (122, 93), (137, 87), (140, 101), (122, 107), (122, 119), (104, 115)], fill=gold_light, outline=outline, width=3)
    draw.line([(58, 125), (165, 146), (204, 122)], fill=outline, width=6)
    image.save(path)


def draw_silhouette(path: Path) -> None:
    image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    ink = (12, 24, 42, 255)
    draw.polygon([(117, 104), (144, 108), (134, 233), (107, 230)], fill=ink)
    draw.polygon([(99, 226), (141, 230), (133, 247), (95, 244)], fill=ink)
    draw.polygon([(45, 47), (87, 22), (207, 42), (166, 68)], fill=ink)
    draw.polygon([(45, 47), (166, 68), (166, 145), (45, 121)], fill=ink)
    draw.polygon([(166, 68), (207, 42), (207, 120), (166, 145)], fill=ink)
    image.save(path)


def draw_symbol(path: Path) -> None:
    image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    outline = (15, 33, 57, 255)
    cyan = (32, 188, 210, 255)
    gold = (248, 179, 43, 255)
    gold_light = (255, 222, 112, 255)
    draw.polygon([(128, 22), (215, 72), (215, 177), (128, 229), (41, 177), (41, 72)], fill=cyan, outline=outline, width=8)
    draw.polygon([(128, 42), (195, 80), (128, 119), (61, 80)], fill=(110, 232, 235, 255), outline=outline, width=5)
    draw.polygon([(61, 92), (120, 126), (120, 196), (61, 161)], fill=(17, 116, 166, 255), outline=outline, width=5)
    draw.polygon([(136, 126), (195, 92), (195, 161), (136, 196)], fill=(13, 92, 151, 255), outline=outline, width=5)
    draw.polygon([(119, 92), (137, 92), (137, 122), (159, 114), (167, 131), (137, 143), (137, 173), (119, 173)], fill=gold_light, outline=outline, width=4)
    draw.polygon([(99, 132), (120, 139), (120, 158), (99, 151)], fill=gold, outline=outline, width=3)
    image.save(path)


def prism_obj() -> str:
    vertices: list[tuple[float, float, float]] = []
    uvs: list[tuple[float, float]] = []
    faces: list[tuple[str, list[tuple[int, int]]]] = []

    def add_box(name: str, material: str, lo: tuple[float, float, float], hi: tuple[float, float, float]) -> None:
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        points = [
            (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
        ]
        # Rotate each head component a quarter turn around the handle axis.
        # The head's span should run across game-space Y after the OBJ import,
        # while the shaft remains on the grip-centered Z axis.
        points = [(y, -x, z) for x, y, z in points]
        start = len(vertices) + 1
        vertices.extend(points)
        uv_start = len(uvs) + 1
        uvs.extend([(0, 0), (1, 0), (1, 1), (0, 1)] * 6)
        quads = [
            (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
            (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
        ]
        for face_index, quad in enumerate(quads):
            pairs = [(start + vertex, uv_start + face_index * 4 + corner) for corner, vertex in enumerate(quad)]
            faces.append((material, pairs))

    def add_prism(name: str, material: str, z0: float, z1: float, radius0: float, radius1: float, sides: int = 8) -> None:
        import math

        base = len(vertices) + 1
        for z, radius in ((z0, radius0), (z1, radius1)):
            for i in range(sides):
                angle = 2 * math.pi * i / sides
                vertices.append((radius * math.cos(angle), radius * math.sin(angle), z))
        uv_start = len(uvs) + 1
        uvs.extend([(i / sides, 0) for i in range(sides)] + [(i / sides, 1) for i in range(sides)])
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((material, [(base + i, uv_start + i), (base + j, uv_start + j), (base + sides + j, uv_start + sides + j), (base + sides + i, uv_start + sides + i)]))
        for ring, reverse in ((0, True), (sides, False)):
            center = len(vertices) + 1
            vertices.append((0, 0, z0 if reverse else z1))
            center_uv = len(uvs) + 1
            uvs.append((0.5, 0.5))
            for i in range(sides):
                j = (i + 1) % sides
                first = base + ring + i
                second = base + ring + j
                tri = [(center, center_uv), (second, uv_start + ring + j), (first, uv_start + ring + i)]
                if not reverse:
                    tri = [tri[0], tri[2], tri[1]]
                faces.append((material, tri))

    # Tapered shaft and wrapped grip, all placed around the grip origin at Z=0.
    add_prism("Shaft", "HammerCyan", -24, 28, 3.0, 4.0)
    add_prism("Grip", "HammerNavy", -20, 1, 4.4, 4.4)
    add_prism("GripBand_Lower", "HammerGold", -20, -17, 4.8, 4.8)
    add_prism("GripBand_Upper", "HammerGold", -1, 2, 4.8, 4.8)
    add_prism("Pommel", "HammerCyan", -27, -23, 5.2, 4.2)

    # Original box head and raised accent plates are modeled as separate material groups.
    add_box("Head", "HammerCyan", (-17, -11, 27), (17, 11, 51))
    add_box("LeftGoldPlate", "HammerGold", (-18, -12, 29), (-14, -12.1, 49))
    add_box("RightGoldPlate", "HammerGold", (14, -12, 29), (18, -12.1, 49))
    add_box("RuneBar", "HammerGold", (-2, -13, 34), (2, -13.1, 45))
    add_box("RuneCross", "HammerGold", (-7, -13, 37), (7, -13.1, 41))

    lines = ["# Generated source mesh: Azure Blockhead", "mtllib T_RMM_AzureBlockhead.mtl", "o SM_RMM_AzureBlockhead"]
    lines.extend(f"v {x:.5f} {y:.5f} {z:.5f}" for x, y, z in vertices)
    lines.extend(f"vt {u:.5f} {v:.5f}" for u, v in uvs)
    current_material = None
    for material, face in faces:
        if material != current_material:
            lines.append(f"usemtl {material}")
            current_material = material
        lines.append("f " + " ".join(f"{vertex}/{uv}" for vertex, uv in face))
    return "\n".join(lines) + "\n"


write_base_color(SOURCE / "T_RMM_AzureBlockhead_BaseColor.png")
draw_icon(SOURCE / "T_RMM_AzureBlockhead_Icon.png")
draw_silhouette(SOURCE / "T_RMM_AzureBlockhead_Silhouette.png")
draw_symbol(SOURCE / "T_RMM_AzureBlockhead_Symbol.png")
(SOURCE / "SM_RMM_AzureBlockhead.obj").write_text(prism_obj(), encoding="utf-8")
(SOURCE / "T_RMM_AzureBlockhead.mtl").write_text(
    """newmtl HammerCyan
Kd 0.09 0.70 0.82
map_Kd T_RMM_AzureBlockhead_BaseColor.png

newmtl HammerGold
Kd 1.00 0.70 0.18

newmtl HammerNavy
Kd 0.05 0.16 0.32
""",
    encoding="utf-8",
)
