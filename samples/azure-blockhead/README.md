# Azure Blockhead sample

Azure Blockhead uses Ragnarock's Default Hammer mesh as a donor. It keeps the stock handle, grip position, orientation, scale, gauge geometry, gauge UVs, and material slots, while replacing the head body with authored geometry. This is mesh and asset reuse, not Unreal Blueprint inheritance: the `.rhammer` includes the custom mesh, body material, texture, and icon.

- `M_Character`: Azure's body material and base-color texture. The retained stock body geometry is remapped to the atlas's padded navy swatch; the authored head uses the cyan, gold, navy, and ice swatches.
- `M_JaugeManche`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HandleGauge` material instance for the `handleGauge` and `circularGauge` fill stages and stock color updates.
- `M_JaugeTete`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HeadGauge` material instance for the full-combo head marker and stock color updates.

The installed game's `BP_RagnarockHand.UpdateHammerCombo` writes these scalar parameters across the hammer mesh materials, maps the combo value through the first and second thresholds, and resets both values on combo loss. The sample keeps the stock gauge sections/material references intact and keeps the custom body material separate from those parameters.

The `.rhammer` supplies the custom mesh, body material, base-color texture, and icon. The donor FBX preserves four UV channels; the mesh's gauge slots resolve to the game's stock gauge instances at runtime. Temporary gauge stand-ins are created only so the UE 4.27 project can cook the mesh; the package builder excludes them.

## Source and build

Export `/Game/Meshes/Hammers/DefaultHammer/SM_Hammer_Default` from your Ragnarock installation as an FBX. Preserve its four UV channels and the material names `M_Character`, `M_JaugeManche`, and `M_JaugeTete`. The donor export is not checked into the repository. Generate the authored body atlas and transparent icon with `python generate_source.py`, then run `build_stock_based_azure_fbx.py` inside Blender to replace the head while retaining the donor gauge sections and UVs. The [UE project README](UEProject/README.md) gives the editor-script and cook commands.

For a different hammer, make a new head mesh in Blender or another 3D editor, keep the donor handle and both gauge sections, and put the new head body in the `M_Character` slot. Preserve the donor pivot, scale, orientation, section order, and gauge UVs. Remap donor body UVs if you use a new texture.

## Package and current evidence

`AzureBlockhead-v33.rhammer` is the current UE 4.27 cook. CUE4Parse confirms five UV channels (the four donor channels plus UE's generated lightmap channel), ordered `M_Character`, `M_JaugeManche`, and `M_JaugeTete` sections, and gauge-section bounds aligned to the installed Default Hammer. The RMM library lists Azure and the Changing Room shows its custom icon and 3D preview. A fresh prepared race with Azure explicitly equipped rendered the hammer in-hand. The Stats screen recorded 13 hits, 27 misses, and 0 combos for the note-paced keyboard run; a faster input run recorded 32 hits, 8 misses, and 0 combos. Neither run established a combo, so the handle fill, gold stage, full-combo head marker, and miss reset are not yet verified. Runtime captures still show pale square head faces and streaking on the held model, so the package is not yet validated as complete.

The earlier v28 package came from an OBJ and lost the stock UV channels. It is retained only as historical build evidence; use the FBX workflow and v33 cook commands for current work.

See the [step-by-step authoring guide](../../docs/custom-hammer-authoring-ue4-27.md) for the full source, UE, cook, package, deploy, and runtime workflow.
