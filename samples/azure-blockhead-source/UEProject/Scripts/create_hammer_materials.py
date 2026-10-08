"""Import Azure Blockhead UI art and create the two required Material Instances."""

import os
import unreal


CONTENT_ROOT = "/Game/RMM/Hammers/azure-blockhead-verified"
UI_FOLDER = CONTENT_ROOT + "/UI"
PROJECT_ROOT = os.path.abspath(unreal.Paths.project_dir())
SOURCE_ROOT = os.path.normpath(os.path.join(PROJECT_ROOT, "..", "Source"))


def import_texture(filename, asset_name):
    task = unreal.AssetImportTask()
    task.filename = os.path.join(SOURCE_ROOT, filename)
    task.destination_path = UI_FOLDER
    task.destination_name = asset_name
    task.automated = True
    task.replace_existing = True
    task.save = True
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    asset = unreal.EditorAssetLibrary.load_asset(UI_FOLDER + "/" + asset_name)
    if not asset or asset.get_class().get_name() != "Texture2D":
        raise RuntimeError("Failed to import Texture2D: " + filename)
    return asset


def create_material(name):
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

        sample = unreal.MaterialEditingLibrary.create_material_expression(
            material,
            unreal.MaterialExpressionTextureSampleParameter2D,
            -400,
            0,
        )
        sample.set_editor_property("parameter_name", "Texture")
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
silhouette = import_texture(
    "T_RMM_AzureBlockhead_Silhouette.png", "T_AzureBlockhead_Silhouette"
)
symbol = import_texture("T_RMM_AzureBlockhead_Symbol.png", "T_AzureBlockhead_Symbol")
parent = create_material("M_AzureBlockhead_Mark")
create_instance("MI_AzureBlockhead_Silhouette", parent, silhouette)
create_instance("MI_AzureBlockhead_Symbol", parent, symbol)
unreal.EditorAssetLibrary.save_directory(CONTENT_ROOT, only_if_is_dirty=False, recursive=True)
unreal.log("AZURE_MARK_ASSETS_READY")
