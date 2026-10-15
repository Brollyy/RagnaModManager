"""Generate reproducible source geometry and icon art for the UE4.27 guide."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).parent
SOURCE = ROOT / "Source"
SOURCE.mkdir(parents=True, exist_ok=True)


def write_base_color(path: Path) -> None:
    # Four flat-color swatches with wide gutters. The mesh maps each face to
    # the interior of one swatch instead of stretching the whole atlas across
    # every face (which sampled unrelated colors and bled at UV edges).
    image = Image.new("RGB", (512, 512), (22, 178, 204))
    draw = ImageDraw.Draw(image)
    draw.rectangle((256, 0, 511, 255), fill=(248, 179, 43))
    draw.rectangle((0, 256, 255, 511), fill=(16, 45, 87))
    draw.rectangle((256, 256, 511, 511), fill=(112, 218, 224))
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
    color_uv = {
        "cyan": (0.08, 0.08, 0.42, 0.42),
        "gold": (0.58, 0.08, 0.92, 0.42),
        "navy": (0.08, 0.58, 0.42, 0.92),
        "ice": (0.58, 0.58, 0.92, 0.92),
    }

    def rect_uvs(region: str) -> list[tuple[float, float]]:
        u0, v0, u1, v1 = color_uv[region]
        # PNG rows are addressed from the top, but OBJ V=0 addresses the
        # bottom of the texture. Flip V so each mesh region samples the
        # swatch that was painted for it after Unreal imports the OBJ.
        return [(u0, 1 - v0), (u1, 1 - v0), (u1, 1 - v1), (u0, 1 - v1)]

    def swatch_uv(region: str, u_fraction: float, v_fraction: float) -> tuple[float, float]:
        u0, v0, u1, v1 = color_uv[region]
        return (
            u0 + (u1 - u0) * u_fraction,
            1 - (v0 + (v1 - v0) * v_fraction),
        )

    def add_box(
        name: str,
        material: str,
        lo: tuple[float, float, float],
        hi: tuple[float, float, float],
        region: str = "cyan",
        gauge_uv: bool = False,
        gauge_uv_axis: str = "x",
    ) -> None:
        if any(low > high for low, high in zip(lo, hi)):
            raise ValueError(f"Box {name} has reversed bounds: {lo!r} .. {hi!r}")
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        source_points = [
            (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1),
        ]
        # Rotate each head component a quarter turn around the handle axis.
        # The head's span should run across game-space Y after the OBJ import,
        # while the shaft remains on the grip-centered Z axis.
        points = [(y, -x, z) for x, y, z in source_points]
        quads = [
            (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
            (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
        ]
        gauge_axis = {"x": 0, "y": 1, "z": 2}.get(gauge_uv_axis, 0)
        for face_index, quad in enumerate(quads):
            # Duplicate each box face's vertices. OBJ importers otherwise
            # smooth the normals across hard corners, even when the source
            # declares `s off`.
            face_vertex_start = len(vertices) + 1
            vertices.extend(points[vertex] for vertex in quad)
            face_uv_start = len(uvs) + 1
            if gauge_uv:
                other_axes = [axis for axis in range(3) if axis != gauge_axis]
                face_spans = {
                    axis: max(source_points[index][axis] for index in quad)
                    - min(source_points[index][axis] for index in quad)
                    for axis in range(3)
                }
                gauge_span = face_spans[gauge_axis]
                varying_axes = sorted(other_axes, key=lambda axis: face_spans[axis], reverse=True)
                if gauge_span > 1e-6:
                    v_axis = varying_axes[0]
                    v_min = min(source_points[index][v_axis] for index in quad)
                    v_span = face_spans[v_axis]
                    for vertex_index in quad:
                        u = 0.01 + 0.98 * (
                            (source_points[vertex_index][gauge_axis] - lo[gauge_axis])
                            / (hi[gauge_axis] - lo[gauge_axis])
                        )
                        v = (source_points[vertex_index][v_axis] - v_min) / v_span
                        uvs.append((u, v))
                else:
                    # The end caps keep a narrow UV strip near their endpoint
                    # so they remain dark until the corresponding fill reaches
                    # the end while still producing a non-degenerate tangent basis.
                    u_axis, v_axis = varying_axes
                    u_min = min(source_points[index][u_axis] for index in quad)
                    v_min = min(source_points[index][v_axis] for index in quad)
                    u_span = face_spans[u_axis]
                    v_span = face_spans[v_axis]
                    at_high_end = (
                        (sum(source_points[index][gauge_axis] for index in quad) / 4)
                        > (lo[gauge_axis] + hi[gauge_axis]) / 2
                    )
                    band_start = 0.98 if at_high_end else 0.01
                    for vertex_index in quad:
                        u = band_start + 0.01 * (
                            (source_points[vertex_index][u_axis] - u_min) / u_span
                        )
                        v = (source_points[vertex_index][v_axis] - v_min) / v_span
                        uvs.append((u, v))
            else:
                uvs.extend(rect_uvs(region))
            pairs = [
                (face_vertex_start + corner, face_uv_start + corner)
                for corner in range(4)
            ]
            faces.append((material, pairs))

    def add_prism(name: str, material: str, z0: float, z1: float, radius0: float, radius1: float, region: str = "cyan", sides: int = 8) -> None:
        import math

        base = len(vertices) + 1
        for z, radius in ((z0, radius0), (z1, radius1)):
            for i in range(sides):
                angle = 2 * math.pi * i / sides
                vertices.append((radius * math.cos(angle), radius * math.sin(angle), z))
        # Keep the cap UVs separate from each side face. Reusing one UV per
        # ring wraps the last side face from u1 back to u0, stretching that
        # face across the atlas and creating a visible sampling seam.
        cap_uv_start = len(uvs) + 1
        uvs.extend(
            [swatch_uv(region, i / sides, 0) for i in range(sides)]
            + [swatch_uv(region, i / sides, 1) for i in range(sides)]
        )
        for i in range(sides):
            j = (i + 1) % sides
            side_uv_start = len(uvs) + 1
            uvs.extend(
                [
                    swatch_uv(region, i / sides, 0),
                    swatch_uv(region, (i + 1) / sides, 0),
                    swatch_uv(region, (i + 1) / sides, 1),
                    swatch_uv(region, i / sides, 1),
                ]
            )
            faces.append((material, [(base + i, side_uv_start), (base + j, side_uv_start + 1), (base + sides + j, side_uv_start + 2), (base + sides + i, side_uv_start + 3)]))
        for ring, reverse in ((0, True), (sides, False)):
            center = len(vertices) + 1
            vertices.append((0, 0, z0 if reverse else z1))
            center_uv = len(uvs) + 1
            uvs.append(swatch_uv(region, 0.5, 0.5))
            for i in range(sides):
                j = (i + 1) % sides
                first = base + ring + i
                second = base + ring + j
                tri = [(center, center_uv), (second, cap_uv_start + ring + j), (first, cap_uv_start + ring + i)]
                if not reverse:
                    tri = [tri[0], tri[2], tri[1]]
                faces.append((material, tri))

    # Tapered shaft, grip, and pommel use separate padded atlas swatches.
    add_prism("Shaft", "M_Character", -24, 28, 3.0, 4.0, "navy")
    add_prism("Grip", "M_Character", -20, 1, 4.4, 4.4, "cyan")
    add_prism("GripBand_Lower", "M_Character", -20, -17, 4.8, 4.8, "gold")
    add_prism("GripBand_Upper", "M_Character", -1, 2, 4.8, 4.8, "gold")
    add_prism("Pommel", "M_Character", -27, -23, 5.2, 4.2, "ice")

    # These distinct raised bars are the only surfaces driven by combo values.
    # Keep a copy on each broad face: the in-race hammer camera sees the back
    # of the mesh, while the changing-room preview sees the authored front.
    # Stock gauge bars remain visible from both of those views.
    # Keep these raised surfaces fully clear of the cylindrical shaft so
    # their hidden faces cannot intersect it and create shadow speckling.
    add_box("HandleGaugeBar_Front", "M_JaugeManche", (-2.5, -5.8, 2), (2.5, -4.5, 22), gauge_uv=True, gauge_uv_axis="z")
    add_box("HandleGaugeBar_Back", "M_JaugeManche", (-2.5, 4.5, 2), (2.5, 5.8, 22), gauge_uv=True, gauge_uv_axis="z")
    # The OBJ import rotates source X onto game-space Y and source Y onto
    # game-space -X. Place the head meters on the two broad +/-X faces, with
    # U running along the head width so the blue/gold stages fill left to
    # right. Putting these at source X +/-17 would put them on the end caps.
    add_box(
        "HeadGaugeBar_Front", "M_JaugeTete", (-14, -13.0, 43), (14, -12.0, 48.5),
        gauge_uv=True, gauge_uv_axis="x"
    )
    add_box(
        "HeadGaugeBar_Back", "M_JaugeTete", (-14, 12.0, 43), (14, 13.0, 48.5),
        gauge_uv=True, gauge_uv_axis="x"
    )

    # The box head and gold decorations stay visually fixed during a combo.
    add_box("Head", "M_Character", (-17, -11, 27), (17, 11, 51))
    # Bounds stay ordered so the exported face winding and normals point
    # outward. Place these details just beyond the front gauge strip.
    add_box("LeftGoldPlate", "M_Character", (-18, -12.0, 29), (-14, -11.9, 49), region="gold")
    add_box("RightGoldPlate", "M_Character", (14, -12.0, 29), (18, -11.9, 49), region="gold")
    add_box("RuneBar", "M_Character", (-2, -13.1, 34), (2, -13.0, 45), region="ice")
    add_box("RuneCross", "M_Character", (-7, -13.1, 37), (7, -13.0, 41), region="gold")

    lines = [
        "# Generated source mesh: Azure Blockhead",
        "mtllib T_RMM_AzureBlockhead.mtl",
        "o SM_RMM_AzureBlockhead",
        # Box faces and the octagonal shaft are intentionally faceted. Mark
        # edges hard so importers do not smooth their shared OBJ vertices.
        "s off",
    ]
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
    """newmtl M_Character
Kd 0.09 0.70 0.82
map_Kd T_RMM_AzureBlockhead_BaseColor.png

newmtl M_JaugeManche
Kd 0.05 0.16 0.32

newmtl M_JaugeTete
Kd 1.00 0.70 0.18
""",
    encoding="utf-8",
)
