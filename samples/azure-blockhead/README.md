# Azure Blockhead hammer sample

This sample contains the editable assets, UE 4.27 project, build scripts, and a cooked `.rhammer` package for a custom Ragnarock hammer made from scratch. The full human-followable workflow is in the [custom hammer authoring guide](../../docs/custom-hammer-authoring-ue4-27.md).

## Files

- `Source/SM_RMM_AzureBlockhead.obj` and `.mtl`: generated mesh and the three material slots `M_Character`, `M_JaugeManche`, and `M_JaugeTete`.
- `Source/T_RMM_AzureBlockhead_BaseColor.png`: four-swatch body atlas. Mesh UVs stay inside each swatch with a gutter to reduce color bleeding.
- `Source/T_RMM_AzureBlockhead_GaugeRamp.png`: grayscale U-coordinate ramp for the combo bars.
- `Source/T_RMM_AzureBlockhead_Icon.png`: separate transparent changing-room icon.
- `Source/T_RMM_AzureBlockhead_Silhouette.png` and `..._Symbol.png`: optional source experiments. The UE project does not import or package them; custom selector materials are not needed for this hammer.
- `UEProject/`: UE 4.27 project settings and Python scripts for texture/material import, mesh reimport, and `.rhammer` assembly.
- `hammer.json.template`: RMM package manifest template.
- `AzureBlockhead-v9.rhammer`: Windows `PCD3D_SM5` package with 16 cooked UE asset files, sidecars, and its shared shader archive.

## Rebuild

1. Run `python3 generate_source.py` to regenerate the OBJ, color atlas, icon, and gauge ramp. Python 3 and Pillow are required.
2. Open `UEProject/RMMAzureBlockhead.uproject` in Unreal Engine 4.27 on Windows. Run `UEProject/Scripts/create_hammer_materials.py`, then `UEProject/Scripts/reimport_azure_mesh.py`, with the Python Editor Script and Editor Scripting Utilities plugins enabled.
3. Cook for `WindowsNoEditor` with `PCD3D_SM5` and shared material shader code enabled. The scripts and commands are documented in `UEProject/README.md`.
4. Run `build_rhammer.py` from `UEProject/Scripts/` with the cooked content directory as input. The builder checks the required mesh, icon, body/gauge materials, textures, shader metadata, manifest references, and ZIP integrity.
5. Import the resulting `.rhammer` into RMM, enable only Azure Blockhead, build/deploy the PAK, and select it in Ragnarock's Changing Room.

The included v9 archive SHA-256 is `61fad0f119cdfa6e424441ea719064efc767bdbb6d3948861033e870cda0d68f`.

## Combo materials

The body material stays fixed. `M_JaugeManche` responds to the `handleGauge` scalar and fills the dedicated handle bar blue. `M_JaugeTete` uses `handleGauge` for the blue first half and `circularGauge` for the gold second half of its dedicated head bar. Both use a dark unfilled track. The game writes these parameters to the hammer's dynamic materials.

The v9 package was deployed, and the Changing Room preview showed Azure's upright mesh, colored surfaces, and item name. Blue buildup, the gold transition, the in-race mesh, and the reset after a dropped combo have not yet been confirmed in a valid race. Do not treat the combo response as verified until the stock-versus-Azure race checks in Step 6 of the guide pass.
