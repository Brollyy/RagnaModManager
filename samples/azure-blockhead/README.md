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

`AzureBlockhead-v46.rhammer` is the current UE 4.27 cook. It passed RMM's hammer-library importer, and RMM built and deployed a PAK containing one custom row. The package contains the authored mesh, body material, base-color texture, transparent icon, and shader archive; the two gauge material references resolve to Ragnarock's stock instances.

A fresh v46 race showed the blue handle fill with dark head markers, the marker and handle gauge reset after a deliberate no-input interval, then the gold handle fill and lit head markers at full combo. UE4SS recorded 104 hits, 16 misses, and `boosts=1` for that run. These inputs intentionally included missed notes, so they are evidence of gauge response and reset, not a clean-streak score. See [before the miss](Verification/Race-v46-before-miss.png), [after the miss reset](Verification/Race-v46-after-miss-reset.png), and [full combo after the reset](Verification/Race-v46-full-after-reset.png).

The source FBX duplicates shared head-marker vertices before moving that section outward by 0.75 units. This keeps the retained marker depth adjustment from moving vertices used by the body. The body atlas renders its intended cyan, gold, navy, and ice regions; fine masked patterning remains visible around the stock head marker where it crosses the custom face, and UE 4.27 still reports degenerate tangent-basis warnings on import. Treat that surface polish as unresolved. The [step-by-step authoring guide](../../docs/custom-hammer-authoring-ue4-27.md) documents the complete source, UE 4.27 cook, package, RMM deployment, and runtime workflow.
