# Custom hammer guide for Ragnarock and RMM

This guide takes you from an original model and image files to a hammer that RagnaModManager (RMM) can install. The example project is [Azure Blockhead](../samples/azure-blockhead-source/README.md); its mesh, textures, icon, UE 4.27 project, and packaging script are included in this repository.

Follow the steps in order: prepare the art, import it into UE 4.27, cook it for Windows, package it as `.rhammer`, then install and check it in Ragnarock. RMM installs a finished package; it does not import FBX/OBJ files or cook UE assets.

## At a glance

1. Prepare the hammer model, textures, and icon.
2. Import the assets and assign materials in UE 4.27.
3. Cook the project for Windows.
4. Assemble the `.rhammer` package.
5. Import and deploy it with RMM.
6. Check the icon, preview, race model, and normal-hit behavior in game.

The [Azure Blockhead sample](../samples/azure-blockhead-source/README.md) provides source art and a UE project you can use to follow along.

## Before you start

You need:

- Unreal Engine **4.27** and a project for your hammer.
- A 3D editor that exports FBX or OBJ, and an image editor for textures and the list icon.
- A Windows UE cooker that can produce `WindowsNoEditor` assets and `PCD3D_SM5` shaders.
- RMM, an installed copy of Ragnarock, and a way to deploy its PAK overlay.

Ragnarock's PC build uses UE 4.27 and Direct3D shader format `PCD3D_SM5`. A Linux UE cooker may produce Vulkan shader maps instead, even if asked to cook for Windows. Those maps do not work in the game. On a Linux-only computer, run UE 4.27 in a Windows VM, a Windows cloud machine, or a Windows CI runner, then copy the `.rhammer` back to Linux. The Azure sample's [UE project README](../samples/azure-blockhead-source/UEProject/README.md) describes its Windows setup. If building UE 4.27 from source, use Visual Studio 2019 and build both `UE4Editor` and `ShaderCompileWorker` before cooking.

## Step 1 — Prepare the model and artwork

1. Model the hammer in Blender or another 3D editor. Put the pivot at the grip point, apply transforms, check face normals, and make UVs for every textured material.
2. Compare the model's size, grip, and orientation with a Ragnarock hammer in UE. A mesh that looks upright in Blender can appear sideways in the game; correct the source mesh before cooking.
3. Keep material slots intentional. Give differently colored parts their own material slots, or make sure each part's UVs sample the intended region of the same texture.
4. Export the mesh as FBX or OBJ. Export only the mesh and material slots you intend to use.
5. Make a square changing-room icon as a PNG. Use a transparent background if you want the game backdrop to show around the hammer. The icon is separate from the 3D mesh and from RMM's optional desktop thumbnail.
6. Make the color and any optional normal, roughness, metallic, or mask textures. Keep the editable source files together with the project.

**You should have:** an exported mesh, its textures, and a separate square icon.

## Step 2 — Import and set up assets in UE 4.27

1. Create a blank UE 4.27 project. Put the hammer's content in a unique folder, for example `Content/RMM/Hammers/<hammer-id>/`. Use a stable, unique ID for each hammer so its cooked package paths cannot collide with another mod.
2. Import the mesh as a **Static Mesh**. In its editor, check pivot, scale, orientation, UVs, material slots, and collision. Fix these here before moving on.
3. Import the color textures as **Texture2D** assets. Create a surface **Material** (and any **Material Instance** assets you use), connect the texture parameters, and assign the right material to each mesh slot.
4. Import the square icon as a **Texture2D** asset. This is the texture RMM will use for the in-game hammer-list icon.
5. Save the project and verify that the mesh and materials look correct in UE's preview. A material that looks right in the editor can still fail in Ragnarock if its Windows shader map is missing, so runtime validation is required.

**You should have:** a saved UE project whose mesh preview shows the intended shape, orientation, and materials.

### Optional selector art

Ragnarock's hammer data asset has `Silhouette` and `Symbol` properties in addition to `Mesh`. The inspected game build expects a base **Material** for `Silhouette` and a **Material Instance Constant** for `Symbol`. Their texture and parent-material dependencies must also be cooked and packaged. RMM can leave these fields at the stock defaults; they are optional for a basic custom hammer. Custom marker materials have crashed the game in sample testing, so treat these overrides as experimental and validate them separately before distributing them.

The Azure project includes `Scripts/create_hammer_materials.py` and `Scripts/reimport_azure_mesh.py` as examples of creating UE materials and assigning the mesh's distinct color slots with UE's Python API. They are sample automation scripts, not prerequisites for other projects.

## Step 3 — Configure and cook for Windows

In `Config/DefaultEngine.ini`, set the Windows target to the game's shader format:

```ini
[/Script/WindowsTargetPlatform.WindowsTargetSettings]
!TargetedRHIs=ClearArray
+TargetedRHIs=PCD3D_SM5
```

In `Config/DefaultGame.ini`, tell UE to write a shared material shader archive:

```ini
[/Script/UnrealEd.ProjectPackagingSettings]
bShareMaterialShaderCode=True
```

On Windows, run the UE 4.27 command-line cooker (or use **File → Package Project → Windows (64-bit)**):

```bat
UE4Editor-Cmd.exe C:\path\to\Project.uproject -run=cook -targetplatform=WindowsNoEditor -cookall
```

Wait for the cook to finish. Inspect its log for errors and successful `PCD3D_SM5` material shader compilation. The output is under the project's cooked `WindowsNoEditor` content directory. Keep every file for an asset together: in addition to `.uasset`, UE may produce `.uexp`, `.ubulk`, or `.uptnl` sidecars. Do not rename cooked assets.

**You should have:** a `WindowsNoEditor` cooked content folder and the project's `PCD3D_SM5` shader archive.

## Step 4 — Assemble the `.rhammer` archive

An `.rhammer` is a ZIP archive with a `hammer.json` manifest, cooked UE assets, and the shared shader archive. RMM uses the manifest to create a Ragnarock hammer data asset and add a row to the game's hammer table.

Start from the example manifest at `samples/azure-blockhead-source/hammer.json.template`. Give your hammer a unique, permanent `id`; keep it unchanged in future updates because the game saves the selected hammer by its row name. Set the mesh and icon paths to the cooked package paths in your project. Leave `silhouetteAssetPath` and `symbolAssetPath` null unless you have validated those custom materials in the game.

Minimal manifest shape:

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
  "silhouetteAssetPath": null,
  "symbolAssetPath": null,
  "shaderArchive": "Shaders/ShaderArchive-MyProject-PCD3D_SM5.ushaderbytecode",
  "dataAssetPath": "/Game/Data/Hammers/DA_RMM_MY_HAMMER",
  "thumbnail": "Preview/my-hammer.png",
  "assets": [
    {
      "source": "Assets/RMM/Hammers/my-hammer/Mesh/SM_MyHammer.uasset",
      "packagePath": "/Game/RMM/Hammers/my-hammer/Mesh/SM_MyHammer"
    },
    {
      "source": "Assets/RMM/Hammers/my-hammer/UI/T_MyHammer_Icon.uasset",
      "packagePath": "/Game/RMM/Hammers/my-hammer/UI/T_MyHammer_Icon"
    }
  ]
}
```

The example only shows `.uasset` files. Add one `assets` entry for **every** cooked file, including sidecars and every custom material or texture dependency. Each entry's `packagePath` is the UE object path without the file extension; all files for the same asset use the same `packagePath`. Include the project's `ShaderArchive-<Project>-PCD3D_SM5.ushaderbytecode` at the path named by `shaderArchive`. `thumbnail` is an optional RMM library preview; it does not set the in-game icon.

To assemble the archive manually, create a folder with `hammer.json`, then copy in the cooked files under the matching `Assets/` paths, the shader archive under `Shaders/`, and the optional image under `Preview/`. Zip the **contents** of that folder and rename the `.zip` file to `.rhammer`.

For the Azure sample, the packaging script validates the cooked directory, required asset paths, and Windows shader metadata, then collects the cooked files and archive:

```bat
python build_rhammer.py C:\path\to\Saved\Cooked\WindowsNoEditor\RMMAzureBlockhead\Content --output ..\..\azure-blockhead.rhammer
```

Run it from `samples/azure-blockhead-source/UEProject/Scripts/`. For a different hammer, adapt the script's content folder, required manifest paths, and output name, or assemble the ZIP using the manifest rules above.

Before importing, open the `.rhammer` as a ZIP and confirm it contains `hammer.json`, every referenced cooked asset and sidecar, and the shader archive. The optional `Preview/` image is for RMM's library only; `iconAssetPath` is the in-game icon.

**You should have:** one `.rhammer` file with no missing manifest references.

## Step 5 — Import and deploy with RMM

1. In RMM, import the `.rhammer` into the hammer library. Fix any missing-file, invalid-class, duplicate-path, or invalid-reference errors; do not work around them by removing dependencies.
2. Enable the hammer and build the custom-hammer PAK for the installed Ragnarock version. RMM validates the game build and checks that the package's referenced UE assets are present.
3. Deploy the PAK through RMM. If you are diagnosing a problem, leave only the hammer under test enabled so the selected row and visible model are unambiguous.

**You should have:** the hammer enabled in RMM and deployed to the game.

## Step 6 — Check it in Ragnarock

Check each output separately:

1. Open the changing room. Confirm the list entry has the custom icon, then select it and confirm the 3D preview is the custom mesh with the intended orientation and materials.
2. Equip it and start an offline song. Confirm the custom mesh appears in the player's hands during the race and that normal hits play the game's shared `DrumHit` sound.
3. Test combo and charge using the normal drum bindings displayed in the game's Input settings. On the tested setup these are `Q`, `W`, `E`, `I`, `O`, and `P`; **One Key One Drum is not required**. Use a chart and timing that produce consecutive accurate hits. Confirm perfect-hit/combo progression in the Results Stats and observe the in-race charge feedback. A few hits, key presses, or a visible hammer alone do not prove the combo path works.

### What makes combo work

In the inspected Ragnarock build, combo and charge are driven by the game's shared gameplay/input logic. The `DT_Hammers` row and hammer data asset do not contain a per-hammer combo meter or combo counter. RMM creates the hammer asset from the game's existing `DA_Hammers_C` class, which supplies the shared `DrumHitSound`; a custom subclass, combo texture, or meter asset is not part of the RMM package format. Therefore, keep the standard data class, provide a valid mesh and selectable hammer row, and test with ordinary accurate input. If hits register but perfect-hit/combo events do not, first validate timing and the game's common hit path with a stock hammer; do not infer success from the custom mesh loading.

## Sample status: Azure Blockhead

The source package is [here](../samples/azure-blockhead-source/). Its `generate_source.py` creates original low-poly mesh and PNG art; the UE project imports a separate cyan texture material and gold/navy slot materials. The checked-in `.rhammer` includes those cooked assets and the Windows `PCD3D_SM5` shader archive.

The checked-in sample package loads in the changing room with its icon and cyan/gold/navy 3D preview. The corrected material build has not yet been independently verified during a race. Combo progression and in-race charge feedback also remain unverified, so the sample is not yet fully runtime-validated. Use Step 6 to check these behaviors before distributing a custom hammer.

## References

- [UE 4.27 FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [UE 4.27 Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [UE 4.27 Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
- [Ragnarock FAQ](https://www.ragnarock-game.com/faq/)
