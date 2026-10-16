# Make a custom Ragnarock hammer for RMM

This guide walks through modeling, authoring, cooking, packaging, and checking a custom hammer. The [Azure Blockhead sample](../samples/azure-blockhead/README.md) provides a complete source set and UE 4.27 project. RMM installs a cooked `.rhammer`; it does not import Blender files or cook UE assets.

## What you need

- Ragnarock for PC and an asset viewer/exporter that can export the game's Default Hammer Static Mesh as FBX.
- Blender (or another mesh editor) and an image editor.
- Python 3 with Pillow for the sample source generator.
- Unreal Engine **4.27** with Windows `PCD3D_SM5` cooking support.
- RagnaModManager and an installed Ragnarock game for deployment and runtime checks.

Ragnarock's PC build uses Direct3D `PCD3D_SM5`. A Linux UE cooker may emit Vulkan shaders even when asked to cook Windows assets. Use UE 4.27 on Windows, such as a Windows VM, a Windows cloud machine, or Windows CI. The sample's [UE project README](../samples/azure-blockhead/UEProject/README.md) contains runnable Windows commands.

## 1. Start from the stock hammer

Yes: use Ragnarock's Basic/Default Hammer as the donor. This is the simplest way to reuse the handle, grip and held pose, combo gauge geometry and UVs, and the game's existing gauge materials. It also avoids rebuilding the combo shader. This is mesh and material reuse, not Unreal inheritance: a Static Mesh does not subclass another Static Mesh. The custom hammer is a new mesh asset assembled from the donor's parts plus your authored body.

Choose one of two starting points:

- **Keep the stock silhouette:** retain the entire donor mesh and customize its body material/texture. This is the smallest change and the best first check that a custom hammer package receives the stock combo response.
- **Make a new silhouette:** retain the donor handle, handle gauge, head marker, pivot, and material sections, and replace only the `M_Character` body faces. Keep the head marker exposed and avoid overlapping it with the custom body. This is the Azure sample's approach.

In both cases, the custom `.rhammer` contains the new mesh and any authored body material, texture, and icon. Gauge materials can remain references to the game's stock assets; do not copy or rename them. Keep the stock material slot names and assign the matching game materials as described below.

`BP_RagnarockHand.UpdateHammerCombo` reads the Beat Manager's combo value and thresholds, maps the current value into a 0–1 range, and writes the `handleGauge` and `circularGauge` scalar parameters across the hammer mesh materials. It also updates the gauge colors. The Default Hammer mesh and its stock gauge materials turn those values into the familiar blue handle bar, gold second stage, and full-combo marker on the head. Keep the gauge parts and materials intact. On the new-silhouette route, replace only the body section:

| Stock mesh slot | Keep from the donor | Stock response |
| --- | --- | --- |
| `M_Character` | Keep the stock body for a texture/material-only design, or replace its body faces for a new silhouette. Keep the grip point, handle alignment, and UV scale. | Fixed hammer surface and base-color texture. |
| `M_JaugeManche` | Keep the narrow gauge beside the handle and its donor UVs. Assign `/Game/Meshes/Hammers/DefaultHammer/MI_HandleGauge`. | Fills blue to the first combo level, then gold toward the next. |
| `M_JaugeTete` | Keep the marker geometry on the hammer head and its donor UVs. Assign `/Game/Meshes/Hammers/DefaultHammer/MI_HeadGauge`. | Lights at full combo. |

Keep the slot names, capitalization, and order exactly: `M_Character`, `M_JaugeManche`, `M_JaugeTete`. Keep the two stock gauge material references and preserve their donor UVs. The UE 4.27 FBX importer can reorder UV channels and flip V, so the final cooked gauge channels must be compared against the stock mesh; the sample builder applies the measured compensation. The Blueprint writes combo values to all material slots, so the body material must not use the gauge parameters or it will glow with the meter. Keep the body material independent.

1. In an asset viewer that can read Ragnarock's UE 4.27 game data, export `/Game/Meshes/Hammers/DefaultHammer/SM_Hammer_Default` as an FBX. Preserve all four UV channels and the three material sections. The stock gauge materials use a nonzero UV channel, so verify the channels survived export.
2. Import the donor mesh into Blender. Keep its handle, handle-side gauge, head marker, pivot, scale, and orientation. For a texture/material-only design, retain all body faces. For a new silhouette, delete only the original body faces you intend to replace.
3. If you chose the new-silhouette route, model your new head around the retained full-combo marker. Preserve the marker's exposed surface so it remains visible when lit. Avoid overlapping the marker with the body mesh. If you chose the stock-silhouette route, keep the donor body geometry and continue to the material steps.
4. Assign the head body to `M_Character`. Do not rename or merge either gauge section. Keep the head upright in the held orientation and the handle axis aligned to the grip.
5. Export the edited mesh with its UVs and all three named material sections. Keep an editable Blender file as well as the exported mesh.

For the Azure sample, generate the authored color atlas and transparent icon:

```bash
python generate_source.py
```

Then use Blender's FBX import/export workflow to preserve the donor's four UV channels and replace its head. The checked-in Azure FBX is already the result of that source-authoring step and can be opened directly in Blender; the donor game export is not checked into the repository.

Use the stock gauge mesh sections and material instances rather than recreating their fill graph. Preserve their UV values in the donor mesh and material references. UE 4.27 may reorder the FBX UV layers and flip V while importing; compensate in the source builder and compare the cooked gauge UVs to the stock mesh. The Azure body atlas is vertically flipped as an image so the cooked UV0 samples the intended tile after the FBX V flip; keep this texture compensation separate from the untouched gauge UV channels. If you deliberately author a custom gauge material, it must respond to the stock `handleGauge`, `circularGauge`, `centerColor`, and `mainColor` parameters and render the same separate handle fill and head marker; compare every stage with a stock hammer in game.

## 2. Create the UE 4.27 assets

1. Create a UE 4.27 project. Put your assets under a unique path such as `Content/RMM/Hammers/my-hammer/`; keep this path unique across hammer packages.
2. Import the edited FBX as a **Static Mesh**. Confirm the four donor UV channels, pivot, scale, held orientation, and three named material slots. After cooking, compare every gauge section's UV range in all four channels against the Default Hammer. UE 4.27's importer may reorder the FBX channel indices and flip V coordinates, so source FBX values alone are not the acceptance check. Confirm that the bar remains alongside the handle and the head marker is unobstructed.
3. Import each artwork file as **Texture2D**. The in-game icon is a separate square PNG from the body texture; preserve alpha if its background should be transparent. For a painted body atlas, use padded UV islands and retain mipmaps to avoid color bleeding at the island edges.
4. Create a fixed body material for `M_Character` and connect the body base-color texture. Assign it only to `M_Character`.
5. Assign `MI_HandleGauge` and `MI_HeadGauge` from the stock Default Hammer paths to their matching sections. The sample creates temporary editor stand-ins at those paths so UE can cook the mesh; the final `.rhammer` omits the stand-ins and resolves the references to Ragnarock's stock material instances at runtime.
6. Save and inspect the mesh in UE's Static Mesh preview. Check that the new body uses the intended texture and that the handle bar and head marker are visible in the held orientation. Verify each face against its atlas region in the preview; a flipped or remapped UV channel can put a face in the wrong color tile or hide the gauge.

The sample automates texture import, material setup, and mesh assignment in its `UEProject/Scripts` folder. Before opening the UE project, run `build_stock_based_azure_fbx.py` in Blender on the stock FBX export. It retains the donor gauge values while compensating for the measured UE 4.27 import mapping; after cooking, verify all four gauge UV channels against the stock mesh. Enable **Python Editor Script Plugin** and **Editor Scripting Utilities**, then run `create_hammer_materials.py`, `create_stock_gauge_refs.py`, and `reimport_azure_mesh.py` in that order. See the [sample UE README](../samples/azure-blockhead/UEProject/README.md) for exact commands and files.

**Optional selector art:** the hammer data asset also has `Silhouette` and `Symbol` fields. They are not required for a custom mesh, icon, or combo meter. Leave them at their stock defaults unless you have cooked and runtime-tested their materials and dependencies.

## 3. Cook for Windows

In `Config/DefaultEngine.ini`, set the game's shader format:

```ini
[/Script/WindowsTargetPlatform.WindowsTargetSettings]
!TargetedRHIs=ClearArray
+TargetedRHIs=PCD3D_SM5
```

For the shared material shader archive used by the sample, `Config/DefaultGame.ini` contains:

```ini
[/Script/UnrealEd.ProjectPackagingSettings]
bShareMaterialShaderCode=True
```

On Windows, run UE 4.27's command-line cooker (or **File → Package Project → Windows (64-bit)**):

```bat
UE4Editor-Cmd.exe C:\path\to\Project.uproject -run=cook -targetplatform=WindowsNoEditor -cookall
```

Wait for a successful cook. Check the log for `PCD3D_SM5` shader compilation and cook errors. The output is in the project's `Saved/Cooked/WindowsNoEditor` tree. Keep every file for each asset together; UE may emit `.uasset`, `.uexp`, `.ubulk`, or `.uptnl` files.

## 4. Assemble the `.rhammer`

An `.rhammer` is a ZIP archive containing `hammer.json`, all cooked assets required by its manifest, and the project's Windows shader archive. Include the mesh, icon, body material, and every custom texture they reference. Stock gauge material instances remain game references and should not be duplicated into the hammer package.

Start from the [Azure manifest template](../samples/azure-blockhead/hammer.json.template). Give the hammer a stable unique `id` and set `meshAssetPath` and `iconAssetPath` to your cooked UE object paths. RMM creates the game's data asset and table row from this metadata.

For every cooked file under your hammer's content folder, add one manifest `assets` record. The `packagePath` is the Unreal object path without a file extension; all sidecars for an asset share that path. Include the `ShaderArchive-<Project>-PCD3D_SM5.ushaderbytecode` at the path declared by `shaderArchive`. Do not rename cooked assets or package another mod's content at a duplicate object path.

The sample `build_rhammer.py` checks the cooked body material, mesh, texture, icon, PCD3D shader metadata, and shader archive. Its cook-only gauge stand-ins are excluded from the package; the mesh resolves the slot references to the game's stock instances.

Open the resulting `.rhammer` as a ZIP and check that every manifest `source` exists in the archive. The optional `Preview/` image is the RMM library thumbnail; it is separate from the game's `iconAssetPath`.

## 5. Import and deploy with RMM

1. Import the `.rhammer` into RMM's hammer library. Fix missing files, invalid references, duplicate IDs, or duplicate package paths before proceeding.
2. Enable the hammer and build the custom-hammer PAK for the installed Ragnarock version.
3. Deploy the PAK through RMM. While debugging, leave only the hammer under test enabled so the selected table row and race model are unambiguous.

## 6. Verify the hammer in game

Check the icon, changing-room preview, race model, hits, and combo response separately:

1. In the Changing Room, open **Hammers**, select the custom card, and press **Space** to equip it. Confirm the icon, head shape, held orientation, handle-side bar, and head marker. Hovering only highlights a card and does not equip it.
2. Start an offline Standard chart and confirm the custom hammer is visible in the race. Confirm actual hits using the game's local Stats screen or RagnaLoader gameplay evidence. Do not evaluate the gauge if the run records only misses.
3. Use the game's normal keyboard input mode; do not switch to **One Key One Drum**. First run a stock hammer on the same chart and input bindings so you know the input is producing hits and combo.
4. Repeat with only the custom hammer enabled and equipped. Capture the handle bar filling blue, advancing to gold, and emptying after a deliberate miss. Confirm that the head marker stays dark before full combo and lights when the gauge reaches full. Confirm that the rest of the body material stays fixed.

A visible Changing Room model proves that the mesh and icon loaded, but not that the custom hammer was used in a race or that the combo response rendered. The in-race check is required before claiming the meter works.

## What drives the combo effect

The stock Default Hammer uses separate handle- and head-gauge mesh sections. The installed game's `BP_RagnarockHand` Blueprint bytecode confirms that `UpdateHammerCombo` calls `SetScalarParameterValueOnMaterials` for `handleGauge` and `circularGauge`, and `SetVectorParameterValueOnMaterials` for the gauge colors. Its material instances include these parameters. The first range maps combo value 0 to `GetComboMin()` onto the handle fill. The next range maps `GetComboMin()` to `GetComboMax()` and changes the gauge color to gold. At the maximum it sets both scalar values to 1 and applies the full-combo color. `OnComboLost` resets both scalars to 0. This is the stock contract custom materials must preserve:

| Slot | Stock reference | Gauge role |
| --- | --- | --- | --- |
| `M_Character` | Default hammer body material | Body stays visually fixed. |
| `M_JaugeManche` | `MI_HandleGauge` | Blue fill along the handle, then gold as combo advances through the second range. |
| `M_JaugeTete` | `MI_HeadGauge` | Separate marker lights on the hammer head at full combo. |

The game owns combo calculation and resets the gauge values when combo drops. `UpdateHammerCombo` applies `handleGauge`, `circularGauge`, `centerColor`, and `mainColor` to the mesh through `SetScalarParameterValueOnMaterials` and `SetVectorParameterValueOnMaterials`. A custom hammer gets the stock response by retaining the Default Hammer's gauge geometry, UVs, material slot references, and a body material that does not consume those parameters. The hammer icon and RMM manifest do not affect combo behavior. During a race, the first visible handle fill should appear after the opening uninterrupted hits; it fills blue to the first combo tier, then gold toward the next tier. At full combo, the marker on the head lights. A miss clears the fill. The exact timestamp depends on the chart and a clean hit streak, so use the fixture's result stats to confirm that the inputs actually built a combo.

## Azure sample status

v35 is the current UE 4.27 cook. CUE4Parse confirms that its mesh keeps the four stock UV channels, the expected three material sections, and references to the game's `MI_HandleGauge` and `MI_HeadGauge`; the `.rhammer` contains the authored mesh, body material, texture, icon, and shader archive, not duplicate gauge materials. The package was deployed and loaded in a race with Azure as the only enabled custom hammer.

A RagnaLoader run using the normal D2 keyboard bindings on the 120-note threshold probe recorded 119 hits, 1 miss, and 2 `Boat.OnComboTriggered` events. Race captures show the handle gauge lighting during the streak and returning dark after a deliberately missed beat. They do not clearly distinguish the blue and gold stages, and the head marker stays dark in the captured frames. The run therefore confirms combo events and a reset response, but does not yet verify the complete stock visual response. Speckling on the authored mesh also remains unresolved. Keep the sample marked incomplete until the gold stage and full-combo head marker are clearly visible and the texture artifacts are fixed.

## References

- [UE 4.27 FBX Static Mesh Pipeline](https://dev.epicgames.com/documentation/en-us/unreal-engine/fbx-static-mesh-pipeline?application_version=4.27)
- [UE 4.27 Texture Import Guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/texture-import-guide?application_version=4.27)
- [UE 4.27 Content Cooking](https://dev.epicgames.com/documentation/unreal-engine/content-cooking?application_version=4.27)
- [UE 4.27 Scripting the Editor using Python](https://dev.epicgames.com/documentation/en-us/unreal-engine/scripting-the-editor-using-python?application_version=4.27)
- [Ragnarock FAQ](https://www.ragnarock-game.com/faq/)
