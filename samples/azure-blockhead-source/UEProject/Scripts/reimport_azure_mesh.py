"""Import the corrected Azure Blockhead mesh into the Windows UE4.27 project."""

import os
import unreal


PROJECT_ROOT = os.path.abspath(unreal.Paths.project_dir())
SOURCE = os.path.normpath(
    os.path.join(os.path.dirname(PROJECT_ROOT), "Source", "SM_RMM_AzureBlockhead_Rotated.obj")
)
DESTINATION = "/Game/RMM/Hammers/azure-blockhead-runtime-verified/Mesh"
ASSET_NAME = "SM_AzureBlockhead_Rotated"
SURFACE_MATERIAL = (
    "/Game/RMM/Hammers/azure-blockhead-runtime-verified/Materials/"
    "M_AzureBlockhead"
)

unreal.EditorAssetLibrary.make_directory(DESTINATION)
task = unreal.AssetImportTask()
task.filename = SOURCE
task.destination_path = DESTINATION
task.destination_name = ASSET_NAME
task.automated = True
task.replace_existing = True
task.save = True
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

asset_path = DESTINATION + "/" + ASSET_NAME
mesh = unreal.EditorAssetLibrary.load_asset(asset_path)
if not mesh or mesh.get_class().get_name() != "StaticMesh":
    raise RuntimeError("UE did not import the corrected Azure StaticMesh: " + asset_path)

material = unreal.EditorAssetLibrary.load_asset(SURFACE_MATERIAL)
if not material or material.get_class().get_name() not in ("Material", "MaterialInstanceConstant"):
    raise RuntimeError("Azure surface material is missing: " + SURFACE_MATERIAL)

slots = mesh.get_editor_property("static_materials")
if not slots:
    raise RuntimeError("The imported Azure StaticMesh has no material slots")
for slot in slots:
    slot.set_editor_property("material_interface", material)
mesh.set_editor_property("static_materials", slots)

unreal.EditorAssetLibrary.save_asset(asset_path, only_if_is_dirty=False)
unreal.log("AZURE_ROTATED_MESH_READY " + asset_path + " with " + str(len(slots)) + " textured material slots")
