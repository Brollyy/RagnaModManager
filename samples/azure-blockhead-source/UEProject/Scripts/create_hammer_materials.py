"""Import Azure Blockhead textures and create its surface and selector materials."""

import os
import unreal


CONTENT_ROOT = "/Game/RMM/Hammers/azure-blockhead-runtime-verified"
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


def connect_combo_glow(material, gauge_parameters, emissive_color):
    """Make this gauge slot respond to BP_RagnarockHand's scalar parameters."""
    gauge_nodes = []
    for index, gauge_parameter in enumerate(gauge_parameters):
        gauge = unreal.MaterialEditingLibrary.create_material_expression(
            material,
            unreal.MaterialExpressionScalarParameter,
            -360,
            260 + index * 160,
        )
        gauge.set_editor_property("parameter_name", gauge_parameter)
        gauge.set_editor_property("default_value", 0.0)
        gauge_nodes.append(gauge)

    total_gauge = gauge_nodes[0]
    for index, gauge in enumerate(gauge_nodes[1:]):
        combined = unreal.MaterialEditingLibrary.create_material_expression(
            material, unreal.MaterialExpressionAdd, -100, 260 + index * 160
        )
        for source, input_name in ((total_gauge, "A"), (gauge, "B")):
            if not unreal.MaterialEditingLibrary.connect_material_expressions(
                source, "", combined, input_name
            ):
                raise RuntimeError("Could not combine Azure combo gauge parameters")
        total_gauge = combined

    strength = unreal.MaterialEditingLibrary.create_material_expression(
        material, unreal.MaterialExpressionConstant, 100, 100
    )
    strength.set_editor_property("r", 5.0)
    glow_amount = unreal.MaterialEditingLibrary.create_material_expression(
        material, unreal.MaterialExpressionMultiply, 300, 260
    )
    glow_color = unreal.MaterialEditingLibrary.create_material_expression(
        material, unreal.MaterialExpressionConstant3Vector, 100, 440
    )
    glow_color.set_editor_property("constant", unreal.LinearColor(*emissive_color, 1.0))
    glow = unreal.MaterialEditingLibrary.create_material_expression(
        material, unreal.MaterialExpressionMultiply, 520, 330
    )

    connections = [
        (total_gauge, "", glow_amount, "A"),
        (strength, "", glow_amount, "B"),
        (glow_amount, "", glow, "A"),
        (glow_color, "", glow, "B"),
    ]
    for source, output, destination, input_name in connections:
        if not unreal.MaterialEditingLibrary.connect_material_expressions(
            source, output, destination, input_name
        ):
            raise RuntimeError(
                "Could not connect the " + ", ".join(gauge_parameters)
                + " combo glow material graph"
            )
    if not unreal.MaterialEditingLibrary.connect_material_property(
        glow, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    ):
        raise RuntimeError("Could not connect the combo glow to Emissive Color")


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
        raise RuntimeError("Could not connect the Azure base-color texture to the surface material")
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    unreal.log("AZURE_SURFACE_MATERIAL_READY " + path + " texture=" + str(base_color))
    return material


def create_color_material(name, color, gauge_parameters, emissive_color):
    """Create a simple authored material for a distinct OBJ material slot."""
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
    color_expression = unreal.MaterialEditingLibrary.create_material_expression(
        material,
        unreal.MaterialExpressionConstant3Vector,
        -400,
        0,
    )
    color_expression.set_editor_property("constant", unreal.LinearColor(*color, 1.0))
    # Constant3Vector exposes its sole output without a named pin in UE 4.27;
    # an empty output name selects that first output.
    if not unreal.MaterialEditingLibrary.connect_material_property(
        color_expression, "", unreal.MaterialProperty.MP_BASE_COLOR
    ):
        raise RuntimeError("Could not connect " + name + " to Base Color")
    connect_combo_glow(material, gauge_parameters, emissive_color)
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    unreal.log("AZURE_SLOT_MATERIAL_READY " + path)
    return material


def create_mark_material(name, texture):
    path = UI_FOLDER + "/" + name
    material = (
        unreal.EditorAssetLibrary.load_asset(path)
        if unreal.EditorAssetLibrary.does_asset_exist(path)
        else None
    )
    if not material:
        material = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            name, UI_FOLDER, unreal.Material, unreal.MaterialFactoryNew()
        )
    material.set_editor_property("material_domain", unreal.MaterialDomain.MD_SURFACE)
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    material.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    unreal.MaterialEditingLibrary.delete_all_material_expressions(material)

    sample = unreal.MaterialEditingLibrary.create_material_expression(
        material,
        unreal.MaterialExpressionTextureSampleParameter2D,
        -400,
        0,
    )
    sample.set_editor_property("parameter_name", "Texture")
    sample.set_editor_property("texture", texture)
    unreal.MaterialEditingLibrary.connect_material_property(
        sample, "RGB", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    )
    unreal.MaterialEditingLibrary.connect_material_property(
        sample, "A", unreal.MaterialProperty.MP_OPACITY
    )
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    return material


def create_instance(name, parent, texture):
    path = UI_FOLDER + "/" + name
    instance = (
        unreal.EditorAssetLibrary.load_asset(path)
        if unreal.EditorAssetLibrary.does_asset_exist(path)
        else None
    )
    if not instance:
        instance = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            name,
            UI_FOLDER,
            unreal.MaterialInstanceConstant,
            unreal.MaterialInstanceConstantFactoryNew(),
        )
    instance.set_editor_property("parent", parent)
    unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
        instance, "Texture", texture
    )
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    return instance


unreal.EditorAssetLibrary.make_directory(UI_FOLDER)
unreal.EditorAssetLibrary.make_directory(MESH_FOLDER)
unreal.EditorAssetLibrary.make_directory(MATERIAL_FOLDER)
silhouette = import_texture(
    "T_RMM_AzureBlockhead_Silhouette.png", "T_AzureBlockhead_Silhouette", UI_FOLDER
)
symbol = import_texture("T_RMM_AzureBlockhead_Symbol.png", "T_AzureBlockhead_Symbol", UI_FOLDER)
base_color = import_texture(
    "T_RMM_AzureBlockhead_BaseColor.png", "T_RMM_AzureBlockhead_BaseColor", MESH_FOLDER
)
create_surface_material(base_color)
create_color_material(
    "M_AzureBlockhead_Gold",
    (0.92, 0.56, 0.06),
    ("circularGauge", "handleGauge"),
    (1.0, 0.52, 0.08),
)
create_color_material(
    "M_AzureBlockhead_Navy",
    (0.01, 0.07, 0.18),
    ("handleGauge",),
    (0.08, 0.68, 1.0),
)
create_mark_material("M_AzureBlockhead_Silhouette", silhouette)
parent = create_mark_material("M_AzureBlockhead_Mark", symbol)
create_instance("MI_AzureBlockhead_Symbol", parent, symbol)
unreal.EditorAssetLibrary.save_directory(CONTENT_ROOT, only_if_is_dirty=False, recursive=True)
unreal.log("AZURE_MARK_ASSETS_READY")
