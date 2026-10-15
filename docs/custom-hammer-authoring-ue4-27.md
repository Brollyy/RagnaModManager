# Custom hammer guide for Ragnarock and RMM

This guide takes you from an original model and image files to a hammer that RagnaModManager (RMM) can install. The [Azure Blockhead sample](https://github.com/Brollyy/RagnaModManager-HammerSamples/tree/master/azure-blockhead-source) includes source art, a UE 4.27 project, build scripts, and a finished package.

Follow the steps in order: prepare the art, import it into UE 4.27, cook it for Windows, package it as `.rhammer`, then install and check it in Ragnarock. RMM installs a finished package; it does not import FBX/OBJ files or cook UE assets.

## At a glance

1. Prepare the hammer model, textures, and icon.
2. Import the assets and assign materials in UE 4.27.
3. Cook the project for Windows.
4. Assemble the `.rhammer` package.
5. Import and deploy it with RMM.
6. Check the icon, preview, race model, and normal-hit behavior in game.

The [Azure Blockhead sample](https://github.com/Brollyy/RagnaModManager-HammerSamples/tree/master/azure-blockhead-source) provides source art and a UE project you can use to follow along.

## Before you start

You need:

- Unreal Engine **4.27** and a project for your hammer.
- A 3D editor that exports FBX or OBJ, and an image editor for textures and the list icon.
- A Windows UE cooker that can produce `WindowsNoEditor` assets and `PCD3D_SM5` shaders.
- RMM, an installed copy of Ragnarock, and a way to deploy its PAK overlay.

Ragnarock's PC build uses UE 4.27 and Direct3D shader format `PCD3D_SM5`. A Linux UE cooker may produce Vulkan shader maps instead, even if asked to cook for Windows. Those maps do not work in the game. On a Linux-only computer, run UE 4.27 in a Windows VM, a Windows cloud machine, or a Windows CI runner, then copy the `.rhammer` back to Linux. The Azure sample's [UE project README](https://github.com/Brollyy/RagnaModManager-HammerSamples/blob/master/azure-blockhead-source/UEProject/README.md) describes its Windows setup.

## Step 1 — Prepare the model and artwork

1. In Blender, create the head and handle as one mesh. Set the origin at the grip point, select the mesh, then use **Object → Apply → Rotation & Scale**. In Edit Mode, use **Mesh → Normals → Recalculate Outside**.
2. Unwrap the mesh: open **UV Editing**, select all faces in Edit Mode, press **U → Unwrap** (or use **Smart UV Project** for a blocky mesh), then use **UV → Pack Islands**. Keep islands inside the 0–1 square and avoid overlap unless the texture is intentionally shared. For an atlas, inset UVs from each painted region and leave a broad gutter around its edges so texture filtering and mipmaps cannot sample a neighboring color; don't stretch the entire atlas over every face.
3. In **Material Properties**, add three material slots in this order: `M_Character`, `M_JaugeManche`, `M_JaugeTete`. Select the faces for each region and click **Assign** for that slot. Keep the body under `M_Character`; assign only the blue handle bar to `M_JaugeManche` and the two-stage head bar to `M_JaugeTete`. The gauge names are case-sensitive and match Ragnarock's stock mesh slots.
4. Compare the hammer against the stock hammer in the UE Static Mesh preview. Verify that the grip point is where the hand should hold it and that the head is upright in the game's held orientation. Correct the source mesh if it appears sideways; don't rely on a changing-room camera angle to disguise a bad pivot or rotation.
5. Export the mesh as FBX or OBJ with UVs and material slots retained. Keep the editable model and texture sources alongside the UE project.
6. Create the color texture(s) and optional normal, roughness, metallic, or mask textures. Make a separate square PNG for the changing-room icon; preserve its alpha channel if you want the background to show through.

**Files to prepare:** `Source/SM_MyHammer.fbx` (or `.obj` plus `.mtl`), `Source/T_MyHammer_BaseColor.png`, `Source/T_MyHammer_Icon.png`, optional `Source/T_MyHammer_Normal.png` and other maps, plus the UE project. The in-game icon and the optional RMM library thumbnail are separate images.

## Step 2 — Import and set up assets in UE 4.27

1. Create a blank UE 4.27 project. Put the hammer's content in a unique folder, for example `Content/RMM/Hammers/<hammer-id>/`. Use a stable, unique ID for each hammer so its cooked package paths cannot collide with another mod.
2. Import the mesh as a **Static Mesh**. In its editor, check pivot, scale, held orientation, UVs, collision, and the three material slot names/order. Assign a material to each slot and save the mesh. Fix lost or renamed slots here before cooking.
3. Import the color textures as **Texture2D** assets. Keep sRGB enabled for painted base-color textures; disable it for linear masks and coordinate ramps used as data. For a grayscale gauge ramp, use **Grayscale** compression, disable mip generation, choose bilinear filtering, and clamp U/V addressing so the sampled 0–1 coordinate stays linear and cannot wrap. Create a fixed surface **Material** for `M_Character`, then create separate gauge materials for the two gauge slots using the combo recipe below.
4. Import the square icon PNG as a separate **Texture2D** asset and verify its transparency in the texture/material preview. This is the texture RMM will use for the in-game hammer-list icon.
5. Save the project and verify the mesh and materials in UE's preview. A material that looks right in the editor can still fail in Ragnarock if its Windows shader map is missing, so runtime validation is required.

**Combo material recipe:** Build the gauge bars as distinct visible geometry and assign it to the corresponding material slot. In each gauge material, add scalar parameters named exactly `handleGauge` and, for the head, `circularGauge`. Keep both parameters disconnected from the body material. Unwrap the bar so its `U` coordinate runs from 0 at empty to 1 at full, with a dark track visible underneath the fill.

In `M_JaugeManche`, compare `handleGauge` against `U` with an **If** node: set its `A` input to `handleGauge`, `B` to `U`, the `A > B` and `A == B` outputs to 1, and `A < B` to 0. Use the result as the alpha of a **Linear Interpolate** between the dark track color and blue. This makes the handle fill blue as `handleGauge` rises.

In `M_JaugeTete`, use `U = 0.5` as the stage boundary. For the first half, compare `handleGauge` with `2 × U`; for the second half, compare `circularGauge` with `2 × (U - 0.5)`. Select the first comparison when `U <= 0.5`, otherwise select the second. Select blue for the first half and gold for the second, then use the selected fill mask as the alpha of a **Linear Interpolate** from the dark track. This gives blue buildup to the first combo level and gold buildup toward the next. When the game drops combo and sends the gauge parameters back to zero, both bars show only the dark track. Put the result in Base Color (or follow the stock gauge material's lighting setup); do not make the whole hammer change color or glow.

The game drives these parameters on dynamic materials, so keep the exact, case-sensitive slot and parameter names. Check the geometry in the Static Mesh preview to ensure the gauge faces are visible in the held orientation. Then test both fill stages and the reset during a race; a declared parameter without a visible gauge surface is not enough.

**You should have:** a saved UE project whose preview shows the intended shape and orientation, whose three slots have assigned materials, and whose combo parameters drive visible surface changes.

### Optional selector art

Ragnarock's hammer data asset has `Silhouette` and `Symbol` properties in addition to `Mesh`. The inspected game build expects a base **Material** for `Silhouette` and a **Material Instance Constant** for `Symbol`. Their texture and parent-material dependencies must also be cooked and packaged if you choose to override them. RMM can leave these fields at the stock defaults; they are optional for a basic custom hammer. Custom marker materials have crashed the game in sample testing, so treat these overrides as experimental and validate them separately before distributing them. The Azure sample keeps the optional silhouette and symbol PNGs as source art only; its default UE project and `.rhammer` omit those assets and leave both manifest fields null.

The Azure sample includes `Scripts/create_hammer_materials.py` and `Scripts/reimport_azure_mesh.py` as examples of importing source images, creating UE materials and assigning the mesh's distinct color slots with UE's Python API. They are sample automation scripts, not prerequisites for other projects. Enable the **Python Editor Script Plugin** and **Editor Scripting Utilities** plugin, then run each through **File → Execute Python Script** or with the `-run=pythonscript -script="<path>"` commandlet form. Run material creation before mesh reimport. The sample scripts create their assets at the same package paths used by its manifest and archive builder; see its [UE project README](https://github.com/Brollyy/RagnaModManager-HammerSamples/blob/master/azure-blockhead-source/UEProject/README.md) for executable Windows commands.

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

An `.rhammer` is a ZIP archive with a `hammer.json` manifest, cooked UE assets, and the shared shader archive. RMM uses the manifest to create a Ragnarock hammer data asset and add a row to the game's hammer table. The archive must carry your custom Static Mesh, in-game icon Texture2D, every material and texture they reference, each cooked sidecar, and the Windows shader archive. Source FBX/OBJ/PNG files and the UE project are authoring inputs; they do not replace the cooked runtime assets.

Start from the example manifest in the [Azure sample repository](https://github.com/Brollyy/RagnaModManager-HammerSamples/blob/master/azure-blockhead-source/hammer.json.template). Give your hammer a unique, permanent `id`; keep it unchanged in future updates because the game saves the selected hammer by its row name. Set the mesh and icon paths to the cooked package paths in your project. Leave `silhouetteAssetPath` and `symbolAssetPath` null unless you have validated those custom materials in the game.

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

The example only shows `.uasset` files. Add one `assets` entry for **every** cooked file, including sidecars and every custom material or texture dependency. Each entry's `packagePath` is the UE object path without the file extension; all files for the same asset use the same `packagePath`. Include the project's `ShaderArchive-<Project>-PCD3D_SM5.ushaderbytecode` at the path named by `shaderArchive`. A practical completeness check is to enumerate all cooked `.uasset`, `.uexp`, `.ubulk`, and `.uptnl` files under your hammer content folder, then confirm every one appears in the manifest and ZIP. Do not package only the mesh: UE material references must resolve at runtime. `thumbnail` is an optional RMM library preview; it does not set the in-game icon.

To assemble the archive manually, create a folder with `hammer.json`, then copy in the cooked files under the matching `Assets/` paths, the shader archive under `Shaders/`, and the optional image under `Preview/`. Zip the **contents** of that folder and rename the `.zip` file to `.rhammer`.

For the Azure sample, its packaging script validates the cooked directory, required asset paths, and Windows shader metadata, then collects the cooked files and archive:

```bat
python build_rhammer.py C:\path\to\Saved\Cooked\WindowsNoEditor\RMMAzureBlockhead\Content --output ..\..\azure-blockhead.rhammer
```

Run it from `azure-blockhead-source/UEProject/Scripts/` in the sample repository. For a different hammer, adapt the script's content folder, required manifest paths, and output name, or assemble the ZIP using the manifest rules above.

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
3. Use a Standard chart with a steady sequence of notes. First test it with a stock hammer; then repeat the same chart and keyboard bindings with only the custom hammer enabled in RMM. Keep the game's normal keyboard input mode; do not switch to **One Key One Drum**. Before starting, verify that the highlighted song row, author, duration, and difficulty are the chart you intend to play. Confirm that accurate hits build the blue stage, further hits build the gold stage, and a deliberate miss clears both gauges. Take a fresh screenshot at each stage and check the results screen for hits and combo activity. A selected preview or visible race model alone does not prove the combo path works.

### Combo meter and charge

The game calculates combo and charge in its shared gameplay code, but each hammer supplies the material response that makes buildup visible. In the inspected build, `BP_RagnarockHand.UpdateHammerCombo` drives scalar parameters on dynamic material instances. The stock material assets expose this contract:

| Mesh material slot | Stock material | Combo scalar parameters |
| --- | --- | --- |
| `M_JaugeManche` | `M_HandleGauge` | `handleGauge` |
| `M_JaugeTete` | `M_HeadGauge` | `handleGauge`, `circularGauge` |

The default mesh also has its fixed body material. The extracted Summer Hammer DLC head-gauge instance contains both combo parameter names. Stock material assets expose other properties too, including gauge textures, colors, and glow controls; those tune each hammer's artwork and are not additional combo-driver names. Stock and DLC hammers can use hammer-specific gauge materials and textures, so inspect their material instances rather than copying another hammer's artwork blindly. The observed progression is blue up to the first combo level, then gold toward the next; a dropped combo clears the fill back to the dark track while leaving the body surface unchanged.

For a custom Static Mesh, keep those three slots in that order and use the exact, case-sensitive names. Put a `handleGauge` response only on the blue handle bar. On the head bar, use `handleGauge` for the blue first half and `circularGauge` for the gold second half. The Azure sample authors this response on dedicated bar surfaces while keeping its body material fixed; its live stage behavior has not yet been verified. Its simple ramp materials are an example implementation, not copies of the stock material graphs or their hammer-specific art. Verify both stages and the reset during a race. Merely declaring the parameters or connecting them to whole-body color/emission does not reproduce the stock gauge.

The names and slot order are based on the installed build's stock `BP_RagnarockHand`, `SM_Hammer_Default`, `MI_HandleGauge`, and `MI_HeadGauge` assets. Keep the standard `DA_Hammers_C` data-asset class; RMM creates that data asset and table row for each package. The hammer's DataTable row and icon do not control combo calculation or material response.

Verify the two parts separately: first run the RagnaLoader combo-fixture case (or the same Standard chart and input sequence) with a stock hammer and confirm the normal combo/charge behavior. Then enable only the custom hammer and repeat that exact case. Confirm that its handle/head material response changes as combo builds and resets when combo is lost. A custom mesh appearing in the changing room or hands proves only that its visual assets loaded. If the stock hammer does not respond under the same chart and inputs, resolve the gameplay/input issue before diagnosing the custom package.

## Sample status: Azure Blockhead

The public [RagnaModManager Hammer Samples repository](https://github.com/Brollyy/RagnaModManager-HammerSamples/tree/master/azure-blockhead-source) contains an earlier Azure source project and `.rhammer`; its default branch does not yet contain the refreshed package and bar-material recipe described here. The refreshed local sample's `generate_source.py` creates the low-poly mesh, padded base-color texture, icon, and gauge ramp. Its UE project imports those textures, creates the body and two gauge materials, assigns the three stock-compatible material slots, and cooks the assets for Windows `PCD3D_SM5`.

The refreshed local `.rhammer` was cooked for Windows `PCD3D_SM5`, includes the mesh, icon, body and gauge materials, their textures and sidecars, and the shader archive, and passes ZIP integrity and manifest-reference checks. It has been imported into RMM with Azure as the only enabled custom hammer and deployed as a PAK. Fresh captures showed Azure selected on the Changing Room pedestal and its model loaded in a race. The exact RMM combo fixture was visually confirmed in Race Info (`RMM COMBO METER FIXTURE`, `RMM Test`, `0:30`) and then started; RagnaLoader recorded a fresh `Boat.OnStartSong`. Captures during gameplay show the Azure model and blue/yellow effects around the drum area. A stock-hammer control with the same fixture and keyboard sequence showed the same effects, so they are game hit feedback rather than proof of Azure's combo material response. The Azure head bars remained too dark to confirm the requested blue-to-gold fill or its reset. The live custom stage behavior remains unverified and needs further work. Do not treat the material graph, a successful cook, a race start, or model visibility as proof of runtime combo behavior; complete the stock-versus-custom race check in Step 6 before describing the meter as working.

## References

- [UE 4.27 FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [UE 4.27 Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [UE 4.27 Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
- [UE 4.27 Scripting the Editor using Python](https://dev.epicgames.com/documentation/en-us/unreal-engine/scripting-the-editor-using-python?application_version=4.27)
- [Ragnarock FAQ](https://www.ragnarock-game.com/faq/)
