"""Create temporary UE editor assets at game stock paths for mesh cooking."""

import unreal


STOCK_DIR = "/Game/Meshes/Hammers/DefaultHammer"
PAIRS = (
    ("MI_HandleGauge", "/Game/RMM/Hammers/azure-blockhead/Materials/M_AzureBlockhead_HandleGauge"),
    ("MI_HeadGauge", "/Game/RMM/Hammers/azure-blockhead/Materials/M_AzureBlockhead_HeadGauge"),
)

unreal.EditorAssetLibrary.make_directory(STOCK_DIR)
tools = unreal.AssetToolsHelpers.get_asset_tools()
for name, parent_path in PAIRS:
    parent = unreal.EditorAssetLibrary.load_asset(parent_path)
    if not parent or parent.get_class().get_name() != "Material":
        raise RuntimeError("Missing authored gauge parent material: " + parent_path)
    path = STOCK_DIR + "/" + name
    if unreal.EditorAssetLibrary.does_asset_exist(path):
        unreal.EditorAssetLibrary.delete_asset(path)
    instance = tools.create_asset(
        name,
        STOCK_DIR,
        unreal.MaterialInstanceConstant,
        unreal.MaterialInstanceConstantFactoryNew(),
    )
    if not instance:
        raise RuntimeError("Could not create temporary stock-path MI: " + path)
    instance.set_editor_property("parent", parent)
    unreal.EditorAssetLibrary.save_asset(path, only_if_is_dirty=False)
    unreal.log("AZURE_STOCK_PATH_MI_READY " + path + " parent=" + parent_path)
