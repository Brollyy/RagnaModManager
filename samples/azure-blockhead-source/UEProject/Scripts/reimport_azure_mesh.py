"""Import the corrected Azure Blockhead mesh into the Windows UE4.27 project."""

import os
import unreal


PROJECT_ROOT = os.path.abspath(unreal.Paths.project_dir())
SOURCE = os.path.normpath(
    os.path.join(os.path.dirname(PROJECT_ROOT), "Source", "SM_RMM_AzureBlockhead.obj")
)
DESTINATION = "/Game/RMM/Hammers/azure-blockhead-runtime-verified/Mesh"
ASSET_NAME = "SM_AzureBlockhead_Rotated"
SURFACE_MATERIAL = (
    "/Game/RMM/Hammers/azure-blockhead-runtime-verified/Materials/"
    "M_AzureBlockhead"
)
SLOT_MATERIALS = {
    "M_Character": SURFACE_MATERIAL,
    "M_JaugeManche": (
        "/Game/RMM/Hammers/azure-blockhead-runtime-verified/Materials/"
        "M_AzureBlockhead_Navy"
    ),
    "M_JaugeTete": (
        "/Game/RMM/Hammers/azure-blockhead-runtime-verified/Materials/"
        "M_AzureBlockhead_Gold"
    ),
}

unreal.EditorAssetLibrary.make_directory(DESTINATION)
asset_path = DESTINATION + "/" + ASSET_NAME
if unreal.EditorAssetLibrary.does_asset_exist(asset_path):
    if not unreal.EditorAssetLibrary.delete_asset(asset_path):
        raise RuntimeError("Could not replace the previous Azure StaticMesh: " + asset_path)

task = unreal.AssetImportTask()
task.filename = SOURCE
task.destination_path = DESTINATION
task.destination_name = ASSET_NAME
task.automated = True
task.replace_existing = True
task.save = True
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

mesh = unreal.EditorAssetLibrary.load_asset(asset_path)
if not mesh or mesh.get_class().get_name() != "StaticMesh":
    raise RuntimeError("UE did not import the corrected Azure StaticMesh: " + asset_path)

slot_count = len(mesh.get_editor_property("static_materials"))
static_materials = mesh.get_editor_property("static_materials")
slot_names = [str(slot.get_editor_property("material_slot_name")) for slot in static_materials]
expected_slot_names = ["M_Character", "M_JaugeManche", "M_JaugeTete"]
if slot_count != len(expected_slot_names) or slot_names != expected_slot_names:
    raise RuntimeError(
        "The Azure StaticMesh must preserve stock material slot order "
        + ", ".join(expected_slot_names)
        + "; received "
        + ", ".join(slot_names)
    )
# Set the StaticMaterials array so assignments are serialized into the cook.
# Applying the cyan atlas to every slot paints accent geometry with unrelated
# atlas colors.
updated_static_materials = []
for slot in static_materials:
    slot_name = str(slot.get_editor_property("material_slot_name"))
    material_path = SLOT_MATERIALS.get(slot_name)
    if not material_path:
        raise RuntimeError("Unexpected Azure StaticMesh material slot: " + slot_name)
    material = unreal.EditorAssetLibrary.load_asset(material_path)
    if not material or material.get_class().get_name() not in ("Material", "MaterialInstanceConstant"):
        raise RuntimeError("Azure slot material is missing: " + material_path)
    # Rebuild each UStaticMaterial value explicitly. Mutating the Python
    # wrapper in place appears to work during this commandlet but UE4.27
    # retained an old material reference in the saved array.
    updated_slot = unreal.StaticMaterial(
        material,
        slot_name,
        slot.get_editor_property("uv_channel_data"),
    )
    updated_static_materials.append(updated_slot)
    unreal.log("AZURE_SLOT_ASSIGN slot=" + slot_name + " material=" + material.get_path_name())
mesh.set_editor_property("static_materials", updated_static_materials)
mesh.modify()

unreal.EditorAssetLibrary.save_asset(asset_path, only_if_is_dirty=False)
mesh = unreal.EditorAssetLibrary.load_asset(asset_path)
saved_slots = mesh.get_editor_property("static_materials")
for index, slot in enumerate(saved_slots):
    assigned = slot.get_editor_property("material_interface")
    slot_name = str(slot.get_editor_property("material_slot_name"))
    expected_path = SLOT_MATERIALS.get(slot_name)
    expected = unreal.EditorAssetLibrary.load_asset(expected_path) if expected_path else None
    unreal.log(
        "AZURE_SLOT_RELOAD slot=" + slot_name
        + " expected=" + (expected.get_path_name() if expected else "<missing>")
        + " assigned=" + (assigned.get_path_name() if assigned else "<null>")
    )
    if not assigned or not expected or assigned.get_path_name() != expected.get_path_name():
        raise RuntimeError("Azure StaticMesh material slot " + str(index) + " was not saved correctly")
unreal.log("AZURE_ROTATED_MESH_READY " + asset_path + " with " + str(len(saved_slots)) + " correctly assigned material slots")
