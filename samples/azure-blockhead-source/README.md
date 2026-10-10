# Azure Blockhead source assets

These are original source assets for the UE 4.27 authoring guide. They are generated locally by `generate_source.py` and do not reuse Ragnarock's mesh or icon assets:

- `SM_RMM_AzureBlockhead.obj` — low-poly head, shaft, grip bands, gold plates, and rune geometry. The pivot is at the intended grip point.
- `T_RMM_AzureBlockhead.mtl` and `T_RMM_AzureBlockhead_BaseColor.png` — source surface material and color texture.
- `T_RMM_AzureBlockhead_Icon.png` — separate in-game changing-room icon source.
- `T_RMM_AzureBlockhead_Silhouette.png` and `T_RMM_AzureBlockhead_Symbol.png` — source art for optional custom `Silhouette` and `Symbol` materials.

Run `python3 generate_source.py` to regenerate the files. In UE 4.27, run `UEProject/Scripts/create_hammer_materials.py` to import the base-color, silhouette, and symbol textures and create the authored surface/marker materials; then run `UEProject/Scripts/reimport_azure_mesh.py` to import the rotated Static Mesh and assign the surface material to each mesh slot. Cook for `WindowsNoEditor` on Windows. The Windows cook is required for game-compatible `PCD3D_SM5` material shaders. The sample sets `bShareMaterialShaderCode=True`; RMM packages the resulting project shader archive in an enabled content plugin so Ragnarock can load the custom shader maps.

The [`UEProject/`](UEProject/README.md) folder contains the imported mesh, surface material, base-color and icon textures, plus optional source-authored marker materials. The [`azure-blockhead.rhammer`](azure-blockhead.rhammer) archive includes the cooked mesh and its texture/material dependencies, icon, marker art, and Windows UE 4.27 shader archive. Its manifest leaves `Silhouette` and `Symbol` at the stock data-asset defaults: runtime probes with custom marker overrides crashed in Ragnarock, including one using the correct base `Material` class for `Silhouette`. The stable RMM PAK has SHA-256 `EAFB6F00608A2100093092DA40FCF71E9A6DB26B182DEBB1C3C7F5D01EB978AA`. A fresh Flat-mode RagnaLoader premade hammer-card case previously inspected 28 live widgets and captured the selected Azure icon and 3D changing-room model; that proves selection-room presentation only. In-race mesh identity, charge feedback, and combo behavior remain unverified.

## Runtime-verified library icon sources

- `T_RagnaBlockhead_Preview.png` — transparent changing-room icon for the Ragna Blockhead library package.
- `T_SummerCustom_Preview.png` — transparent changing-room icon for the Summer Custom library package.

The Ragna Blockhead and Summer Custom images have transparent backgrounds so they composite over the changing-room backdrop. Reimport these PNGs as their existing `Texture2D` assets with `UEProject/Scripts/import_demo_hammer_icons.py`, then cook and repack those two library packages before deployment. This replaces the older opaque preview textures.

Both were imported in UE 4.27 as `Texture2D` assets, cooked for `WindowsNoEditor`, added to their `.rhammer` manifests as `iconAssetPath` packages (including `.uasset` and `.uexp`), and rebuilt through RMM. RMM's two-row PAK mounted in Ragnarock, and both separate thumbnails were visible in the changing room. The cooked packages remain in the local RMM hammer library rather than this source repository.
