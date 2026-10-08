# Azure Blockhead source assets

These are original source assets for the UE 4.27 authoring guide. They are generated locally by `generate_source.py` and do not reuse Ragnarock's mesh or icon assets:

- `SM_RMM_AzureBlockhead.obj` — low-poly head, shaft, grip bands, gold plates, and rune geometry. The pivot is at the intended grip point.
- `T_RMM_AzureBlockhead.mtl` and `T_RMM_AzureBlockhead_BaseColor.png` — source surface material and color texture.
- `T_RMM_AzureBlockhead_Icon.png` — separate in-game changing-room icon source.

Run `python3 generate_source.py` to regenerate the files. Import the OBJ and images into UE 4.27, create the mesh/material/texture assets plus the `Silhouette` and `Symbol` Material Instances described in [the authoring guide](../../docs/custom-hammer-authoring-ue4-27.md), then cook for `WindowsNoEditor`.

This folder currently contains **source art only**. UE 4.27 cooked package files and a runtime-verified `.rhammer` have not yet been produced in this environment.
