# Azure Blockhead source assets

These are original source assets for the UE 4.27 authoring guide. They are generated locally by `generate_source.py` and do not reuse Ragnarock's mesh or icon assets:

- `SM_RMM_AzureBlockhead.obj` — low-poly head, shaft, grip bands, gold plates, and rune geometry. The pivot is at the intended grip point.
- `T_RMM_AzureBlockhead.mtl` and `T_RMM_AzureBlockhead_BaseColor.png` — source surface material and color texture.
- `T_RMM_AzureBlockhead_Icon.png` — separate in-game changing-room icon source.
- `T_RMM_AzureBlockhead_Silhouette.png` and `T_RMM_AzureBlockhead_Symbol.png` — source art for the custom `Silhouette` and `Symbol` Material Instances.

Run `python3 generate_source.py` to regenerate the files. Import the OBJ and images into UE 4.27, create the mesh/material/texture assets plus the `Silhouette` and `Symbol` Material Instances described in [the authoring guide](../../docs/custom-hammer-authoring-ue4-27.md), then cook for `WindowsNoEditor` on Windows. The Windows cook is required for game-compatible `PCD3D_SM5` material shaders.

The [`UEProject/`](UEProject/README.md) folder contains a UE 4.27 editor project with the imported mesh, surface material, base-color and icon textures, and source-authored `Silhouette` and `Symbol` Material Instances. It is not a complete `.rhammer` package: the Windows cook, manifest assembly, and final runtime material check remain.

A runtime trial confirmed that the model and custom icon load in the changing room and that both in-game `HammerMesh` components reference the Azure mesh during a song. Its surface material falls back to the checker/default material because the Linux UE cook produced Vulkan shaders and Ragnarock requires `PCD3D_SM5`. The project explicitly configures the Windows target for `PCD3D_SM5`; cook with Windows UE 4.27, then verify the surface in game before treating the package as complete.

## Runtime-verified library icon sources

- `T_RagnaBlockhead_Icon.png` — changing-room icon for the Ragna Blockhead library package.
- `T_SummerCustom_Icon.png` — distinct changing-room icon for the Summer Custom library package.

Both were imported in UE 4.27 as `Texture2D` assets, cooked for `WindowsNoEditor`, added to their `.rhammer` manifests as `iconAssetPath` packages (including `.uasset` and `.uexp`), and rebuilt through RMM. RMM's two-row PAK mounted in Ragnarock, and both separate thumbnails were visible in the changing room. The cooked packages remain in the local RMM hammer library rather than this source repository.
