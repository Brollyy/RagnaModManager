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
create_mark_material("M_AzureBlockhead_Silhouette", silhouette)
parent = create_mark_material("M_AzureBlockhead_Mark", symbol)
create_instance("MI_AzureBlockhead_Symbol", parent, symbol)
unreal.EditorAssetLibrary.save_directory(CONTENT_ROOT, only_if_is_dirty=False, recursive=True)
unreal.log("AZURE_MARK_ASSETS_READY")
