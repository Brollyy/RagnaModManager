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


def create_gauge_material(name, parameter, fill_color, ramp_texture, second_parameter=None, second_color=None):
    """Make a dark track with stock blue first-stage and gold second-stage fill."""
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
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_OPAQUE)
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_DEFAULT_LIT)
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
    # This grayscale texture encodes the bar's U coordinate in its red channel.
    # Sampling it avoids a fragile editor-only TextureCoordinate graph pin.
    ramp = expression(unreal.MaterialExpressionTextureSampleParameter2D, -520, 120)
    ramp.set_editor_property("parameter_name", "GaugeRamp")
    ramp.set_editor_property("texture", ramp_texture)
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

    if second_parameter:
        second_gauge = scalar(second_parameter, -520, 40)
        half = constant(0.5, -300, 220)
        two = constant(2.0, -300, 340)
        first_position = expression(unreal.MaterialExpressionMultiply, -100, 260)
        connect(ramp, "R", first_position, "A")
        connect(two, "", first_position, "B")
        shifted_position = expression(unreal.MaterialExpressionSubtract, -100, 420)
        connect(ramp, "R", shifted_position, "A")
        connect(half, "", shifted_position, "B")
        second_position = expression(unreal.MaterialExpressionMultiply, 80, 420)
        connect(shifted_position, "", second_position, "A")
        connect(two, "", second_position, "B")
        first_fill = threshold(gauge, first_position, "", 100, -100)
        second_fill = threshold(second_gauge, second_position, "", 100, 100)

        filled = expression(unreal.MaterialExpressionIf, 300, 80)
        connect(ramp, "R", filled, "A")
        connect(half, "", filled, "B")
        connect(second_fill, "", filled, "A > B")
        connect(first_fill, "", filled, "A == B")
        connect(first_fill, "", filled, "A < B")

        first_color = expression(unreal.MaterialExpressionConstant3Vector, 100, 500)
        first_color.set_editor_property("constant", unreal.LinearColor(*fill_color, 1.0))
        second_color_node = expression(unreal.MaterialExpressionConstant3Vector, 100, 660)
        second_color_node.set_editor_property(
            "constant", unreal.LinearColor(*(second_color or fill_color), 1.0)
        )
        lit_color = expression(unreal.MaterialExpressionIf, 300, 380)
        connect(ramp, "R", lit_color, "A")
        connect(half, "", lit_color, "B")
        connect(second_color_node, "", lit_color, "A > B")
        connect(first_color, "", lit_color, "A == B")
        connect(first_color, "", lit_color, "A < B")
    else:
        filled = threshold(gauge, ramp, "R", 100, 80)
        lit_color = expression(unreal.MaterialExpressionConstant3Vector, -80, 520)
        lit_color.set_editor_property("constant", unreal.LinearColor(*fill_color, 1.0))

    track_color = expression(unreal.MaterialExpressionConstant3Vector, -80, 360)
    track_color.set_editor_property("constant", unreal.LinearColor(0.008, 0.018, 0.035, 1.0))
    surface = expression(unreal.MaterialExpressionLinearInterpolate, 520, 260)
    connect(track_color, "", surface, "A")
    connect(lit_color, "", surface, "B")
    connect(filled, "", surface, "Alpha")
    if not unreal.MaterialEditingLibrary.connect_material_property(
        surface, "", unreal.MaterialProperty.MP_BASE_COLOR
    ):
        raise RuntimeError("Could not connect the Azure gauge bar to Base Color")

    # Stock gauge materials expose a `glow` scalar and use emissive output for
    # the active fill. Keep the empty track lit only by scene lighting so it
    # stays dark, while the blue/gold section remains legible in the race.
    glow = scalar("glow", 520, 620, 34.242756)
    emissive_fill = expression(unreal.MaterialExpressionMultiply, 520, 460)
    connect(lit_color, "", emissive_fill, "A")
    connect(filled, "", emissive_fill, "B")
    emissive_strength = expression(unreal.MaterialExpressionMultiply, 760, 460)
    connect(emissive_fill, "", emissive_strength, "A")
    connect(glow, "", emissive_strength, "B")
    if not unreal.MaterialEditingLibrary.connect_material_property(
        emissive_strength, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    ):
        raise RuntimeError("Could not connect the Azure gauge fill to Emissive Color")
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    unreal.log("AZURE_GAUGE_MATERIAL_READY " + path + " parameter=" + parameter)
    return material

def create_surface_material(base_color):
    path = MATERIAL_FOLDER + "/M_AzureBlockhead"
    material = (
        unreal.EditorAssetLibrary.load_asset(path)
        if unreal.EditorAssetLibrary.does_asset_exist(path)
        else None
    )
    if not material:
        material = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            "M_AzureBlockhead", MATERIAL_FOLDER, unreal.Material, unreal.MaterialFactoryNew()
        )
    material.set_editor_property("material_domain", unreal.MaterialDomain.MD_SURFACE)
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_OPAQUE)
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_DEFAULT_LIT)
    unreal.MaterialEditingLibrary.delete_all_material_expressions(material)
    sample = unreal.MaterialEditingLibrary.create_material_expression(
        material,
        unreal.MaterialExpressionTextureSampleParameter2D,
        -400,
        0,
    )
    sample.set_editor_property("parameter_name", "BaseColor")
    sample.set_editor_property("texture", base_color)
    if not unreal.MaterialEditingLibrary.connect_material_property(
        sample, "RGB", unreal.MaterialProperty.MP_BASE_COLOR
    ):
        raise RuntimeError("Could not connect the Azure body texture")
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
    UI_FOLDER + "/MI_AzureBlockhead_Silhouette",
    UI_FOLDER + "/MI_AzureBlockhead_Symbol",
    UI_FOLDER + "/M_AzureBlockhead_Mark",
    UI_FOLDER + "/M_AzureBlockhead_Silhouette",
    UI_FOLDER + "/T_AzureBlockhead_Silhouette",
    UI_FOLDER + "/T_AzureBlockhead_Symbol",
):
    if unreal.EditorAssetLibrary.does_asset_exist(obsolete):
        unreal.EditorAssetLibrary.delete_asset(obsolete)
icon = import_texture("T_RMM_AzureBlockhead_Icon.png", "T_AzureBlockhead_Icon", UI_FOLDER)
gauge_ramp = import_texture(
    "T_RMM_AzureBlockhead_GaugeRamp.png", "T_AzureBlockhead_GaugeRamp", UI_FOLDER
)
gauge_ramp.set_editor_property("srgb", False)
gauge_ramp.set_editor_property(
    "compression_settings", unreal.TextureCompressionSettings.TC_GRAYSCALE
)
gauge_ramp.set_editor_property(
    "mip_gen_settings", unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS
)
gauge_ramp.set_editor_property("filter", unreal.TextureFilter.TF_BILINEAR)
gauge_ramp.set_editor_property("address_x", unreal.TextureAddress.TA_CLAMP)
gauge_ramp.set_editor_property("address_y", unreal.TextureAddress.TA_CLAMP)
unreal.EditorAssetLibrary.save_asset(
    UI_FOLDER + "/T_AzureBlockhead_GaugeRamp", only_if_is_dirty=False
)
base_color = import_texture(
    "T_RMM_AzureBlockhead_BaseColor.png", "T_RMM_AzureBlockhead_BaseColor", MESH_FOLDER
)
create_surface_material(base_color)
create_gauge_material(
    "M_AzureBlockhead_HandleGauge", "handleGauge", (0.05, 0.55, 1.0), gauge_ramp
)
create_gauge_material(
    "M_AzureBlockhead_HeadGauge",
    "handleGauge",
    (0.05, 0.55, 1.0),
    gauge_ramp,
    second_parameter="circularGauge",
    second_color=(1.0, 0.48, 0.02),
)
unreal.EditorAssetLibrary.save_directory(CONTENT_ROOT, only_if_is_dirty=False, recursive=True)
unreal.log("AZURE_HAMMER_MATERIALS_READY")
