# Azure Blockhead UE 4.27 project

Open `RMMAzureBlockhead.uproject` in UE 4.27 on Windows. Generated editor `Content/` assets are not checked in. The source FBX is built from the stock Default Hammer export plus an authored Azure head; textures and the icon are generated locally. The project targets `PCD3D_SM5`, matching Ragnarock on PC.

The source mesh uses the stock Default Hammer as a donor for the grip, handle, gauge geometry and all four UV channels, pivot, and section layout. The mesh retains the stock gauge material references. Temporary stand-ins are created at the stock paths while cooking in UE; the final package excludes them so the running game resolves the mesh to Ragnarock's own gauge instances.

## Prepare the source

Export `/Game/Meshes/Hammers/DefaultHammer/SM_Hammer_Default` from the installed game as FBX, preserving all four UV channels and the three material section names. Generate the source artwork:

```bat
python ..\..\generate_source.py
```

The generated source artwork lives in `../Source/`. From Blender, run the stock-based mesh builder to preserve the gauge UV channels and replace the head:

```bat
blender --background --python build_stock_based_azure_fbx.py -- C:\path\to\SM_Hammer_Default.fbx ..\Source\SM_RMM_AzureBlockhead.fbx
```

For your own hammer, replace the authored Azure head with your design in Blender. Preserve the stock handle, handle gauge, head marker, material slots, pivot, scale, orientation, and every UV channel.

## Create UE assets and cook

The project enables **Python Editor Script Plugin** and **Editor Scripting Utilities**. From this `Scripts/` directory, run the scripts in order:

```bat
set UE427=C:\Program Files\Epic Games\UE_4.27
"%UE427%\Engine\Binaries\Win64\UE4Editor-Cmd.exe" "..\RMMAzureBlockhead.uproject" -run=pythonscript -script="%CD%\create_hammer_materials.py"
"%UE427%\Engine\Binaries\Win64\UE4Editor-Cmd.exe" "..\RMMAzureBlockhead.uproject" -run=pythonscript -script="%CD%\create_stock_gauge_refs.py"
"%UE427%\Engine\Binaries\Win64\UE4Editor-Cmd.exe" "..\RMMAzureBlockhead.uproject" -run=pythonscript -script="%CD%\reimport_azure_mesh.py"
"%UE427%\Engine\Binaries\Win64\UE4Editor-Cmd.exe" "..\RMMAzureBlockhead.uproject" -run=cook -targetplatform=WindowsNoEditor -cookall
```

`create_hammer_materials.py` imports the body texture and icon and creates the Azure body material plus temporary gauge parent materials. `create_stock_gauge_refs.py` creates editor-only material instances at the game's stock gauge paths. `reimport_azure_mesh.py` imports the donor-based mesh, checks the three stock section names and order, and assigns the body material plus those stock paths. Review the cook log: each slot must reload with its intended material path and the cook must complete for `WindowsNoEditor` / `PCD3D_SM5`.

## Package

Use the package builder to assemble the `.rhammer` from the Windows cook. For example, still from this directory:

```bat
python build_rhammer.py "..\Saved\Cooked\WindowsNoEditor\RMMAzureBlockhead\Content" --output "..\..\AzureBlockhead-v35.rhammer"
```

The builder checks the cooked body material, mesh, texture, icon, PCD3D shader metadata, and shader archive. The temporary gauge materials are excluded from the `.rhammer`; at runtime the stock material paths resolve inside Ragnarock. Keep the manifest and builder paths aligned if you change the package's unique content root.

For RMM import, deployment, and in-game validation, follow the [full authoring guide](../../../docs/custom-hammer-authoring-ue4-27.md).
