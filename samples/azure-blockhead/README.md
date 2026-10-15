# Azure Blockhead hammer sample

This sample contains the editable assets, UE 4.27 project, build scripts, and a cooked `.rhammer` package for a custom Ragnarock hammer made from scratch. The full human-followable workflow is in the [custom hammer authoring guide](../../docs/custom-hammer-authoring-ue4-27.md).

## Files

- `Source/SM_RMM_AzureBlockhead.obj` and `.mtl`: generated mesh and the three material slots `M_Character`, `M_JaugeManche`, and `M_JaugeTete`.
- `Source/T_RMM_AzureBlockhead_BaseColor.png`: four-swatch body atlas. Mesh UVs stay inside each swatch, and the imported texture has no mipmaps to prevent distant mips from blending neighboring colors.
- `Source/T_RMM_AzureBlockhead_GaugeRamp.png`: grayscale U-coordinate ramp for the combo bars.
- `Source/T_RMM_AzureBlockhead_Icon.png`: separate transparent changing-room icon.
- `Source/T_RMM_AzureBlockhead_Silhouette.png` and `..._Symbol.png`: optional source experiments. The UE project does not import or package them; custom selector materials are not needed for this hammer.
- `UEProject/`: UE 4.27 project settings and Python scripts for texture/material import, mesh reimport, and `.rhammer` assembly.
- `hammer.json.template`: RMM package manifest template.
- `AzureBlockhead-v9.rhammer`: previous Windows `PCD3D_SM5` package.
- `AzureBlockhead-v10.rhammer`: refreshed Windows `PCD3D_SM5` package with 15 cooked UE assets and its shared shader archive.

## Rebuild

1. Run `python3 generate_source.py` to regenerate the OBJ, color atlas, icon, and gauge ramp. Python 3 and Pillow are required.
2. Open `UEProject/RMMAzureBlockhead.uproject` in Unreal Engine 4.27 on Windows. Run `UEProject/Scripts/create_hammer_materials.py`, then `UEProject/Scripts/reimport_azure_mesh.py`, with the Python Editor Script and Editor Scripting Utilities plugins enabled.
3. Cook for `WindowsNoEditor` with `PCD3D_SM5` and shared material shader code enabled. The scripts and commands are documented in `UEProject/README.md`.
4. Run `build_rhammer.py` from `UEProject/Scripts/` with the cooked content directory as input. The builder checks the required mesh, icon, body/gauge materials, textures, shader metadata, manifest references, and ZIP integrity.
5. Import the resulting `.rhammer` into RMM, enable only Azure Blockhead, build/deploy the PAK, and select it in Ragnarock's Changing Room.

The included v9 archive SHA-256 is `61fad0f119cdfa6e424441ea719064efc767bdbb6d3948861033e870cda0d68f`. The refreshed v10 archive SHA-256 is `fe3261100923af47b88b3f8301719b41ad922d861b59374e2d51d228e4bb09ee`.

The [v10 Changing Room capture](Verification/ChangingRoom-v10.png) shows the custom icon, selected Azure entry, and its 3D preview after deployment. The earlier [v9 capture](Verification/ChangingRoom-v9.png) is kept for comparison.

The [v10 race capture](Verification/Race-Azure-v10-active.png) shows the square Azure model in both player hammer hands after explicitly selecting the Azure card. The earlier [v9 race capture](Verification/Race-Azure-active.png) and paired [stock active](Verification/Race-stock-active.png) and [stock after pause](Verification/Race-stock-after-pause.png) captures are kept for comparison. The v10 capture does not show clear blue buildup, a gold transition, or a reset matching stock. Treat the combo appearance as unresolved.

## Combo materials

The body material stays fixed. `M_JaugeManche` responds to the `handleGauge` scalar and fills the dedicated handle bar blue. `M_JaugeTete` uses `handleGauge` for the blue first half and `circularGauge` for the gold second half of its dedicated head bar. Both use a dark unfilled track. The game writes these parameters to the hammer's dynamic materials.

V9's upright mesh, colored surfaces, and item name were verified earlier. V10 was rebuilt from the updated UE 4.27 project, deployed through RMM, and selected from the Hammer catalog. Its icon, item label, and upright 3D preview are visible in the [v10 Changing Room capture](Verification/ChangingRoom-v10.png). The v10 square race model is now verified in the [v10 race capture](Verification/Race-Azure-v10-active.png). Its combo progression, reset, and visual comparison against stock remain unverified; do not claim the combo meter works until Step 6 of the guide passes with v10.
