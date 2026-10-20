# Azure Blockhead sample

Azure Blockhead uses Ragnarock's Default Hammer mesh as a donor. It keeps the stock handle, grip position, orientation, scale, gauge geometry, gauge UVs, and gauge material slots, while replacing the head body with authored geometry. This is mesh and asset reuse, not Unreal Blueprint inheritance: the `.rhammer` includes the custom mesh, body and tile materials, four tile textures, and icon.

- `M_Character`: Azure's navy tile material. It covers the retained body and the navy head faces.
- `M_Azure_Cyan`, `M_Azure_Gold`, and `M_Azure_Ice`: separate clamp-addressed tile materials for the corresponding head faces. The authored 2×2 art sheet is split into four Texture2D assets so filtered samples cannot cross from one atlas region into another.
- `M_JaugeManche`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HandleGauge` material instance for the `handleGauge` and `circularGauge` fill stages and stock color updates.
- `M_JaugeTete`: assigned the stock `/Game/Meshes/Hammers/DefaultHammer/MI_HeadGauge` material instance for the full-combo head marker and stock color updates.

The game updates these two gauge sections from its combo state and resets them when a combo breaks. The sample retains their stock geometry and material references; the custom body material stays separate.

The donor FBX preserves four UV channels; the first three slots remain the stock body, handle gauge, and head gauge, so the game retains its combo response. Temporary gauge stand-ins are created only so the UE 4.27 project can cook the mesh; the package builder excludes them. A recessed navy pocket keeps the stock head marker visible, with four inner walls connecting the opening to the pocket.

## Source and build

Export `/Game/Meshes/Hammers/DefaultHammer/SM_Hammer_Default` from your Ragnarock installation as an FBX. Preserve its four UV channels and the material names `M_Character`, `M_JaugeManche`, and `M_JaugeTete`. The donor export is not checked into the repository. Install Pillow for the source generator (`python -m pip install Pillow`). `Source/T_RMM_AzureBlockhead_Atlas_Art_v3.png` is the authored 2×2 artwork; its cyan emblem is moved away from the center gauge pocket. `python generate_source.py` writes one clamp-addressed Texture2D per tile and draws the transparent icon. Then run `build_stock_based_azure_fbx.py` inside Blender to replace the head while retaining the donor gauge sections and UVs. The [UE project README](UEProject/README.md) gives the editor-script and cook commands.

For a different hammer, make a new head mesh in Blender or another 3D editor, keep the donor handle and both gauge sections, and put the new head body in the `M_Character` slot. Preserve the donor pivot, scale, orientation, section order, and gauge UVs. Remap donor body UVs if you use a new texture. The FBX builder compensates for the observed UE 4.27 channel reorder and V flips. After import and cook, compare all four gauge UV channels against the stock Default Hammer; do not infer UV correctness from the source FBX or mesh bounds alone.

## Package

The stock gauge materials and donor UV channels work in game. The v158 full build matches the custom head faces to the stock donor bounds and passed the RagnaLoader Changing Room case. Its 12-frame rotation sweep shows the painted faces tracking with the hammer head. This run used flat mode; physical VR head-motion behavior still needs confirmation. See [Verification](Verification/README.md) for the stock comparison and captures.
