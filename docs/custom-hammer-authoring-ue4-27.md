# Custom hammer guide for Ragnarock and RMM

This guide takes you from an original model and image files to a hammer that RagnaModManager (RMM) can install. The in-repository [Azure Blockhead sample](../samples/azure-blockhead/README.md) includes source art, a UE 4.27 project, build scripts, and a cooked package.

Follow the steps in order: prepare the art, import it into UE 4.27, cook it for Windows, package it as `.rhammer`, then install and check it in Ragnarock. RMM installs a finished package; it does not import FBX/OBJ files or cook UE assets.

## At a glance

1. Prepare the hammer model, textures, and icon.
2. Import the assets and assign materials in UE 4.27.
3. Cook the project for Windows.
4. Assemble the `.rhammer` package.
5. Import and deploy it with RMM.
6. Check the icon, preview, race model, and normal-hit behavior in game.

The [Azure Blockhead sample](../samples/azure-blockhead/README.md) provides source art and a UE project you can use to follow along.

## Before you start

You need:

- Unreal Engine **4.27** and a project for your hammer.
- A 3D editor that exports FBX or OBJ, and an image editor for textures and the list icon.
- A Windows UE cooker that can produce `WindowsNoEditor` assets and `PCD3D_SM5` shaders.
- RMM, an installed copy of Ragnarock, and a way to deploy its PAK overlay.

Ragnarock's PC build uses UE 4.27 and Direct3D shader format `PCD3D_SM5`. A Linux UE cooker may produce Vulkan shader maps instead, even if asked to cook for Windows. Those maps do not work in the game. On a Linux-only computer, run UE 4.27 in a Windows VM, a Windows cloud machine, or a Windows CI runner, then copy the `.rhammer` back to Linux. The Azure sample's [UE project README](../samples/azure-blockhead/UEProject/README.md) describes its Windows setup.

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

**Combo material recipe:** Build the gauge bars as distinct visible geometry and assign it to the corresponding material slot. In each gauge material, add scalar parameters named exactly `handleGauge` and, for the head, `circularGauge`. Keep both parameters disconnected from the body material. Unwrap the bar so its `U` coordinate runs from 0 at empty to 1 at full, with a dark track visible underneath the fill. A separate `glow` scalar may control the active fill's emissive strength; it improves visibility but is not a combo value driven by the game. The Azure sample sets it to `34.242756` and uses the stock default gauge colors: blue `centerColor=(0.030109, 0.079745, 0.320833)` and orange-gold `mainColor=(1.0, 0.400337, 0.0)`. Other stock and DLC hammers can use different colors and glow values, so inspect the target material instance when matching a particular design.

In `M_JaugeManche`, compare `handleGauge` against `U` with an **If** node: set its `A` input to `handleGauge`, `B` to `U`, the `A > B` and `A == B` outputs to 1, and `A < B` to 0. Use the result as the alpha of a **Linear Interpolate** between the dark track color and blue. This makes the handle fill blue as `handleGauge` rises.

In `M_JaugeTete`, use `U = 0.5` as the stage boundary. For the first half, compare `handleGauge` with `2 × U`; for the second half, compare `circularGauge` with `2 × (U - 0.5)`. Select the first comparison when `U <= 0.5`, otherwise select the second. Select blue for the first half and gold for the second, then use the selected fill mask as the alpha of a **Linear Interpolate** from the dark track. This gives blue buildup to the first combo level and gold buildup toward the next. When the game drops combo and sends the gauge parameters back to zero, both bars show only the dark track. Put the result in Base Color (or follow the stock gauge material's lighting setup). If using emissive output for readability, limit it to the active gauge fill; do not make the body color or emission change with combo.

The game drives these parameters on dynamic materials, so keep the exact, case-sensitive slot and parameter names. Check the geometry in the Static Mesh preview to ensure the gauge faces are visible in the held orientation. Then test both fill stages and the reset during a race; a declared parameter without a visible gauge surface is not enough.

**You should have:** a saved UE project whose preview shows the intended shape and orientation, whose three slots have assigned materials, and whose combo parameters drive visible surface changes.

### Optional selector art

Ragnarock's hammer data asset has `Silhouette` and `Symbol` properties in addition to `Mesh`. The inspected game build expects a base **Material** for `Silhouette` and a **Material Instance Constant** for `Symbol`. Their texture and parent-material dependencies must also be cooked and packaged if you choose to override them. RMM can leave these fields at the stock defaults; they are optional for a basic custom hammer. Custom marker materials have crashed the game in sample testing, so treat these overrides as experimental and validate them separately before distributing them. The Azure sample keeps the optional silhouette and symbol PNGs as source art only; its default UE project and `.rhammer` omit those assets and leave both manifest fields null.

The Azure sample includes `Scripts/create_hammer_materials.py` and `Scripts/reimport_azure_mesh.py` as examples of importing source images, creating UE materials and assigning the mesh's distinct color slots with UE's Python API. They are sample automation scripts, not prerequisites for other projects. Enable the **Python Editor Script Plugin** and **Editor Scripting Utilities** plugin, then run each through **File → Execute Python Script** or with the `-run=pythonscript -script="<path>"` commandlet form. Run material creation before mesh reimport. The sample scripts create their assets at the same package paths used by its manifest and archive builder; see its [UE project README](../samples/azure-blockhead/UEProject/README.md) for executable Windows commands.

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

Start from the example [Azure manifest template](../samples/azure-blockhead/hammer.json.template). Give your hammer a unique, permanent `id`; keep it unchanged in future updates because the game saves the selected hammer by its row name. Set the mesh and icon paths to the cooked package paths in your project. Leave `silhouetteAssetPath` and `symbolAssetPath` null unless you have validated those custom materials in the game.

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

For the Azure sample, its packaging script validates the cooked directory, required asset paths, and Windows shader metadata, then collects the cooked files and archive. This example assumes the shell is in `samples/azure-blockhead/UEProject/Scripts/` and that the Windows cook was copied to the indicated path:

```bat
python build_rhammer.py C:\path\to\Saved\Cooked\WindowsNoEditor\RMMAzureBlockhead\Content --output ..\..\AzureBlockhead-v10.rhammer
```

Run it from `samples/azure-blockhead/UEProject/Scripts/`. For a different hammer, adapt the script's content folder, required manifest paths, and output name, or assemble the ZIP using the manifest rules above.

Before importing, open the `.rhammer` as a ZIP and confirm it contains `hammer.json`, every referenced cooked asset and sidecar, and the shader archive. The optional `Preview/` image is for RMM's library only; `iconAssetPath` is the in-game icon.

**You should have:** one `.rhammer` file with no missing manifest references.

## Step 5 — Import and deploy with RMM

1. In RMM, import the `.rhammer` into the hammer library. Fix any missing-file, invalid-class, duplicate-path, or invalid-reference errors; do not work around them by removing dependencies.
2. Enable the hammer and build the custom-hammer PAK for the installed Ragnarock version. RMM validates the game build and checks that the package's referenced UE assets are present.
3. Deploy the PAK through RMM. If you are diagnosing a problem, leave only the hammer under test enabled so the selected row and visible model are unambiguous.

**You should have:** the hammer enabled in RMM and deployed to the game.

## Step 6 — Check it in Ragnarock

Check each output separately:

1. Open the changing room. Confirm the list entry has the custom icon, select the card, and confirm the 3D preview is the custom mesh with the intended orientation and materials. In the Flat UI, moving over the card only highlights it; click the card and press Space (Select) to equip it before leaving the room. A hover highlight alone left the stock hammers in the race during testing.
2. Equip it and start an offline song. Confirm the custom mesh appears in the player's hands during the race and that normal hits play the game's shared `DrumHit` sound.
3. In **Settings → Input**, check which keyboard key is assigned to each drum. Use the key for the lane and timing of each chart note; do not infer that input is working just because keys are being pressed. Keep the game's normal keyboard input mode; do not switch to **One Key One Drum**.
4. Use a Standard chart with a steady sequence of notes. First test it with a stock hammer; then repeat the same chart and keyboard bindings with only the custom hammer enabled in RMM. Before starting, verify that the highlighted song row, author, duration, and difficulty are the chart you intend to play. Check the local Stats screen: if it reports no hits or no combo, the run is not valid evidence about the hammer's materials. Once the stock hammer builds combo under those inputs, confirm that the custom hammer builds the blue stage, advances to gold, and clears both gauges after a deliberate miss. Capture each stage during the race. A selected preview or visible race model alone does not prove the combo path works.

### Combo meter and charge

The game calculates combo and charge in its shared gameplay code, but each hammer supplies the material response that makes buildup visible. In the inspected build, `BP_RagnarockHand.UpdateHammerCombo` drives scalar parameters on dynamic material instances. The stock material assets expose this contract:

| Mesh material slot | Stock material | Combo scalar parameters |
| --- | --- | --- |
| `M_JaugeManche` | `M_HandleGauge` | `handleGauge` |
| `M_JaugeTete` | `M_HeadGauge` | `handleGauge`, `circularGauge` |

The default mesh also has its fixed body material. The extracted Summer Hammer DLC head-gauge instance contains both combo parameter names. Stock material assets expose other properties too, including gauge textures, colors, and glow controls; those tune each hammer's artwork and are not additional combo-driver names. Stock and DLC hammers can use hammer-specific gauge materials and textures, so inspect their material instances rather than copying another hammer's artwork blindly. The observed progression is blue up to the first combo level, then gold toward the next; a dropped combo clears the fill back to the dark track while leaving the body surface unchanged.

For a custom Static Mesh, keep those three slots in that order and use the exact, case-sensitive names. Put a `handleGauge` response only on the blue handle bar. On the head bar, use `handleGauge` for the blue first half and `circularGauge` for the gold second half. The Azure sample authors this response on dedicated bar surfaces while keeping its body material fixed. Its upright square model has now been seen in the race, but the current captures do not clearly prove the stock-like blue-to-gold progression. The simple ramp materials are an example implementation, not copies of the stock material graphs or their hammer-specific art. Verify both stages and the reset during a race. Merely declaring the parameters or connecting them to whole-body color/emission does not reproduce the stock gauge.

The names and slot order are based on the installed build's stock `BP_RagnarockHand`, `SM_Hammer_Default`, `MI_HandleGauge`, and `MI_HeadGauge` assets. Keep the standard `DA_Hammers_C` data-asset class; RMM creates that data asset and table row for each package. The hammer's DataTable row and icon do not control combo calculation or material response.

Verify the two parts separately with the same Standard chart and six-key bindings. First use a stock hammer and confirm the hits reach both combo levels. Then enable only the custom hammer and repeat the same chart and inputs. Before starting either run, confirm the selected chart's title, artist, duration, and difficulty in the song row and race dialog. Capture the custom hammer's handle/head response as the meter fills blue, advances to gold, and resets after a deliberate miss. A custom mesh appearing in the changing room or hands proves only that its visual assets loaded. If the stock hammer does not reach the combo levels under the same chart and inputs, resolve the gameplay/input issue before diagnosing the custom package.

## Sample status: Azure Blockhead

The [Azure Blockhead sample directory](../samples/azure-blockhead/README.md) contains the generated source art, UE 4.27 project, build scripts, manifest template, and v10 `.rhammer` package used for this guide.

The local `generate_source.py` produces the low-poly mesh, padded base-color atlas, icon, and grayscale gauge ramp. The UE 4.27 project imports them, creates the fixed body material and two gauge materials, assigns the three stock-compatible material slots, then cooks for Windows `PCD3D_SM5`. Cylinder side faces have distinct UVs through the seam to avoid stretching a face across the atlas.

**Package and deployment**

- The refreshed Windows `PCD3D_SM5` cook was packaged as `AzureBlockhead-v10.rhammer`; it contains 15 cooked package files plus the shared shader archive, and the package builder's ZIP and manifest-reference checks passed. The v10 materials use the stock default blue/orange values. Its generated mesh gives every gauge face a non-degenerate UV chart and splits vertices at hard edges to avoid degenerate tangent bases.
- The installed library mesh sidecar matches the package. Azure is enabled; both demo hammers are disabled.
- RMM rebuilt and deployed the v10 PAK with Azure as the only enabled custom hammer. The generated and installed PAK SHA-256 values match: `69c22ecaf15a24ff1308f82be31df875618d1c8672e4cd1ae924a92d659025d7`.

**Runtime verification status**

- V10 is deployed and selected through the Hammer catalog. Its icon and upright preview appear in the Changing Room, and its square model is visible in both player hammer hands in the [v10 race capture](../samples/azure-blockhead/Verification/Race-Azure-v10-active.png). The [v10 Changing Room capture](../samples/azure-blockhead/Verification/ChangingRoom-v10.png) shows its icon, label, and preview. The earlier [v9 Changing Room](../samples/azure-blockhead/Verification/ChangingRoom-v9.png) and [v9 in-race capture](../samples/azure-blockhead/Verification/Race-Azure-active.png) remain separate evidence for v9.
- A stock-hammer control on the same fixture shows the stock blue handle indicator during play and absent after a pause. Azure's captures show localized light on its gauge bars while playing and a visual change after a pause, but they do not clearly distinguish the blue and gold stages. See the [stock active](../samples/azure-blockhead/Verification/Race-stock-active.png), [stock after pause](../samples/azure-blockhead/Verification/Race-stock-after-pause.png), and [Azure after pause](../samples/azure-blockhead/Verification/Race-Azure-after-pause.png) captures.
- A previous run on the RMM Combo Meter Fixture in the normal six-key layout recorded 40 hits and 0 misses, but the local Stats tab reported 0% perfects and `0 (0 blue + 0 gold)` combos. That pass does not prove combo levels were reached.
- A read-only UE4SS snapshot during that pass found Azure hammer material instances with `handleGauge=1.0` and `circularGauge=0.3333`. This confirms that the game writes gauge parameter values to the hammer's dynamic materials, but does not prove that the intended stages render visibly.
- The v10 in-race mesh is verified, but the Azure package's blue buildup, transition to gold, and visual reset after a dropped combo are still inconclusive. The captured stock control did not clearly reach its gold stage either. Fixture navigation varies between launches, so confirm the highlighted song name, artist, duration, and difficulty in the live list and race dialog before starting a run; also confirm actual hit/combo counts before judging the materials.

Do not describe the combo meter as working until Step 6 confirms both stages and the reset during a race with valid hits and a deliberate miss, and the same input sequence builds combo on a stock hammer.

## References

- [UE 4.27 FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [UE 4.27 Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [UE 4.27 Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
- [UE 4.27 Scripting the Editor using Python](https://dev.epicgames.com/documentation/en-us/unreal-engine/scripting-the-editor-using-python?application_version=4.27)
- [Ragnarock FAQ](https://www.ragnarock-game.com/faq/)
