# Azure Blockhead UE 4.27 project

Open `RMMAzureBlockhead.uproject` in UE 4.27 on Windows. The project includes the imported mesh, its original base-color texture and surface material, the in-game icon, and source-authored silhouette/symbol textures with their two `MaterialInstanceConstant` assets. Its Windows target is explicitly `PCD3D_SM5`, matching Ragnarock on PC.

The OBJ, MTL, and PNG files in the parent `Source/` directory are the editable source art. `Scripts/create_hammer_materials.py` imports the silhouette/symbol source textures and creates the material graph and instances; run it from the UE 4.27 editor's Python commandlet if you regenerate the source assets. The `.uasset` files under `Content/` are UE 4.27 editor assets, not a finished `.rhammer` archive. Open the mesh and materials in the editor, verify the references, then cook/package for WindowsNoEditor. Follow the parent [full authoring guide](../../../../docs/custom-hammer-authoring-ue4-27.md) for the archive manifest, deployment, and in-game checks.

This project has not yet been cooked on Windows. The Linux-built assets do not contain the Direct3D shader map required to verify the material in Ragnarock. A successful Windows `PCD3D_SM5` cook and an in-game material check remain required before this sample can be called a working hammer package.
