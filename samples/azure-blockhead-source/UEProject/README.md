# Azure Blockhead UE 4.27 project

Open `RMMAzureBlockhead.uproject` in UE 4.27 on Windows. Generated editor `Content/` assets are not checked in; the authored assets are regenerated from the adjacent original OBJ and PNGs at the package paths used by the manifest. Its Windows target is explicitly `PCD3D_SM5`, matching Ragnarock on PC.

The OBJ, MTL, and PNG files in the parent `Source/` directory are the editable source art. Run `Scripts/create_hammer_materials.py` to import the base-color, silhouette, and symbol textures and build the surface, accent, and marker material graphs. Then run `Scripts/reimport_azure_mesh.py` to import `SM_RMM_AzureBlockhead.obj` as the game-oriented Static Mesh and assign the cyan texture, gold, and navy materials to matching source slots. Enable the **Python Editor Script Plugin** for the project, restart the editor, and run the scripts in order. You can use UE's File → Execute Python Script menu, or run the commandlet from `Scripts/` in a Windows Command Prompt (replace the UE install path if needed):

```bat
set UE427=C:\Program Files\Epic Games\UE_4.27
"%UE427%\Engine\Binaries\Win64\UE4Editor-Cmd.exe" "..\RMMAzureBlockhead.uproject" -run=pythonscript -script="%CD%\create_hammer_materials.py"
"%UE427%\Engine\Binaries\Win64\UE4Editor-Cmd.exe" "..\RMMAzureBlockhead.uproject" -run=pythonscript -script="%CD%\reimport_azure_mesh.py"
```

Running both scripts recreates the editor assets under `Content/RMM/Hammers/azure-blockhead-runtime-verified/`; the cooked `.rhammer` sample is in the parent directory. A Windows `PCD3D_SM5` cook produces the shared shader archive, which `build_rhammer.py` validates and includes. Follow the parent [full authoring guide](../../../../docs/custom-hammer-authoring-ue4-27.md) for cooking, archive assembly, deployment, and in-game checks.
