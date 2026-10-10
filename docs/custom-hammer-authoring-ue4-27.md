# Create a Ragnarock hammer package with UE 4.27

This guide covers a self-contained custom hammer package for RagnaModManager (RMM). It starts with new source files, imports and cooks them in Unreal Engine 4.27, and packages the cooked assets as an `.rhammer` library archive. RMM then creates the game-specific data asset and merged PAK for the installed Ragnarock build.

The repository includes original source geometry, material art, and an icon for the [Azure Blockhead sample](../samples/azure-blockhead-source/README.md). Those source files are ready for the UE import/cook steps below; they are not mislabeled as cooked game assets.

Ragnarock's inspected build uses UE 4.27 and a V11 PAK. Use the same engine version and cook for `WindowsNoEditor`, even when playing through Proton. The public RMM app does not import FBX/OBJ or cook Unreal assets; the creator must provide cooked UE assets. For materials, use a Windows UE 4.27 cooker: Linux UE builds cook Vulkan shader maps, while this game requests `PCD3D_SM5` shader maps at runtime. Set `bShareMaterialShaderCode=True` to create a shared shader archive. RMM places that archive in a content plugin and marks its descriptor `EnabledByDefault=true`. A live game log confirms that Ragnarock mounted the plugin, opened its `PCD3D_SM5` library, and set the Azure row as the selected changing-room avatar. A separate `Global_SC` experiment crashed during runtime and is not part of this route.

### Linux-to-Windows cooking

UE 4.27 does not provide a supported Linux-hosted cross-compile workflow for Windows. Epic's documented cross-compilation support is Windows-hosted development targeting Linux; the reverse direction is not the supported toolchain. Also, a WindowsNoEditor cook is not sufficient by itself: the cook must compile the project's material shaders for `PCD3D_SM5`. This sample's Linux cook fell back to Ragnarock's checker material because it had no usable Direct3D shader map.

Without a local Windows PC, use a Windows guest VM on a Linux host, a temporary Windows cloud VM, or a Windows self-hosted CI runner to run UE 4.27 natively. A QEMU/KVM guest is usually the most direct local option when the Linux host exposes hardware virtualization. The VM needs the UE 4.27 editor/cooker, Visual Studio 2019 and a Windows SDK if building UE from source, and enough disk space for the engine and project. Epic's UE 4.27 setup lists VS 2019 and Windows SDK 10.0.18362 or newer. A UE 4.27 binary installation can avoid compiling the whole engine from source if it is available to the VM. Open this sample project, confirm `PCD3D_SM5` in `Config/DefaultEngine.ini`, cook `WindowsNoEditor`, and inspect the cooker log for successful SM5 material compilation. Run `build_rhammer.py` on the resulting cooked Content directory, then copy the `.rhammer` back to the Linux machine for RMM import, PAK deployment, and Proton runtime checks.

### Build the UE 4.27 editor from source on Windows

When using the UE 4.27 source tree instead of an installed editor, compile it natively in Windows. The Epic GitHub source checkout or a transferred source snapshot is sufficient; Unreal Editor and the command-line cooker do not require Epic Games Launcher sign-in. Install Visual Studio 2019 with the **Desktop development with C++** workload, its v142 x86/x64 toolset, and a Windows 10 SDK (10.0.18362 or newer). UE4's project-file generator also checks for the .NET Framework SDK: install the **.NET Framework 4.6.2 Developer Pack**, which includes the SDK and targeting pack, if UnrealBuildTool reports that `NetFxSDK` cannot be found. A targeting pack by itself may not register the SDK directory UnrealBuildTool expects.

To run the sample's material-creation script, the source-built editor must include the working Python Script Plugin. UE 4.27 bundles Python 3.7; install a compatible **64-bit Python 3.7 development installation** with its headers and import libraries, then set `UE_PYTHON_DIR` to that installation before building the editor. For example, in the same command prompt use `set UE_PYTHON_DIR=C:\Python37`. Without the development SDK, the plugin can build as a stub and `-run=pythonscript` fails with “Python script cannot run as the plugin was built as a stub.” Epic documents this source-build configuration in [Scripting the Editor using Python](https://dev.epicgames.com/documentation/unreal-engine/scripting-the-editor-using-python?application_version=4.27&lang=en-US). If using an editor where the script plugin is already enabled and functional, this source-build setup is unnecessary.

Then open a Developer Command Prompt and run these commands from the engine source root. Epic's [UE 4.27 Project Files for IDEs](https://dev.epicgames.com/documentation/unreal-engine/project-files-for-ides?application_version=4.27) describes project-file generation, and its [UE 4.27 Unreal Build System guide](https://dev.epicgames.com/documentation/unreal-engine/unreal-build-system?application_version=4.27) describes compiling UE4 targets. Microsoft's [.NET Framework 4.6.2 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net462) page links the offline installer:

```bat
Setup.bat
GenerateProjectFiles.bat -2019
Engine\Build\BatchFiles\Build.bat UE4Editor Win64 Development -WaitMutex
Engine\Build\BatchFiles\Build.bat ShaderCompileWorker Win64 Development -WaitMutex
```

`Setup.bat` obtains the engine's pinned binary dependencies; it may ask Windows to approve the signed UE prerequisite installer. On a clean Windows image, the prerequisite setup can also enable .NET Framework 3.5 through Windows Features and request a restart. Let Windows finish that update, sign back in, and rerun `Setup.bat` if the original command did not resume. When the console reaches `Installing prerequisites...`, check for the UE4 Prerequisites consent prompt and approve it; the command waits while that prompt is unanswered. The cook also needs `Engine\Binaries\Win64\ShaderCompileWorker.exe`. The editor target does not always build this separate program, so build the `ShaderCompileWorker` target once if the file is missing; otherwise the cook exits when it tries to compile shader maps. After both targets build, use `Engine\Binaries\Win64\UE4Editor-Cmd.exe` for the sample project's cook command below. These targets build the editor/cooker and its shader compiler; they do not build Ragnarock or its game binaries. If using an already built UE 4.27 editor, skip the engine build and run its `UE4Editor-Cmd.exe` directly, but confirm its `ShaderCompileWorker.exe` exists before cooking.

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

The game hammer data asset is an instance of the shared `DA_Hammers_C` class. Its inspected fields are `Mesh`, `Silhouette`, and `Symbol`. In the stock `DA_DrumWarriorHammer`, `Silhouette` references a base `Material`, while `Symbol` references a `MaterialInstanceConstant`. The common class default object also supplies `DrumHitSound`, an FMOD `DrumHit` event. RMM preserves this class and its default when it generates each hammer data asset, so creators do not need to add a combo counter or a new sound class.

There is no combo-meter field or combo asset in `DT_Hammers` or the custom hammer data asset schema inspected for this game build. The extracted `BP_HammerSelector` contains the `Silhouette` and `Symbol` references, while the shared hammer class default supplies `DrumHitSound`; none of these is a custom combo-counter asset. RMM creates each hammer data asset using the game's existing `DA_Hammers_C` class. Keep that parent class, provide a valid mesh, and leave `EntitlementId` empty; no extra combo asset or subclass is needed. The game's normal input and hit logic drives combo progression for custom rows just as it does for stock rows. `DrumHitSound` is the shared hit sound, not the combo counter. A custom hit sound is outside the current RMM package format. To verify progression during a race, RagnaLoader can trace `/Script/Ragnarock.Boat:OnComboTriggered` and record its combo value and bounds. That checks the game-level combo event; separately inspect the in-race hammer to confirm its mesh and any visual feedback. This sample's fresh in-race mesh, charge feedback, and combo behavior have not yet been verified.

For complete custom presentation, include all four kinds of assets:

1. A cooked `StaticMesh` and its custom material/texture dependency graph.
2. A cooked `Texture2D` for the changing-room list icon.
3. A cooked base `Material` for the `Silhouette` data asset property, plus its texture dependencies.
4. A cooked `MaterialInstanceConstant` for the `Symbol` data asset property, plus its material and texture dependencies.

Although both properties use Unreal material references, their cooked target classes differ: `Silhouette` must reference a base `Material`, while `Symbol` must reference a `MaterialInstanceConstant`. RMM preserves those classes from the stock data asset template and rejects a package asset whose class does not match. Include every custom material and texture dependency in the archive. A library `thumbnail` is only a desktop preview and does not become the in-game `Icon`. In the current Azure sample, both fields are intentionally left at Ragnarock’s stock defaults. Runtime tests with the sample’s custom marker materials crashed on the render thread even after correcting the `Silhouette` class. Keep these overrides unset unless their specific cooked material graphs have been validated in-game.

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

The Azure Blockhead UE project includes `Scripts/create_hammer_materials.py`, which imports its authored base-color, silhouette, and symbol PNGs as `Texture2D` assets. It creates an opaque lit surface Material with the base-color texture connected to `Base Color`, a translucent unlit base Material for `Silhouette`, and a translucent unlit parent Material with a `MaterialInstanceConstant` for `Symbol`. Run it in UE 4.27 with the Python Script Plugin enabled:

```text
UE4Editor-Cmd.exe <ProjectPath>/RMMAzureBlockhead.uproject -run=pythonscript -script=<ProjectPath>/Scripts/create_hammer_materials.py -unattended -nop4 -nullrhi
```

The resulting paths are `Materials/M_AzureBlockhead`, `UI/M_AzureBlockhead_Silhouette`, `UI/M_AzureBlockhead_Mark`, and `UI/MI_AzureBlockhead_Symbol`. The silhouette texture is the base Material's default texture; the symbol texture is set on its Material Instance. For a different hammer, adapt the texture names and asset names in that script. Preview both outputs in the game context and adjust them if Ragnarock's selector expects a different visual treatment.

For the Azure sample, `generate_source.py` rotates the head assembly a quarter turn around the handle axis while leaving the handle geometry fixed. After regenerating the OBJ and creating the material assets, run `Scripts/reimport_azure_mesh.py` in the UE project before cooking. It imports `Source/SM_RMM_AzureBlockhead.obj` as `SM_AzureBlockhead_Rotated` and writes the authored textured `M_AzureBlockhead` into every serialized Static Mesh material slot. This avoids the OBJ importer's unresolved `HammerCyan`, `HammerGold`, and `HammerNavy` material references. The script saves, reloads, and verifies the slot assignments and stops if any slot is empty or points to another material.

## 2. Import and author assets in UE 4.27

Create a blank UE 4.27 project and keep the project content under unique folders that will not collide with Ragnarock's `/Game/` assets. For example:

```text
Content/RMM/Hammers/<hammer-id>/Mesh/SM_<Hammer>
Content/RMM/Hammers/<hammer-id>/Materials/M_<Hammer>
Content/RMM/Hammers/<hammer-id>/Materials/MI_<Hammer>
Content/RMM/Hammers/<hammer-id>/Textures/T_<Hammer>_BaseColor
Content/RMM/Hammers/<hammer-id>/UI/T_<Hammer>_Icon
Content/RMM/Hammers/<hammer-id>/UI/M_<Hammer>_Silhouette
Content/RMM/Hammers/<hammer-id>/UI/M_<Hammer>_Symbol
Content/RMM/Hammers/<hammer-id>/UI/MI_<Hammer>_Symbol
```

Import the FBX as a Static Mesh. Inspect scale, orientation, pivot, normals, material slots, UVs, and collision in the Static Mesh Editor. Set up the surface Material and Material Instances and assign them to the mesh sections. Import the icon and any silhouette/symbol textures as Texture assets. Create a base `Material` for `Silhouette`, and create a separate base Material plus a `MaterialInstanceConstant` for `Symbol`.

Keep all custom dependencies in the project. Do not use a Material Instance whose parent, texture, or other referenced package is in the project unless that package is included in the hammer archive too. References to Ragnarock's stock content may resolve from the installed game, but a package intended to work independently should own its mesh and visual dependencies.

Use the editor to preview the mesh and UI materials and fix broken references before cooking. The changing-room 3D preview, list icon, silhouette material, and symbol material are separate outputs; verify each one separately.

## 3. Cook the assets for the game

Unreal stores editor assets in source formats and converts them to platform-specific formats during cooking. Cook for `WindowsNoEditor`, which is the platform used by the installed PC game:

Make sure the project targets the game's shader format. A project-level Windows target override can replace the engine default. In `Config/DefaultEngine.ini`, use:

```ini
[/Script/WindowsTargetPlatform.WindowsTargetSettings]
!TargetedRHIs=ClearArray
+TargetedRHIs=PCD3D_SM5

[/Script/UnrealEd.ProjectPackagingSettings]
bShareMaterialShaderCode=True
```

Do not set the Windows target to `SF_VULKAN_SM5`; that is the Linux shader format. A WindowsNoEditor cook made by the Linux editor can still contain Vulkan shader maps if the project overrides `TargetedRHIs`, and Ragnarock will fall back to its default checker material. The project must explicitly set `bShareMaterialShaderCode=True` before the cook so the archive contains the custom material shader maps.

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
  "silhouetteAssetPath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Silhouette",
  "symbolAssetPath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Symbol",
  "shaderArchive": "Shaders/ShaderArchive-MyProject-PCD3D_SM5.ushaderbytecode",
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
    { "source": "Assets/UI/M_MyHammer_Silhouette.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Silhouette" },
    { "source": "Assets/UI/M_MyHammer_Symbol.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/M_MyHammer_Symbol" },
    { "source": "Assets/UI/MI_MyHammer_Symbol.uasset", "packagePath": "/Game/RMM/Hammers/my-hammer/UI/MI_MyHammer_Symbol" }
  ]
}
```

The example omits sidecar entries for brevity. Add an `assets` entry for every cooked sidecar and every transitive custom dependency, using the same `packagePath` as its `.uasset`. Include the project `ShaderArchive-<Project>-PCD3D_SM5.ushaderbytecode` as `shaderArchive`. RMM places it in a unique content plugin inside the main overlay PAK and generates a plugin descriptor with `EnabledByDefault=true`; it also writes the plugin manifest UE4.27 uses to discover the descriptor. The live test below confirms this route opens the archive in Ragnarock. ZIP `hammer.json`, all cooked assets, the shader archive, and the optional preview into a `.zip`, then rename it to `.rhammer`.

For the Azure Blockhead sample, after a successful cook, run `python build_rhammer.py <Project>/Saved/Cooked/WindowsNoEditor/RMMAzureBlockhead/Content --output azure-blockhead.rhammer` from `samples/azure-blockhead-source/UEProject/Scripts/`. The script requires `PCD3D_SM5` shader metadata containing compiled maps for the authored surface and marker materials, refuses a Vulkan cook, scans only the sample's unique `/Game/RMM/Hammers/azure-blockhead-runtime-verified` folder, includes `.uasset` and any `.uexp`, `.ubulk`, or `.uptnl` sidecars, maps those files into the manifest, and stops if any manifest reference is absent from the cook. It also includes the project's `ShaderArchive-<Project>-PCD3D_SM5.ushaderbytecode` in the `.rhammer`; cooked material shader-map IDs need this archive at runtime. Keep this content folder unique so its assets cannot replace or shadow assets supplied by another hammer package.

## 5. Import and verify in RMM and Ragnarock

1. Import the `.rhammer` in RMM. Reject and fix any missing-file, invalid-class, duplicate-path, or invalid-reference error rather than omitting dependencies.
2. Enable the hammer and build the overlay PAK against the installed game build. RMM verifies the build hash so the merged DataTable matches the game version.
3. Deploy through RMM. The merged DataTable row sets `Title`, `Description`, `Icon`, clears `EntitlementId`, and points `Data` to the generated `DA_RMM_MY_HAMMER` asset. That generated asset points to `meshAssetPath`, `silhouetteAssetPath`, and `symbolAssetPath`.
4. Open the changing room. Confirm the custom icon appears in the list, the correct 3D model appears in the preview, and the symbol/silhouette visuals load.
5. Equip the hammer and start an offline song. Confirm the selected model appears in gameplay and registered hits play the normal `DrumHit` sound. For the combo check, play or automate a consecutive run with note timing accurate enough to avoid misses; the results Stats tab must report at least one combo. A few registered hits mixed with many misses are not evidence that the combo meter works. Combo generation comes from Ragnarock's common input/gameplay logic and is not a custom asset in this package.

## Current RMM support and known limits

RMM validates cooked Static Mesh, Texture2D, base Material, and Material Instance exports against the stock property classes, copies the declared package files into its PAK, creates the hammer Data Asset, and merges the row into a matching `DT_Hammers`. At PAK build time it also checks each declared cooked package's `/Game/` imports against the enabled package collection and the installed game build. An unresolved project dependency stops the build with the missing package path. Source meshes and textures must be cooked before import. RMM does not invoke Unreal Editor or create cooked meshes/materials/textures.

Stock build evidence used for this guide: UE 4.27 metadata; `DT_Hammers` row fields `Title`, `Description`, `Icon`, `EntitlementId`, and `Data`; `DA_SummerHammer` fields `Mesh`, `Silhouette`, and `Symbol`; `Default__DA_Hammers_C.DrumHitSound` references the shared FMOD event `DrumHit`.

The Azure Blockhead sample was imported and cooked with the Windows source-built UE 4.27 editor. Its shared `PCD3D_SM5` archive contains 1,453 shaders. The RMM-generated plugin descriptor must set `EnabledByDefault=true`; otherwise Ragnarock does not mount the plugin or open the archive. `build_rhammer.py` packages the rotated mesh and its dependencies and omits the obsolete pre-rotation mesh. The project content folder is `/Game/RMM/Hammers/azure-blockhead-runtime-verified`; the package's permanent identity is `azure-blockhead-verified` with row `RMM_AZURE_BLOCKHEAD_VERIFIED`. Keep this ID and row name for updates because Ragnarock saves the selected row name in player data.

**Runtime status (2026-10-10):** The earlier PAK (SHA-256 `726f8ef812404bd155d140167aa4b52ae8d2a400a44ce679d37060089d8eb366`) crashed 23 seconds after launch with `EXCEPTION_ACCESS_VIOLATION reading 0x18` on `RenderThread 2`; its UI case had reported `passed` before the crash. Its generated data asset assigned a `MaterialInstanceConstant` to `Silhouette`, while stock `DA_DrumWarriorHammer.Silhouette` uses a base `Material`. Correcting that class alone did not stop the crash. A separate synthetic-VR run of the stable package ended with `GameThread timed out waiting for RenderThread after 120.00 secs`; its screenshot was taken before the timeout and is not a successful-run artifact. That result does not by itself establish that the stable package caused the renderer stall.

The corrected stable PAK SHA-256 is `EAFB6F00608A2100093092DA40FCF71E9A6DB26B182DEBB1C3C7F5D01EB978AA` (27 stock rows plus the Azure row). It is deployed both to RMM's active package source and the game's `~mods` directory, and RMM's active deployment record contains the same checksum. This build restores the `RMM_AZURE_BLOCKHEAD_VERIFIED` row referenced by the current player save; the prior `E455...` PAK used `RMM_AZURE_BLOCKHEAD` and caused the race hand to fall back when the saved row was missing. A fresh Flat-mode run using RagnaLoader's premade live hammer-card scan had previously inspected 28 `FlatOneHammerSetting_C` widgets and showed the Azure icon and 3D changing-room model. That earlier screenshot predates the restored row identity and does not prove the corrected build's in-race model. The model has visible texture artifacts. A post-deployment race check, charge feedback, and combo behavior remain unverified.

Several older RagnaLoader cases returned `passed` without proving their hypothesis: some screenshots were captured on the initial card list, the custom-play case targeted stale transient widget IDs, and an earlier hammer-card scan inspected zero widgets while the game was still on a splash screen. Those screenshots are invalid evidence. The latest valid Flat hammer-card case logged `hammer card state scan inspected=28` and its new screenshot shows the Azure card and preview. A separate local-fixture case reached `race.started` but did not establish which hammer was equipped; its stats showed placeholder hit/miss values (`999`) and `combo=0`, so this is not evidence of Azure's gameplay model or combo behavior.

No Azure combo-display proof has been captured. Prior synthetic-chart runs produced zero combo, and the gameplay screenshot did not identify Azure as equipped. The user also reported no charging indication during play. The shared `DA_Hammers_C` class has no per-hammer combo asset or field; combo and charge feedback should come from the game's common gameplay path. Verification still requires selecting this specific row, confirming its model is loaded in-song, hitting a consecutive streak, observing charge feedback, and confirming a nonzero combo in Results Stats.

## UE 4.27 references

- [FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
