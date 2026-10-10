"""Import transparent preview textures for the Ragna and Summer demo hammers."""

import os
import unreal


PROJECT_ROOT = os.path.abspath(unreal.Paths.project_dir())
SOURCE_ROOT = os.path.normpath(os.path.join(PROJECT_ROOT, "..", "Source"))
ICONS = {
    "T_BlockheadDemo_Icon": ("T_RagnaBlockhead_Preview.png", "blockhead-demo"),
    "T_SummerDemo_Icon": ("T_SummerCustom_Preview.png", "summer-demo"),
}


for asset_name, (filename, hammer_id) in ICONS.items():
    destination = "/Game/RMM/Hammers/{}/UI".format(hammer_id)
    unreal.EditorAssetLibrary.make_directory(destination)

    task = unreal.AssetImportTask()
    task.filename = os.path.join(SOURCE_ROOT, filename)
    task.destination_path = destination
    task.destination_name = asset_name
    task.automated = True
    task.replace_existing = True
    task.save = True
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

    asset_path = destination + "/" + asset_name
    texture = unreal.EditorAssetLibrary.load_asset(asset_path)
    if not texture or texture.get_class().get_name() != "Texture2D":
        raise RuntimeError("Failed to import transparent preview Texture2D: " + asset_path)
    unreal.EditorAssetLibrary.save_asset(asset_path, only_if_is_dirty=False)

unreal.log("RMM_DEMO_TRANSPARENT_PREVIEW_TEXTURES_READY")
