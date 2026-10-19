# Azure Blockhead sample

Azure Blockhead uses Ragnarock's Default Hammer mesh as a donor. It keeps the stock handle, grip position, orientation, scale, gauge geometry, gauge UVs, and material slots, while replacing the head body with authored geometry. This is mesh and asset reuse, not Unreal Blueprint inheritance: the `.rhammer` includes the custom mesh, body material, texture, and icon.

- `M_Character`: Azure's body material and base-color texture. The retained stock body geometry is remapped to the atlas's padded navy swatch; the authored head uses the cyan, gold, navy, and ice swatches.
- `M_JaugeManche`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HandleGauge` material instance for the `handleGauge` and `circularGauge` fill stages and stock color updates.
- `M_JaugeTete`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HeadGauge` material instance for the full-combo head marker and stock color updates.

The game updates these two gauge sections from its combo state and resets them when a combo breaks. The sample retains their stock geometry and material references; the custom body material stays separate.

The `.rhammer` supplies the custom mesh, body material, base-color texture, and icon. The donor FBX preserves four UV channels; the mesh's gauge slots resolve to the game's stock gauge instances at runtime. Temporary gauge stand-ins are created only so the UE 4.27 project can cook the mesh; the package builder excludes them. The generated base-color atlas is vertically flipped to compensate for the FBX V flip observed in the UE 4.27 cook; do not apply this correction to the donor gauge UVs. The authored head's long axis runs behind the stock head marker, placing it on the narrow end face rather than the broad side. A recessed navy pocket keeps the stock faceted marker visible while preserving its original UVs and material response.

## Source and build

Export `/Game/Meshes/Hammers/DefaultHammer/SM_Hammer_Default` from your Ragnarock installation as an FBX. Preserve its four UV channels and the material names `M_Character`, `M_JaugeManche`, and `M_JaugeTete`. The donor export is not checked into the repository. Install Pillow for the source generator (`python -m pip install Pillow`). `Source/T_RMM_AzureBlockhead_Atlas_Art.png` is the authored 2×2 base-color atlas; `python generate_source.py` resizes it to the cooked texture size, applies the measured UE 4.27 V orientation, and draws the transparent icon. Then run `build_stock_based_azure_fbx.py` inside Blender to replace the head while retaining the donor gauge sections and UVs. The [UE project README](UEProject/README.md) gives the editor-script and cook commands.

For a different hammer, make a new head mesh in Blender or another 3D editor, keep the donor handle and both gauge sections, and put the new head body in the `M_Character` slot. Preserve the donor pivot, scale, orientation, section order, and gauge UVs. Remap donor body UVs if you use a new texture. The FBX builder compensates for the observed UE 4.27 channel reorder and V flips. After import and cook, compare all four gauge UV channels against the stock Default Hammer; do not infer UV correctness from the source FBX or mesh bounds alone.

## Package

`AzureBlockhead-v65.rhammer` is the current cooked example package. It contains the authored mesh, detailed 2×2 base-color atlas, transparent icon, and shader archive; the gauge material references resolve to Ragnarock's stock assets. The body atlas uses the World texture group's mip chain with trilinear filtering to reduce shimmer on the moving mesh. The mesh builder gives the stock head marker a 0.75-unit offset ahead of the atlas face and a 3-unit clearance pocket. For build and runtime verification notes, see [Verification](Verification/README.md).
