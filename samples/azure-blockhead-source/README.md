# Azure Blockhead source assets

These are original source assets for the UE 4.27 authoring guide. They are generated locally by `generate_source.py` and do not reuse Ragnarock's mesh or icon assets:

- `SM_RMM_AzureBlockhead.obj` — low-poly head, shaft, grip bands, gold plates, and rune geometry. The pivot is at the intended grip point.
- `T_RMM_AzureBlockhead.mtl` and `T_RMM_AzureBlockhead_BaseColor.png` — source surface material and color texture.
- `T_RMM_AzureBlockhead_Icon.png` — separate in-game changing-room icon source.
- `T_RMM_AzureBlockhead_Silhouette.png` and `T_RMM_AzureBlockhead_Symbol.png` — source art for the custom `Silhouette` and `Symbol` Material Instances.

Run `python3 generate_source.py` to regenerate the files. Import the OBJ and images into UE 4.27, create the mesh/material/texture assets plus the `Silhouette` and `Symbol` Material Instances described in [the authoring guide](../../docs/custom-hammer-authoring-ue4-27.md), then cook for `WindowsNoEditor` on Windows. The Windows cook is required for game-compatible `PCD3D_SM5` material shaders.

The [`UEProject/`](UEProject/README.md) folder contains the imported mesh, surface material, base-color and icon textures, and source-authored `Silhouette` and `Symbol` Material Instances. The [`azure-blockhead-verified.rhammer`](azure-blockhead-verified.rhammer) sample package was cooked with Windows UE 4.27, includes its project `PCD3D_SM5` shader archive, and is checked by `build_rhammer.py` before assembly. RMM mounts that archive through a separate chunk PAK because Ragnarock resolves cooked shared-material shader maps through `ShaderCodeLibrary`. Final runtime verification must confirm the archive loads, the changing-room icon and preview appear, and the custom surface and gameplay mesh render correctly.

## Runtime-verified library icon sources

- `T_RagnaBlockhead_Preview.png` — transparent changing-room icon for the Ragna Blockhead library package.
- `T_SummerCustom_Preview.png` — transparent changing-room icon for the Summer Custom library package.

The Ragna Blockhead and Summer Custom images have transparent backgrounds so they composite over the changing-room backdrop. Reimport these PNGs as their existing `Texture2D` assets with `UEProject/Scripts/import_demo_hammer_icons.py`, then cook and repack those two library packages before deployment. This replaces the older opaque preview textures.

Both were imported in UE 4.27 as `Texture2D` assets, cooked for `WindowsNoEditor`, added to their `.rhammer` manifests as `iconAssetPath` packages (including `.uasset` and `.uexp`), and rebuilt through RMM. RMM's two-row PAK mounted in Ragnarock, and both separate thumbnails were visible in the changing room. The cooked packages remain in the local RMM hammer library rather than this source repository.
