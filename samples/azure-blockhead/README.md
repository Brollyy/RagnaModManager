# Azure Blockhead sample

Azure Blockhead uses Ragnarock's Default Hammer mesh as a donor. It keeps the stock handle, grip position, orientation, scale, gauge geometry, gauge UVs, and material slots, while replacing the head body with authored geometry. This is mesh and asset reuse, not Unreal Blueprint inheritance: the `.rhammer` includes the custom mesh, body material, texture, and icon.

- `M_Character`: Azure's body material and base-color texture. The retained stock body geometry is remapped to the atlas's padded navy swatch; the authored head uses the cyan, gold, navy, and ice swatches.
- `M_JaugeManche`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HandleGauge` material instance for the `handleGauge` and `circularGauge` fill stages and stock color updates.
- `M_JaugeTete`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HeadGauge` material instance for the full-combo head marker and stock color updates.

The installed game's `BP_RagnarockHand.UpdateHammerCombo` writes these scalar parameters across the hammer mesh materials, maps the combo value through the first and second thresholds, and resets both values on combo loss. The sample keeps the stock gauge sections/material references intact and keeps the custom body material separate from those parameters.

The `.rhammer` supplies the custom mesh, body material, base-color texture, and icon. The donor FBX preserves four UV channels; the mesh's gauge slots resolve to the game's stock gauge instances at runtime. Temporary gauge stand-ins are created only so the UE 4.27 project can cook the mesh; the package builder excludes them. The generated base-color atlas is vertically flipped to compensate for the FBX V flip observed in the UE 4.27 cook; do not apply this correction to the donor gauge UVs.

## Source and build

Export `/Game/Meshes/Hammers/DefaultHammer/SM_Hammer_Default` from your Ragnarock installation as an FBX. Preserve its four UV channels and the material names `M_Character`, `M_JaugeManche`, and `M_JaugeTete`. The donor export is not checked into the repository. Generate the authored body atlas and transparent icon with `python generate_source.py`, then run `build_stock_based_azure_fbx.py` inside Blender to replace the head while retaining the donor gauge sections and UVs. The [UE project README](UEProject/README.md) gives the editor-script and cook commands.

For a different hammer, make a new head mesh in Blender or another 3D editor, keep the donor handle and both gauge sections, and put the new head body in the `M_Character` slot. Preserve the donor pivot, scale, orientation, section order, and gauge UVs. Remap donor body UVs if you use a new texture. The FBX builder compensates for the observed UE 4.27 channel reorder and V flips. After import and cook, compare all four gauge UV channels against the stock Default Hammer; do not infer UV correctness from the source FBX or mesh bounds alone.

## Package and current evidence

`AzureBlockhead-v35.rhammer` is the latest UE 4.27 cook. It was built against the installed game's exact hammer metadata, deployed, and loaded in a race. CUE4Parse confirms five UV channels (four imported channels plus UE's generated lightmap channel), the expected material section order, and gauge-section bounds aligned to the installed Default Hammer. After compensating for UE's FBX channel reorder and V flips, all four cooked gauge UV ranges match the stock Default Hammer ranges. The body atlas UVs land in cooked UV0; vertically flipping the source atlas makes the intended colors appear after the UE import V flip. The Changing Room displayed the Azure icon and preview, and a race displayed the Azure mesh.

The cooked v35 mesh imports the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HandleGauge` and `MI_HeadGauge` references in its two gauge slots. The package contains no gauge material assets; those stock references are the intended behavior path. This package-level check proves the references are serialized, but not that the meter rendered correctly at runtime.

A clean RagnaLoader run using normal D2 keyboard bindings on the 120-note threshold probe recorded 120 hits, 0 misses, and two `Boat.OnComboTriggered` events. The [full-combo capture](Verification/Race-v35-full-combo.png) shows the handle gauge in its gold stage and both head markers lit. A separate interrupted run captured the gauge dark after a missed beat, but its segmented input did not produce a clean streak. The early blue fill still needs a clear capture, and visible flecking remains near the authored head's face seams. The full-combo response is confirmed; the package remains incomplete until those two issues are resolved.

The earlier v28 package came from an OBJ and lost the stock UV channels. v33 is retained as the pre-compensation cook and v34 as the pre-atlas-flip cook; use the FBX workflow and v35 cook commands for current work.

See the [step-by-step authoring guide](../../docs/custom-hammer-authoring-ue4-27.md) for the full source, UE, cook, package, deploy, and runtime workflow.
