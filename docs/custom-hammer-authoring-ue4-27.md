# Create a Ragnarock hammer package with UE 4.27

This guide covers a self-contained custom hammer package for RagnaModManager (RMM). It starts with new source files, imports and cooks them in Unreal Engine 4.27, and packages the cooked assets as an `.rhammer` library archive. RMM then creates the game-specific data asset and merged PAK for the installed Ragnarock build.

The repository includes original source geometry, material art, and an icon for the [Azure Blockhead sample](../samples/azure-blockhead-source/README.md). Those source files are ready for the UE import/cook steps below; they are not mislabeled as cooked game assets.

Ragnarock's inspected build uses UE 4.27 and a V11 PAK. Use the same engine version and cook for `WindowsNoEditor`, even when playing through Proton. The public RMM app does not import FBX/OBJ or cook Unreal assets; the creator must provide cooked UE assets. For materials, use a Windows UE 4.27 cooker: Linux UE builds cook Vulkan shader maps, while this game requests `PCD3D_SM5` shader maps at runtime.

### Linux-to-Windows cooking

UE 4.27 does not provide a supported Linux-hosted cross-compile workflow for Windows. Epic's documented cross-compilation support is Windows-hosted development targeting Linux; the reverse direction is not the supported toolchain. Also, a WindowsNoEditor cook is not sufficient by itself: the cook must compile the project's material shaders for `PCD3D_SM5`. This sample's Linux cook fell back to Ragnarock's checker material because it had no usable Direct3D shader map.

Without a local Windows PC, use a Windows guest VM on a Linux host, a temporary Windows cloud VM, or a Windows self-hosted CI runner to run UE 4.27 natively. A QEMU/KVM guest is usually the most direct local option when the Linux host exposes hardware virtualization. The VM needs the UE 4.27 editor/cooker, Visual Studio 2019 and a Windows SDK if building UE from source, and enough disk space for the engine and project. Epic's UE 4.27 setup lists VS 2019 and Windows SDK 10.0.18362 or newer. A UE 4.27 binary installation can avoid compiling the whole engine from source if it is available to the VM. Open this sample project, confirm `PCD3D_SM5` in `Config/DefaultEngine.ini`, cook `WindowsNoEditor`, and inspect the cooker log for successful SM5 material compilation. Run `build_rhammer.py` on the resulting cooked Content directory, then copy the `.rhammer` back to the Linux machine for RMM import, PAK deployment, and Proton runtime checks.

### Build the UE 4.27 editor from source on Windows

When using the UE 4.27 source tree instead of an installed editor, compile it natively in Windows. The Epic GitHub source checkout or a transferred source snapshot is sufficient; Unreal Editor and the command-line cooker do not require Epic Games Launcher sign-in. Install Visual Studio 2019 with the **Desktop development with C++** workload, its v142 x86/x64 toolset, and a Windows 10 SDK (10.0.18362 or newer). UE4's project-file generator also checks for the .NET Framework SDK: install the **.NET Framework 4.6.2 Developer Pack**, which includes the SDK and targeting pack, if UnrealBuildTool reports that `NetFxSDK` cannot be found. A targeting pack by itself may not register the SDK directory UnrealBuildTool expects. Then open a Developer Command Prompt and run these commands from the engine source root. Epic's [UE 4.27 Project Files for IDEs](https://dev.epicgames.com/documentation/unreal-engine/project-files-for-ides?application_version=4.27) describes project-file generation, and its [UE 4.27 Unreal Build System guide](https://dev.epicgames.com/documentation/unreal-engine/unreal-build-system?application_version=4.27) describes compiling UE4 targets. Microsoft's [.NET Framework 4.6.2 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net462) page links the offline installer:

```bat
Setup.bat
GenerateProjectFiles.bat -2019
Engine\Build\BatchFiles\Build.bat UE4Editor Win64 Development -WaitMutex
```

`Setup.bat` obtains the engine's pinned binary dependencies; it may ask Windows to approve the signed UE prerequisite installer. On a clean Windows image, the prerequisite setup can also enable .NET Framework 3.5 through Windows Features and request a restart. Let Windows finish that update, sign back in, and rerun `Setup.bat` if the original command did not resume. When the console reaches `Installing prerequisites...`, check for the UE4 Prerequisites consent prompt and approve it; the command waits while that prompt is unanswered. After the editor build succeeds, use `Engine\Binaries\Win64\UE4Editor-Cmd.exe` for the sample project's cook command below. This builds the editor/cooker required to import and cook the custom assets; it does not build Ragnarock or its game binaries. If using an already built UE 4.27 editor, skip the engine build and run its `UE4Editor-Cmd.exe` directly.

This is a remote native cook rather than cross-compilation. Building only Unreal's Linux editor on Linux, or forcing `WindowsNoEditor` as a cook target, does not supply the Windows D3D shader compiler path used by Ragnarock.

## What the game expects

The installed `DT_Hammers` DataTable row has these fields:

| Field | Purpose in the custom row |
| --- | --- |
| `Title` | In-game hammer name |
| `Description` | In-game description |
| `Icon` | `Texture2D` shown in the changing-room hammer list |
| `EntitlementId` | Empty for a selectable, non-DLC custom hammer |
| `Data` | Reference to the hammer's data asset |

The game hammer data asset is an instance of the shared `DA_Hammers_C` class. Its inspected fields are `Mesh`, `Silhouette`, and `Symbol`; the stock `DA_SummerHammer` references a Static Mesh and two Material Instances for these fields. The common class default object also supplies `DrumHitSound`, an FMOD `DrumHit` event. RMM preserves this class and its default when it generates each hammer data asset, so creators do not need to add a combo counter or a new sound class.

There is no combo-meter field or combo asset in `DT_Hammers` or the custom hammer data asset schema inspected for this game build. The extracted `BP_HammerSelector` contains the `Silhouette` and `Symbol` references, while the shared hammer class default supplies `DrumHitSound`; none of these is a custom combo-counter asset. RMM creates each hammer data asset using the game's existing `DA_Hammers_C` class. Keep that parent class, provide a valid mesh, and leave `EntitlementId` empty; no extra combo asset or subclass is needed. The game's normal input and hit logic drives the combo meter for custom rows just as it does for stock rows. `DrumHitSound` is the shared hit sound, not the combo counter. A custom hit sound is outside the current RMM package format.

For complete custom presentation, include all four kinds of assets:

1. A cooked `StaticMesh` and its custom material/texture dependency graph.
2. A cooked `Texture2D` for the changing-room list icon.
3. A cooked `MaterialInstanceConstant` for the `Silhouette` data asset property, plus its material and texture dependencies.
4. A cooked `MaterialInstanceConstant` for the `Symbol` data asset property, plus its material and texture dependencies.

`Silhouette` and `Symbol` are `UMaterialInterface` references. This RMM format expects their assigned package exports to be `MaterialInstanceConstant` assets. Base Materials and textures can be shared between these instances if the visual design needs it, but every custom dependency must be included in the archive. A library `thumbnail` is only a desktop preview and does not become the in-game `Icon`.

## 1. Make the source model and texture art

Create the hammer in a 3D application such as Blender. Use a single Static Mesh unless the model needs separate pieces. Keep the grip and head proportions close to Ragnarock's existing hammers so the item reads clearly at the in-game size.

Before export:

- Put the mesh at the origin and set its pivot where the hand should grip it. Apply scale and rotation.
- Triangulate the mesh and check face normals.
- Create a non-overlapping UV set for the surface textures. Add a second UV set only if the mesh needs baked lighting.
- Assign stable material slot names and keep the number of slots small.
- Inspect the collision generated for the Static Mesh and keep it simple. The hammer Data Asset has no per-hammer collision field, so collision settings come from the mesh asset itself.
- Export FBX 2018, which matches UE 4.27's FBX importer. Export only the intended mesh and material assignments.

Create the texture art for the body and optional normal, roughness, metallic, or mask maps. Keep the icon art as a separate square PNG with a transparent or solid background and a strong silhouette at small size. Create separate silhouette/symbol art if those in-game material effects should differ from the surface material.

The Azure Blockhead UE project includes `Scripts/create_hammer_materials.py`, which imports its authored silhouette and symbol PNGs as `Texture2D` assets and creates a translucent unlit parent Material with one `MaterialInstanceConstant` per image. Run it in UE 4.27 with the Python Script Plugin enabled:

```text
UE4Editor-Cmd.exe <ProjectPath>/RMMAzureBlockhead.uproject -run=pythonscript -script=<ProjectPath>/Scripts/create_hammer_materials.py -unattended -nop4 -nullrhi
```

The resulting paths are `UI/MI_AzureBlockhead_Silhouette` and `UI/MI_AzureBlockhead_Symbol`. For a different hammer, adapt the texture names and asset names in that script. These are starter material graphs; preview the instances in the game context and adjust them if Ragnarock's selector expects a different visual treatment.

## 2. Import and author assets in UE 4.27

Create a blank UE 4.27 project and keep the project content under unique folders that will not collide with Ragnarock's `/Game/` assets. For example:

```text
Content/RMM/Hammers/<hammer-id>/Mesh/SM_<Hammer>
Content/RMM/Hammers/<hammer-id>/Materials/M_<Hammer>
Content/RMM/Hammers/<hammer-id>/Materials/MI_<Hammer>
Content/RMM/Hammers/<hammer-id>/Textures/T_<Hammer>_BaseColor
Content/RMM/Hammers/<hammer-id>/UI/T_<Hammer>_Icon
Content/RMM/Hammers/<hammer-id>/UI/M_<Hammer>_Silhouette
Content/RMM/Hammers/<hammer-id>/UI/MI_<Hammer>_Silhouette
Content/RMM/Hammers/<hammer-id>/UI/M_<Hammer>_Symbol
Content/RMM/Hammers/<hammer-id>/UI/MI_<Hammer>_Symbol
```

Import the FBX as a Static Mesh. Inspect scale, orientation, pivot, normals, material slots, UVs, and collision in the Static Mesh Editor. Set up the surface Material and Material Instances and assign them to the mesh sections. Import the icon and any silhouette/symbol textures as Texture assets. Build base Materials and create the two `MaterialInstanceConstant` assets assigned to `Silhouette` and `Symbol`.

Keep all custom dependencies in the project. Do not use a Material Instance whose parent, texture, or other referenced package is in the project unless that package is included in the hammer archive too. References to Ragnarock's stock content may resolve from the installed game, but a package intended to work independently should own its mesh and visual dependencies.

Use the editor to preview the mesh and UI materials and fix broken references before cooking. The changing-room 3D preview, list icon, silhouette material, and symbol material are separate outputs; verify each one separately.

## 3. Cook the assets for the game

Unreal stores editor assets in source formats and converts them to platform-specific formats during cooking. Cook for `WindowsNoEditor`, which is the platform used by the installed PC game:

Make sure the project targets the game's shader format. A project-level Windows target override can replace the engine default. In `Config/DefaultEngine.ini`, use:

```ini
[/Script/WindowsTargetPlatform.WindowsTargetSettings]
!TargetedRHIs=ClearArray
+TargetedRHIs=PCD3D_SM5
```

Do not set the Windows target to `SF_VULKAN_SM5`; that is the Linux shader format. A WindowsNoEditor cook made by the Linux editor can still contain Vulkan shader maps if the project overrides `TargetedRHIs`, and Ragnarock will fall back to its default checker material.

```text
UE4Editor-Cmd.exe <ProjectPath>/<ProjectName>.uproject -run=cook -targetplatform=WindowsNoEditor -cookall
```

Alternatively, use **File → Package Project → Windows (64-bit)** in UE 4.27. The command-line cooker writes under the project `Saved/Sandboxes/Cooked-WindowsNoEditor` directory. Packaging writes into `Saved/StagedBuilds/WindowsNoEditor`. Locate the generated `.uasset` files and their sidecars (`.uexp`, `.ubulk`, or `.uptnl` when present) under the project's cooked `Content` tree. Keep each file belonging to an asset together.

Do not rename cooked package internals by renaming files. Make package names and folders in the editor before cooking. For each manifest `packagePath`, copy the matching cooked package files and preserve their relative sidecars. The RMM archive path is independent of the in-game package path.

## 4. Build the `.rhammer` archive

Create `hammer.json` at the archive root. RMM derives the DataTable row name and generated hammer Data Asset path from `id`. The `iconAssetPath`, `silhouetteAssetPath`, and `symbolAssetPath` values must each name a cooked asset included in `assets`.

```json
{
  "formatVersion": 1,
  "id": "my-hammer",
  "name": "My Hammer",
  "author": "Creator",
  "version": "1.0.0",
  "description": "A custom Ragnarock hammer.",
  "rowName": "RMM_MY_HAMMER",
  "displayName": "My Hammer",
  "meshAssetPath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer",
  "iconAssetPath": "/Game/RMM/Hammers/my-hammer/UI/T_MyHammer_Icon",
  "silhouetteAssetPath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Silhouette",
  "symbolAssetPath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Symbol",
  "dataAssetPath": "/Game/Data/Hammers/DA_RMM_MY_HAMMER",
  "thumbnail": "Preview/my-hammer.png",
  "assets": [
    { "source": "Assets/Mesh/SM_MyHammer.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer" },
    { "source": "Assets/Mesh/SM_MyHammer.uexp", "packagePath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer" },
    { "source": "Assets/Materials/M_MyHammer.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Materials/M_MyHammer" },
    { "source": "Assets/Materials/MI_MyHammer.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Materials/MI_MyHammer" },
    { "source": "Assets/Textures/T_MyHammer_BaseColor.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/Textures/T_MyHammer_BaseColor" },
    { "source": "Assets/UI/T_MyHammer_Icon.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/T_MyHammer_Icon" },
    { "source": "Assets/UI/M_MyHammer_Silhouette.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Silhouette" },
    { "source": "Assets/UI/MI_MyHammer_Silhouette.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Silhouette" },
    { "source": "Assets/UI/M_MyHammer_Symbol.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Symbol" },
    { "source": "Assets/UI/MI_MyHammer_Symbol.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Symbol" }
  ]
}
```

The example omits sidecar entries for brevity. Add an `assets` entry for every cooked sidecar and every transitive custom dependency, using the same `packagePath` as its `.uasset`. ZIP `hammer.json`, the assets, and optional preview into a `.zip`, then rename it to `.rhammer`.

For the Azure Blockhead sample, after a successful cook, run `python build_rhammer.py <Project>/Saved/Sandboxes/Cooked-WindowsNoEditor/RMMAzureBlockhead/Content --output azure-blockhead-verified.rhammer` from `samples/azure-blockhead-source/UEProject/Scripts/`. The script requires `PCD3D_SM5` shader metadata containing compiled maps for both authored base Materials, refuses a Vulkan cook, scans only the sample's unique `/Game/RMM/Hammers/azure-blockhead-verified` folder, includes `.uasset` and any `.uexp`, `.ubulk`, or `.uptnl` sidecars, maps those files into the manifest, and stops if any of the four manifest references is absent from the cook.

## 5. Import and verify in RMM and Ragnarock

1. Import the `.rhammer` in RMM. Reject and fix any missing-file, invalid-class, duplicate-path, or invalid-reference error rather than omitting dependencies.
2. Enable the hammer and build the overlay PAK against the installed game build. RMM verifies the build hash so the merged DataTable matches the game version.
3. Deploy through RMM. The merged DataTable row sets `Title`, `Description`, `Icon`, clears `EntitlementId`, and points `Data` to the generated `DA_RMM_MY_HAMMER` asset. That generated asset points to `meshAssetPath`, `silhouetteAssetPath`, and `symbolAssetPath`.
4. Open the changing room. Confirm the custom icon appears in the list, the correct 3D model appears in the preview, and the symbol/silhouette visuals load.
5. Equip the hammer and start an offline song. Confirm the selected model appears in gameplay and registered hits play the normal `DrumHit` sound. For the combo check, play or automate a consecutive run with note timing accurate enough to avoid misses; the results Stats tab must report at least one combo. A few registered hits mixed with many misses are not evidence that the combo meter works. Combo generation comes from Ragnarock's common input/gameplay logic and is not a custom asset in this package.

## Current RMM support and known limits

RMM validates cooked Static Mesh, Texture2D, and Material Instance exports, copies the declared package files into its PAK, creates the hammer Data Asset, and merges the row into a matching `DT_Hammers`. At PAK build time it also checks each declared cooked package's `/Game/` imports against the enabled package collection and the installed game build. An unresolved project dependency stops the build with the missing package path. Source meshes and textures must be cooked before import. RMM does not invoke Unreal Editor or create cooked meshes/materials/textures.

Stock build evidence used for this guide: UE 4.27 metadata; `DT_Hammers` row fields `Title`, `Description`, `Icon`, `EntitlementId`, and `Data`; `DA_SummerHammer` fields `Mesh`, `Silhouette`, and `Symbol`; `Default__DA_Hammers_C.DrumHitSound` references the shared FMOD event `DrumHit`.

The Azure Blockhead source model and textures in this repository were imported and packaged for a runtime check. Its distinct icon, mesh preview, and gameplay hammer mesh load. A read-only UE4SS probe confirmed both gameplay `HammerMesh` components reference `/Game/RMM/Hammers/azure-blockhead-verified/Mesh/SM_AzureBlockhead`. Its surface still renders as a checker because the Linux cook produced no `PCD3D_SM5` shader map; Ragnarock logs that it falls back to the default material. A trial reassignment to stock game material instances after cooking did not resolve the checker rendering. Treat this sample as source art and a runtime-tested mesh, not as a finished working hammer package, until a Windows UE 4.27 cook targets `PCD3D_SM5` and the surface is rechecked in game. Stock material assets cannot be inspected from the encrypted game PAK in this environment.

## UE 4.27 references

- [FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
