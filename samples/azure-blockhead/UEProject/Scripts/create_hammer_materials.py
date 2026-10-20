"""Import Azure Blockhead textures and create its surface and selector materials."""

import os
import unreal


CONTENT_ROOT = "/Game/RMM/Hammers/azure-blockhead"
UI_FOLDER = CONTENT_ROOT + "/UI"
MESH_FOLDER = CONTENT_ROOT + "/Mesh"
MATERIAL_FOLDER = CONTENT_ROOT + "/Materials"
PROJECT_ROOT = os.path.abspath(unreal.Paths.project_dir())
SOURCE_ROOT = os.path.normpath(os.path.join(PROJECT_ROOT, "..", "Source"))


def import_texture(filename, asset_name, destination):
    task = unreal.AssetImportTask()
    task.filename = os.path.join(SOURCE_ROOT, filename)
    task.destination_path = destination
    task.destination_name = asset_name
    task.automated = True
    task.replace_existing = True
    task.save = True
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    asset = unreal.EditorAssetLibrary.load_asset(destination + "/" + asset_name)
    if not asset or asset.get_class().get_name() != "Texture2D":
        raise RuntimeError("Failed to import Texture2D: " + filename)
    return asset


def create_gauge_material(name, parameter, second_parameter=None, full_combo_marker=False):
    """Create the stock-style two-stage handle fill or full-combo head marker."""
    path = MATERIAL_FOLDER + "/" + name
    material = (
        unreal.EditorAssetLibrary.load_asset(path)
        if unreal.EditorAssetLibrary.does_asset_exist(path)
        else None
    )
    if not material:
        material = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            name, MATERIAL_FOLDER, unreal.Material, unreal.MaterialFactoryNew()
        )
    material.set_editor_property("material_domain", unreal.MaterialDomain.MD_SURFACE)
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_MASKED)
    material.set_editor_property("opacity_mask_clip_value", 0.1)
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    unreal.MaterialEditingLibrary.delete_all_material_expressions(material)

    def expression(expression_class, x, y):
        return unreal.MaterialEditingLibrary.create_material_expression(
            material, expression_class, x, y
        )

    def connect(source, output, destination, input_name):
        if not unreal.MaterialEditingLibrary.connect_material_expressions(
            source, output, destination, input_name
        ):
            raise RuntimeError("Could not connect the Azure gauge material graph at " + input_name)

    def constant(value, x, y):
        node = expression(unreal.MaterialExpressionConstant, x, y)
        node.set_editor_property("r", value)
        return node

    def scalar(name, x, y, default=0.0):
        node = expression(unreal.MaterialExpressionScalarParameter, x, y)
        node.set_editor_property("parameter_name", name)
        node.set_editor_property("default_value", default)
        return node

    gauge = scalar(parameter, -520, -100)
    texcoord = expression(unreal.MaterialExpressionTextureCoordinate, -520, 120)
    texcoord.set_editor_property("coordinate_index", 0)
    u_coordinate = expression(unreal.MaterialExpressionComponentMask, -320, 120)
    u_coordinate.set_editor_property("r", True)
    u_coordinate.set_editor_property("g", False)
    u_coordinate.set_editor_property("b", False)
    u_coordinate.set_editor_property("a", False)
    # UE 4.27's Python editor API does not expose a stable name for the
    # ComponentMask input pin, so connect to the first pins on both nodes.
    connect(texcoord, "", u_coordinate, "")
    zero = constant(0.0, -300, 500)
    one = constant(1.0, -300, 620)

    def threshold(gauge_node, position_node, position_output, x, y):
        node = expression(unreal.MaterialExpressionIf, x, y)
        connect(gauge_node, "", node, "A")
        connect(position_node, position_output, node, "B")
        connect(one, "", node, "A > B")
        connect(one, "", node, "A == B")
        connect(zero, "", node, "A < B")
        return node

    if full_combo_marker:
        # circularGauge reaches 1 only when the combo gauge completes. Keep
        # the head mark dark below that point, then light the whole mark at once.
        gauge = scalar(parameter, -520, -100)
        one = constant(1.0, -300, 120)
        zero = constant(0.0, -300, 240)
        filled = expression(unreal.MaterialExpressionIf, 100, 80)
        connect(gauge, "", filled, "A")
        connect(one, "", filled, "B")
        connect(one, "", filled, "A > B")
        connect(one, "", filled, "A == B")
        connect(zero, "", filled, "A < B")
        lit_color = expression(unreal.MaterialExpressionVectorParameter, -80, 500)
        lit_color.set_editor_property("parameter_name", "mainColor")
        lit_color.set_editor_property(
            "default_value", unreal.LinearColor(1.0, 0.400337, 0.0, 1.0)
        )
    elif second_parameter:
        second_gauge = scalar(second_parameter, -520, 40)
        half = constant(0.5, -300, 220)
        two = constant(2.0, -300, 340)
        first_position = expression(unreal.MaterialExpressionMultiply, -100, 260)
        connect(u_coordinate, "", first_position, "A")
        connect(two, "", first_position, "B")
        shifted_position = expression(unreal.MaterialExpressionSubtract, -100, 420)
        connect(u_coordinate, "", shifted_position, "A")
        connect(half, "", shifted_position, "B")
        second_position = expression(unreal.MaterialExpressionMultiply, 80, 420)
        connect(shifted_position, "", second_position, "A")
        connect(two, "", second_position, "B")
        first_fill = threshold(gauge, first_position, "", 100, -100)
        second_fill = threshold(second_gauge, second_position, "", 100, 100)

        filled = expression(unreal.MaterialExpressionIf, 300, 80)
        connect(u_coordinate, "", filled, "A")
        connect(half, "", filled, "B")
        connect(second_fill, "", filled, "A > B")
        connect(first_fill, "", filled, "A == B")
        connect(first_fill, "", filled, "A < B")

        first_color = expression(unreal.MaterialExpressionVectorParameter, 100, 500)
        first_color.set_editor_property("parameter_name", "centerColor")
        first_color.set_editor_property(
            "default_value", unreal.LinearColor(0.030109, 0.079745, 0.320833, 1.0)
        )
        second_color_node = expression(unreal.MaterialExpressionVectorParameter, 100, 660)
        second_color_node.set_editor_property("parameter_name", "mainColor")
        second_color_node.set_editor_property(
            "default_value", unreal.LinearColor(1.0, 0.400337, 0.0, 1.0)
        )
        lit_color = expression(unreal.MaterialExpressionIf, 300, 380)
        connect(u_coordinate, "", lit_color, "A")
        connect(half, "", lit_color, "B")
        connect(second_color_node, "", lit_color, "A > B")
        connect(first_color, "", lit_color, "A == B")
        connect(first_color, "", lit_color, "A < B")
    else:
        filled = threshold(gauge, u_coordinate, "", 100, 80)
        lit_color = expression(unreal.MaterialExpressionVectorParameter, -80, 520)
        lit_color.set_editor_property("parameter_name", "centerColor")
        lit_color.set_editor_property(
            "default_value", unreal.LinearColor(0.030109, 0.079745, 0.320833, 1.0)
        )

    # Stock gauges expose a `glow` scalar and use emissive output for active
    # fills. Mask the unfilled surface so the stock head marker does not show
    # as a dark patch over the authored hammer head when combo is empty.
    glow = scalar("glow", 520, 620, 34.242756)
    emissive_fill = expression(unreal.MaterialExpressionMultiply, 520, 460)
    connect(lit_color, "", emissive_fill, "A")
    connect(glow, "", emissive_fill, "B")
    if not unreal.MaterialEditingLibrary.connect_material_property(
        emissive_fill, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    ):
        raise RuntimeError("Could not connect the Azure gauge fill to Emissive Color")
    if not unreal.MaterialEditingLibrary.connect_material_property(
        filled, "", unreal.MaterialProperty.MP_OPACITY_MASK
    ):
        raise RuntimeError("Could not connect the Azure gauge fill to Opacity Mask")
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    unreal.log("AZURE_GAUGE_MATERIAL_READY " + path + " parameter=" + parameter)
    return material

def create_surface_material(base_color, material_name):
    path = MATERIAL_FOLDER + "/" + material_name
    material = (
        unreal.EditorAssetLibrary.load_asset(path)
        if unreal.EditorAssetLibrary.does_asset_exist(path)
        else None
    )
    if not material:
        material = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            material_name, MATERIAL_FOLDER, unreal.Material, unreal.MaterialFactoryNew()
        )
    material.set_editor_property("material_domain", unreal.MaterialDomain.MD_SURFACE)
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_OPAQUE)
    # Keep the painted body color stable under UE's race shadows. The raised
    # gauge bars have their own lit materials and provide the combo response.
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    unreal.MaterialEditingLibrary.delete_all_material_expressions(material)
    sample = unreal.MaterialEditingLibrary.create_material_expression(
        material,
        unreal.MaterialExpressionTextureSampleParameter2D,
        -400,
        0,
    )
    sample.set_editor_property("parameter_name", "BaseColor")
    sample.set_editor_property("texture", base_color)
    # The authored head and handle use independent tile textures. Clamp
    # addressing keeps every minified sample inside its own material texture.
    sample.set_editor_property("const_coordinate", 0)
    if not unreal.MaterialEditingLibrary.connect_material_property(
        sample, "RGB", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    ):
        raise RuntimeError("Could not connect the Azure unlit body texture")
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    unreal.log("AZURE_SURFACE_MATERIAL_READY " + path + " texture=" + str(base_color))
    return material


unreal.EditorAssetLibrary.make_directory(UI_FOLDER)
unreal.EditorAssetLibrary.make_directory(MESH_FOLDER)
unreal.EditorAssetLibrary.make_directory(MATERIAL_FOLDER)
for obsolete in (
    MATERIAL_FOLDER + "/M_AzureBlockhead_Gold",
    MATERIAL_FOLDER + "/M_AzureBlockhead_Navy",
    MESH_FOLDER + "/T_RMM_AzureBlockhead_BaseColor",
    UI_FOLDER + "/MI_AzureBlockhead_Silhouette",
    UI_FOLDER + "/MI_AzureBlockhead_Symbol",
    UI_FOLDER + "/M_AzureBlockhead_Mark",
    UI_FOLDER + "/M_AzureBlockhead_Silhouette",
    UI_FOLDER + "/T_AzureBlockhead_Silhouette",
    UI_FOLDER + "/T_AzureBlockhead_Symbol",
    UI_FOLDER + "/T_AzureBlockhead_GaugeRamp",
):
    if unreal.EditorAssetLibrary.does_asset_exist(obsolete):
        unreal.EditorAssetLibrary.delete_asset(obsolete)
icon = import_texture("T_RMM_AzureBlockhead_Icon.png", "T_AzureBlockhead_Icon", UI_FOLDER)
for tile_name in ("Cyan", "Gold", "Navy", "Ice"):
    texture = import_texture(
        "T_RMM_AzureBlockhead_" + tile_name + ".png",
        "T_AzureBlockhead_" + tile_name,
        MESH_FOLDER,
    )
    texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_BC7)
    texture.set_editor_property("compression_quality", unreal.TextureCompressionQuality.TCQ_HIGHEST)
    texture.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_FROM_TEXTURE_GROUP)
    texture.set_editor_property("filter", unreal.TextureFilter.TF_TRILINEAR)
    texture.set_editor_property("address_x", unreal.TextureAddress.TA_CLAMP)
    texture.set_editor_property("address_y", unreal.TextureAddress.TA_CLAMP)
    unreal.EditorAssetLibrary.save_asset(
        MESH_FOLDER + "/T_AzureBlockhead_" + tile_name,
        only_if_is_dirty=False,
    )
    material_name = "M_AzureBlockhead" if tile_name == "Navy" else "M_Azure_" + tile_name
    create_surface_material(texture, material_name)
create_gauge_material(
    "M_AzureBlockhead_HandleGauge", "handleGauge", second_parameter="circularGauge"
)
create_gauge_material("M_AzureBlockhead_HeadGauge", "circularGauge", full_combo_marker=True)
unreal.EditorAssetLibrary.save_directory(CONTENT_ROOT, only_if_is_dirty=False, recursive=True)
unreal.log("AZURE_HAMMER_MATERIALS_READY")
