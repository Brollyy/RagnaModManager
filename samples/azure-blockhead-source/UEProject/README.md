# Azure Blockhead UE 4.27 project

Open `RMMAzureBlockhead.uproject` in UE 4.27 on Windows. The project includes the imported mesh, its original base-color texture and surface material, and the in-game icon. Its Windows target is explicitly `PCD3D_SM5`, matching Ragnarock on PC.

The OBJ, MTL, and PNG files in the parent `Source/` directory are the editable source art. The `.uasset` files under `Content/` are UE 4.27 editor assets, not a finished `.rhammer` archive. Open the mesh and material in the editor, verify the references, then cook/package for WindowsNoEditor. Follow the parent [full authoring guide](../../../../docs/custom-hammer-authoring-ue4-27.md) for the remaining RMM properties, symbol/silhouette assets, archive manifest, deployment, and in-game checks.

This project has not yet been cooked on Windows. The Linux-built assets do not contain the Direct3D shader map required to verify the material in Ragnarock. A successful Windows `PCD3D_SM5` cook and an in-game material check remain required before this sample can be called a working hammer package.
